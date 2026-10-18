# Windows display settings on the disposable hosted runner only. No registry hacks.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Visual) { throw 'CI visual harness required.' }
$scaleChanged = $false
$scaleBox = $null
$originalScale = $null
function Display-Items {
    $pidCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $settingsProcess.Id)
    return [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants, $pidCondition)
}
try {
    Start-Process 'ms-settings:display' -WindowStyle Hidden
    Start-Sleep -Seconds 3
    $settingsProcess = Get-Process SystemSettings -ErrorAction Stop | Select-Object -First 1
    $nodes = @(Display-Items)
    $evidence.Add('Display controls: ' + (($nodes | ForEach-Object { "$($_.Current.ControlType.ProgrammaticName): $($_.Current.Name) [$($_.Current.AutomationId)]" }) -join ' | '))
    $scaleBox = $nodes | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::ComboBox -and
        ($_.Current.Name -match '(?i)scale' -or $_.Current.AutomationId -match '(?i)scale') -and $_.Current.IsEnabled } | Select-Object -First 1
    if ($null -eq $scaleBox) { throw 'No enabled display scale selector is exposed by this runner.' }
    $selection = $scaleBox.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).GetCurrentSelection()
    if ($selection.Count -ne 1) { throw 'Cannot determine original display scale.' }
    $originalScale = $selection[0].Current.Name
    foreach ($percent in @(150, 200)) {
        $scaleBox.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 300
        $option = @(Display-Items) | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::ListItem -and
            $_.Current.Name -match "^$percent\s*%" } | Select-Object -First 1
        if ($null -eq $option) { throw "Display scale $percent percent is unavailable." }
        $option.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $scaleChanged = $true
        Start-Sleep -Seconds 2
        $actual = Record-Dpi "requested-$percent-percent"
        if ($actual -ne (96 * $percent / 100)) { throw "Scale selector did not change application DPI to $percent percent." }
        $area = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        [SmokeWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero, $area.X, $area.Y, $area.Width, $area.Height, 0x0040) | Out-Null
        Set-Appearance 'Dark' 'en-US'
        Check-Pages "dpi-$actual-Dark"
        Set-Appearance 'Light' 'zh-CN'
        Check-Pages "dpi-$actual-Light"
    }
} catch {
    if ($scaleChanged) { throw }
    $evidence.Add("HIGH DPI UNVERIFIED: $_")
    Write-Warning "High DPI environment unavailable: $_"
} finally {
    if ($scaleChanged -and $null -ne $scaleBox) {
        $scaleBox.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        $original = @(Display-Items) | Where-Object { $_.Current.Name -eq $originalScale -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::ListItem } | Select-Object -First 1
        if ($null -eq $original) { throw 'Cannot restore original runner display scale.' }
        $original.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    }
    [SmokeWindow]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
}
