# Creates the release signing key for the ProMap Cargo Android app.
#
#   pwsh Tools/release/create-signing-key.ps1
#
# The key is stored OUTSIDE the repository (default: %USERPROFILE%\.promap\) and the
# password is asked for here and never written anywhere. Keep a backup of the
# keystore file and the password in a safe place: without them no update of the app
# can be published under the same signature (Google Play rejects a different key).
param(
    [string]$KeystorePath = (Join-Path $env:USERPROFILE ".promap\promapcargo-release.keystore"),
    [string]$Alias = "promapcargo",
    [string]$DistinguishedName = "CN=ProMap Cargo, O=ProMap Cargo, C=RS"
)

$ErrorActionPreference = "Stop"

if (Test-Path $KeystorePath) {
    Write-Host "Ključ već postoji: $KeystorePath" -ForegroundColor Yellow
    Write-Host "Ne pravim novi (aplikacija mora da ostane potpisana istim ključem)."
    exit 1
}

$keytool = Get-ChildItem "C:\Program Files\Microsoft", "C:\Program Files\Android", "C:\Program Files (x86)\Android", "C:\Program Files\Java" `
    -Recurse -Filter keytool.exe -ErrorAction SilentlyContinue -Depth 4 | Select-Object -First 1
if (-not $keytool) { throw "keytool.exe nije pronađen (instalirajte JDK)." }

$first = Read-Host "Lozinka za ključ (najmanje 6 znakova)" -AsSecureString
$second = Read-Host "Ponovite lozinku" -AsSecureString
$plain = [System.Net.NetworkCredential]::new("", $first).Password
if ($plain -ne [System.Net.NetworkCredential]::new("", $second).Password) { throw "Lozinke se ne poklapaju." }
if ($plain.Length -lt 6) { throw "Lozinka mora imati najmanje 6 znakova." }

New-Item -ItemType Directory -Force (Split-Path $KeystorePath) | Out-Null

# The password goes to keytool through an environment variable of this process only.
$env:PROMAP_KEYSTORE_PASS = $plain
try {
    & $keytool.FullName -genkeypair -v `
        -keystore $KeystorePath -alias $Alias `
        -keyalg RSA -keysize 4096 -validity 10000 `
        -dname $DistinguishedName `
        -storepass:env PROMAP_KEYSTORE_PASS -keypass:env PROMAP_KEYSTORE_PASS
    if ($LASTEXITCODE -ne 0) { throw "keytool nije uspeo." }

    Write-Host ""
    Write-Host "Ključ je napravljen: $KeystorePath" -ForegroundColor Green
    & $keytool.FullName -list -v -keystore $KeystorePath -alias $Alias -storepass:env PROMAP_KEYSTORE_PASS |
        Select-String "SHA256:|Valid"
    Write-Host ""
    Write-Host "VAŽNO: napravite rezervnu kopiju ovog fajla i zapamtite lozinku." -ForegroundColor Yellow
}
finally {
    Remove-Item Env:PROMAP_KEYSTORE_PASS -ErrorAction SilentlyContinue
    $plain = $null
}
