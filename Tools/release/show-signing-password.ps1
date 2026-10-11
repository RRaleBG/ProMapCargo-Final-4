# Shows the generated release-key password (stored DPAPI-encrypted, readable only by
# this Windows user) so it can be written into a password manager as a backup.
#
#   pwsh Tools/release/show-signing-password.ps1
param(
    [string]$PasswordFile = (Join-Path $env:USERPROFILE ".promap\keystore-password.dpapi")
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $PasswordFile)) { throw "Nema sačuvane lozinke: $PasswordFile" }
$secure = Get-Content $PasswordFile | ConvertTo-SecureString
Write-Host ([System.Net.NetworkCredential]::new("", $secure).Password)
Write-Host "Upišite je u menadžer lozinki zajedno sa rezervnom kopijom ključa." -ForegroundColor Yellow
