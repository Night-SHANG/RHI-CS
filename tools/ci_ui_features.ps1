# Dot-sourced only by ci_ui_smoke.ps1 on an isolated GitHub runner.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Features) { throw 'Feature checks require the CI UI harness.' }
$fixture = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $FixtureMetadata).Path, [Text.Encoding]::UTF8) | ConvertFrom-Json
$alpha = $fixture.AlphaId.Replace('-', '')
$beta = $fixture.BetaId.Replace('-', '')
$reshade = $fixture.ReShadeId.Replace('-', '')
$taskPrefix = 'CI Game ' + [char]0x00B7 + ' '
$evidence = [Collections.Generic.List[string]]::new()
. (Join-Path $PSScriptRoot 'ci_ui_display.ps1') -PrepareWideOnly
function Reveal-Control([string]$id, [string]$scrollId) {
    # Search/selection can replace the detail view before its first layout has settled.
    Start-Sleep -Milliseconds 400
    $stable = $false
    $searchPercent = 0
    for ($i = 0; $i -lt 24; $i++) {
        $element = Find-Control -id $id -IncludeOffscreen
        $viewer = Find-Control -id $scrollId
        $bounds = $element.Current.BoundingRectangle
        $viewport = $viewer.Current.BoundingRectangle
        $visible = -not $element.Current.IsOffscreen -and $bounds.Height -gt 0 -and
            $bounds.Top -ge $viewport.Top -and $bounds.Bottom -le $viewport.Bottom
        if ($visible -and $stable) { return $element }
        $stable = $visible
        if (-not $visible) {
            $scroll = $viewer.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
            if ($scroll.Current.VerticallyScrollable) {
                # Fully offscreen WinUI controls can expose an empty/infinite rectangle.
                # Search from the top in small steps instead of inferring direction from it.
                $scroll.SetScrollPercent(-1, [Math]::Min(100, $searchPercent))
                $searchPercent += 5
            }
        }
        Start-Sleep -Milliseconds 200
    }
    throw "Cannot reveal complete control: $id; bounds=$bounds; viewport=$viewport; offscreen=$($element.Current.IsOffscreen); scroll=$($scroll.Current.VerticalScrollPercent)"
}
function Invoke-Id([string]$id) {
    $node = Find-Control -id $id -Enabled
    $pattern = $null
    if ($node.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke() }
    elseif ($node.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern, [ref]$pattern)) { $pattern.Toggle() }
    else { throw "Not an action: $id" }
    Start-Sleep -Milliseconds 250
}
function Select-Batch {
    foreach ($id in @($alpha, $beta)) {
        Reveal-Control "SelectLibrary_$id" 'GamesScroll' | Out-Null
        Invoke-Id "SelectLibrary_$id"
    }
    Invoke-Id 'BatchPreflight'
}

