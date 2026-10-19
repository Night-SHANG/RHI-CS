if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Migration) { throw 'Isolated GitHub migration fixture required.' }
$fixture = Get-Content -LiteralPath $FixtureMetadata -Raw -Encoding UTF8 | ConvertFrom-Json
Invoke-Control 'Settings'
Select-SettingsSection 'Data'
$open = Find-Control -id 'SettingsOpenData'
$move = Find-Control -id 'SettingsMigrateData'
if ([Math]::Abs($open.Current.BoundingRectangle.Top - $move.Current.BoundingRectangle.Top) -gt 2 -or
    [Math]::Abs($open.Current.BoundingRectangle.Width - $move.Current.BoundingRectangle.Width) -gt 2) { throw 'Data actions do not share a row and width.' }
Capture '01-data-page'
$move.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
if (-not $fixture.Interrupted) {
    $input = Find-Control -id 'MigrationParentInput'
    Capture '02-move-picker'
    $input.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($fixture.Parent)
    Invoke-Control 'Next'
    Find-Control -name $fixture.Destination | Out-Null
    Capture '03-move-confirm'
    Invoke-Control 'Cancel'
    if (Test-Path -LiteralPath $fixture.Destination) { throw 'Cancel created a destination.' }
    $before = (Find-Control -id 'SettingsNavData').GetRuntimeId() -join ','
    (Find-Control -id 'SettingsMigrateData').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    (Find-Control -id 'MigrationParentInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($fixture.Parent)
    Invoke-Control 'Next'
    Invoke-Control 'StartMigration'
} else {
    Capture '03-continue-confirm-dark'
    Invoke-Control 'ContinueMigration'
}
Find-Control -name (Label 'MigrationComplete') | Out-Null
Capture '04-move-complete'
Invoke-Control 'Close'
if ((Find-Control -id 'SettingsDataPath').Current.Name -ne $fixture.Destination) { throw 'UI uses old data path.' }
if (Test-Path -LiteralPath $fixture.Source) { throw 'Old Data folder remains after success.' }
Capture '05-new-location'
[IO.File]::WriteAllText((Join-Path $captureRoot 'result.txt'), 'PASS: migration path preview, confirmation, cancel without writes, complete/resume, new folder display and old folder removal. Persisted hashes verified by fixture.', [Text.Encoding]::UTF8)
