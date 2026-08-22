# AutoControl QR — Infraestructura, ubicaciones y respaldo

Documento de referencia con todo lo necesario para recuperar, respaldar o asegurar este proyecto. Última actualización: 2026-08-22 (segunda revisión — repo remoto y respaldos ya resueltos).

## 1. Qué es

AutoControl QR: SaaS de control de mantenimiento vehicular con QR, multiempresa. Roles: PLATFORM_ADMIN, COMPANY_ADMIN, TECHNICIAN, más flujo público sin login para conductores (vía token QR).

## 2. Ubicación del código

- **Carpeta local (tu Mac):** `/Users/luismiguelsanchez/Desktop/Prueba Autocontrol`
- **Control de versiones:** Git, rama `main`, 20 commits. **Remoto privado:** [github.com/lsanchezarevalo-ui/AutoControlQR](https://github.com/lsanchezarevalo-ui/AutoControlQR) (privado). El código ya está respaldado fuera de tu Mac. Acceso vía SSH con la llave `~/.ssh/id_ed25519` (la misma que usas para el servidor; su clave pública está agregada en GitHub → Settings → SSH and GPG keys).
  - Para subir cambios futuros: `git push` (puede requerir tu confirmación explícita cada vez, según el modo de permisos activo).
- **Documentos de trabajo** (no forman parte del código, no se despliegan): `Sugiero hacer los siguientes cambios.docx`, `Mejora 2.docx`, y similares en el Escritorio — son tus notas, excluidos del repo.

### Estructura de carpetas
```
Prueba Autocontrol/
├── backend/              API en .NET 8 (Minimal API)
│   ├── Program.cs        Arranque, middleware, CORS, rate limiting
│   ├── Models.cs          Todos los DTOs/records de request-response
│   ├── Helpers.cs         JWT, cálculo de estado de mantenimiento, catálogo de servicios
│   ├── Data/Schema.cs     Migraciones aditivas (EnsureV5Schema, V6, V12... V32, V33)
│   ├── Endpoints/         Un archivo por módulo (Vehicles, Plans, Maintenance, Users, etc.)
│   └── Dockerfile
├── frontend/              SPA en JavaScript vanilla (sin build, sin framework)
│   ├── index.html         Carga todos los <script> en orden (scope global compartido)
│   ├── styles.css         Un solo archivo de estilos para toda la app
│   ├── js/                Un archivo por pantalla/módulo
│   └── Dockerfile         Sirve todo con nginx
├── database/init/         SQL base (tablas fundacionales: companies, users, vehicles...)
├── docker-compose.yml     Entorno de desarrollo local
├── docker-compose.prod.yml Entorno de producción (usa volumen externo + Caddy)
├── deploy/Caddyfile       Config de HTTPS automático
├── scripts/               backup.sh, restore.sh, prod-backup.sh, prod-up.sh, prod-down.sh
├── .env.production.example Plantilla de variables secretas (sin valores reales)
└── backups/               Respaldos .dump generados localmente (excluidos de git)
```

## 3. Stack tecnológico

| Capa | Tecnología |
|---|---|
| Backend | C# / .NET 8, ASP.NET Core Minimal API |
| Base de datos | PostgreSQL 16 (imagen `postgres:16-alpine`), acceso vía Npgsql (SQL parametrizado, sin ORM) |
| Autenticación | JWT (`Microsoft.AspNetCore.Authentication.JwtBearer`), claims: user_id, company_id, role |
| QR | Librería `QRCoder` |
| Frontend | JavaScript vanilla, HTML/CSS planos — sin React/Vue/build step |
| Servidor web frontend | nginx (sirve archivos estáticos) |
| Proxy/HTTPS | Caddy 2 (certificados automáticos vía Let's Encrypt) |
| Contenedores | Docker + Docker Compose |
| Dependencias backend | Npgsql 8.0.5, JwtBearer 8.0.8, QRCoder 1.6.0 (ver `backend/AutoControlQR.Api.csproj`) |

## 4. Servidor de producción

- **Proveedor:** DigitalOcean (Droplet)
- **IP:** `67.205.190.114`
- **Sistema operativo:** Ubuntu 24.04.4 LTS
- **Acceso SSH:** `ssh root@67.205.190.114`
  - Llave privada local: `~/.ssh/id_ed25519` (protegida con passphrase — nunca la compartas, ni siquiera conmigo)
  - Llave pública: `~/.ssh/id_ed25519.pub`
- **Dominio:** `autocontrolqr.com` — subdominio de la app: `app.autocontrolqr.com`
- **Carpeta del proyecto en el servidor:** `/root/AutoControlQR_runnable_v31_6/` (misma estructura que en tu Mac)
- **Firewall (ufw):** activo — solo permite SSH (22), HTTP (80) y HTTPS (443/tcp+udp). Correcto y mínimo.
- **Disco:** 77 GB totales, 5 GB usados (7%) — amplio margen.
- **Base de datos actual:** 1 empresa, 10 vehículos activos, 3 usuarios, ~8.9 MB de datos.

### Contenedores en producción (docker-compose.prod.yml)
| Contenedor | Rol |
|---|---|
| `...-postgres-1` | Base de datos, usa el **volumen externo permanente** `autocontrolqr_prod_pgdata` |
| `...-api-1` | Backend .NET |
| `...-web-1` | Frontend (nginx) |
| `...-caddy-1` | Proxy HTTPS automático, expone 80/443 al público |

⚠️ **El volumen `autocontrolqr_prod_pgdata` es la única fuente de verdad de todos los datos reales (empresas, vehículos, usuarios, historial). Nunca debe borrarse ni recrearse sin respaldo previo.**

### Variables secretas
Viven en `/root/AutoControlQR_runnable_v31_6/.env.production` (permisos `600`, solo root puede leerlo; no está en git, nunca se ha mostrado en esta conversación):
- `DOMAIN` — dominio público
- `POSTGRES_PASSWORD` — contraseña de la base de datos
- `JWT_KEY` — clave de firma de sesiones

Plantilla de referencia (sin valores): `.env.production.example` en el repo.

## 5. Cómo desplegar cambios (flujo ya establecido)

```bash
# 1. Respaldo antes de tocar nada
ssh root@67.205.190.114 "cd /root/AutoControlQR_runnable_v31_6 && ./scripts/prod-backup.sh"

# 2. Sincronizar código (dry-run primero para revisar)
rsync -avn --exclude='.env.production' --exclude='backups/' --exclude='.git/' --exclude='.DS_Store' --exclude='*.docx' ./ root@67.205.190.114:/root/AutoControlQR_runnable_v31_6/
rsync -av  --exclude='.env.production' --exclude='backups/' --exclude='.git/' --exclude='.DS_Store' --exclude='*.docx' ./ root@67.205.190.114:/root/AutoControlQR_runnable_v31_6/

# 3. Reconstruir solo lo que cambió
ssh root@67.205.190.114 "cd /root/AutoControlQR_runnable_v31_6 && docker compose --env-file .env.production -f docker-compose.prod.yml up -d --build api web"

# 4. Verificar
curl -I https://app.autocontrolqr.com/
```

## 6. Respaldos

- `scripts/prod-backup.sh` genera un `.dump` de PostgreSQL en `/root/AutoControlQR_runnable_v31_6/backups/` en el servidor, y borra automáticamente los de más de 30 días.
- **Cron diario a las 3am** (hora del servidor) ejecuta este script solo, sin intervención manual. Log en `backups/backup.log`.
- `scripts/pull-backups.sh` baja con un solo comando los `.dump` del servidor a `~/Backups/autocontrolqr/` en tu Mac — este es el respaldo que realmente vive fuera del servidor.
- `scripts/restore.sh` permite restaurar desde un `.dump` (pide confirmación explícita escribiendo "RESTAURAR").

✅ Resuelto: los respaldos ya no dependen únicamente del mismo servidor — corre `./scripts/pull-backups.sh` periódicamente (por ejemplo, cada vez que hagas un despliegue grande) para mantener la copia en tu Mac al día.

## 7. Estado de las recomendaciones

1. ✅ **Respaldos de BD fuera del servidor** — resuelto. `~/Backups/autocontrolqr/` en tu Mac + `scripts/pull-backups.sh` para actualizarlo cuando quieras.
2. ✅ **Repositorio remoto privado** — resuelto. Código en `github.com/lsanchezarevalo-ui/AutoControlQR` (privado).
3. ✅ **Respaldo automatizado** — resuelto. Cron diario a las 3am en el servidor.
4. ✅ **Limpieza de volúmenes viejos** — resuelto. Se eliminaron 10 volúmenes huérfanos (`v4`, `v30_4`, `v31_3`, `v31_4`); solo quedan los 3 que usan los contenedores activos.
5. ⏳ **Rotar `JWT_KEY`** — pendiente, a criterio tuyo. Invalida todas las sesiones activas al hacerlo, así que conviene avisar a los usuarios antes.
6. ⏳ **Guardar la passphrase de tu llave SSH** (`~/.ssh/id_ed25519`) en un gestor de contraseñas — acción personal tuya, es la única llave con acceso al servidor y ahora también a GitHub.

## 8. Contacto/plataforma

- Cuenta PLATFORM_ADMIN: `platform@autocontrol.local` (contraseña inicial fue generada aleatoriamente al crear el servidor — si no la guardaste, puede resetearse por base de datos).
