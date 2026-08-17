#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
mkdir -p backups

STAMP="$(date +'%Y%m%d_%H%M%S')"
FILE="backups/autocontrolqr_${STAMP}.dump"

if ! docker compose ps --status running postgres | grep -q postgres; then
  echo "PostgreSQL no está ejecutándose. Inicia AutoControl QR primero:"
  echo "  docker compose up -d"
  exit 1
fi

echo "Creando respaldo..."
docker compose exec -T postgres pg_dump \
  -U autocontrolqr \
  -d autocontrolqr \
  -Fc \
  --no-owner \
  --no-acl > "$FILE"

if [ ! -s "$FILE" ]; then
  echo "ERROR: el respaldo quedó vacío."
  rm -f "$FILE"
  exit 1
fi

SIZE="$(du -h "$FILE" | awk '{print $1}')"
echo "✓ Respaldo creado correctamente"
echo "  Archivo: $FILE"
echo "  Tamaño:  $SIZE"
