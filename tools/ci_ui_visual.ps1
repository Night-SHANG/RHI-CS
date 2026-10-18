# Shared native UI harness; no application test hooks or simulated DPI scaling.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Visual) { throw 'CI visual harness required.' }
$evidence = [Collections.Generic.List[string]]::new()
function Select-Option([string]$id, [string]$key) {
    (Find-Control -id $id -Enabled).GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    (Find-Control -name (Label $key)).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
}
function Set-Appearance([string]$theme, [string]$language) {
    Invoke-Control 'Settings'
    Select-Option 'SettingsTheme' $theme
    Select-Option 'SettingsLanguage' $language
    Invoke-Control 'Save'
    $script:labels = [xml][IO.File]::ReadAllText((Join-Path $appRoot "Strings/$language/Resources.resw"), [Text.Encoding]::UTF8)
    Find-Control -id 'SettingsTheme' -Enabled | Out-Null
}
function Invoke-VisualId([string]$id) {
    (Find-Control -id $id -Enabled).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 250
}
function Assert-InWindow($node) {
    $rect = [SmokeWindow+Rect]::new()
    [SmokeWindow]::GetWindowRect($process.MainWindowHandle, [ref]$rect) | Out-Null
    $bounds = $node.Current.BoundingRectangle
    if ($node.Current.IsOffscreen -or $bounds.Width -le 0 -or $bounds.Left -lt $rect.Left -or
        $bounds.Right -gt $rect.Right -or $bounds.Top -lt $rect.Top -or $bounds.Bottom -gt $rect.Bottom) {
        throw "Control clipped outside window: $($node.Current.Name) $bounds"
    }
}
function Record-Dpi([string]$phase) {
    $dpi = [SmokeWindow]::GetDpiForWindow($process.MainWindowHandle)
    $awareness = [SmokeWindow]::GetAwarenessFromDpiAwarenessContext([SmokeWindow]::GetWindowDpiAwarenessContext($process.MainWindowHandle))
    if ($awareness -ne 2 -or $dpi -lt 96) { throw "Unexpected DPI awareness: $awareness / $dpi" }
    $evidence.Add("$phase : actual DPI=$dpi; per-monitor awareness=$awareness")
    return $dpi
}
function Reveal-SettingsControl([string]$id) {
    Start-Sleep -Milliseconds 300
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        $node = Find-Control -id $id -IncludeOffscreen
        $viewer = Find-Control -id 'SettingsScroll'
        $bounds = $node.Current.BoundingRectangle
        $viewport = $viewer.Current.BoundingRectangle
        if ($viewport.IsEmpty -or $viewport.Height -le 0) { Start-Sleep -Milliseconds 200; continue }
        if (-not $bounds.IsEmpty -and -not $node.Current.IsOffscreen -and $bounds.Height -gt 0 -and $bounds.Top -ge $viewport.Top -and $bounds.Bottom -le $viewport.Bottom) { return $node }
        $scroll = $viewer.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
        if ($scroll.Current.VerticallyScrollable) {
            $viewSize = $scroll.Current.VerticalViewSize
            if ($viewSize -le 0 -or $viewSize -ge 100) { throw "Invalid Settings viewport size: $viewSize" }
            $position = $scroll.Current.VerticalScrollPercent
            if ($bounds.IsEmpty -or [double]::IsInfinity($bounds.Top) -or [double]::IsInfinity($bounds.Bottom)) {
                # A refreshed/offscreen peer can expose Rect.Empty until it is laid out in the viewport.
                $requested = if ($position -ge 100) { 0.0 } else { $position + 10.0 }
            } else {
                $travel = $viewport.Height * (100 / $viewSize - 1)
                $delta = if ($bounds.Top -lt $viewport.Top) { $bounds.Top - $viewport.Top - 8 } else { $bounds.Bottom - $viewport.Bottom + 8 }
                $requested = $position + 100 * $delta / $travel
            }
            $target = [Math]::Max(0.0, [Math]::Min(100.0, [double]$requested))
            $scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, $target)
        }
        Start-Sleep -Milliseconds 150
    }
    throw "Settings control is not fully visible: $id"
}
function Assert-OfflineLayout {
    $left = (Find-Control -id 'SettingsGeneralTitle' -IncludeOffscreen).Current.BoundingRectangle
    $right = (Find-Control -id 'OfflineTitle' -IncludeOffscreen).Current.BoundingRectangle
    $dpiScale = [SmokeWindow]::GetDpiForWindow($process.MainWindowHandle) / 96.0
    $width = (Find-Control -id 'SettingsScroll').Current.BoundingRectangle.Width / $dpiScale
    if ($width -ge 1016 -and ($right.Left -le $left.Right -or [Math]::Abs($right.Top - $left.Top) -gt 2)) { throw 'Wide Settings must have top-aligned left settings and right offline form.' }
    if ($width -ge 1016) {
        $viewport = (Find-Control -id 'SettingsScroll').Current.BoundingRectangle
        $general = (Find-Control -id 'SettingsGeneralCard' -IncludeOffscreen).Current.BoundingRectangle
        $offlineCard = (Find-Control -id 'OfflinePreparationCard' -IncludeOffscreen).Current.BoundingRectangle
        $evidence.Add("Settings horizontal bounds: viewport=$viewport; general=$general; offline=$offlineCard")
        foreach ($card in @($general, $offlineCard)) {
            if ($card.Left -lt $viewport.Left - 2 -or $card.Right -gt $viewport.Right + 2) {
                throw 'Settings card extends outside its horizontal viewport.'
            }
        }
        # UIA may report a clipped rectangle as visible; verify the complete planned column ratio too.
        if ([Math]::Abs($offlineCard.Width - $general.Width * 540 / 780) -gt 3 * $dpiScale) {
            throw 'Settings card is clipped or no longer follows the planned 780:540 column ratio.'
        }
    }
    if ($width -le 992) {
        # Offscreen peers may not expose usable bounds. Bring the lower card into view before comparing alignment.
        $right = (Reveal-SettingsControl 'OfflineTitle').Current.BoundingRectangle
        if ([Math]::Abs($right.Left - $left.Left) -gt 2) { throw 'Narrow Settings must align the stacked cards.' }
        $scroll = (Find-Control -id 'SettingsScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
        $scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 0)
        Start-Sleep -Milliseconds 200
    }
}
function Check-Pages([string]$prefix) {
    Assert-OfflineLayout
    Capture "$prefix-settings"
    Reveal-SettingsControl 'OfflineChooseFolder' | Out-Null
    Assert-InWindow (Find-Control -id 'OfflineChooseFolder')
    Reveal-SettingsControl 'OfflineCreate' | Out-Null
    Assert-InWindow (Find-Control -id 'OfflineReset')
    Assert-InWindow (Find-Control -id 'OfflineCreate')
    Capture "$prefix-offline-form"
    Invoke-Control 'Games'
    Assert-InWindow (Find-Control -id 'GameEdit')
    Assert-InWindow (Find-Control -id 'BatchPreflight')
    Capture "$prefix-games"
    $hover = (Find-Control -id 'GameEdit').Current.BoundingRectangle
    [Windows.Forms.Cursor]::Position = [Drawing.Point]::new([int]($hover.X + $hover.Width / 2), [int]($hover.Y + $hover.Height / 2))
    Capture "$prefix-hover"
    Invoke-Control 'Libraries'
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $offlineId = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'OfflineInstallerInput')
    if ($window.FindFirst([Windows.Automation.TreeScope]::Descendants, $offlineId)) { throw 'Offline creation belongs in Settings only.' }
    $searchBounds = (Find-Control -id 'LibrariesSearch').Current.BoundingRectangle
    $addBounds = (Find-Control -id 'AddLibrary').Current.BoundingRectangle
    if ([Math]::Abs($searchBounds.Left - $addBounds.Left) -gt 2 -or [Math]::Abs($searchBounds.Width - $addBounds.Width) -gt 2) {
        throw 'Library action is not aligned with the search field.'
    }
    Capture "$prefix-library"
    Invoke-Control 'AddLibrary'
    $before = Assert-ActionOrder 'Save'
    if ((Find-Control -name (Label 'Save') -Action).Current.IsEnabled) { throw 'Empty library form is valid.' }
    Capture "$prefix-invalid"
    (Find-Control -id 'LibraryNameInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Visual validation')
    (Find-Control -id 'LibraryTargetInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('F:\Resources\OfflineReShade')
    Assert-FormError 'TargetFolderAbsolute'
    Assert-FixedAction $before
    Capture "$prefix-library-path-error"
    (Find-Control -id 'LibraryTargetInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('assets')
    Assert-FixedAction $before
    if (-not (Find-Control -name (Label 'Save') -Action).Current.IsEnabled) { throw 'Valid form remains disabled.' }
    Assert-FormReady
    Capture "$prefix-valid"
    Invoke-Control 'Cancel'
    Invoke-VisualId 'LibraryAddVersion'
    $before = Assert-ActionOrder 'Save'
    Assert-NoResourceType
    Assert-FixedAction $before
    Capture "$prefix-version-source"
    Scroll-DialogToEnd 'Save' -IfNeeded
    Capture "$prefix-version-destination"
    Assert-FixedAction $before
    Invoke-Control 'Cancel'
    $query = (Find-Control -id 'LibrariesSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    $query.SetValue('no-match-visual-check')
    Find-Control -name (Label 'NoMatches') | Out-Null
    Capture "$prefix-empty"
    $query.SetValue('')
    Invoke-Control 'Games'
    $deploy = Find-Control -name (Label 'Deploy') -Action -IncludeOffscreen
    $gameScroll = (Find-Control -id 'GamesScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
    for ($step = 0; $step -lt 15 -and $deploy.Current.IsOffscreen; $step++) {
        $gameScroll.Scroll([Windows.Automation.ScrollAmount]::NoAmount, [Windows.Automation.ScrollAmount]::SmallIncrement)
        Start-Sleep -Milliseconds 150
    }
    if ($deploy.Current.IsOffscreen) { throw 'Deployment preview cannot be reached by scrolling.' }
    $deploy.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Find-Control -name (Label 'Preflight') | Out-Null
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button))
    $buttons = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)
    $action = @('ConfirmExecute', 'BackupAndExecute', 'ProtectAndExecute') | Where-Object {
        $name = Label $_
        @($buttons | Where-Object { $_.Current.Name -eq $name -and -not $_.Current.IsOffscreen }).Count -gt 0
    } | Select-Object -First 1
    if (-not $action) { throw 'Preflight action is missing.' }
    $before = Assert-ActionOrder $action
    Assert-InWindow (Find-Control -name (Label $action) -Action)
    Capture "$prefix-preflight"
    Scroll-DialogToEnd $action -IfNeeded
    Assert-FixedAction $before $action
    Capture "$prefix-preflight-scrolled"
    Invoke-Control 'Cancel'
}

