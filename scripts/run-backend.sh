#!/usr/bin/env bash
# macOS/Linux counterpart to run-backend.ps1.
#
#   ./scripts/run-backend.sh            -> http://localhost:5080
#   PORT=5090 ./scripts/run-backend.sh  -> http://localhost:5090
#
# Port 5080 rather than the 5000 the PowerShell script uses: on macOS,
# 5000 belongs to ControlCenter's AirPlay Receiver.
#
# Secrets come from `dotnet user-secrets`, never from a file in the tree.
# See docs/notes/local-setup.md for the values this needs.
set -euo pipefail

cd "$(dirname "$0")/.."

PORT="${PORT:-5080}"

# The project targets net8.0 and this machine ships only the .NET 10 runtime.
# Roll-forward lets the same build run without a second SDK install.
export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"

# --no-launch-profile matters: the first profile in launchSettings.json is
# "Production", and dotnet run would apply its ASPNETCORE_ENVIRONMENT over the
# one exported above. User secrets only load in Development, so without this
# the connection string silently goes missing.
exec dotnet run --project KidsAdventuresAPI/KidsAdventuresAPI.csproj \
  --no-launch-profile \
  --urls "http://localhost:${PORT}"
