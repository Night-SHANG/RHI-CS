# Dot-sourced by the isolated Features harness before its first CheckTargets.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Features) { throw 'Busy regression requires the disposable CI feature fixture.' }
$busyFixture = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $FixtureMetadata).Path, [Text.Encoding]::UTF8) | ConvertFrom-Json
$busyLibrary = $busyFixture.ReShadeId.Replace('-', '')
$busyTarget = Join-Path (Split-Path -Parent $busyFixture.UnknownFile) 'ReShade.ini'
$fixtureBase = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'RhiTests')) + [IO.Path]::DirectorySeparatorChar
if (-not [IO.Path]::GetFullPath($busyTarget).StartsWith($fixtureBase, [StringComparison]::OrdinalIgnoreCase)) { throw 'Not an isolated test target.' }

function Assert-NoBusyError {
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, (Label 'Error'))
    foreach ($node in $window.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)) {
        if (-not $node.Current.IsOffscreen) { throw 'Operation incomplete/error dialog appeared during differences.' }
    }
}
function Assert-DifferencesDialog {
    Find-Control -name (Label 'TargetDifferences') | Out-Null
    Find-Control -id 'FileDetails_Inspection' | Out-Null
    Find-Control -name (Label 'Close') -Action -Enabled | Out-Null
    Assert-NoBusyError
}
function Close-DifferencesAndAssertIdle {
    Invoke-Control 'Close'
    Find-Control -id 'GameInspectTargets' -Enabled | Out-Null
    Find-Control -id "ViewDifferences_$busyLibrary" -Enabled | Out-Null
    Assert-NoBusyError
}

Invoke-Control 'Games'
Reveal-Control "ViewDifferences_$busyLibrary" 'GamesScroll' | Out-Null
# Independent single click, with no required preceding CheckTargets or double click.
Invoke-Id "ViewDifferences_$busyLibrary"
Assert-DifferencesDialog
Capture '22-busy-single-click'
Close-DifferencesAndAssertIdle

Invoke-Id 'GameInspectTargets'
Find-Control -id 'GameInspectTargets' -Enabled | Out-Null
# The displayed checked snapshot must not rehash all files just to open details.
# Holding a read-denying handle is reversible and never alters target bytes.
$locked = [IO.File]::Open($busyTarget, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
try {
    Invoke-Id "ViewDifferences_$busyLibrary"
    Assert-DifferencesDialog
    $expander = Find-Control -id 'FileDetails_Inspection'
    $expander.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Find-Control -id 'FileList_Inspection' | Out-Null
    Find-Control -name ('ReShade.ini ' + [char]0x00B7 + ' ' + (Label 'TargetModified')) -IncludeOffscreen | Out-Null
    Capture '22-busy-checked-snapshot'
    Close-DifferencesAndAssertIdle
    # A fresh check must handle the actual inaccessible target and release Busy.
    Invoke-Id 'GameInspectTargets'
    Find-Control -id 'GameInspectTargets' -Enabled | Out-Null
    Find-Control -name (Label 'TargetUnavailable') -IncludeOffscreen | Out-Null
    Invoke-Id "ViewDifferences_$busyLibrary"
    Assert-DifferencesDialog
    Capture '22-busy-target-read-error'
    Close-DifferencesAndAssertIdle
} finally { $locked.Dispose() }
Invoke-Id 'GameInspectTargets'
Find-Control -id 'GameInspectTargets' -Enabled | Out-Null

# Rapid events must not stack Busy/error dialogs or leave the shell disabled.
for ($round = 0; $round -lt 3; $round++) {
    $button = Find-Control -id "ViewDifferences_$busyLibrary" -Enabled
    $invoke = $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
    for ($click = 0; $click -lt 5; $click++) {
        try { $invoke.Invoke() }
        catch {
            $errorType = $_.Exception.GetType().FullName
            $innerType = if ($_.Exception.InnerException) { $_.Exception.InnerException.GetType().FullName } else { '' }
            if (($errorType + ' ' + $innerType) -notmatch 'ElementNotEnabledException|ElementNotAvailableException') { throw }
        }
    }
    Assert-DifferencesDialog
    Close-DifferencesAndAssertIdle
}
# Cancelling preflight must release its read operation before reopening details.
Invoke-Id "ClearLibrary_$busyLibrary"
Find-Control -name (Label 'Preflight') | Out-Null
Invoke-Control 'Cancel'
Invoke-Id "ViewDifferences_$busyLibrary"
Assert-DifferencesDialog
Close-DifferencesAndAssertIdle
Invoke-Control 'Libraries'
Find-Control -id 'LibrariesSearch' -Enabled | Out-Null
Invoke-Control 'Games'
Find-Control -id 'GameInspectTargets' -Enabled | Out-Null
Capture '22-busy-idle-restored'
[IO.File]::WriteAllText((Join-Path $captureRoot 'baseline-busy-result.txt'), 'PASS: independent single click; checked snapshot without rehash; locked target handled and Busy released; rapid repeated clicks; cancel then reopen; navigation restored and no stacked error modal.', [Text.Encoding]::UTF8)
