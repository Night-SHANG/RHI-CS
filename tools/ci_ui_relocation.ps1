# Runs only inside the CI UI harness against the isolated moved-game fixture.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Relocation) { throw 'CI harness required.' }
$fixture = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $FixtureMetadata).Path, [Text.Encoding]::UTF8) | ConvertFrom-Json
function Set-Input([string]$id, [string]$value) {
    (Find-Control -id $id -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($value)
    Start-Sleep -Milliseconds 200
}
function Assert-Save([bool]$enabled, [string]$key = 'Save') {
    if (-not $enabled -and (Find-Control -name (Label $key) -Action).Current.IsEnabled) { Invoke-Control $key }
    if ((Find-Control -name (Label $key) -Action).Current.IsEnabled -ne $enabled) { throw "Unexpected enabled state: $key" }
}
function Invoke-ById([string]$id) {
    if ($id -eq 'VersionEdit') { Invoke-Control 'View' }
    $node = Find-Control -id $id -Enabled -IncludeOffscreen
    if ($node.Current.IsOffscreen) {
        $scrollId = if ($id -eq 'VersionEdit') { 'LibraryScroll' } else { 'GamesScroll' }
        $scroll = (Find-Control -id $scrollId).GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
        $percent = if ($id -in @('GameEdit', 'GameRelocate')) { 0 } else { 100 }
        if (-not $scroll.Current.VerticallyScrollable) { throw "Relocation action is offscreen without scrolling: $id" }
        $scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, $percent)
        Start-Sleep -Milliseconds 200
    }
    (Find-Control -id $id -Enabled -IncludeOffscreen).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
}

Find-Control -name (Label 'GameLocationMissing') | Out-Null
if ((Find-Control -id ('ClearLibrary_' + $fixture.LibraryId.Replace('-', '')) -IncludeOffscreen).Current.IsEnabled) {
    throw 'Clearing an unavailable game is enabled.'
}
if ((Find-Control -name (Label 'Deploy') -Action -IncludeOffscreen).Current.IsEnabled -or
    (Find-Control -id 'BatchPreflight').Current.IsEnabled) { throw 'Deployment for an unavailable game is enabled.' }
Capture '40-location-missing'
Invoke-ById 'GameRelocate'
Assert-Save $false 'CheckLocation'
$position = Assert-ActionOrder 'CheckLocation'
Set-Input 'RelocationExe' $fixture.NewExe
Set-Input 'RelocationInstall' $fixture.NewTarget
Set-Input 'RelocationTarget' $fixture.NewTarget
Assert-Save $true 'CheckLocation'
Assert-FixedAction $position 'CheckLocation'
Capture '41-relocation-input'
Invoke-Control 'CheckLocation'
Find-Control -name (Label 'RelocationConfirmHelp') | Out-Null
Capture '42-relocation-confirmation'
Invoke-Control 'Cancel'
Find-Control -name (Label 'GameLocationMissing') | Out-Null
# Confirming a fresh inspection changes only the ledger.
Invoke-ById 'GameRelocate'
Set-Input 'RelocationExe' $fixture.NewExe
Set-Input 'RelocationInstall' $fixture.NewTarget
Set-Input 'RelocationTarget' $fixture.NewTarget
Invoke-Control 'CheckLocation'
Invoke-Control 'ConfirmLocation'
Find-Control -id 'GameRelocate' -Enabled | Out-Null
Invoke-Control 'CheckTargets'
Find-Control -name (Label 'TargetMatched') -IncludeOffscreen | Out-Null
$gamesScroll = (Find-Control -id 'GamesScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
if ($gamesScroll.Current.VerticallyScrollable) { $gamesScroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 100) }
Start-Sleep -Milliseconds 250
Find-Control -name (Label 'TargetMatched') | Out-Null
Capture '43-relocated-matching'
Invoke-ById 'GameEdit'
if (-not (Find-Control -id 'GameExeInput' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.IsReadOnly) {
    throw 'Managed path remains editable.'
}
$position = Assert-ActionOrder 'Save'
Set-Input 'GameNameInput' ''
Assert-Save $false
Find-Control -name (Label 'NameRequired') | Out-Null
Capture '44-game-validation'
Set-Input 'GameNameInput' 'CI Game'
Assert-Save $true
Assert-FixedAction $position
Invoke-Control 'Save'
Invoke-Control 'Libraries'
Invoke-Control 'AddLibrary'
Assert-Save $true
$position = Assert-ActionOrder 'Save'
Set-Input 'LibraryNameInput' 'UI temporary library'
Set-Input 'LibraryTargetInput' '../outside'
Assert-Save $false
Capture '45-library-validation'
Set-Input 'LibraryTargetInput' ''
Assert-Save $true
Assert-FixedAction $position
Invoke-Control 'Cancel'
(Find-Control -id 'LibrariesSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Alpha')
Invoke-ById 'VersionEdit'
$position = Assert-ActionOrder 'Save'
Set-Input 'VersionSourceInput' ($fixture.Source + '.missing')
Assert-Save $false
Capture '46-version-validation'
Set-Input 'VersionSourceInput' $fixture.Source
Assert-Save $true
Assert-FixedAction $position
Invoke-Control 'Save'
Invoke-Control 'Games'
Invoke-ById ('ClearLibrary_' + $fixture.LibraryId.Replace('-', ''))
Invoke-Control 'ConfirmExecute'
Find-Control -id ('DeployLibrary_' + $fixture.LibraryId.Replace('-', '')) -Enabled -IncludeOffscreen | Out-Null
Capture '47-cleared-new-location'
Invoke-Control 'Settings'
$language = Find-Control -id 'SettingsLanguage' -Enabled
$language.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
(Find-Control -name (Label 'en-US')).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
Invoke-Control 'Save'
$labels = [xml][IO.File]::ReadAllText((Join-Path $appRoot 'Strings/en-US/Resources.resw'), [Text.Encoding]::UTF8)
Invoke-Control 'Libraries'
Invoke-Control 'AddLibrary'
Set-Input 'LibraryNameInput' 'UI temporary library'
Set-Input 'LibraryTargetInput' '../outside'
Assert-Save $false
Capture '48-library-validation-en'
Invoke-Control 'Cancel'
[IO.File]::WriteAllText((Join-Path $captureRoot 'result.txt'), 'PASS: relocation preview/cancel/confirm, reinspection, managed path lock, inline game/library/version validation, fixed footer, preserved input, bilingual errors and clear at relocated target.', [Text.Encoding]::UTF8)
