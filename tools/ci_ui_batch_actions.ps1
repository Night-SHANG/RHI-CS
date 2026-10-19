# Continues the feature fixture: Alpha v1 is installed; Beta is not.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Features) { throw 'CI feature harness required.' }
Invoke-Id 'LibraryEdit'
(Find-Control -id 'LibraryTargetInput' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('beta')
Invoke-Control 'Save'
Invoke-Control 'Games'
$rowSpacingBefore = Assert-ResourceRows 'before mixed batch'
Reveal-Control ("VersionChoice_$alpha") 'GamesScroll' | Out-Null
$choice = Find-Control -id "VersionChoice_$alpha"
$choice.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
(Find-Control -name 'v2').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
Find-Control -id "ChangeLibrary_$alpha" -Enabled -IncludeOffscreen | Out-Null
Capture '33-change-version-enabled'
Select-Batch
Capture '33-mixed-change-deploy-preflight'
Invoke-Control 'ConfirmExecute'
Find-Control -id "ClearLibrary_$beta" -Enabled -IncludeOffscreen | Out-Null
$rowSpacingAfter = Assert-ResourceRows 'after mixed batch'
if ([Math]::Abs($rowSpacingBefore - $rowSpacingAfter) -gt 2) { throw 'Deployment changed resource row height.' }
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
$wideRect = [SmokeWindow+Rect]::new()
[SmokeWindow]::GetWindowRect($process.MainWindowHandle, [ref]$wideRect) | Out-Null
[SmokeWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero, $wideRect.Left, $wideRect.Top, 1000, 900, 0x0040) | Out-Null
Start-Sleep -Milliseconds 400
Assert-ResourceRows 'compact mixed states' | Out-Null
Capture '34-compact-equal-rows'
[SmokeWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero, $wideRect.Left, $wideRect.Top, ($wideRect.Right-$wideRect.Left), ($wideRect.Bottom-$wideRect.Top), 0x0040) | Out-Null
Start-Sleep -Milliseconds 400
# Modify only a managed CI fixture file; cancellation must leave it untouched.
$alphaReceipt = @($state.Receipts | Where-Object { $_.LibraryId.Replace('-', '') -eq $alpha })[0]
$changedFile = Join-Path $alphaReceipt.TargetRoot $alphaReceipt.Files[0].RelativePath
[IO.File]::WriteAllText($changedFile, 'explicit-discard-ui', [Text.Encoding]::UTF8)
$protectionBefore = @(Get-ChildItem -LiteralPath (Join-Path $fixture.Root 'protection') -Filter '*.json').Count
Invoke-Id 'BatchClear'
Find-Control -name (Label 'DirectClear') -Action | Out-Null
Capture '35-batch-clear-preflight'
Invoke-Control 'Cancel'
$cancelled = [IO.File]::ReadAllText($statePath, [Text.Encoding]::UTF8) | ConvertFrom-Json
if (@($cancelled.Receipts).Count -ne 2) { throw 'Cancelling batch clear changed receipts.' }
if ([IO.File]::ReadAllText($changedFile, [Text.Encoding]::UTF8) -ne 'explicit-discard-ui') { throw 'Cancel changed the modified file.' }
# Preflight refresh resets row selection; select explicitly again.
Reveal-Control 'SelectAllLibraries' 'GamesScroll' | Out-Null
Invoke-Id 'SelectAllLibraries'
Invoke-Id 'BatchClear'
Capture '35-explicit-direct-clear'
Invoke-Control 'DirectClear'
Find-Control -id "DeployLibrary_$alpha" -Enabled -IncludeOffscreen | Out-Null
$cleared = [IO.File]::ReadAllText($statePath, [Text.Encoding]::UTF8) | ConvertFrom-Json
if (@($cleared.Receipts).Count -ne 0) { throw 'Batch clear left receipts.' }
if (@(Get-ChildItem -LiteralPath (Join-Path $fixture.Root 'protection') -Filter '*.json').Count -ne $protectionBefore) { throw 'Direct clear unexpectedly made a protection copy.' }
Capture '36-batch-cleared'
if ([Math]::Abs((Assert-ResourceRows 'after batch clear') - $rowSpacingBefore) -gt 2) { throw 'Clearing deployment changed row height.' }
# Restore the existing fixture's final state for its independent file/receipt verification.
Reveal-Control "DeployLibrary_$alpha" 'GamesScroll' | Out-Null
Invoke-Id "DeployLibrary_$alpha"
Invoke-Control 'ConfirmExecute'
Find-Control -id "ClearLibrary_$alpha" -Enabled -IncludeOffscreen | Out-Null
