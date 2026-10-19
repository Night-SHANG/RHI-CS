# Folder launching is exercised only on GitHub's disposable interactive desktop.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Features) { throw 'CI feature harness required.' }
function Check-FolderAction([string]$id, [string]$expected, [string]$scroll) {
    Reveal-Control $id $scroll | Out-Null
    Invoke-Id $id
    $found = $null
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 200
        $shell = New-Object -ComObject Shell.Application
        foreach ($window in $shell.Windows()) {
            try {
                if ($window.Document.Folder.Self.Path -eq $expected) { $found = $window; break }
            } catch { }
        }
        if ($found) { break }
    }
    if (-not $found) { throw "Folder action did not open its expected directory: $id; $expected" }
    $found.Quit()
    Start-Sleep -Milliseconds 300
    [SmokeWindow]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
}
$folderState = [IO.File]::ReadAllText((Join-Path $fixture.Root 'workspace.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
Invoke-Control 'Games'
Check-FolderAction 'GameOpenFolder' $folderState.Games[0].DeploymentRoot 'GamesScroll'
Invoke-Control 'Libraries'
(Find-Control -id 'LibrariesSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Beta')
if ((Find-Control -id 'LibraryOpenFolder').Current.IsEnabled) { throw 'Unset library directory can be opened.' }
$alphaLibrary = @($folderState.Libraries | Where-Object { $_.Id.Replace('-', '') -eq $alpha })[0]
(Find-Control -id 'LibrariesSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Alpha')
Check-FolderAction 'LibraryOpenFolder' $alphaLibrary.SourceRoot 'LibraryScroll'
Check-FolderAction ('VersionOpenFolder_' + $alphaLibrary.Versions[0].Id.Replace('-', '')) ([IO.Path]::GetDirectoryName($alphaLibrary.Versions[0].SourcePath)) 'LibraryScroll'
Assert-VersionActions $alphaLibrary.Versions[0].Id.Replace('-', '')
Capture '20-library-folder-actions'
(Find-Control -id 'LibrariesSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('ReShade')
(Find-Control -id 'LibrariesSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('')
