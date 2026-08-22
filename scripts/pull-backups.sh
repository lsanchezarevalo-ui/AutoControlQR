#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

DEST="${1:-$HOME/Backups/autocontrolqr}"
mkdir -p "$DEST"

echo "Descargando respaldos del servidor a $DEST ..."
rsync -av "root@67.205.190.114:/root/AutoControlQR_runnable_v31_6/backups/" "$DEST/" --include="*.dump" --exclude="*"
echo "✓ Listo. Respaldos en $DEST"
