#!/usr/bin/env bash
# Ephemeral local SQL Server for development.
#
#   ./scripts/local-db.sh up      start it (data lives in RAM)
#   ./scripts/local-db.sh down    stop and discard
#   ./scripts/local-db.sh reset   down + up, i.e. a clean schema
#   ./scripts/local-db.sh psql    open a sqlcmd session
#
# The data directory is a tmpfs, so every restart gives a blank server and the
# API's migrator replays Data/Scripts from 001. Nothing survives a reboot and
# nothing touches a real database.
#
# sqlcmd talks to the container with encryption disabled (-N disable). The engine
# generates a fresh self-signed certificate on every container start, and go-sqlcmd
# refuses certificates whose serial number parses negative — a coin flip per start.
# The API is immune for the same reason: its connection string says Encrypt=False.
#
# The SA password is a fixed local constant on purpose: the port is bound to
# 127.0.0.1 only, and a throwaway container that resets on restart has no
# secret worth protecting. Never reuse it anywhere else.
set -euo pipefail

CONTAINER="${CONTAINER:-adventrya-mssql}"
# Azure SQL Edge is the SQL Server engine built for arm64. On an Intel machine
# set IMAGE=mcr.microsoft.com/mssql/server:2022-latest for the full edition.
IMAGE="${IMAGE:-mcr.microsoft.com/azure-sql-edge:latest}"
SA_PASSWORD="${SA_PASSWORD:-Adventrya!Local1}"
DB_NAME="${DB_NAME:-adventuresapi-database}"
PORT="${PORT:-1433}"
TMPFS_SIZE="${TMPFS_SIZE:-3g}"

conn_string() {
  echo "Server=tcp:127.0.0.1,${PORT};Initial Catalog=${DB_NAME};User ID=sa;Password=${SA_PASSWORD};Encrypt=False;TrustServerCertificate=True;Connection Timeout=30;"
}

wait_ready() {
  echo -n "waiting for SQL Server"
  for _ in $(seq 1 90); do
    if sqlcmd -S "127.0.0.1,${PORT}" -U sa -P "${SA_PASSWORD}" -C -N disable -Q "SELECT 1" >/dev/null 2>&1; then
      echo " ready"
      return 0
    fi
    echo -n "."
    sleep 2
  done
  echo " TIMED OUT"
  echo "--- container log ---"
  docker logs --tail 40 "${CONTAINER}" || true
  return 1
}

up() {
  if docker ps --format '{{.Names}}' | grep -qx "${CONTAINER}"; then
    echo "${CONTAINER} already running."
  else
    docker rm -f "${CONTAINER}" >/dev/null 2>&1 || true
    docker run -d --name "${CONTAINER}" \
      -e "ACCEPT_EULA=1" \
      -e "MSSQL_SA_PASSWORD=${SA_PASSWORD}" \
      -e "MSSQL_PID=Developer" \
      -p "127.0.0.1:${PORT}:1433" \
      --tmpfs "/var/opt/mssql:rw,size=${TMPFS_SIZE},mode=1777" \
      "${IMAGE}" >/dev/null
    echo "started ${CONTAINER} (${IMAGE})"
  fi

  wait_ready
  sqlcmd -S "127.0.0.1,${PORT}" -U sa -P "${SA_PASSWORD}" -C -N disable \
    -Q "IF DB_ID(N'${DB_NAME}') IS NULL CREATE DATABASE [${DB_NAME}];"
  echo "database ${DB_NAME} ready"
  echo
  echo "Point the API at it, once:"
  echo "  dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" '$(conn_string)' \\"
  echo "    --project KidsAdventuresAPI/KidsAdventuresAPI.csproj"
}

case "${1:-up}" in
  up)    up ;;
  down)  docker rm -f "${CONTAINER}" >/dev/null 2>&1 && echo "removed ${CONTAINER}" || echo "not running" ;;
  reset) "$0" down; "$0" up ;;
  psql)  sqlcmd -S "127.0.0.1,${PORT}" -U sa -P "${SA_PASSWORD}" -C -N disable -d "${DB_NAME}" ;;
  conn)  conn_string ;;
  *)     echo "usage: $0 {up|down|reset|psql|conn}" >&2; exit 1 ;;
esac
