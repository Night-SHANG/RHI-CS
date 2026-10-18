# Native dialog integration, executed only by the GitHub UI harness.
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'CI only.' }
$expectedFolder = [IO.Path]::GetDirectoryName($legacySource)
$beforeSource = (Find-Control -id 'VersionSourceInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
foreach ($pickerId in @('VersionChooseFolder', 'VersionChooseFile')) {
    (Find-Control -id $pickerId).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    $native = $null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 150
        $filter = [Windows.Automation.AndCondition]::new(
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty, '#32770'))
        $native = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $filter)
        if ($native) { break }
    }
    if (-not $native) { throw "Native picker did not open: $pickerId" }
    $nodes = $native.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
    $names = @($nodes | ForEach-Object { $_.Current.Name }) -join "`n"
    # The native address bar exposes its absolute location to UI Automation.
    if (-not $names.Contains($expectedFolder)) { throw "Picker did not start in library folder: $expectedFolder; $names" }
    $cancel = @($nodes | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button -and $_.Current.Name -match '^(Cancel|\u53d6\u6d88)$' }) | Select-Object -First 1
    if (-not $cancel) { throw 'Native picker cancel missing.' }
    # Capture the desktop so the owned native window is included.
    $screen = [Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = [Drawing.Bitmap]::new($screen.Width, $screen.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($screen.Left, $screen.Top, 0, 0, $screen.Size)
        $bitmap.Save((Join-Path $captureRoot ("library-" + $pickerId + '.png')), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    $cancel.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 250
    if ((Find-Control -id 'VersionSourceInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $beforeSource) {
        throw 'Cancelling native picker changed source.'
    }
}
