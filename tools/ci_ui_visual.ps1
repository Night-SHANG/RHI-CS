# Shared native UI harness; no application test hooks or simulated DPI scaling.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Visual) { throw 'CI visual harness required.' }
$evidence = [Collections.Generic.List[string]]::new()
. (Join-Path $PSScriptRoot 'ci_ui_resource_layout.ps1')
function Select-Option([string]$id, [string]$key) {
    (Find-Control -id $id -Enabled).GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    (Find-Control -name (Label $key)).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
}
function Set-Appearance([string]$theme, [string]$language) {
    Invoke-Control 'Settings'
    Select-SettingsSection 'General'
    Select-Option 'SettingsTheme' $theme
    Select-Option 'SettingsLanguage' $language
    Reveal-SettingsControl 'SettingsSave' | Out-Null
    Invoke-VisualId 'SettingsSave'
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
    $minimumHeight = 30 * [SmokeWindow]::GetDpiForWindow($process.MainWindowHandle) / 96.0
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        $node = Find-Control -id $id -IncludeOffscreen
        $viewer = Find-Control -id 'SettingsScroll'
        $bounds = $node.Current.BoundingRectangle
        $viewport = $viewer.Current.BoundingRectangle
        $requiredHeight = if ($node.Current.ControlType -eq [Windows.Automation.ControlType]::Button) { $minimumHeight } else { 1 }
        if ($viewport.IsEmpty -or $viewport.Height -le 0) { Start-Sleep -Milliseconds 200; continue }
        # WinUI may expose a clipped peer rectangle: one visible border is not a visible button.
        if (-not $bounds.IsEmpty -and -not $node.Current.IsOffscreen -and $bounds.Height -ge $requiredHeight -and $bounds.Top -ge $viewport.Top -and $bounds.Bottom -le $viewport.Bottom - 2) { return $node }
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
                $delta = if ($bounds.Top -lt $viewport.Top) { $bounds.Top - $viewport.Top - 8 } else { [Math]::Max($minimumHeight, $bounds.Bottom - $viewport.Bottom + 8) }
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
    Select-SettingsSection 'Offline'
    $title = (Reveal-SettingsControl 'OfflineTitle').Current.BoundingRectangle
    $nav = (Find-Control -id 'SettingsNavOffline').Current.BoundingRectangle
    $dpiScale = [SmokeWindow]::GetDpiForWindow($process.MainWindowHandle) / 96.0
    $rect = [SmokeWindow+Rect]::new()
    [SmokeWindow]::GetWindowRect($process.MainWindowHandle, [ref]$rect) | Out-Null
    $wide = ($rect.Right - $rect.Left) / $dpiScale -ge 1016
    if ($wide -and $nav.Right -ge $title.Left) { throw 'Wide Settings requires a separate left navigation.' }
    if (-not $wide -and $nav.Bottom -gt $title.Top) { throw 'Compact Settings requires navigation above the active page.' }
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    foreach ($id in @('SettingsTheme', 'ProtectionUsage', 'SettingsOpenData')) {
        $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
        if ($window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)) { throw "Inactive Settings page is still exposed: $id" }
    }
    $evidence.Add("Settings section layout: wide=$wide; nav=$nav; title=$title")
}
function Check-Pages([string]$prefix) {
    Select-SettingsSection 'General'
    Capture "$prefix-general"
    Assert-InWindow (Reveal-SettingsControl 'SettingsSave')
    Capture "$prefix-general-save"
    Select-SettingsSection 'Data'
    Assert-InWindow (Find-Control -id 'SettingsOpenData')
    Capture "$prefix-data"
    Select-SettingsSection 'Protection'
    $navBefore = (Find-Control -id 'SettingsNavProtection').Current.BoundingRectangle
    $refreshBefore = (Find-Control -id 'RefreshProtection').Current.BoundingRectangle
    $list = (Find-Control -id 'ProtectionScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
    if (-not $list.Current.VerticallyScrollable) { throw 'Long protection catalogue has no independent scrolling.' }
    $list.SetScrollPercent(-1, 0)
    Capture "$prefix-protection-top"
    $list.SetScrollPercent(-1, 100)
    Start-Sleep -Milliseconds 300
    $navAfter = (Find-Control -id 'SettingsNavProtection').Current.BoundingRectangle
    $refreshAfter = (Find-Control -id 'RefreshProtection').Current.BoundingRectangle
    if ($navBefore -ne $navAfter -or $refreshBefore -ne $refreshAfter) { throw 'Scrolling protected copies moved navigation or fixed toolbar.' }
    Capture "$prefix-protection-end"
    Select-SettingsSection 'General'
    Select-SettingsSection 'Protection'
    $list = (Find-Control -id 'ProtectionScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
    if ($list.Current.VerticalScrollPercent -lt 99) { throw 'Changing sections lost protection scroll position.' }
    $list.SetScrollPercent(-1, 0)
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
    Assert-HeaderActions 'Game'
    Assert-InWindow (Find-Control -id 'BatchPreflight')
    Capture "$prefix-games"
    Invoke-Control 'AddGame'
    Assert-FormReady
    $blankGameAction = Assert-ActionOrder 'Save'
    Capture "$prefix-game-initial"
    Invoke-Control 'Save'
    Assert-FormError 'NameRequired'
    Assert-FixedAction $blankGameAction
    Capture "$prefix-game-required"
    Invoke-Control 'Cancel'
    $hover = (Find-Control -id 'GameEdit').Current.BoundingRectangle
    [Windows.Forms.Cursor]::Position = [Drawing.Point]::new([int]($hover.X + $hover.Width / 2), [int]($hover.Y + $hover.Height / 2))
    Capture "$prefix-hover"
    Invoke-VisualId 'GameEdit'
    $apiField = Find-Control -id 'GameApiInput' -IncludeOffscreen
    $dialogViewport = (Find-Control -id 'DialogContentScroll').Current.BoundingRectangle
    if (([SmokeWindow]::GetDpiForWindow($process.MainWindowHandle) -eq 96) -and
        ($apiField.Current.BoundingRectangle.Bottom -gt $dialogViewport.Bottom -or $apiField.Current.IsOffscreen)) {
        throw 'Game API is unnecessarily hidden at normal wide-window size.'
    }
    Capture "$prefix-game-form"
    Invoke-Control 'Cancel'
    Invoke-Control 'Libraries'
    Assert-HeaderActions 'Library'
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $offlineId = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'OfflineInstallerInput')
    if ($window.FindFirst([Windows.Automation.TreeScope]::Descendants, $offlineId)) { throw 'Offline creation belongs in Settings only.' }
    $searchBounds = (Find-Control -id 'LibrariesSearch').Current.BoundingRectangle
    $addBounds = (Find-Control -id 'AddLibrary').Current.BoundingRectangle
    if ([Math]::Abs($searchBounds.Left - $addBounds.Left) -gt 2 -or [Math]::Abs($searchBounds.Width - $addBounds.Width) -gt 2) {
        throw 'Library action is not aligned with the search field.'
    }
    Capture "$prefix-library"
    $libraryScroll = (Find-Control -id 'LibraryScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
    if ($libraryScroll.Current.VerticallyScrollable) { $libraryScroll.SetScrollPercent(-1, 100) }
    Start-Sleep -Milliseconds 500
    Capture "$prefix-library-actions"
    if ($libraryScroll.Current.VerticallyScrollable) { $libraryScroll.SetScrollPercent(-1, 0) }
    Start-Sleep -Milliseconds 300
    Invoke-Control 'AddLibrary'
    $before = Assert-ActionOrder 'Save'
    Assert-FormReady
    Capture "$prefix-library-initial"
    Invoke-Control 'Save'
    Find-Control -name (Label 'NameRequired') | Out-Null
    Assert-FixedAction $before
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
    Assert-FormReady
    Capture "$prefix-version-initial"
    Invoke-Control 'Save'
    Assert-FormError 'NameRequired'
    Assert-FixedAction $before
    Capture "$prefix-version-required"
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
Invoke-Control 'Games'
Assert-HeaderActions 'Game'
Capture 'compact-dark-game-header'
Invoke-Control 'Libraries'
Assert-HeaderActions 'Library'
Capture 'compact-dark-library-header'
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
