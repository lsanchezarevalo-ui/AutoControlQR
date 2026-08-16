# AutoControl QR — Guía de piloto V30.3

Esta versión está pensada para comenzar pruebas reales sin agregar funciones nuevas.

## 1. Antes del piloto

1. Levanta la aplicación:
   ```bash
   docker compose up -d --build
   ```

2. Verifica que los tres servicios estén activos:
   ```bash
   docker compose ps
   ```

3. Crea un respaldo inicial:
   ```bash
   ./scripts/backup.sh
   ```

4. Conserva el archivo `.dump` generado dentro de `backups/`.

## 2. Alcance recomendado

Comienza con 3 a 5 vehículos y usuarios reales.

Prueba especialmente:
- ingreso del Administrador;
- búsqueda de vehículo;
- QR del Conductor;
- actualización de kilometraje;
- alerta por salto anormal de kilometraje;
- flujo del Técnico;
- registro de mantenimiento;
- cambio de estado Al Día / Próximo / Vencido;
- Sin kilometraje hoy;
- reportes, Excel y PDF;
- permisos entre Administrador, Técnico y Conductor.

## 3. Rutina de respaldo

Durante el piloto ejecuta:

```bash
./scripts/backup.sh
```

Recomendación inicial:
- respaldo antes de cada actualización de versión;
- respaldo al final de cada jornada durante la primera semana;
- después, al menos un respaldo diario mientras dure el piloto.

No borres el volumen de PostgreSQL con `docker compose down -v`.

## 4. Restauración

Solo si es necesario:

```bash
./scripts/restore.sh backups/autocontrolqr_AAAAMMDD_HHMMSS.dump
```

El script:
- pide una confirmación explícita;
- crea primero un respaldo de seguridad;
- recrea la base de datos;
- restaura el archivo seleccionado;
- reinicia API y web.

## 5. Registro de hallazgos

Durante el piloto, anota cada hallazgo con:
- fecha;
- usuario/rol;
- vehículo;
- pantalla;
- qué intentó hacer;
- qué ocurrió;
- qué esperaba que ocurriera;
- captura de pantalla si aplica.

No conviertas cada comentario en una modificación inmediata. Agrupa los hallazgos y revísalos al final de la prueba para decidir qué debe entrar en V31.

## 6. Próximo paso

Cuando el respaldo/restauración esté probado, el siguiente paso es desplegar esta misma versión en un servidor de pruebas con dominio y HTTPS para que Conductores y Técnicos puedan acceder desde fuera de la red local.
