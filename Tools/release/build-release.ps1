# Builds the signed Release APK of the mobile app with the release key
# (see create-signing-key.ps1) and optionally installs it on a connected device.
#
#   pwsh Tools/release/build-release.ps1                    # APK in ProMapCargo.Mobile/bin/release-publish
#   pwsh Tools/release/build-release.ps1 -Install           # also install via adb
#   pwsh Tools/release/build-release.ps1 -Version 1.1 -Build 2
#
# The password is asked for and only passed to this build through an environment
# variable; it is not stored anywhere.
param(
    [string]$KeystorePath = (Join-Path $env:USERPROFILE ".promap\promapcargo-release.keystore"),
    [string]$Version,
    [int]$Build,
    [switch]$Install,
    [string]$Device
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $KeystorePath)) {
    throw "Ključ ne postoji ($KeystorePath). Prvo: pwsh Tools/release/create-signing-key.ps1"
}

$repo = Resolve-Path (Join-Path $PSScriptRoot "../..")
$project = Join-Path $repo "ProMapCargo.Mobile/ProMapCargo.Mobile.csproj"
$output = Join-Path $repo "ProMapCargo.Mobile/bin/release-publish"

$secure = Read-Host "Lozinka ključa" -AsSecureString
$env:PROMAP_KEYSTORE_PASS = [System.Net.NetworkCredential]::new("", $secure).Password
try {
    $arguments = @("publish", $project, "-f", "net10.0-android", "-c", "Release", "-nologo", "-v", "q",
        "-p:AndroidPackageFormat=apk", "-p:PromapKeystore=$KeystorePath", "-o", $output)
    if ($Version) { $arguments += "-p:ApplicationDisplayVersion=$Version" }
    if ($Build) { $arguments += "-p:ApplicationVersion=$Build" }
    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Build nije uspeo." }
}
finally {
    Remove-Item Env:PROMAP_KEYSTORE_PASS -ErrorAction SilentlyContinue
}

$apk = Get-ChildItem $output -Filter "*-Signed.apk" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "Potpisan APK: $($apk.FullName)" -ForegroundColor Green

if ($Install) {
    $adb = "C:/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe"
    $target = if ($Device) { @("-s", $Device) } else { @() }
    & $adb @target install -r --no-incremental $apk.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Instalacija nije uspela. Ako piše INSTALL_FAILED_UPDATE_INCOMPATIBLE, na uređaju je" -ForegroundColor Yellow
        Write-Host "verzija potpisana drugim (debug) ključem: deinstalirajte je jednom, pa ponovite." -ForegroundColor Yellow
        Write-Host "Pažnja: deinstalacija briše preuzete mape i pakete na uređaju." -ForegroundColor Yellow
    }
}
