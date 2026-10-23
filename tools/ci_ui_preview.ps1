# Bounded previews of the synthetic application-window captures only.
# No desktop capture, user uploads, environment dump, network, or credentials.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Disposable CI runner required.' }
Add-Type -AssemblyName System.Drawing
$paths = @(
    'artifacts/ui/failure.png',
    'artifacts/ui-features/failure.png',
    'artifacts/ui-visual/failure.png',
    'artifacts/ui-usability/failure.png',
    'artifacts/ui-features/22-busy-checked-snapshot.png',
    'artifacts/ui-features/33-protection-remove-confirm-cancel.png',
    'artifacts/ui-features/36-protection-record-restart-hidden.png',
    'artifacts/ui-visual/compact-dark-library-header.png',
    'artifacts/ui-visual/dpi-168-Light-game-form.png',
    'artifacts/ui-visual/dpi-192-Light-game-form.png',
    'artifacts/ui-usability/baseline-root-long.png'
)
$codec = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$emitted = 0
foreach ($path in $paths) {
    if (-not (Test-Path -LiteralPath $path) -or $emitted -ge 8) { continue }
    $source = [Drawing.Image]::FromFile((Join-Path (Get-Location) $path))
    $scaled = $null; $graphics = $null; $stream = $null; $parameters = $null
    try {
        $ratio = [Math]::Min(1.0, [Math]::Min(1280.0 / $source.Width, 960.0 / $source.Height))
        $scaled = [Drawing.Bitmap]::new([int]($source.Width * $ratio), [int]($source.Height * $ratio))
        $graphics = [Drawing.Graphics]::FromImage($scaled)
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($source, 0, 0, $scaled.Width, $scaled.Height)
        $parameters = [Drawing.Imaging.EncoderParameters]::new(1)
        $parameters.Param[0] = [Drawing.Imaging.EncoderParameter]::new([Drawing.Imaging.Encoder]::Quality, [long]65)
        $stream = [IO.MemoryStream]::new()
        $scaled.Save($stream, $codec, $parameters)
        if ($stream.Length -gt 180000) { throw "Synthetic preview exceeds bounded size: $path" }
        Write-Host ('RHI_UI_PREVIEW|' + $path + '|' + [Convert]::ToBase64String($stream.ToArray()))
        $emitted++
    } finally {
        if ($parameters) { $parameters.Dispose() }
        if ($stream) { $stream.Dispose() }
        if ($graphics) { $graphics.Dispose() }
        if ($scaled) { $scaled.Dispose() }
        $source.Dispose()
    }
}
Write-Host "Synthetic window previews emitted: $emitted (maximum 8)."
