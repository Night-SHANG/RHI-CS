if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Startup checks require a disposable GitHub runner.' }
$locator = Join-Path $env:LOCALAPPDATA 'RHI-CS/bootstrap.json'
$original = [IO.File]::ReadAllText($locator, [Text.Encoding]::UTF8)
$data = Join-Path $appRoot 'Data'
Find-Control -name (Label 'DataUnavailable') | Out-Null
function Assert-StartupRow {
    $bounds = @('ReconnectData', 'Retry', 'StartNewData') | ForEach-Object {
        (Find-Control -name (Label $_) -Action).Current.BoundingRectangle
    }
    for ($i = 1; $i -lt 3; $i++) {
        if ([Math]::Abs($bounds[$i].Width - $bounds[0].Width) -gt 1 -or
            [Math]::Abs($bounds[$i].Height - $bounds[0].Height) -gt 1 -or
            [Math]::Abs($bounds[$i].Y - $bounds[0].Y) -gt 1 -or $bounds[$i].X -le $bounds[$i-1].Right) {
            throw 'Startup actions are not an equal-sized horizontal row.'
        }
    }
}
Assert-StartupRow
Capture 'startup-missing-data'
$process.Refresh()
[SmokeWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero, 0, 0, 780, 620, 0x0040) | Out-Null
Start-Sleep -Milliseconds 300
Assert-StartupRow
Capture 'startup-missing-data-compact'
[SmokeWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero, 0, 0, 1320, 860, 0x0040) | Out-Null
Invoke-Control 'StartNewData'
Assert-ActionOrder 'CreateNewData' | Out-Null
Capture 'startup-confirm-new-data'
Invoke-Control 'Cancel'
Find-Control -name (Label 'DataUnavailable') | Out-Null
if ((Test-Path -LiteralPath $data) -or [IO.File]::ReadAllText($locator, [Text.Encoding]::UTF8) -cne $original) {
    throw 'Cancelling restart changed data or the locator.'
}
Invoke-Control 'StartNewData'
Assert-ActionOrder 'CreateNewData' | Out-Null
Invoke-Control 'CreateNewData'
Find-Control -id 'GamesSearch' | Out-Null
Capture 'startup-new-empty-workspace'
$created = [IO.File]::ReadAllText($locator, [Text.Encoding]::UTF8)
if (-not (Test-Path -LiteralPath (Join-Path $data 'workspace.json'))) { throw 'New Data was not created.' }
$process.Kill(); $process.WaitForExit()
$process = Start-Process -FilePath (Join-Path $appRoot 'RHI-CS.exe') -PassThru
$processHandle = $process.Handle
Find-Control -id 'GamesSearch' | Out-Null
if ([IO.File]::ReadAllText($locator, [Text.Encoding]::UTF8) -cne $created) { throw 'Restart changed workspace identity.' }
Capture 'startup-reopened-workspace'
Invoke-Control 'Settings'
$language = Find-Control -id 'SettingsLanguage' -Enabled
$language.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
(Find-Control -name (Label 'zh-CN')).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
Invoke-Control 'Save'
$labels = [xml][IO.File]::ReadAllText((Join-Path $appRoot 'Strings/zh-CN/Resources.resw'), [Text.Encoding]::UTF8)
Find-Control -name (Label 'Games') | Out-Null
# Simulate a disconnected live workspace while retaining its ledger for reconnection.
# Both paths belong to this disposable runner fixture, never the user's data.
$ledger = Join-Path $data 'workspace.json'
$heldLedger = Join-Path $captureRoot 'workspace-held.json'
[IO.File]::Move($ledger, $heldLedger)
try {
    Invoke-Control 'Games'
    Find-Control -name (Label 'DataUnavailable') | Out-Null
    Assert-StartupRow
    Capture 'startup-missing-data-zh'
    [SmokeWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero, 0, 0, 780, 620, 0x0040) | Out-Null
    Start-Sleep -Milliseconds 300
    Assert-StartupRow
    Capture 'startup-missing-data-compact-zh'
    Invoke-Control 'StartNewData'
    Assert-ActionOrder 'CreateNewData' | Out-Null
    Capture 'startup-confirm-new-data-zh'
    Invoke-Control 'Cancel'
} finally {
    [IO.File]::Move($heldLedger, $ledger)
}
Invoke-Control 'Retry'
Find-Control -id 'GamesSearch' | Out-Null
Write-Host 'STARTUP UI PASSED: missing old location, cancellation, explicit creation, restart persistence.'
