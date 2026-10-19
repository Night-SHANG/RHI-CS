# Native geometry checks on the disposable runner, not simulated layout assertions.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Features) { throw 'CI feature harness required.' }
function Assert-ResourceRows([string]$phase) {
    $centers = @()
    foreach ($id in @($alpha, $beta, $reshade)) {
        Reveal-Control "SelectLibrary_$id" 'GamesScroll' | Out-Null
    }
    $viewer = (Find-Control -id 'GamesScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
    if ($viewer.Current.VerticallyScrollable) { $viewer.SetScrollPercent(-1, 100) }
    Start-Sleep -Milliseconds 250
    foreach ($id in @($alpha, $beta, $reshade)) {
        $check = (Find-Control -id "SelectLibrary_$id" -IncludeOffscreen).Current.BoundingRectangle
        $centers += $check.Top + $check.Height / 2
    }
    $ordered = @($centers | Sort-Object)
    $first = $ordered[1] - $ordered[0]
    $second = $ordered[2] - $ordered[1]
    $scale = [SmokeWindow]::GetDpiForWindow($process.MainWindowHandle) / 96.0
    if ([Math]::Abs($first - $second) -gt 2 * $scale -or $first -le 0) { throw "Unequal resource row heights: $phase; $first / $second" }
    if ([Math]::Min([Math]::Abs($first / $scale - 81), [Math]::Abs($first / $scale - 125)) -gt 2) { throw "Unexpected resource density: $phase; $first" }
    $evidence.Add("Equal resource row heights: $phase; spacing=$first; DPI scale=$scale")
    return $first
}
function Assert-VersionActions([string]$versionId) {
    $bounds = @('VersionView_', 'VersionRemove_', 'VersionOpenFolder_') | ForEach-Object {
        (Find-Control -id ($_ + $versionId) -IncludeOffscreen).Current.BoundingRectangle
    }
    for ($i = 1; $i -lt 3; $i++) {
        if ([Math]::Abs($bounds[$i].Top - $bounds[0].Top) -gt 2 -or $bounds[$i].Left -le $bounds[$i-1].Right) { throw 'Version actions are not on one horizontal row.' }
    }
}
