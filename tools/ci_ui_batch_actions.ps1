# Continues the feature fixture: Alpha v1 is installed; Beta is not.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Features) { throw 'CI feature harness required.' }
Invoke-Id 'LibraryEdit'
(Find-Control -id 'LibraryTargetInput' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('beta')
Invoke-Control 'Save'
Invoke-Control 'Games'
Reveal-Control ("VersionChoice_$alpha") 'GamesScroll' | Out-Null
$choice = Find-Control -id "VersionChoice_$alpha"
$choice.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
(Find-Control -name 'v2').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
Find-Control -id "ChangeLibrary_$alpha" -Enabled -IncludeOffscreen | Out-Null
Select-Batch
Capture '33-mixed-change-deploy-preflight'
Invoke-Control 'ConfirmExecute'
Find-Control -id "ClearLibrary_$beta" -Enabled -IncludeOffscreen | Out-Null
$statePath = Join-Path $fixture.Root 'workspace.json'
$state = [IO.File]::ReadAllText($statePath, [Text.Encoding]::UTF8) | ConvertFrom-Json
if (@($state.Receipts).Count -ne 2 -or @($state.Receipts | Where-Object { $_.LibraryId.Replace('-', '') -eq $alpha })[0].VersionLabel -ne 'v2') {
    throw 'Mixed batch did not change Alpha and deploy Beta.'
}
Reveal-Control 'SelectAllLibraries' 'GamesScroll' | Out-Null
Invoke-Id 'SelectAllLibraries'
foreach ($id in @($alpha, $beta, $reshade)) {
    if ((Find-Control -id "SelectLibrary_$id" -IncludeOffscreen).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [Windows.Automation.ToggleState]::On) { throw 'Select all omitted a resource.' }
}
Invoke-Id "SelectLibrary_$reshade"
if ((Find-Control -id 'SelectAllLibraries' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [Windows.Automation.ToggleState]::Indeterminate) { throw 'Partial selection is not reflected in header.' }
Capture '34-two-installed-selected'
Invoke-Id 'BatchClear'
Capture '35-batch-clear-preflight'
Invoke-Control 'Cancel'
$cancelled = [IO.File]::ReadAllText($statePath, [Text.Encoding]::UTF8) | ConvertFrom-Json
if (@($cancelled.Receipts).Count -ne 2) { throw 'Cancelling batch clear changed receipts.' }
# Preflight refresh resets row selection; select explicitly again.
Reveal-Control 'SelectAllLibraries' 'GamesScroll' | Out-Null
Invoke-Id 'SelectAllLibraries'
Invoke-Id 'BatchClear'
Invoke-Control 'ConfirmExecute'
Find-Control -id "DeployLibrary_$alpha" -Enabled -IncludeOffscreen | Out-Null
$cleared = [IO.File]::ReadAllText($statePath, [Text.Encoding]::UTF8) | ConvertFrom-Json
if (@($cleared.Receipts).Count -ne 0) { throw 'Batch clear left receipts.' }
Capture '36-batch-cleared'
# Restore the existing fixture's final state for its independent file/receipt verification.
Reveal-Control "DeployLibrary_$alpha" 'GamesScroll' | Out-Null
Invoke-Id "DeployLibrary_$alpha"
Invoke-Control 'ConfirmExecute'
Find-Control -id "ClearLibrary_$alpha" -Enabled -IncludeOffscreen | Out-Null
