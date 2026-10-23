if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Usability) { throw 'CI usability fixture required.' }
$fixture = Get-Content -LiteralPath $FixtureMetadata -Raw -Encoding UTF8 | ConvertFrom-Json
$libraryId = ([guid]$fixture.LibraryId).ToString('N')
$versionId = ([guid]$fixture.VersionId).ToString('N')
function Read-State { return Get-Content -LiteralPath (Join-Path $fixture.Data 'workspace.json') -Raw -Encoding UTF8 | ConvertFrom-Json }
function Click-Id([string]$id) {
    (Find-Control -id $id -Enabled).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
}
function Set-Text([string]$id, [string]$text) {
    (Find-Control -id $id).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($text)
    Start-Sleep -Milliseconds 250
}
function Check-Picker([string]$id, [string]$expected) {
    Click-Id $id
    $native = $null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 150
        $filter = [Windows.Automation.AndCondition]::new(
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty, '#32770'))
        $native = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $filter)
        if ($native) { break }
    }
    if (-not $native) { throw 'Native folder picker missing.' }
    $nodes = $native.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
    $names = @($nodes | ForEach-Object { $_.Current.Name }) -join "`n"
    if (-not $names.Contains($expected)) { throw "Wrong picker initial directory: $expected; $names" }
    $cancel = @($nodes | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button -and $_.Current.Name -match '^(Cancel|\u53d6\u6d88)$' }) | Select-Object -First 1
    if (-not $cancel) { throw 'Native picker cancellation unavailable.' }
    $cancel.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
}
Invoke-Control 'Libraries'
Click-Id 'AddLibrary'
Find-Control -id 'FirstLibraryRootInput' | Out-Null
Invoke-BaselineRootLayout
Capture 'r12-first-root-empty'
Invoke-Control 'Cancel'
if ((Read-State).Preferences.LibraryRoot) { throw 'Cancel saved a default directory.' }
Click-Id 'AddLibrary'
Set-Text 'FirstLibraryRootInput' $fixture.BaseRoot
Check-Picker 'FirstLibraryRootBrowse' $fixture.BaseRoot
Assert-ActionOrder 'SaveAndContinue' | Out-Null
Capture 'r12-first-root-ready'
Invoke-Control 'SaveAndContinue'
Find-Control -id 'LibraryNameInput' | Out-Null
if ((Read-State).Preferences.LibraryRoot -ne $fixture.BaseRoot) { throw 'Root was not persisted before continuing.' }
Check-Picker 'LibraryChooseFolder' $fixture.BaseRoot
Invoke-Control 'Cancel'
Capture 'r12-library-direct-edit'
Click-Id ("VersionEdit_" + $versionId)
Set-Text 'VersionLabelInput' '6.8.0 renamed'
Assert-FormReady
Capture 'r12-version-edit'
Invoke-Control 'Save'
if ((Read-State).Libraries[0].Versions[0].Label -ne '6.8.0 renamed') { throw 'Direct edit failed to persist.' }
Click-Id 'LibraryAddVersion'
Check-Picker 'VersionChooseFolder' $fixture.LibraryRoot
Invoke-Control 'Cancel'
$before = (Read-State).Libraries | ConvertTo-Json -Depth 20 -Compress
Invoke-Control 'Settings'
Select-SettingsSection 'General'
Set-Text 'SettingsLibraryRoot' $fixture.OtherRoot
Click-Id 'SettingsSave'
if (((Read-State).Libraries | ConvertTo-Json -Depth 20 -Compress) -ne $before) { throw 'Changing the default rewrote existing libraries.' }
Capture 'r12-settings-root'
Invoke-Control 'Libraries'
Click-Id 'AddLibrary'
Find-Control -id 'LibraryNameInput' | Out-Null
Check-Picker 'LibraryChooseFolder' $fixture.OtherRoot
Invoke-Control 'Cancel'
Click-Id 'LibraryRelocate'
Set-Text 'RelocateLibraryInput' $fixture.MovedRoot
Capture 'r12-relocation-input'
Invoke-Control 'ReviewMatches'
Find-Control -name (Label 'RelocationMatched') | Out-Null
Capture 'r12-relocation-review'
Invoke-Control 'Cancel'
if (((Read-State).Libraries | ConvertTo-Json -Depth 20 -Compress) -ne $before) { throw 'Cancelled relocation changed config.' }
Click-Id 'LibraryRelocate'
Set-Text 'RelocateLibraryInput' $fixture.MovedRoot
Invoke-Control 'ReviewMatches'
Invoke-Control 'Save'
if ((Read-State).Libraries[0].SourceRoot -ne $fixture.MovedRoot) { throw 'Confirmed relocation not saved.' }
Invoke-Control 'Games'
Invoke-Control 'Deploy'
Find-Control -id ("FileDetails_" + $libraryId) | Out-Null
if (Find-OptionalId ("FileList_" + $libraryId)) { throw 'Collapsed details eagerly constructed their list.' }
$footer = Assert-ActionOrder 'ConfirmExecute'
Capture 'r12-preflight-collapsed'
$expander = Expand-FileDetails $libraryId
Assert-FixedAction $footer 'ConfirmExecute'
$list = Find-Control -id ("FileList_" + $libraryId)
$scale = [SmokeWindow]::GetDpiForWindow($process.MainWindowHandle) / 96.0
if ([Math]::Abs($list.Current.BoundingRectangle.Height / $scale - 200) -gt 2) { throw 'File list does not have a bounded viewport.' }
$children = $list.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
if ($children.Count -ge 500) { throw "Too many live list automation elements: $($children.Count)" }
Capture 'r12-preflight-expanded'
$scroll = $list.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
if (-not $scroll.Current.VerticallyScrollable) { throw 'Large file list is not scrollable.' }
$scroll.SetScrollPercent(-1, 100)
Start-Sleep -Milliseconds 400
$names = @($list.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }) -join "`n"
if (-not $names.Contains('shader-0989.fx')) { throw 'Last file cannot be reached.' }
Assert-FixedAction $footer 'ConfirmExecute'
Capture 'r12-preflight-last-file'
Invoke-Control 'Cancel'
Invoke-Control 'Settings'
Select-SettingsSection 'General'
$theme = Find-Control -id 'SettingsTheme'
$theme.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
(Find-Control -name (Label 'Dark')).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
$language = Find-Control -id 'SettingsLanguage'
$language.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
(Find-Control -name (Label 'en-US')).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
Click-Id 'SettingsSave'
$labels = [xml][IO.File]::ReadAllText((Join-Path $appRoot 'Strings/en-US/Resources.resw'), [Text.Encoding]::UTF8)
Capture 'r12-settings-dark-en'
Invoke-Control 'Libraries'
Capture 'r12-library-dark-en'
Invoke-Control 'Games'; Invoke-Control 'Deploy'
Capture 'r12-preflight-dark-en'
Invoke-Control 'Cancel'
[IO.File]::WriteAllText((Join-Path $captureRoot 'result.txt'), 'PASS: first-use cancellation/save-and-continue; native picker starting folders; direct rename; default change preserves existing paths; relocation preview/cancel/commit; lazy 990-file list with bounded virtualization and last-file access; fixed footer; light Chinese and dark English screenshots.', [Text.Encoding]::UTF8)
