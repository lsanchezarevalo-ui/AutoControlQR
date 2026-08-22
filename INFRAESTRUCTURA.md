# AutoControl QR — Infraestructura, ubicaciones y respaldo

Documento de referencia con todo lo necesario para recuperar, respaldar o asegurar este proyecto. Última actualización: 2026-08-22.

## 1. Qué es

AutoControl QR: SaaS de control de mantenimiento vehicular con QR, multiempresa. Roles: PLATFORM_ADMIN, COMPANY_ADMIN, TECHNICIAN, más flujo público sin login para conductores (vía token QR).

## 2. Ubicación del código

- **Carpeta local (tu Mac):** `/Users/luismiguelsanchez/Desktop/Prueba Autocontrol`
- **Control de versiones:** Git local, rama `main`, 18 commits. **No tiene remoto configurado** (ni GitHub, ni GitLab, ni ningún backup fuera de tu Mac). Ver sección 7, gap #1.
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

## 6. Respaldos — estado actual y **el hueco importante**

- `scripts/prod-backup.sh` genera un `.dump` de PostgreSQL en `/root/AutoControlQR_runnable_v31_6/backups/` en el servidor.
- Hay 11 respaldos ahí ahora mismo, el más reciente de hoy.
- `scripts/restore.sh` permite restaurar desde un `.dump` (pide confirmación explícita escribiendo "RESTAURAR").

⚠️ **Gap real: los respaldos viven en el mismo servidor que los datos originales.** Si el droplet se pierde, se borra por error, o DigitalOcean tiene un problema, se pierden los datos **y** los respaldos al mismo tiempo. Esto NO es un respaldo real todavía — es solo una copia local en la misma máquina.

⚠️ **Gap real #2: el código no tiene copia fuera de tu Mac.** Si tu computador falla, se pierden 18 commits de historial y el único checkout completo del proyecto (el servidor solo tiene los archivos desplegados, no el historial de git).

## 7. Recomendaciones concretas (en orden de importancia)

1. **Sacar los respaldos de BD fuera del servidor.** Ejemplos simples:
   - Descargar el `.dump` más reciente a tu Mac después de cada respaldo (`scp root@67.205.190.114:/root/AutoControlQR_runnable_v31_6/backups/*.dump ~/Backups/autocontrolqr/`), o
   - Configurar un cron en el servidor que además suba cada `.dump` a un bucket (DigitalOcean Spaces, S3, Backblaze).
2. **Subir el repositorio a un remoto privado** (GitHub o GitLab, repo privado). Con `git remote add origin <url> && git push -u origin main` basta. Esto te da historial fuera de tu Mac y la posibilidad de recuperar el código si tu computador falla.
3. **Automatizar el respaldo** con un cron job en el servidor (ej. diario a las 3am) en vez de depender de que se ejecute manualmente antes de cada despliegue.
4. **Limpiar volúmenes viejos sin uso** en el servidor: `autocontrolqr_runnable_v4_pgdata`, `autocontrolqr_runnable_v30_4_*`, `autocontrolqr_runnable_v31_3_*`, `autocontrolqr_runnable_v31_4_*` — restos de despliegues anteriores, no los usa ningún contenedor activo. No es urgente (hay 72 GB libres) pero conviene revisarlos y borrarlos si confirmas que no los necesitas.
5. **Rotar `JWT_KEY`** periódicamente y después de cualquier sospecha de filtración (invalida todas las sesiones activas al cambiarla).
6. **Guardar la passphrase de tu llave SSH** (`~/.ssh/id_ed25519`) en un gestor de contraseñas — es la única llave con acceso al servidor.

## 8. Contacto/plataforma

- Cuenta PLATFORM_ADMIN: `platform@autocontrol.local` (contraseña inicial fue generada aleatoriamente al crear el servidor — si no la guardaste, puede resetearse por base de datos).
