#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p backups
STAMP="$(date +'%Y%m%d_%H%M%S')"
FILE="backups/autocontrolqr_prod_${STAMP}.dump"
docker compose --env-file .env.production -f docker-compose.prod.yml exec -T postgres \
  pg_dump -U autocontrolqr -d autocontrolqr -Fc --no-owner --no-acl > "$FILE"
test -s "$FILE"
echo "✓ Respaldo: $FILE"

# Conserva solo los últimos 30 días de respaldos locales (el respaldo real vive fuera del servidor).
find backups -name "autocontrolqr_prod_*.dump" -mtime +30 -delete
