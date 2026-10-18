param([switch]$PrepareWideOnly)
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
    if ($PrepareWideOnly) {
        $wideArea = [SmokeWindow+Rect]::new()
        if (-not [SmokeWindow]::SystemParametersInfo(0x0030, 0, [ref]$wideArea, 0)) { throw 'Cannot read wide work area.' }
        $wideScale = [SmokeWindow]::GetDpiForWindow($process.MainWindowHandle) / 96.0
        if (($wideArea.Right - $wideArea.Left) / $wideScale -lt 1200) { throw 'Runner cannot provide a wide Settings layout.' }
        [SmokeWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero, $wideArea.Left, $wideArea.Top,
            [int][Math]::Min(1440 * $wideScale, $wideArea.Right - $wideArea.Left),
            [int][Math]::Min(960 * $wideScale, $wideArea.Bottom - $wideArea.Top), 0x0040) | Out-Null
        $evidence.Add("Wide layout: physical work area $($wideArea.Right - $wideArea.Left) x $($wideArea.Bottom - $wideArea.Top); DPI scale $wideScale")
        Start-Sleep -Milliseconds 300
        return
    }
    $scaleBox = $nodes | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::ComboBox -and
        ($_.Current.Name -match '(?i)scale' -or $_.Current.AutomationId -match '(?i)scale') -and $_.Current.IsEnabled } | Select-Object -First 1
    if ($null -eq $scaleBox) { throw 'No enabled display scale selector is exposed by this runner.' }
    $selection = $scaleBox.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selection.Count -ne 1) { throw 'Cannot determine original display scale.' }
    $originalScale = $selection[0].Current.Name
    foreach ($percent in @(125, 150, 175, 200)) {
        $scaleBox.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 300
        $scaleOptions = @(Display-Items) | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::ListItem }
        $evidence.Add('Scale choices: ' + (($scaleOptions | ForEach-Object { $_.Current.Name }) -join ' | '))
        $option = $scaleOptions | Where-Object {
            $_.Current.Name -match "^$percent\s*%" } | Select-Object -First 1
        if ($null -eq $option) {
            $evidence.Add("NOT VERIFIED: display scale $percent percent is not offered by this runner.")
            $scaleBox.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
            continue
        }
        $option.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $scaleChanged = $true
        Start-Sleep -Seconds 2
        $actual = Record-Dpi "requested-$percent-percent"
        if ($actual -ne (96 * $percent / 100)) { throw "Scale selector did not change application DPI to $percent percent." }
        $area = [SmokeWindow+Rect]::new()
        if (-not [SmokeWindow]::SystemParametersInfo(0x0030, 0, [ref]$area, 0)) { throw 'Cannot read the physical work area.' }
        $evidence.Add("Physical work area: $($area.Right - $area.Left) x $($area.Bottom - $area.Top)")
        [SmokeWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero, $area.Left, $area.Top, ($area.Right - $area.Left), ($area.Bottom - $area.Top), 0x0040) | Out-Null
        Set-Appearance 'Dark' 'en-US'
        Check-Pages "dpi-$actual-Dark"
        Set-Appearance 'Light' 'zh-CN'
        Check-Pages "dpi-$actual-Light"
        $evidence.Add("PASS: actual $actual DPI / $percent percent; Light and Dark pages, forms and preflight.")
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
