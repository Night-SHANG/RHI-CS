if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Visual) { throw 'Remote visual fixture required.' }
$offline = Get-Content -LiteralPath $FixtureMetadata -Raw -Encoding UTF8 | ConvertFrom-Json
function Assert-OfflineTextCount([string]$key, [int]$expected) {
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, (Label $key)),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Text))
    $offlineWindow = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $count = $offlineWindow.FindAll([Windows.Automation.TreeScope]::Descendants, $condition).Count
    if ($count -ne $expected) { throw "Offline text count for ${key}: expected $expected, found $count." }
}
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
$picker = $null
$pickerCancel = $null
for ($attempt = 0; $attempt -lt 50; $attempt++) {
    Start-Sleep -Milliseconds 200
    $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Window))
    foreach ($candidate in $windows) {
        if ($candidate.Current.IsOffscreen -or $candidate.Current.NativeWindowHandle -eq $process.MainWindowHandle.ToInt64()) { continue }
        $buttons = $candidate.FindAll([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button))
        $selectFolder = @($buttons | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -match '^(Select Folder|Select folder|Choose folder|\u9009\u62e9\u6587\u4ef6\u5939|\u9009\u62e9\u76ee\u5f55)$' })
        $cancelFolder = @($buttons | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -match '^(Cancel|\u53d6\u6d88)$' })
        if ($selectFolder.Count -gt 0 -and $cancelFolder.Count -gt 0) { $picker = $candidate; $pickerCancel = $cancelFolder[0]; break }
    }
    if ($null -ne $picker) { break }
}
if ($null -eq $picker) {
    Capture-Desktop 'offline-folder-picker-not-found'
    $evidence.Add('Native windows: ' + (($windows | ForEach-Object { "$($_.Current.Name) [$($_.Current.ClassName)]" }) -join ' | '))
    throw 'Native folder picker with selection and cancel buttons did not open.'
}
[SmokeWindow]::SetForegroundWindow([IntPtr]$picker.Current.NativeWindowHandle) | Out-Null
Start-Sleep -Milliseconds 500
Capture-Desktop 'offline-native-folder-picker'
$pickerCancel.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
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
$offlineIdentity = (Find-Control -id 'OfflineInstallerInput' -IncludeOffscreen).GetRuntimeId() -join ','
$offlineNavigation = (Find-Control -id 'SettingsNavOffline' -IncludeOffscreen).GetRuntimeId() -join ','
$offlineActionOffset = $createBounds.Top - (Find-Control -id 'OfflinePreparationCard' -IncludeOffscreen).Current.BoundingRectangle.Top
Invoke-VisualId 'OfflineCreate'
Find-Control -id 'OfflineOutput' -IncludeOffscreen | Out-Null
if (((Find-Control -id 'OfflineInstallerInput' -IncludeOffscreen).GetRuntimeId() -join ',') -ne $offlineIdentity -or
    ((Find-Control -id 'SettingsNavOffline' -IncludeOffscreen).GetRuntimeId() -join ',') -ne $offlineNavigation) { throw 'Offline creation rebuilt the form or Settings navigation.' }
Reveal-SettingsControl 'OfflineOpenOutput' | Out-Null
Assert-OfflineTextCount 'OfflineReady' 1
Assert-OfflineTextCount 'OfflineOutputExists' 0
$completedActionOffset = (Find-Control -id 'OfflineCreate' -IncludeOffscreen).Current.BoundingRectangle.Top - (Find-Control -id 'OfflinePreparationCard' -IncludeOffscreen).Current.BoundingRectangle.Top
if ([Math]::Abs($offlineActionOffset - $completedActionOffset) -gt 2) { throw 'Success feedback moved the offline actions within the card.' }
Capture 'offline-created'
Set-Appearance 'Dark' 'en-US'
Capture 'settings-redesign-dark'
Select-SettingsSection 'Offline'
Assert-OfflineTextCount 'OfflineReady' 1
Reveal-SettingsControl 'OfflineOpenOutput' | Out-Null
Capture 'offline-created-dark-en'
if ((Find-Control -id 'OfflineNameInput' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'UI-offline') { throw 'Appearance change lost the offline draft.' }
Reveal-SettingsControl 'OfflineReset' | Out-Null
Invoke-VisualId 'OfflineReset'
Assert-OfflineTextCount 'OfflineReady' 0
foreach ($field in @(@('OfflineInstallerInput', $offline.Installer), @('OfflineParentInput', $offline.Parent), @('OfflineNameInput', 'UI-offline'))) {
    (Find-Control -id $field[0] -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($field[1])
}
Reveal-SettingsControl 'OfflineCreate' | Out-Null
if ((Find-Control -id 'OfflineCreate').Current.IsEnabled) { throw 'Existing output would be overwritten.' }
if ((Find-Control -id 'OfflineValidation' -IncludeOffscreen).Current.Name -ne (Label 'OfflineOutputExists')) { throw 'Existing output conflict is not explained.' }
Assert-OfflineTextCount 'OfflineOutputExists' 1
Assert-OfflineTextCount 'OfflineReady' 0
Capture 'offline-existing-output-dark'
Invoke-VisualId 'OfflineReset'
if ((Find-Control -id 'OfflineInstallerInput' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne '') { throw 'Reset did not clear the offline draft.' }
$evidence.Add('PASS: inline Settings form, native folder picker open/cancel, persistent draft, DLL+INI export, one success message, stable action position, reset and duplicate-output prevention in both languages.')
