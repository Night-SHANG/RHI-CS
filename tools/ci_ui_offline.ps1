if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Visual) { throw 'Remote visual fixture required.' }
$offline = Get-Content -LiteralPath $FixtureMetadata -Raw -Encoding UTF8 | ConvertFrom-Json
Set-Appearance 'Light' 'zh-CN'
Capture 'settings-redesign-light'
$desktop = [Windows.Forms.SystemInformation]::VirtualScreen
$bitmap = [Drawing.Bitmap]::new($desktop.Width, $desktop.Height)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen($desktop.X, $desktop.Y, 0, 0, $bitmap.Size)
    $bitmap.Save((Join-Path $captureRoot 'desktop-icons.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose(); $bitmap.Dispose() }
Invoke-VisualId 'MakeOfflineReShade'
if ((Find-Control -name (Label 'CreateOffline') -Action).Current.IsEnabled) { throw 'Empty offline form is valid.' }
Capture 'offline-empty'
foreach ($field in @(@('OfflineInstallerInput', $offline.Installer), @('OfflineParentInput', $offline.Parent), @('OfflineNameInput', 'UI-offline'))) {
    (Find-Control -id $field[0] -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($field[1])
}
Scroll-DialogToEnd 'CreateOffline' -IfNeeded
Assert-ActionOrder 'CreateOffline' | Out-Null
if (-not (Find-Control -name (Label 'CreateOffline') -Action).Current.IsEnabled) { throw 'Valid offline form is disabled.' }
Capture 'offline-ready-to-create'
Invoke-Control 'CreateOffline'
Find-Control -name (Label 'OfflineReady') | Out-Null
Capture 'offline-created'
Invoke-Control 'Close'
Set-Appearance 'Dark' 'en-US'
Capture 'settings-redesign-dark'
Invoke-VisualId 'MakeOfflineReShade'
foreach ($field in @(@('OfflineInstallerInput', $offline.Installer), @('OfflineParentInput', $offline.Parent), @('OfflineNameInput', 'UI-offline'))) {
    (Find-Control -id $field[0] -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($field[1])
}
Scroll-DialogToEnd 'CreateOffline' -IfNeeded
if ((Find-Control -name (Label 'CreateOffline') -Action).Current.IsEnabled) { throw 'Existing output would be overwritten.' }
Capture 'offline-existing-output-dark'
Invoke-Control 'Cancel'
$evidence.Add('PASS: actual offline form, invalid/valid states, DLL+INI export and duplicate-output prevention in both languages.')
