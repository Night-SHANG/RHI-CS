# Dot-source at the end of feature checks. Only synthetic CI data is inspected.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Features) { throw 'Disposable feature fixture required.' }
$protectionRoot = Join-Path $fixture.Root 'protection'
$fixtureBoundary = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'RhiTests')) + [IO.Path]::DirectorySeparatorChar
if (-not [IO.Path]::GetFullPath($fixture.Root).StartsWith($fixtureBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Not a synthetic fixture.' }
$ledgerPath = Join-Path $fixture.Root 'workspace.json'
$ledger = Get-Content -LiteralPath $ledgerPath -Raw -Encoding UTF8 | ConvertFrom-Json
$gameRoot = $ledger.Games[0].DeploymentRoot
if (-not [IO.Path]::GetFullPath($gameRoot).StartsWith($fixtureBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Not a synthetic game.' }
$recordFiles = @(Get-ChildItem -LiteralPath $protectionRoot -Filter '*.json')
if ($recordFiles.Count -ne 1) { throw 'Expected the existing single protected ReShade record.' }
$record = Get-Content -LiteralPath $recordFiles[0].FullName -Raw -Encoding UTF8 | ConvertFrom-Json
$operation = $record.OperationId
$removeId = 'RemoveProtectionRecord_' + $operation.Replace('-', '')
$openId = 'OpenProtection_' + $operation.Replace('-', '')
function Get-ProtectionFixtureSnapshot {
    $files = [ordered]@{}
    $directories = @()
    foreach ($root in @($protectionRoot, $gameRoot)) {
        $directories += $root
        $directories += @(Get-ChildItem -LiteralPath $root -Directory -Recurse -Force | ForEach-Object { $_.FullName })
        foreach ($file in @(Get-ChildItem -LiteralPath $root -File -Recurse -Force | Sort-Object FullName)) {
            $files[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    return [ordered]@{ Files = $files; Directories = @($directories | Sort-Object) }
}
$before = Get-ProtectionFixtureSnapshot
$beforeJson = ConvertTo-Json -InputObject $before -Depth 8 -Compress
$ledgerBefore = [IO.File]::ReadAllText($ledgerPath, [Text.Encoding]::UTF8)
function Assert-ProtectionFixtureUnchanged {
    $afterJson = ConvertTo-Json -InputObject (Get-ProtectionFixtureSnapshot) -Depth 8 -Compress
    if ($afterJson -cne $beforeJson) { throw 'Record removal changed protection/game files or directories.' }
}
function Assert-ProtectionRecordHidden {
    Find-Control -id 'RefreshProtection' -Enabled | Out-Null
    Find-Control -name (Label 'ProtectionEmpty') | Out-Null
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    foreach ($id in @($removeId, $openId)) {
        $selector = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
        if ($window.FindFirst([Windows.Automation.TreeScope]::Descendants, $selector)) { throw "Hidden row reappeared: $id" }
    }
    $saved = Get-Content -LiteralPath $ledgerPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (@($saved.HiddenProtectionIds).Count -ne 1 -or $saved.HiddenProtectionIds[0] -ne $operation) { throw 'Wrong persisted hidden record.' }
    Assert-ProtectionFixtureUnchanged
}
Invoke-Control 'Settings'
Select-SettingsSection 'Protection'
Reveal-Control $removeId 'ProtectionScroll' | Out-Null
Invoke-Id $removeId
Find-Control -name (Label 'RemoveProtectionRecordConfirm') | Out-Null
Capture '33-protection-remove-confirm-cancel'
Invoke-Control 'Cancel'
Find-Control -id $removeId -Enabled | Out-Null
if ([IO.File]::ReadAllText($ledgerPath, [Text.Encoding]::UTF8) -cne $ledgerBefore) { throw 'Cancel changed workspace metadata.' }
Assert-ProtectionFixtureUnchanged
Invoke-Id $removeId
Find-Control -name (Label 'RemoveProtectionRecordConfirm') | Out-Null
# ContentDialog names can match the underlying row. Target its specific button.
Invoke-Id 'SecondaryButton'
Assert-ProtectionRecordHidden
Capture '34-protection-record-hidden'
Invoke-Id 'RefreshProtection'
Assert-ProtectionRecordHidden
Capture '35-protection-record-refresh-hidden'
$process.Kill(); $process.WaitForExit()
$process = Start-Process -FilePath (Join-Path $appRoot 'RHI-CS.exe') -PassThru `
    -RedirectStandardOutput (Join-Path $captureRoot 'protection-restart-stdout.txt') `
    -RedirectStandardError (Join-Path $captureRoot 'protection-restart-stderr.txt')
$processHandle = $process.Handle
Find-Control -id 'GamesSearch' | Out-Null
Invoke-Control 'Settings'
Select-SettingsSection 'Protection'
Assert-ProtectionRecordHidden
Invoke-Id 'RefreshProtection'
Assert-ProtectionRecordHidden
Capture '36-protection-record-restart-hidden'
$proof = [ordered]@{ OperationId = $operation; GameRoot = $gameRoot; Files = $before.Files; Directories = $before.Directories }
[IO.File]::WriteAllText(($FixtureMetadata + '.protection.json'), (ConvertTo-Json -InputObject $proof -Depth 8), [Text.Encoding]::UTF8)
[IO.File]::WriteAllText((Join-Path $captureRoot 'protection-record-result.txt'), 'PASS: cancel writes nothing; confirmed removal hides one record after refresh and a second application launch; every protection/game file hash and directory remains unchanged.', [Text.Encoding]::UTF8)
