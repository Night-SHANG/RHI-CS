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
    $resolution = $nodes | Where-Object { $_.Current.AutomationId -eq 'SystemSettings_Display_Resolution_ComboBox' } | Select-Object -First 1
    if ($null -ne $resolution -and $resolution.Current.IsEnabled) {
        $resolution.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 300
        $options = @(Display-Items) | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::ListItem }
        $evidence.Add('Resolution choices: ' + (($options | ForEach-Object { $_.Current.Name }) -join ' | '))
        $fullHd = $options | Where-Object { $_.Current.Name -match '^1920\D+1080' } | Select-Object -First 1
        if ($null -ne $fullHd) {
            $fullHd.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Seconds 2
            $keep = @(Display-Items) | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button -and $_.Current.Name -eq 'Keep changes' } | Select-Object -First 1
            if ($null -ne $keep) { $keep.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
            Start-Sleep -Seconds 1
        } else { $resolution.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() }
    }
    $scaleBox = $nodes | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::ComboBox -and
        ($_.Current.Name -match '(?i)scale' -or $_.Current.AutomationId -match '(?i)scale') -and $_.Current.IsEnabled } | Select-Object -First 1
    if ($null -eq $scaleBox) { throw 'No enabled display scale selector is exposed by this runner.' }
    $selection = $scaleBox.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selection.Count -ne 1) { throw 'Cannot determine original display scale.' }
    $originalScale = $selection[0].Current.Name
    foreach ($percent in @(150, 200)) {
        $scaleBox.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 300
        $scaleOptions = @(Display-Items) | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::ListItem }
        $evidence.Add('Scale choices: ' + (($scaleOptions | ForEach-Object { $_.Current.Name }) -join ' | '))
        $option = $scaleOptions | Where-Object {
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
    if ($scaleChanged -or $_.Exception.Message -notmatch 'No enabled display scale selector|Cannot determine original display scale|Display scale .* is unavailable|Cannot find a process with the name') { throw }
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
    [IO.File]::WriteAllLines((Join-Path $captureRoot 'display-evidence.txt'), $evidence, [Text.Encoding]::UTF8)
}
