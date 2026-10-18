param([Parameter(Mandatory)][string]$AppDirectory, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'UI smoke runs only on disposable GitHub Actions runners.' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SmokeWindow {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
}
'@
$appRoot = (Resolve-Path -LiteralPath $AppDirectory).Path
$captureRoot = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($captureRoot) | Out-Null
$labels = [xml][IO.File]::ReadAllText((Join-Path $appRoot 'Strings/zh-CN/Resources.resw'), [Text.Encoding]::UTF8)
function Label([string]$key) {
    $node = $labels.SelectSingleNode("/root/data[@name='$key']/value")
    if ($null -eq $node) { throw "Missing locale key: $key" }
    return $node.InnerText
}
$process = Start-Process -FilePath (Join-Path $appRoot 'RHI-CS.exe') -PassThru `
    -RedirectStandardOutput (Join-Path $captureRoot 'stdout.txt') -RedirectStandardError (Join-Path $captureRoot 'stderr.txt')
function Find-Control([string]$name = '', [string]$id = '', [switch]$Action) {
    $limit = [Diagnostics.Stopwatch]::StartNew()
    while ($limit.Elapsed.TotalSeconds -lt 25) {
        if ($process.HasExited) { throw "Application exited with code $($process.ExitCode)." }
        $pidFilter = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
        $property = if ($id) { [Windows.Automation.AutomationElement]::AutomationIdProperty } else { [Windows.Automation.AutomationElement]::NameProperty }
        $value = if ($id) { $id } else { $name }
        $selector = [Windows.Automation.PropertyCondition]::new($property, $value)
        $condition = [Windows.Automation.AndCondition]::new($pidFilter, $selector)
        $items = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)
        foreach ($item in $items) {
            if ($item.Current.IsOffscreen) { continue }
            if ($Action) {
                $candidatePattern = $null
                if (-not $item.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$candidatePattern) -and
                    -not $item.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern, [ref]$candidatePattern)) { continue }
            }
            return $item
        }
        Start-Sleep -Milliseconds 200
    }
    throw "UI control not found: $name $id"
}
function Invoke-Control([string]$key) {
    $item = Find-Control -name (Label $key) -Action
    $pattern = $null
    if ($item.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke() }
    elseif ($item.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern, [ref]$pattern)) { $pattern.Toggle() }
    else { throw "Control cannot be invoked: $key" }
    Start-Sleep -Milliseconds 250
}
function Capture([string]$name) {
    $process.Refresh()
    $handle = $process.MainWindowHandle
    [SmokeWindow]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Milliseconds 350
    $rect = [SmokeWindow+Rect]::new()
    if (-not [SmokeWindow]::GetWindowRect($handle, [ref]$rect)) { throw 'Cannot inspect application window.' }
    $width = $rect.Right - $rect.Left; $height = $rect.Bottom - $rect.Top
    if ($width -lt 600 -or $height -lt 400) { throw "Unexpected window size: $width x $height" }
    $bitmap = [Drawing.Bitmap]::new($width, $height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, [Drawing.Size]::new($width, $height))
        $bitmap.Save((Join-Path $captureRoot "$name.png"), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
try {
    Find-Control -name (Label 'Libraries') | Out-Null
    $process.Refresh()
    $screen = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    [SmokeWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero, $screen.X, $screen.Y,
        [Math]::Min(1320, $screen.Width), [Math]::Min(860, $screen.Height), 0x0040) | Out-Null
    Capture '01-games'
    Invoke-Control 'Libraries'
    Find-Control -name (Label 'AddVersion') | Out-Null
    Capture '02-library'
    Invoke-Control 'AddVersion'
    $kind = Find-Control -id 'VersionResourceType'
    Capture '03-version-files'
    $expand = $kind.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    $option = Find-Control -name (Label 'ReShadeStandard')
    $option.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if ((Find-Control -id 'VersionChooseFolder').Current.IsEnabled) { throw 'ReShade folder picker must be disabled.' }
    if ((Find-Control -id 'VersionTargetOverride').Current.IsEnabled) { throw 'ReShade subfolder override must be disabled.' }
    Capture '04-version-reshade'
    Invoke-Control 'Cancel'
    Invoke-Control 'Games'
    Invoke-Control 'Deploy'
    Find-Control -name (Label 'ConfigurationChanges') | Out-Null
    Capture '05-reshade-preflight'
    Invoke-Control 'Cancel'
    $search = Find-Control -id 'GamesSearch'
    $search.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('CI')
    $search.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('CI Game')
    if ((Find-Control -id 'GamesSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'CI Game') {
        throw 'Search query was lost while filtering.'
    }
    Invoke-Control 'Libraries'; Invoke-Control 'Games'
    if ((Find-Control -id 'GamesSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'CI Game') {
        throw 'Search query was lost during navigation.'
    }
    Capture '06-search-retained'
    [IO.File]::WriteAllText((Join-Path $captureRoot 'result.txt'), 'PASS: application launch, type controls, INI preview, and search retention. Screenshots require visual review.', [Text.Encoding]::UTF8)
} catch {
    try { Capture 'failure' } catch { Write-Warning $_ }
    throw
} finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
}
