#!/usr/bin/env bash
set -euo pipefail

dotnet restore ProMapCargo.sln
dotnet build ProMapCargo.sln --configuration Release --no-restore

echo "ProMapCargo solution build completed successfully."