Record-Dpi 'initial' | Out-Null
. (Join-Path $PSScriptRoot 'ci_ui_display.ps1') -PrepareWideOnly
. (Join-Path $PSScriptRoot 'ci_ui_offline.ps1')
foreach ($language in @('zh-CN', 'en-US')) {
    foreach ($theme in @('Light', 'Dark')) {
        Set-Appearance $theme $language
        Check-Pages "$language-$theme"
    }
}
Set-Appearance 'System' 'en-US'
Capture 'en-US-System-settings'
Set-Appearance 'Dark' 'en-US'
# Test a smaller real window, explicitly recorded as a layout check, not high DPI.
[SmokeWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero, $screen.X, $screen.Y,
    [Math]::Min(800, $screen.Width), [Math]::Min(600, $screen.Height), 0x0040) | Out-Null
Start-Sleep -Milliseconds 300
Invoke-Control 'Settings'
Assert-OfflineLayout
Capture 'compact-dark-settings-top'
Reveal-SettingsControl 'OfflineChooseFolder' | Out-Null
Capture 'compact-dark-settings-offline'
Reveal-SettingsControl 'OfflineCreate' | Out-Null
Assert-InWindow (Find-Control -id 'OfflineReset')
Capture 'compact-dark-settings-actions'
Invoke-Control 'Libraries'
Invoke-Control 'AddLibrary'
Assert-InWindow (Find-Control -name (Label 'Save') -Action)
Assert-InWindow (Find-Control -name (Label 'Cancel') -Action)
Capture 'compact-dark-form'
$before = Assert-ActionOrder 'Save'
(Find-Control -id 'LibraryNameInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Compact validation')
Assert-FixedAction $before
Invoke-Control 'Cancel'
Capture 'compact-dark-library'
$evidence.Add('PASS: Chinese/English x Light/Dark pages, disabled/valid forms, fixed actions, ReShade expansion, empty search, hover, System selection, compact window.')
# Inspect the runner display UI and only claim high DPI if GetDpiForWindow confirms it.
. (Join-Path $PSScriptRoot 'ci_ui_display.ps1')
[IO.File]::WriteAllLines((Join-Path $captureRoot 'visual-result.txt'), $evidence, [Text.Encoding]::UTF8)
