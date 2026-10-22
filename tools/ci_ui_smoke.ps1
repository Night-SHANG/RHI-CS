param([Parameter(Mandatory)][string]$AppDirectory, [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$PendingConfiguration, [switch]$Features, [switch]$Relocation, [switch]$Visual, [switch]$Startup, [switch]$Migration, [switch]$Usability, [string]$FixtureMetadata)
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
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool SystemParametersInfo(uint action, uint parameter, out Rect rect, uint flags);
}
'@
[SmokeWindow]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$appRoot = (Resolve-Path -LiteralPath $AppDirectory).Path
$captureRoot = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($captureRoot) | Out-Null
$initialLanguage = if ($Startup) { 'en-US' } else { 'zh-CN' }
if ($Migration -and (Get-Content -LiteralPath $FixtureMetadata -Raw -Encoding UTF8 | ConvertFrom-Json).Interrupted) { $initialLanguage = 'en-US' }
$labels = [xml][IO.File]::ReadAllText((Join-Path $appRoot "Strings/$initialLanguage/Resources.resw"), [Text.Encoding]::UTF8)
function Label([string]$key) {
    $node = $labels.SelectSingleNode("/root/data[@name='$key']/value")
    if ($null -eq $node) { throw "Missing locale key: $key" }
    return $node.InnerText
}
$launchTime = Get-Date
$process = Start-Process -FilePath (Join-Path $appRoot 'RHI-CS.exe') -PassThru `
    -RedirectStandardOutput (Join-Path $captureRoot 'stdout.txt') -RedirectStandardError (Join-Path $captureRoot 'stderr.txt')
$processHandle = $process.Handle
function Find-Control([string]$name = '', [string]$id = '', [switch]$Action, [switch]$IncludeOffscreen, [switch]$Enabled) {
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
            if ($item.Current.IsOffscreen -and -not $IncludeOffscreen) { continue }
            if ($Enabled -and -not $item.Current.IsEnabled) { continue }
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
    $item = Find-Control -name (Label $key) -Action -Enabled
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
function Select-SettingsSection([string]$section) {
    $item = Find-Control -id ("SettingsNav" + $section)
    $pattern = $null
    if ($item.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) { $pattern.Select() }
    elseif ($item.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke() }
    else { throw "Settings section cannot be selected: $section" }
    Start-Sleep -Milliseconds 350
}
function Assert-ActionOrder([string]$action) {
    $cancel = (Find-Control -name (Label 'Cancel') -Action).Current.BoundingRectangle
    $confirm = (Find-Control -name (Label $action) -Action).Current.BoundingRectangle
    if ($cancel.X -ge $confirm.X -or [Math]::Abs($cancel.Y - $confirm.Y) -gt 1) {
        throw "Dialog actions do not follow cancel-left / confirm-right layout: $action"
    }
    return $confirm
}
function Assert-NoResourceType {
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $selector = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'VersionResourceType')
    if ($window.FindFirst([Windows.Automation.TreeScope]::Descendants, $selector)) { throw 'Resource type selector must not be shown.' }
}
function Assert-FormError([string]$key) {
    if ((Find-Control -name (Label 'Save') -Action).Current.IsEnabled) { Invoke-Control 'Save' }
    Start-Sleep -Milliseconds 250
    if ((Find-Control -id 'FormValidation' -IncludeOffscreen).Current.Name -ne (Label $key)) { throw "Wrong form guidance: $key" }
    if ((Find-Control -name (Label 'Save') -Action).Current.IsEnabled) { throw 'Invalid form can be saved.' }
}
function Assert-FormReady {
    Start-Sleep -Milliseconds 250
    if (-not (Find-Control -name (Label 'Save') -Action).Current.IsEnabled) { throw 'Valid form cannot be saved.' }
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $selector = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'FormValidation')
    $feedback = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $selector)
    if ($feedback -and $feedback.Current.Name) { throw 'Valid form still displays validation prose.' }
}
function Assert-Destination([string]$key, [string]$folder) {
    Start-Sleep -Milliseconds 250
    $location = if ($folder) { (Label 'GameDeploymentSubfolder') -f $folder } else { Label 'GameDeploymentFolder' }
    $expected = (Label $key) -f $location
    if ((Find-Control -id 'VersionTargetHint' -IncludeOffscreen).Current.Name -ne $expected) {
        throw "Incorrect destination guidance: $expected"
    }
}
function Invoke-LibraryControl([string]$id) {
    if ($id -eq 'VersionEdit') { Invoke-Control 'View' }
    $node = Find-Control -id $id -Enabled -IncludeOffscreen
    if ($node.Current.IsOffscreen) {
        # Match the existing relocation check: reach the actual button by scrolling its owning view.
        $scroll = (Find-Control -id 'LibraryScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
        $percent = if ($id -eq 'VersionEdit') { 100 } else { 0 }
        if (-not $scroll.Current.VerticallyScrollable) { throw "Library action is offscreen without scrolling: $id" }
        $scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, $percent)
    }
    (Find-Control -id $id -Enabled).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 250
}
function Assert-FixedAction($before, [string]$action = 'Save') {
    $after = Assert-ActionOrder $action
    foreach ($property in @('X', 'Y', 'Width', 'Height')) {
        if ([Math]::Abs($before.$property - $after.$property) -gt 1) {
            throw "Form state change moved action bounds: $property ($($before.$property) -> $($after.$property))"
        }
    }
}
function Scroll-DialogToEnd([string]$action, [switch]$IfNeeded) {
    (Find-Control -name (Label 'Cancel') -Action).SetFocus()
    $node = Find-Control -id 'DialogContentScroll'
    $pattern = $node.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
    if (-not $pattern.Current.VerticallyScrollable) {
        if ($IfNeeded) { return }
        throw 'Long dialog content is not scrollable.'
    }
    Write-Host "Dialog scroll before: $($pattern.Current.VerticalScrollPercent), view size: $($pattern.Current.VerticalViewSize)"
    $pattern.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 100)
    $scrollWait = [Diagnostics.Stopwatch]::StartNew()
    while ($pattern.Current.VerticalScrollPercent -lt 99 -and $scrollWait.Elapsed.TotalSeconds -lt 1) {
        Start-Sleep -Milliseconds 100
    }
    for ($step = 0; $step -lt 5 -and $pattern.Current.VerticalScrollPercent -lt 99; $step++) {
        Write-Host "Dialog page scroll from: $($pattern.Current.VerticalScrollPercent)"
        $pattern.Scroll([Windows.Automation.ScrollAmount]::NoAmount, [Windows.Automation.ScrollAmount]::LargeIncrement)
        Start-Sleep -Milliseconds 500
    }
    Write-Host "Dialog scroll after: $($pattern.Current.VerticalScrollPercent), view size: $($pattern.Current.VerticalViewSize)"
    if ($pattern.Current.VerticalScrollPercent -lt 99) { throw 'Dialog content did not scroll to its end.' }
}
. (Join-Path $PSScriptRoot 'ci_ui_file_details.ps1')
try {
    Find-Control -name (Label 'Libraries') | Out-Null
    $process.Refresh()
    $screen = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    [SmokeWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero, $screen.X, $screen.Y,
        [Math]::Min(1320, $screen.Width), [Math]::Min(860, $screen.Height), 0x0040) | Out-Null
    if ($Features) { . (Join-Path $PSScriptRoot 'ci_ui_features.ps1'); return }
    if ($Relocation) { . (Join-Path $PSScriptRoot 'ci_ui_relocation.ps1'); return }
    if ($Visual) { . (Join-Path $PSScriptRoot 'ci_ui_visual.ps1'); return }
    if ($Startup) { . (Join-Path $PSScriptRoot 'ci_ui_startup.ps1'); return }
    if ($Migration) { . (Join-Path $PSScriptRoot 'ci_ui_migration.ps1'); return }
    if ($Usability) { . (Join-Path $PSScriptRoot 'ci_ui_usability.ps1'); return }
    if ($PendingConfiguration) {
        if ((Find-Control -id 'GameEdit').Current.IsEnabled) { throw 'Pending game configuration is editable.' }
        if ((Find-Control -id 'GameRelocate' -IncludeOffscreen).Current.IsEnabled) { throw 'Pending game can be relocated.' }
        Invoke-Control 'Libraries'
        Find-Control -name (Label 'PendingConfigurationLocked') | Out-Null
        foreach ($id in @('LibraryEdit', 'LibraryAddVersion')) {
            if ((Find-Control -id $id -IncludeOffscreen).Current.IsEnabled) { throw "Pending library control is enabled: $id" }
        }
        Invoke-Control 'View'
        if ((Find-Control -id 'VersionEdit').Current.IsEnabled) { throw 'Pending version is editable.' }
        Capture '17-version-locked-zh'
        Invoke-Control 'Close'
        Capture '17-library-locked-zh'
        $query = (Find-Control -id 'LibrariesSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
        $query.SetValue('Other library')
        Start-Sleep -Milliseconds 250
        if (-not (Find-Control -id 'LibraryEdit').Current.IsEnabled) { throw 'Unrelated library was incorrectly locked.' }
        $query.SetValue('')
        Invoke-Control 'Settings'
        $language = Find-Control -id 'SettingsLanguage' -Enabled
        $language.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        (Find-Control -name (Label 'en-US')).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Invoke-Control 'Save'
        $labels = [xml][IO.File]::ReadAllText((Join-Path $appRoot 'Strings/en-US/Resources.resw'), [Text.Encoding]::UTF8)
        Invoke-Control 'Libraries'
        # Search selects the affected library independently of earlier selection in the unrelated-library check.
        (Find-Control -id 'LibrariesSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('ReShade')
        Find-Control -name (Label 'PendingConfigurationLocked') | Out-Null
        $lockedBounds = (Find-Control -id 'LibraryEdit').Current.BoundingRectangle
        Capture '18-library-locked-en'
        Invoke-Control 'Games'
        Invoke-Control 'CheckRecovery'
        Assert-ActionOrder 'Recover' | Out-Null
        Invoke-Control 'Recover'
        $unlockWait = [Diagnostics.Stopwatch]::StartNew()
        while (-not (Find-Control -id 'GameEdit').Current.IsEnabled -and $unlockWait.Elapsed.TotalSeconds -lt 15) {
            Start-Sleep -Milliseconds 200
        }
        if (-not (Find-Control -id 'GameEdit').Current.IsEnabled) { throw 'Recovered game is still locked.' }
        Invoke-Control 'Libraries'
        foreach ($id in @('LibraryEdit', 'LibraryAddVersion')) {
            if (-not (Find-Control -id $id -IncludeOffscreen).Current.IsEnabled) { throw "Recovered library is still locked: $id" }
        }
        Invoke-Control 'View'
        if (-not (Find-Control -id 'VersionEdit').Current.IsEnabled) { throw 'Recovered version is locked.' }
        Invoke-Control 'Close'
        $unlockedBounds = (Find-Control -id 'LibraryEdit').Current.BoundingRectangle
        if ([Math]::Abs($lockedBounds.X - $unlockedBounds.X) -gt 1 -or [Math]::Abs($lockedBounds.Y - $unlockedBounds.Y) -gt 1) {
            throw 'Changing lock state moved the library edit action.'
        }
        Find-Control -name (Label 'ConfigOnly') | Out-Null
        Capture '19-library-unlocked-en'
        [IO.File]::WriteAllText((Join-Path $captureRoot 'result.txt'), 'PASS: pending game/library controls locked in Chinese and English; reason visible; unrelated library editable; existing recovery flow unlocks controls. Interrupted preparation is a synthetic journal fixture.', [Text.Encoding]::UTF8)
        return
    }
    Capture '01-games'
    Invoke-Control 'Libraries'
    Find-Control -name (Label 'AddVersion') | Out-Null
    Capture '02-library'
    Invoke-LibraryControl 'VersionEdit'
    $legacySource = (Find-Control -id 'VersionSourceInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    Assert-NoResourceType
    if ((Find-Control -id 'VersionChooseFolder').Current.IsEnabled) { throw 'Legacy adapter must not accept a folder.' }
    if ((Find-Control -id 'VersionTargetOverride' -IncludeOffscreen).Current.IsEnabled) { throw 'Legacy adapter must retain root placement.' }
    Scroll-DialogToEnd 'Save' -IfNeeded
    Capture '04-version-legacy'
    Invoke-Control 'Save'
    Invoke-LibraryControl 'LibraryEdit'
    $libraryTarget = (Find-Control -id 'LibraryTargetInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    $originalLibraryTarget = $libraryTarget.Current.Value
    $libraryTarget.SetValue('mods')
    (Find-Control -id 'LibrarySourceRootInput' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue([IO.Path]::GetDirectoryName($legacySource))
    Assert-FormReady
    Capture '03-library-subfolder-ready'
    Invoke-Control 'Save'
    Invoke-LibraryControl 'LibraryAddVersion'
    . (Join-Path $PSScriptRoot 'ci_ui_library_folder.ps1')
    Assert-NoResourceType
    $filesSave = Assert-ActionOrder 'Save'
    Capture '03-version-files'
    if (-not (Find-Control -id 'VersionChooseFolder').Current.IsEnabled) { throw 'Ordinary file folder picker must be enabled.' }
    if (-not (Find-Control -id 'VersionChooseFile').Current.IsEnabled) { throw 'File picker must be enabled.' }
    if (-not (Find-Control -id 'VersionTargetOverride' -IncludeOffscreen).Current.IsEnabled) { throw 'Version folder setting must be enabled.' }
    (Find-Control -id 'VersionLabelInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Prepared offline folder')
    Assert-FormError 'SourcePathRequired'
    $sourceInput = (Find-Control -id 'VersionSourceInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    $sourceInput.SetValue('mods')
    Assert-FormError 'SourcePathInvalid'
    $sourceInput.SetValue($legacySource + '.missing')
    Assert-FormError 'SourcePathMissing'
    $sourceInput.SetValue([IO.Path]::GetDirectoryName($legacySource))
    $targetInput = (Find-Control -id 'VersionTargetOverride' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    Assert-Destination 'VersionTargetHelp' 'mods'
    Assert-FormReady
    $targetInput.SetValue('mods\textures')
    Assert-Destination 'VersionTargetLocation' 'mods\textures'
    Assert-FormReady
    Assert-FixedAction $filesSave
    Scroll-DialogToEnd 'Save' -IfNeeded
    Capture '07-version-explicit-destination'
    $targetInput.SetValue('F:\Resources\OfflineReShade')
    Assert-FormError 'TargetFolderAbsolute'
    Assert-FixedAction $filesSave
    Scroll-DialogToEnd 'Save' -IfNeeded
    Capture '07-version-path-error'
    $targetInput.SetValue('')
    Assert-Destination 'VersionTargetHelp' 'mods'
    Assert-FormReady
    Assert-FixedAction $filesSave
    Capture '08-version-folder-ready'
    Invoke-Control 'Save'
    $pointer = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'RHI-CS/bootstrap.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $savedState = Get-Content -LiteralPath (Join-Path $pointer.Root 'workspace.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $savedVersion = @($savedState.Libraries.Versions | Where-Object { $_.Label -eq 'Prepared offline folder' })
    if ($savedVersion.Count -ne 1 -or $savedVersion[0].Kind -notin @('Files', 0) -or $null -ne $savedVersion[0].TargetOverride) {
        throw 'New folder version was not saved as ordinary files following the library setting.'
    }
    Invoke-LibraryControl 'LibraryEdit'
    (Find-Control -id 'LibraryTargetInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($originalLibraryTarget)
    Invoke-Control 'Save'
    Invoke-Control 'Games'
    Invoke-Control 'Deploy'
    Assert-IniPreview
    Assert-ActionOrder 'ConfirmExecute' | Out-Null
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
    Invoke-Control 'Settings'
    $language = Find-Control -id 'SettingsLanguage' -Enabled
    Capture '09-settings-zh'
    $language.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    (Find-Control -name (Label 'en-US')).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-Control 'Save'
    $labels = [xml][IO.File]::ReadAllText((Join-Path $appRoot 'Strings/en-US/Resources.resw'), [Text.Encoding]::UTF8)
    Find-Control -name (Label 'Settings') | Out-Null
    Capture '10-settings-en'
    Invoke-Control 'Libraries'
    Invoke-LibraryControl 'LibraryAddVersion'
    $englishSave = Assert-ActionOrder 'Save'
    Assert-NoResourceType
    Assert-FixedAction $englishSave
    (Find-Control -id 'VersionLabelInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('1.0')
    (Find-Control -id 'VersionSourceInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue([IO.Path]::GetDirectoryName($legacySource))
    Assert-Destination 'VersionTargetHelp' $originalLibraryTarget
    Assert-FormReady
    Capture '11-version-files-en'
    Scroll-DialogToEnd 'Save' -IfNeeded
    Assert-FixedAction $englishSave
    Capture '13-version-files-en-scrolled'
    Invoke-Control 'Cancel'
    Invoke-Control 'Games'
    if ((Find-Control -id 'GamesSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'CI Game') {
        throw 'Search query was lost during language switching.'
    }
    Invoke-Control 'Deploy'
    Assert-IniPreview
    $englishConfirm = Assert-ActionOrder 'ConfirmExecute'
    Capture '12-reshade-preflight-en'
    Scroll-DialogToEnd 'ConfirmExecute' -IfNeeded
    Assert-FixedAction $englishConfirm 'ConfirmExecute'
    Capture '14-reshade-preflight-en-scrolled'
    Invoke-Control 'ConfirmExecute'
    $clearAction = Find-Control -name (Label 'ClearDeployment') -Action -IncludeOffscreen
    if ($clearAction.Current.IsOffscreen) {
        (Find-Control -id 'GamesScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 100)
        Start-Sleep -Milliseconds 250
    }
    Find-Control -name (Label 'ClearDeployment') -Action | Out-Null
    Find-Control -name (Label 'Installed') | Out-Null
    Capture '15-reshade-deployed-en'
    Invoke-Control 'ClearDeployment'
    Assert-ActionOrder 'ConfirmExecute' | Out-Null
    Invoke-Control 'ConfirmExecute'
    Find-Control -name (Label 'Deploy') -Action | Out-Null
    Find-Control -name (Label 'NotDeployed') | Out-Null
    Capture '16-reshade-cleared-en'
    [IO.File]::WriteAllText((Join-Path $captureRoot 'result.txt'), 'PASS: ordinary folder versions without a type selector; contextual source/destination guidance; saved Files kind; legacy record editing and INI preview; fixed dialog actions; search retention; Chinese/English switching; legacy deploy/clear. Screenshots require visual review; fixture binaries are synthetic and never executed.', [Text.Encoding]::UTF8)
} catch {
    $failure = $_
    Write-Warning "$failure`n$($failure.ScriptStackTrace)"
    try { Capture 'failure' } catch { Write-Warning $_ }
    $diagnostic = @(
        "Failure: $failure", "$($failure.ScriptStackTrace)",
        "Interactive: $([Environment]::UserInteractive)",
        "Process ID: $($process.Id)",
        "Exited: $($process.HasExited)",
        "Exit code: $($process.ExitCode)",
        "Launch: $launchTime"
    )
    foreach ($stream in @('stdout.txt', 'stderr.txt')) {
        $streamPath = Join-Path $captureRoot $stream
        if (Test-Path -LiteralPath $streamPath) {
            try {
                $reader = [IO.StreamReader]::new([IO.File]::Open($streamPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite))
                try { $diagnostic += $reader.ReadToEnd() } finally { $reader.Dispose() }
            } catch { $diagnostic += "Stream unavailable: $stream $_" }
        }
    }
    try {
        $diagnostic += Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$launchTime.AddSeconds(-5)} -ErrorAction Stop |
            Where-Object { $_.Message -match 'RHI-CS|Microsoft.UI.Xaml|WinUI3Localizer|WindowsAppRuntime' } |
            Select-Object -First 15 | ForEach-Object { "$($_.ProviderName): $($_.Message)" }
    } catch { $diagnostic += "Event log: $_" }
    [IO.File]::WriteAllLines((Join-Path $captureRoot 'startup-diagnostics.txt'), [string[]]$diagnostic, [Text.Encoding]::UTF8)
    $diagnostic | Write-Host
    throw $failure
} finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
}
