# Read-only native geometry from the current dialog, never the UIA subtree union.
function Get-FormViewport {
    $path = Join-Path $captureRoot 'stderr.txt'
    $reason = 'No current geometry snapshot.'
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            $reader = [IO.StreamReader]::new($stream)
            try {
                if ($stream.Length -gt 8388608) { throw 'Geometry evidence exceeded its bound.' }
                $records = @($reader.ReadToEnd() -split "`r?`n" | Where-Object { $_.StartsWith('RHI_UI_GEOMETRY:') })
            } finally { $reader.Dispose() }
            if ($records.Count -eq 0) { throw 'No native form geometry.' }
            $latest = $records[-1].Substring('RHI_UI_GEOMETRY:'.Length) | ConvertFrom-Json
            if ($latest.kind -ne 'snapshot') { throw "Current geometry is $($latest.kind), not an active snapshot." }
            $opens = @($records | Where-Object { $_.StartsWith('RHI_UI_GEOMETRY:{"kind":"open",') })
            if ($opens.Count -eq 0) { throw 'Missing form generation marker.' }
            $opened = $opens[-1].Substring('RHI_UI_GEOMETRY:'.Length) | ConvertFrom-Json
            if ($opened.id -ne $latest.id -or $latest.sequence -le $opened.sequence) {
                throw 'Stale or unordered form geometry.'
            }
            $geometry = $latest.geometry
            $scale = [SmokeWindow]::GetDpiForWindow($process.MainWindowHandle) / 96.0
            if ([Math]::Abs($geometry.scale - $scale) -gt 0.001) { throw 'Native geometry uses a stale DPI scale.' }
            $origin = [SmokeWindow+Point]::new()
            if (-not [SmokeWindow]::ClientToScreen($process.MainWindowHandle, [ref]$origin)) { throw 'Cannot map native geometry to the desktop.' }
            $view = $geometry.viewport
            $values = @($view.x, $view.y, $view.width, $view.height, $geometry.scale)
            foreach ($value in $values) {
                if ([double]::IsNaN([double]$value) -or [double]::IsInfinity([double]$value)) { throw 'Non-finite native geometry.' }
            }
            if ($view.width -le 0 -or $view.height -le 0) { throw 'Empty native viewport.' }
            $rect = [Windows.Rect]::new($origin.X + $view.x * $scale, $origin.Y + $view.y * $scale, $view.width * $scale, $view.height * $scale)
            $feedback = Find-OptionalId 'FormValidation'
            $visible = $feedback -and -not $feedback.Current.IsOffscreen -and $feedback.Current.Name
            if ([bool]$geometry.feedbackVisible -ne [bool]$visible) { throw 'Feedback visibility has not settled.' }
            if ($visible) {
                $actual = $feedback.Current.BoundingRectangle
                $native = $geometry.feedback
                if ([Math]::Abs($actual.Left - ($origin.X + $native.x * $scale)) -gt 2 -or
                    [Math]::Abs($actual.Top - ($origin.Y + $native.y * $scale)) -gt 2 -or
                    [Math]::Abs($actual.Height - $native.height * $scale) -gt 2) {
                    throw 'Native and UIA feedback coordinates do not agree.'
                }
            }
            $key = "$($latest.id)/$($latest.sequence)"
            if ($script:lastGeometryEvidence -ne $key) {
                Write-Host "Native form viewport: DPI=$($scale * 96); id=$key; rect=$rect; client=$($origin.X),$($origin.Y)"
                $script:lastGeometryEvidence = $key
            }
            return $rect
        } catch { $reason = $_.Exception.Message; Start-Sleep -Milliseconds 100 }
    }
    throw "Cannot establish current native form viewport: $reason"
}
