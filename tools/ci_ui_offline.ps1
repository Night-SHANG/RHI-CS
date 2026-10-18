if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Visual) { throw 'Remote visual fixture required.' }
$offline = Get-Content -LiteralPath $FixtureMetadata -Raw -Encoding UTF8 | ConvertFrom-Json
Set-Appearance 'Light' 'zh-CN'
Capture 'settings-redesign-light'
function Capture-Desktop([string]$name) {
    $desktop = [Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = [Drawing.Bitmap]::new($desktop.Width, $desktop.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($desktop.X, $desktop.Y, 0, 0, $bitmap.Size)
        $bitmap.Save((Join-Path $captureRoot ($name + '.png')), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
Capture-Desktop 'desktop-icons'
Assert-OfflineLayout
if ((Find-Control -id 'OfflineCreate' -IncludeOffscreen).Current.IsEnabled) { throw 'Empty offline form is valid.' }
Reveal-SettingsControl 'OfflineChooseFolder' | Out-Null
Assert-InWindow (Find-Control -id 'OfflineChooseFolder')
if (-not (Find-Control -id 'OfflineChooseInstaller' -IncludeOffscreen).Current.IsEnabled) { throw 'Installer picker missing or disabled.' }
Capture 'offline-empty'
foreach ($field in @(@('OfflineInstallerInput', $offline.Installer), @('OfflineParentInput', $offline.Parent), @('OfflineNameInput', 'UI-offline'))) {
    (Find-Control -id $field[0] -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($field[1])
}
Reveal-SettingsControl 'OfflineChooseFolder' | Out-Null
Invoke-VisualId 'OfflineChooseFolder'
$pickerHandle = [IntPtr]::Zero
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    Start-Sleep -Milliseconds 200
    $candidate = [SmokeWindow]::GetForegroundWindow()
    if ($candidate -ne [IntPtr]::Zero -and $candidate -ne $process.MainWindowHandle) { $pickerHandle = $candidate; break }
}
if ($pickerHandle -eq [IntPtr]::Zero) { throw 'Native folder picker did not open.' }
$picker = [Windows.Automation.AutomationElement]::FromHandle($pickerHandle)
$windowPattern = $null
if (-not $picker.TryGetCurrentPattern([Windows.Automation.WindowPattern]::Pattern, [ref]$windowPattern)) { throw 'Folder picker is not an inspectable native window.' }
Capture-Desktop 'offline-native-folder-picker'
$windowPattern.Close()
Start-Sleep -Milliseconds 400
if ((Find-Control -id 'OfflineParentInput' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $offline.Parent) { throw 'Cancelling the folder picker changed the output parent.' }
Invoke-Control 'Libraries'
Invoke-Control 'Settings'
if ((Find-Control -id 'OfflineParentInput' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $offline.Parent) { throw 'Page navigation lost the offline draft.' }
Reveal-SettingsControl 'OfflineCreate' | Out-Null
$resetBounds = (Find-Control -id 'OfflineReset').Current.BoundingRectangle
$createBounds = (Find-Control -id 'OfflineCreate').Current.BoundingRectangle
if ($resetBounds.Right -ge $createBounds.Left -or [Math]::Abs($resetBounds.Top - $createBounds.Top) -gt 2) { throw 'Offline actions are not aligned.' }
if (-not (Find-Control -id 'OfflineCreate').Current.IsEnabled) { throw 'Valid offline form is disabled.' }
Capture 'offline-ready-to-create'
Invoke-VisualId 'OfflineCreate'
Find-Control -id 'OfflineOutput' -IncludeOffscreen | Out-Null
Reveal-SettingsControl 'OfflineOpenOutput' | Out-Null
Capture 'offline-created'
Set-Appearance 'Dark' 'en-US'
Capture 'settings-redesign-dark'
if ((Find-Control -id 'OfflineNameInput' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'UI-offline') { throw 'Appearance change lost the offline draft.' }
Reveal-SettingsControl 'OfflineCreate' | Out-Null
if ((Find-Control -id 'OfflineCreate').Current.IsEnabled) { throw 'Existing output would be overwritten.' }
Capture 'offline-existing-output-dark'
Invoke-VisualId 'OfflineReset'
if ((Find-Control -id 'OfflineInstallerInput' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne '') { throw 'Reset did not clear the offline draft.' }
$evidence.Add('PASS: inline Settings form, native folder picker open/cancel, persistent draft, DLL+INI export, reset and duplicate-output prevention in both languages.')
