# Native geometry checks on the disposable runner, not simulated layout assertions.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not ($Features -or $Visual)) { throw 'CI feature or visual harness required.' }
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
    $bounds = @('VersionEdit_', 'VersionRemove_', 'VersionOpenFolder_') | ForEach-Object {
        (Find-Control -id ($_ + $versionId) -IncludeOffscreen).Current.BoundingRectangle
    }
    for ($i = 1; $i -lt 3; $i++) {
        if ([Math]::Abs($bounds[$i].Top - $bounds[0].Top) -gt 2 -or $bounds[$i].Left -le $bounds[$i-1].Right) { throw 'Version actions are not on one horizontal row.' }
    }
}

function Assert-HeaderActions([string]$page) {
    $ids = if ($page -eq 'Game') { @('GameEdit', 'GameInspectTargets', 'GameRelocate', 'GameOpenFolder') }
        else { @('LibraryEdit', 'LibraryAddVersion', 'LibraryRemove', 'LibraryOpenFolder', 'LibraryRelocate') }
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $bounds = @()
    foreach ($id in $ids) {
        $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
        $node = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
        if (-not $node -and $id -eq 'GameRelocate') { continue }
        if (-not $node) { throw "Missing header action: $id" }
        Assert-InWindow $node
        $bounds += $node.Current.BoundingRectangle
    }
    $scale = [SmokeWindow]::GetDpiForWindow($process.MainWindowHandle) / 96.0
    $first = $bounds[0]
    $rows = @($bounds | Group-Object { [Math]::Round($_.Top / $scale) })
    $columns = $rows[0].Count
    $scrollId = if ($page -eq 'Game') { 'GamesScroll' } else { 'LibraryScroll' }
    $available = (Find-Control -id $scrollId).Current.BoundingRectangle.Width / $scale
    # The native vertical scrollbar can consume up to 16 DIP of the content viewport.
    $minimum = [Math]::Max(1, [Math]::Min($bounds.Count, [Math]::Floor(($available - 16) / 188)))
    $maximum = [Math]::Max(1, [Math]::Min($bounds.Count, [Math]::Floor($available / 188)))
    if ($columns -lt $minimum -or $columns -gt $maximum) { throw "Unnecessary header wrapping: $page; expected=$minimum..$maximum; columns=$columns" }
    for ($i = 0; $i -lt $bounds.Count; $i++) {
        $box = $bounds[$i]
        if ([Math]::Abs($box.Width / $scale - 180) -gt 2 -or [Math]::Abs($box.Height / $scale - 36) -gt 2) {
            throw "Unequal or clipped header action: $page; $box"
        }
        $left = $first.Left + ($i % $columns) * 188 * $scale
        $top = $first.Top + [Math]::Floor($i / $columns) * 44 * $scale
        if ([Math]::Abs($box.Left - $left) -gt 2 * $scale -or [Math]::Abs($box.Top - $top) -gt 2 * $scale) {
            throw "Header actions are not consistently aligned: $page; $box"
        }
    }
    $evidence.Add("PASS: $page header; buttons=$($bounds.Count); columns=$columns; size=180x36; gap=8; DPI=$scale")
}
