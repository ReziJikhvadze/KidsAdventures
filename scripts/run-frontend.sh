#!/usr/bin/env bash
# macOS/Linux counterpart to run-frontend.ps1.
#
#   ./scripts/run-frontend.sh      -> http://localhost:8080
#
# Vite 7 needs Node >=20.19. The Node on a bare PATH here is 18.x, so pick up
# nvm's version first when nvm is installed; otherwise trust whatever is on
# PATH and let npm complain.
set -euo pipefail

cd "$(dirname "$0")/.."

if [ -s "${NVM_DIR:-$HOME/.nvm}/nvm.sh" ]; then
  # shellcheck disable=SC1090
  . "${NVM_DIR:-$HOME/.nvm}/nvm.sh"
  nvm use 22 >/dev/null 2>&1 || nvm use default >/dev/null 2>&1 || true
fi

cd KidsAdventuresAPI/wwwroot

[ -d node_modules ] || npm install

echo "node $(node --version)"
# Port is overridable: 8080 is CORS-allowed but currently held by another project's
# process-compose on this machine; 5173 is the other CORS-allowed dev port.
exec npm run dev -- --port "${PORT:-5173}"
