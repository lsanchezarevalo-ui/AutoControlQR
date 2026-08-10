# AutoControl QR

Plataforma para el control de mantenimiento preventivo de flotas mediante códigos QR.

## Visión

AutoControl QR transforma una simple lectura de kilometraje en decisiones automáticas de mantenimiento, reemplazando hojas de papel y hojas de cálculo y permitiendo a las empresas conocer en tiempo real el estado de su flota.

## MVP (Versión 1.0)

El MVP permite:

- Registrar empresas y vehículos.
- Generar un QR único por vehículo (sticker estático).
- Actualizar kilometraje desde acceso público (sin login) vía QR.
- Gestionar planes de mantenimiento (intervalos por km/tiempo, prealertas, versiones).
- Registrar mantenimientos y conservar historial auditado.
- Calcular automáticamente próximos servicios y mostrar el estado (Al día / Próximo / Vencido / Sin historial).
- Dashboard de control y reportes (PDF / Excel).

> Principio clave: cada dato ingresado debe generar valor y guiar la acción.

## Roles de usuario

- Superadministrador: gestiona la plataforma y empresas.
- Administrador de empresa: configura vehículos, planes y usuarios.
- Jefe de mantenimiento: gestiona operación diaria y dashboard.
- Técnico: registra servicios y consulta historial.
- Usuario público: puede escanear QR y actualizar el kilometraje (sin acceso administrativo).

## Reglas de negocio (resumen)

- Un QR por vehículo, inmutable.
- Kilometraje no puede disminuir. Si se detecta un salto mayor al umbral configurable (por defecto 2000 km) se requiere confirmación y se registra una advertencia.
- Planes versionados; técnicos no pueden editar intervalos ni especificaciones.
- Historial inmutable; las correcciones se mantienen auditadas.

## Módulos principales

- Empresas
- Vehículos (importación, búsqueda, activación)
- Códigos QR (generación e impresión)
- Planes de mantenimiento (servicios, intervalos, prealertas)
- Motor de mantenimiento (cálculo de próximos servicios y estados)
- Registro de mantenimiento y historial
- Centro de Control (dashboard)
- Reportes (exportar PDF/Excel)
- Auditoría y notificaciones

## Propuesta técnica (recomendación para MVP)

- Frontend: React (PWA) con soporte de cámara para escaneo de QR (react-qr-reader o similar).
- Backend: Node.js + Express o NestJS.
- ORM: Prisma o TypeORM.
- Base de datos: PostgreSQL.
- Autenticación: JWT (para usuarios administrativos). El registro de kilometraje público puede permitir envío sin autenticación pero debe capturar algún identificador (nombre/email/pin) y la IP/metadata.
- Notificaciones: FCM (push) y/o correo (SendGrid).

## Esquema de datos básico (ejemplo simplificado)

- users (id, name, email, role, created_at)
- companies (id, name, logo, params, created_at)
- vehicles (id, plate, alias, make, model, year, service_interval_km, last_service_km, company_id, created_at)
- readings (id, vehicle_id, user_id?, odometer_km, note, photo_url, created_at)
- services (id, vehicle_id, type, performed_km, performed_at, notes, performed_by)
- plans (id, company_id, name, version, items...)
- notifications (id, vehicle_id, type, message, sent_at)

## Endpoints API (sugeridos)

- POST /api/readings — crear lectura desde QR { vehicle_id, odometer_km, name?, note? }
- GET /api/vehicles — listar vehículos
- GET /api/vehicles/:id/history — lecturas históricas
- POST /api/vehicles/:id/service — registrar mantenimiento
- GET /api/alerts — listar alertas/servicios pendientes

## Validaciones y seguridad

- Validar que el nuevo kilometraje >= último registrado (permitir corrección auditada).
- Umbral configurable para saltos extraordinarios.
- Control de roles para acciones administrativas.
- Registrar metadata (IP, user agent, photo) para auditoría.

## Desarrollo local (instrucciones rápidas)

Sugerencia de stack local:
- Node.js 18+
- PostgreSQL 14+
- Yarn o npm

Comandos ejemplo (esqueleto):

1) Clonar repo

   git clone https://github.com/lsanchezarevalo-ui/AutoControlQR.git

2) Backend (ejemplo con npm)

   cd backend
   npm install
   # configurar .env con DATABASE_URL
   npx prisma migrate dev
   npm run dev

3) Frontend (React PWA)

   cd frontend
   npm install
   npm run dev

## Siguientes pasos que puedo hacer ahora

1. Convertir este blueprint en issues y epics (backlog).  
2. Crear un branch feature/blueprint-scaffold y subir un scaffold mínimo (backend + frontend).  
3. Añadir migraciones SQL/Prisma para las tablas básicas.  
4. Diseñar la especificación OpenAPI para los endpoints principales.  

Dime cuál prefieres y lo inicio. Si quieres, creo un branch y subo el scaffold ahora — indícame el nombre del branch si tienes preferencia.

---

Lema: "El mantenimiento inteligente comienza con un simple escaneo."