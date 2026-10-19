# Builds the signed Release APK of the mobile app with the release key
# (see create-signing-key.ps1) and optionally installs it on a connected device.
#
#   pwsh Tools/release/build-release.ps1                    # APK in ProMapCargo.Mobile/bin/release-publish
#   pwsh Tools/release/build-release.ps1 -Install           # also install via adb
#   pwsh Tools/release/build-release.ps1 -Version 1.1 -Build 2
#   pwsh Tools/release/build-release.ps1 -Aab               # .aab for Google Play
#   pwsh Tools/release/build-release.ps1 -Version 1.1 -Build 2 -Publish -Notes "Šta je novo"
#                                                           # the server offers it to the phones
#
# The password is asked for and only passed to this build through an environment
# variable; it is not stored anywhere.
param(
    [string]$KeystorePath = (Join-Path $env:USERPROFILE ".promap\promapcargo-release.keystore"),
    [string]$Version,
    [int]$Build,
    [switch]$Install,
    [switch]$Aab,
    [string]$Device,
    # Copies the APK to app-releases/ on this server: the phones see the new version
    # (api/mobile/app/latest) and offer the update. -Build must be higher than the installed one.
    [switch]$Publish,
    [string]$Notes
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $KeystorePath)) {
    throw "Ključ ne postoji ($KeystorePath). Prvo: pwsh Tools/release/create-signing-key.ps1"
}

$repo = Resolve-Path (Join-Path $PSScriptRoot "../..")
$project = Join-Path $repo "ProMapCargo.Mobile/ProMapCargo.Mobile.csproj"
$output = Join-Path $repo "ProMapCargo.Mobile/bin/release-publish"

# Password: from the DPAPI-protected file next to the key (only this Windows user can
# decrypt it, see create-signing-key.ps1 -GeneratePassword), otherwise asked for.
$passwordFile = Join-Path (Split-Path $KeystorePath) "keystore-password.dpapi"
$secure = if (Test-Path $passwordFile) {
    Get-Content $passwordFile | ConvertTo-SecureString
} else {
    Read-Host "Lozinka ključa" -AsSecureString
}
$env:PROMAP_KEYSTORE_PASS = [System.Net.NetworkCredential]::new("", $secure).Password
try {
    $arguments = @("publish", $project, "-f", "net10.0-android", "-c", "Release", "-nologo", "-v", "q",
        "-p:AndroidPackageFormat=$(if ($Aab) { 'aab' } else { 'apk' })", "-p:PromapKeystore=$KeystorePath", "-o", $output)
    if ($Version) { $arguments += "-p:ApplicationDisplayVersion=$Version" }
    if ($Build) { $arguments += "-p:ApplicationVersion=$Build" }
    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Build nije uspeo." }
}
finally {
    Remove-Item Env:PROMAP_KEYSTORE_PASS -ErrorAction SilentlyContinue
}

$apk = Get-ChildItem $output -Filter "*-Signed.$(if ($Aab) { 'aab' } else { 'apk' })" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "Potpisan paket: $($apk.FullName)" -ForegroundColor Green

if ($Publish -and -not $Aab) {
    [xml]$csproj = Get-Content $project
    $versionName = if ($Version) { $Version } else { @($csproj.Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ })[0] }
    $versionCode = if ($Build) { $Build } else { [int]@($csproj.Project.PropertyGroup.ApplicationVersion | Where-Object { $_ })[0] }
    $releases = Join-Path $repo "app-releases"
    New-Item -ItemType Directory -Force $releases | Out-Null
    Copy-Item $apk.FullName (Join-Path $releases "promap-cargo.apk") -Force
    [ordered]@{
        versionCode = $versionCode
        versionName = $versionName
        notes = $Notes
        publishedAt = (Get-Date).ToUniversalTime().ToString("o")
    } | ConvertTo-Json | Set-Content (Join-Path $releases "version.json") -Encoding utf8
    Write-Host "Objavljeno na serveru: verzija $versionName (build $versionCode) u $releases" -ForegroundColor Green
}

if ($Install -and -not $Aab) {
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
