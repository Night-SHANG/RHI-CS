# Dot-sourced only by ci_ui_smoke.ps1 on an isolated GitHub runner.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Features) { throw 'Feature checks require the CI UI harness.' }
$fixture = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $FixtureMetadata).Path, [Text.Encoding]::UTF8) | ConvertFrom-Json
$alpha = $fixture.AlphaId.Replace('-', '')
$beta = $fixture.BetaId.Replace('-', '')
$reshade = $fixture.ReShadeId.Replace('-', '')
$taskPrefix = 'CI Game ' + [char]0x00B7 + ' '
function Reveal-Control([string]$id, [string]$scrollId) {
    # Search/selection can replace the detail view before its first layout has settled.
    Start-Sleep -Milliseconds 400
    $stable = $false
    for ($i = 0; $i -lt 16; $i++) {
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
            if ($scroll.Current.VerticallyScrollable -and $scroll.Current.VerticalScrollPercent -lt 100) {
                $scroll.Scroll([Windows.Automation.ScrollAmount]::NoAmount, [Windows.Automation.ScrollAmount]::LargeIncrement)
            }
        }
        Start-Sleep -Milliseconds 200
    }
    throw "Cannot reveal complete control: $id"
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

Invoke-Control 'Libraries'
if ((Find-Control -id 'LibraryRemove').Current.IsEnabled) { throw 'A deployed library can be removed.' }
if ((Find-Control -id 'VersionRemove' -IncludeOffscreen).Current.IsEnabled) { throw 'A deployed version can be removed.' }
Capture '20-referenced-library'
Invoke-Control 'Games'
Invoke-Control 'CheckTargets'
Find-Control -name (Label 'TargetModified') | Out-Null
Capture '21-target-changes'
Invoke-Control 'ViewDifferences'
Find-Control -name (Label 'NextAction') | Out-Null
(Find-Control -id 'DifferenceNextAction').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
$clearCondition = [Windows.Automation.AndCondition]::new(
    [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id),
    [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem),
    [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, (Label 'ClearDeployment')))
$clearOption = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $clearCondition)
if (-not $clearOption) { throw 'Clear deployment option is missing from differences.' }
$clearOption.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
Capture '22-target-differences'
Invoke-Control 'Preflight'
Assert-ActionOrder 'ProtectAndExecute' | Out-Null
Capture '23-protect-before-clear'
Invoke-Control 'ProtectAndExecute'
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
Reveal-Control 'VersionRemove' 'LibraryScroll' | Out-Null
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
Find-Control -name (Label 'SkippedByUser') | Out-Null
Capture '32-batch-result-en'
Invoke-Control 'Close'
[IO.File]::WriteAllText((Join-Path $captureRoot 'result.txt'), 'PASS: target inspection, differences to preflight, protection and clear, stable task identity, protection usage, reference guards, all-skipped and partial-skipped batches, version/library removal, and live bilingual results. Verify persisted files in the following fixture step.', [Text.Encoding]::UTF8)
