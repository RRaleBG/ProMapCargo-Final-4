# Writes "<file>.sha256" (sha256sum format) next to every published map and routing package
# whose checksum is missing or older than the file. The API puts the checksums in its
# catalogs and the app verifies each download against them. Run by update-all.ps1.
#
#   pwsh scripts/write-checksums.ps1
param(
    [string[]]$Folders = @("wwwroot/offline-bundles", "wwwroot/maps")
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

foreach ($folder in $Folders) {
    $path = Join-Path $repo $folder
    if (-not (Test-Path $path)) { continue }
    $files = Get-ChildItem $path -File | Where-Object { $_.Extension -in ".graphbundle", ".places", ".pmtiles" }
    foreach ($file in $files) {
        $sidecar = "$($file.FullName).sha256"
        if ((Test-Path $sidecar) -and (Get-Item $sidecar).LastWriteTimeUtc -ge $file.LastWriteTimeUtc) { continue }
        $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -Path $sidecar -Value "$hash  $($file.Name)" -NoNewline
        Write-Host "  $($file.Name): $hash"
    }
}
