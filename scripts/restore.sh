#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

if [ $# -ne 1 ]; then
  echo "Uso:"
  echo "  ./scripts/restore.sh backups/archivo.dump"
  exit 1
fi

FILE="$1"

if [ ! -f "$FILE" ]; then
  echo "ERROR: no existe el archivo: $FILE"
  exit 1
fi

if ! docker compose ps --status running postgres | grep -q postgres; then
  echo "PostgreSQL no está ejecutándose. Inicia AutoControl QR primero:"
  echo "  docker compose up -d"
  exit 1
fi

echo "ATENCIÓN: esta operación reemplazará los datos actuales de AutoControl QR."
printf "Escribe RESTAURAR para continuar: "
read -r CONFIRM

if [ "$CONFIRM" != "RESTAURAR" ]; then
  echo "Restauración cancelada."
  exit 0
fi

echo "Creando respaldo de seguridad antes de restaurar..."
./scripts/backup.sh

echo "Cerrando conexiones activas..."
docker compose exec -T postgres psql -U autocontrolqr -d postgres -v ON_ERROR_STOP=1 <<'SQL'
SELECT pg_terminate_backend(pid)
FROM pg_stat_activity
WHERE datname='autocontrolqr'
  AND pid <> pg_backend_pid();
SQL

echo "Recreando la base de datos..."
docker compose exec -T postgres psql -U autocontrolqr -d postgres -v ON_ERROR_STOP=1 <<'SQL'
DROP DATABASE IF EXISTS autocontrolqr;
CREATE DATABASE autocontrolqr OWNER autocontrolqr;
SQL

echo "Restaurando $FILE..."
cat "$FILE" | docker compose exec -T postgres pg_restore \
  -U autocontrolqr \
  -d autocontrolqr \
  --no-owner \
  --no-acl \
  --exit-on-error

echo "✓ Restauración terminada correctamente"
echo "Reiniciando API y web..."
docker compose restart api web
