# Geometry and interaction checks for the 1530417 layout regression.
# UIA rectangles may ignore WinUI clipping, so use native bounds, hit tests,
# real pointer clicks, and screenshots. Never treat a ScrollViewer peer as a viewport.
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Disposable CI harness required.' }
function Assert-LayoutHitArea($control) {
    $box = $control.Current.BoundingRectangle
    $rect = [SmokeWindow+Rect]::new()
    if (-not [SmokeWindow]::GetWindowRect($process.MainWindowHandle, [ref]$rect)) { throw 'Native window bounds unavailable.' }
    if ($box.Width -le 0 -or $box.Height -le 0 -or $box.Left -lt $rect.Left -or $box.Right -gt $rect.Right -or
        $box.Top -lt $rect.Top -or $box.Bottom -gt $rect.Bottom) { throw "Control outside native window: $($control.Current.Name)" }
    foreach ($x in @(($box.Left + 4), ($box.Right - 4))) {
        foreach ($y in @(($box.Top + 4), ($box.Bottom - 4))) {
            $hit = [Windows.Automation.AutomationElement]::FromPoint([Windows.Point]::new($x, $y))
            $found = $false
            while ($hit) {
                if ([Windows.Automation.Automation]::Compare($hit, $control)) { $found = $true; break }
                $hit = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($hit)
            }
            if (-not $found) { throw "Control clipped or occluded: $($control.Current.Name) at $x,$y" }
        }
    }
}
function Assert-ContentSizedFormActions([string]$action) {
    $confirm = Assert-ActionOrder $action
    $cancel = Find-Control -name (Label 'Cancel') -Action
    $submit = Find-Control -name (Label $action) -Action
    Assert-LayoutHitArea $cancel
    Assert-LayoutHitArea $submit
    if ($cancel.Current.BoundingRectangle.Right -ge $confirm.Left) { throw 'Form actions overlap.' }
}
function Click-LayoutPoint([double]$x, [double]$y) {
    [Windows.Forms.Cursor]::Position = [Drawing.Point]::new([int]$x, [int]$y)
    [SmokeWindow]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    [SmokeWindow]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
}
function Invoke-BaselineRootLayout {
    # Called with the first-library root dialog open. These paths are synthetic.
    $field = Find-Control -id 'FirstLibraryRootInput'
    $value = $field.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    $original = $value.Current.Value
    $longPath = 'C:\合成测试\示例资源集合与多行中文路径验证\仅用于自动化回归的目录\离线素材资源\目录换行末尾'
    $longHeight = 0
    foreach ($phase in @('long', 'short', 'long-again')) {
        $value.SetValue($(if ($phase -eq 'short') { 'C:\test' } else { $longPath }))
        Start-Sleep -Milliseconds 400
        $field = Find-Control -id 'FirstLibraryRootInput'
        $box = $field.Current.BoundingRectangle
        $browse = Find-Control -id 'FirstLibraryRootBrowse'
        $browseBox = $browse.Current.BoundingRectangle
        $footer = (Find-Control -name (Label 'Cancel') -Action).Current.BoundingRectangle
        if ($box.Right -ge $browseBox.Left -or $box.Bottom -gt $footer.Top -or $browseBox.Bottom -gt $footer.Top) {
            throw 'Root path, browse action, or footer overlaps.'
        }
        $scale = [SmokeWindow]::GetDpiForWindow($process.MainWindowHandle) / 96.0
        $tailGap = $footer.Top - [Math]::Max($box.Bottom, $browseBox.Bottom)
        if ($tailGap -gt 64 * $scale) { throw "Empty root form still reserves excessive feedback space: $tailGap" }
        if ($phase -eq 'long') { $longHeight = $box.Height }
        elseif ($phase -eq 'short' -and $longHeight -le $box.Height) { throw 'Long-path fixture did not wrap.' }
        # Defocus then click the last text line. UIA SetFocus/Invoke alone would
        # falsely succeed even if this line were outside the visual clipping area.
        (Find-Control -name (Label 'Cancel') -Action).SetFocus()
        Click-LayoutPoint ($box.Left + 20) ($box.Bottom - 8)
        $focus = [Windows.Automation.AutomationElement]::FocusedElement
        $focused = $false
        while ($focus) {
            if ($focus.Current.AutomationId -eq 'FirstLibraryRootInput') { $focused = $true; break }
            $focus = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($focus)
        }
        if (-not $focused) { throw 'Pointer cannot reach the final wrapped path line.' }
        Assert-LayoutHitArea $browse
        Assert-ContentSizedFormActions 'SaveAndContinue'
        Capture ("baseline-root-" + $phase)
    }
    $value.SetValue($original)
    Start-Sleep -Milliseconds 300
}