. (Join-Path $PSScriptRoot 'ci_ui_folder_actions.ps1')
Invoke-Control 'Libraries'
if ((Find-Control -id 'LibraryRemove').Current.IsEnabled) { throw 'A deployed library can be removed.' }
$listBefore = (Find-Control -id 'LibrariesSearch').GetRuntimeId() -join ','
Invoke-Control 'View'
if ((Find-Control -id 'VersionRemove').Current.IsEnabled) { throw 'A deployed version can be removed.' }
Capture '20-version-details-dialog'
Invoke-Control 'Close'
if (((Find-Control -id 'LibrariesSearch').GetRuntimeId() -join ',') -ne $listBefore) { throw 'Viewing a version rebuilt the library list.' }
Capture '20-referenced-library'
Invoke-Control 'Games'
Invoke-Control 'CheckTargets'
Find-Control -name (Label 'TargetModified') | Out-Null
Capture '21-target-changes'
Invoke-Control 'ViewDifferences'
Find-Control -name (Label 'Close') -Action | Out-Null
Capture '22-target-differences'
Invoke-Control 'Close'
Reveal-Control 'SelectAllLibraries' 'GamesScroll' | Out-Null
Invoke-Id 'SelectAllLibraries'
if ((Find-Control -id "SelectLibrary_$reshade" -IncludeOffscreen).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [Windows.Automation.ToggleState]::On) { throw 'Select all omitted deployed library.' }
Capture '22-all-selected-mixed'
Invoke-Id 'BatchClear'
foreach ($key in @('ProtectAndClear', 'DirectClear', 'Cancel')) { Find-Control -name (Label $key) -Action | Out-Null }
Capture '23-protect-before-clear'
Invoke-Control 'ProtectAndClear'
Find-Control -id "DeployLibrary_$reshade" -Enabled | Out-Null
Invoke-Control 'TaskResults'
Find-Control -name ($taskPrefix + (Label 'Clear')) | Out-Null
Find-Control -name (Label 'OpenProtectedCopies') -Action | Out-Null
Capture '24-clear-result-with-backup'
Invoke-Control 'Close'
Invoke-Control 'Settings'
Find-Control -id 'ProtectionUsage' -IncludeOffscreen | Out-Null
Reveal-Control 'ProtectionUsage' 'SettingsScroll' | Out-Null
Capture '25-protection-inventory'
$protectionRecord = @(Get-ChildItem -LiteralPath (Join-Path $fixture.Root 'protection') -Filter '*.json')
if ($protectionRecord.Count -ne 1) { throw 'Expected one complete protection copy.' }
$copy = [IO.File]::ReadAllText($protectionRecord[0].FullName, [Text.Encoding]::UTF8) | ConvertFrom-Json
$copyId = 'OpenProtection_' + $copy.OperationId.Replace('-', '')
$inventoryScroll = (Find-Control -id 'SettingsScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
if ($inventoryScroll.Current.VerticallyScrollable) { $inventoryScroll.SetScrollPercent(-1, 100) }
Reveal-Control $copyId 'SettingsScroll' | Out-Null
Capture '25-protection-version-entry'
$copyFolder = Join-Path (Join-Path (Join-Path $fixture.Root 'protection') $copy.ProtectionDirectory) $copy.VersionLabel
Check-FolderAction $copyId $copyFolder 'SettingsScroll'
$settingsIdentity = (Find-Control -id 'SettingsTheme' -IncludeOffscreen).GetRuntimeId() -join ','
$offlineDraft = (Find-Control -id 'OfflineNameInput' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
$offlineDraft.SetValue('Keep draft across statistics')
$settingsScroll = (Find-Control -id 'SettingsScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
$position = $settingsScroll.Current.VerticalScrollPercent
Reveal-Control 'RefreshProtection' 'SettingsScroll' | Out-Null
$position = $settingsScroll.Current.VerticalScrollPercent
Invoke-Id 'RefreshProtection'
Find-Control -id 'RefreshProtection' -Enabled | Out-Null
if (((Find-Control -id 'SettingsTheme' -IncludeOffscreen).GetRuntimeId() -join ',') -ne $settingsIdentity -or
    $offlineDraft.Current.Value -ne 'Keep draft across statistics' -or
    [Math]::Abs($settingsScroll.Current.VerticalScrollPercent - $position) -gt 1) { throw 'Statistics refresh replaced page or changed draft/scroll.' }
Capture '25-statistics-refresh-retained'
# Results retain their original game/action when opened from another page.
Invoke-Control 'TaskResults'
Find-Control -name ($taskPrefix + (Label 'Clear')) | Out-Null
Invoke-Control 'Close'
Invoke-Control 'Games'
Select-Batch
if ((Find-Control -name (Label 'ConfirmExecute') -Action).Current.IsEnabled) { throw 'Overlapping batch unexpectedly executable.' }
Capture '26-batch-conflict'
# Explicitly skipping all libraries produces a result without a write operation.
foreach ($id in @($alpha, $beta)) {
    Reveal-Control "SkipLibrary_$id" 'DialogContentScroll' | Out-Null
    Invoke-Id "SkipLibrary_$id"
}
Invoke-Control 'RecheckSelection'
Invoke-Control 'FinishReview'
Invoke-Control 'TaskResults'
Find-Control -name (Label 'SkippedByUser') | Out-Null
Invoke-Control 'Close'
Select-Batch
Reveal-Control "SkipLibrary_$beta" 'DialogContentScroll' | Out-Null
Invoke-Id "SkipLibrary_$beta"
Invoke-Control 'RecheckSelection'
Assert-ActionOrder 'ConfirmExecute' | Out-Null
Capture '27-batch-rechecked'
Invoke-Control 'ConfirmExecute'
Find-Control -id "ClearLibrary_$alpha" -Enabled -IncludeOffscreen | Out-Null
Invoke-Control 'TaskResults'
Find-Control -name ($taskPrefix + (Label 'Deploy')) | Out-Null
Find-Control -name (Label 'SkippedByUser') | Out-Null
Capture '28-batch-result'
Invoke-Control 'Close'
Invoke-Control 'Libraries'
(Find-Control -id 'LibrariesSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Beta')
. (Join-Path $PSScriptRoot 'ci_ui_batch_actions.ps1')
Invoke-Control 'Libraries'
(Find-Control -id 'LibrariesSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Beta')
Find-Control -name (Label 'RemoveVersion') -Action | Out-Null
Invoke-Control 'RemoveVersion'
Assert-ActionOrder 'RemoveVersion' | Out-Null
Capture '29-remove-version-confirmation'
Invoke-Control 'RemoveVersion'
Find-Control -id 'LibraryRemove' -Enabled | Out-Null
Invoke-Id 'LibraryRemove'
Assert-ActionOrder 'RemoveLibrary' | Out-Null
Invoke-Control 'RemoveLibrary'
Find-Control -name (Label 'NoMatches') | Out-Null
Capture '30-library-removed'
# The English resources use the same flows and remain visible after live language switching.
Invoke-Control 'Settings'
$language = Find-Control -id 'SettingsLanguage' -Enabled
$language.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
(Find-Control -name (Label 'en-US')).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
Invoke-Control 'Save'
$labels = [xml][IO.File]::ReadAllText((Join-Path $appRoot 'Strings/en-US/Resources.resw'), [Text.Encoding]::UTF8)
Reveal-Control 'ProtectionUsage' 'SettingsScroll' | Out-Null
Capture '31-protection-inventory-en'
Invoke-Control 'TaskResults'
Find-Control -name ($taskPrefix + (Label 'Deploy')) | Out-Null
Capture '32-batch-result-en'
Invoke-Control 'Close'
[IO.File]::WriteAllText((Join-Path $captureRoot 'result.txt'), 'PASS: read-only differences, version-details dialog without rebuilding the sidebar, protection statistics without replacing page/draft/scroll, reference guards, mixed deployment/version change, select-all and partial selection, batch clear and cancellation, skipped batches, version/library removal, and bilingual results. Verify persisted files in the following fixture step.', [Text.Encoding]::UTF8)
