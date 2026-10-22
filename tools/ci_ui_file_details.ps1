# Shared native expander checks. Loaded only by the remote UI harness.
function Find-OptionalId([string]$id) {
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
function Expand-FileDetails([string]$suffix = '') {
    if ($suffix) { $node = Find-Control -id ("FileDetails_" + $suffix) }
    else {
        $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        $node = @($window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
            Where-Object { $_.Current.AutomationId.StartsWith('FileDetails_') }) | Select-Object -First 1
    }
    if (-not $node) { throw 'File details expander missing.' }
    $pattern = $node.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($pattern.Current.ExpandCollapseState -ne [Windows.Automation.ExpandCollapseState]::Collapsed) { throw 'Details must initially be collapsed.' }
    $pattern.Expand()
    Start-Sleep -Milliseconds 400
    return $node
}
function Assert-IniPreview {
    $expander = Expand-FileDetails
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $names = @($window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { $_.Current.Name }) -join "`n"
    if (-not $names.Contains('EffectSearchPaths') -or -not $names.Contains('TextureSearchPaths') -or -not $names.Contains('ReShade.ini')) {
        throw 'Expanded configuration preview must show INI path and both search path edits.'
    }
}
