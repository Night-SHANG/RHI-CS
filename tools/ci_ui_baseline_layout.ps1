# Geometry and interaction checks for the 1530417 layout regression.
# UIA rectangles may ignore WinUI clipping, so use native bounds,
# real pointer clicks, and screenshots. Never treat a ScrollViewer peer as a viewport.
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Disposable CI harness required.' }
function Assert-LayoutNativeBounds($control) {
    $box = $control.Current.BoundingRectangle
    $rect = [SmokeWindow+Rect]::new()
    if (-not [SmokeWindow]::GetWindowRect($process.MainWindowHandle, [ref]$rect)) { throw 'Native window bounds unavailable.' }
    if ($box.Width -le 0 -or $box.Height -le 0 -or $box.Left -lt $rect.Left -or $box.Right -gt $rect.Right -or
        $box.Top -lt $rect.Top -or $box.Bottom -gt $rect.Bottom) { throw "Control outside native window: $($control.Current.Name)" }
    # Native containment is necessary but does not establish visual clipping.
    # Actual pointer/input outcomes and captured pixels are separate checks.
}
function Assert-ContentSizedFormActions([string]$action) {
    $confirm = Assert-ActionOrder $action
    $cancel = Find-Control -name (Label 'Cancel') -Action
    $submit = Find-Control -name (Label $action) -Action
    Assert-LayoutNativeBounds $cancel
    Assert-LayoutNativeBounds $submit
    if ($cancel.Current.BoundingRectangle.Right -ge $confirm.Left) { throw 'Form actions overlap.' }
}
function Click-LayoutPoint([double]$x, [double]$y) {
    [SmokeWindow]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 100
    if ([SmokeWindow]::GetForegroundWindow() -ne $process.MainWindowHandle) { throw 'Cannot foreground app for physical layout interaction.' }
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
        # A real keystroke must change this field. Do not infer success from
        # UIA focus ancestry, which is also unreliable across WinUI popup roots.
        $beforeTyping = $value.Current.Value
        [Windows.Forms.SendKeys]::SendWait('Q')
        Start-Sleep -Milliseconds 150
        $afterTyping = $value.Current.Value
        if ($afterTyping.Length -ne $beforeTyping.Length + 1 -or $afterTyping.Replace('Q', '') -ne $beforeTyping) {
            throw 'Physical input did not reach the final wrapped path line.'
        }
        $value.SetValue($beforeTyping)
        Start-Sleep -Milliseconds 150
        Assert-LayoutNativeBounds $browse
        Assert-ContentSizedFormActions 'SaveAndContinue'
        Capture ("baseline-root-" + $phase)
    }
    $value.SetValue($original)
    Start-Sleep -Milliseconds 300
}
