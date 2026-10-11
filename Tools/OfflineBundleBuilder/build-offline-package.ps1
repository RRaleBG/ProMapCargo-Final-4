# Rebuilds the offline routing package for the mobile app (graph + places) after a
# new OSM import. Skips the work when the package already matches the graph in
# PostGIS. The API serves wwwroot/offline-bundles directly (mounted into the
# container), and the app offers the new version on its next sync.
#
#   pwsh Tools/OfflineBundleBuilder/build-offline-package.ps1            # Serbia
#   pwsh Tools/OfflineBundleBuilder/build-offline-package.ps1 -Force     # rebuild anyway
param(
    [string]$Pbf = "osm/serbia-latest.osm.pbf",
    [string]$Name = "serbia",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "../..")
Push-Location $repo
try {
    dotnet build Tools/OfflineBundleBuilder -c Release -nologo -v q
    $arguments = @("Tools/OfflineBundleBuilder/bin/Release/net10.0/ProMapCargo.OfflineBundleBuilder.dll", "wwwroot/offline-bundles", $Pbf, $Name)
    if ($Force) { $arguments += "--force" }
    dotnet @arguments
}
finally {
    Pop-Location
}
