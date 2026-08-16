# Despliegue piloto por Internet — AutoControl QR V30.4

## Arquitectura

Internet → Caddy (HTTPS) → Nginx/Web → API → PostgreSQL

Solo se publican los puertos 80 y 443. PostgreSQL y la API permanecen dentro de la red privada de Docker.

## Servidor recomendado para piloto

- Ubuntu 24.04 LTS
- 2 vCPU
- 4 GB RAM recomendados
- 40 GB o más de disco
- IPv4 pública
- Docker Engine + Docker Compose plugin

Para un piloto pequeño también puede funcionar con 2 GB RAM, pero 4 GB da más margen al compilar imágenes y ejecutar PostgreSQL.

## 1. DNS

Crea un subdominio, por ejemplo:

    app.tudominio.com

Crea un registro DNS tipo A que apunte a la IPv4 pública del servidor.

Espera a que el dominio resuelva hacia el servidor antes de iniciar Caddy.

## 2. Seguridad inicial del servidor

Actualiza paquetes y permite solo SSH/HTTP/HTTPS en el firewall:

    sudo apt update && sudo apt upgrade -y
    sudo ufw allow OpenSSH
    sudo ufw allow 80/tcp
    sudo ufw allow 443/tcp
    sudo ufw allow 443/udp
    sudo ufw enable

No abras 5432 ni 8080 en Internet.

## 3. Instalar Docker

Instala Docker Engine y el plugin Docker Compose siguiendo la documentación oficial de Docker para Ubuntu.

Verifica:

    docker --version
    docker compose version

## 4. Subir AutoControl QR

Copia la carpeta completa AutoControlQR_runnable_v30_4 al servidor.

Entra a la carpeta:

    cd AutoControlQR_runnable_v30_4

## 5. Crear secretos

    cp .env.production.example .env.production

Edita:

    nano .env.production

Ejemplo:

    DOMAIN=app.tudominio.com
    POSTGRES_PASSWORD=<clave larga y única>
    JWT_KEY=<cadena aleatoria muy larga>

No publiques ni compartas el archivo .env.production.

Puedes generar secretos desde Linux con:

    openssl rand -hex 32
    openssl rand -hex 64

## 6. Arrancar

    ./scripts/prod-up.sh

Comprueba:

    docker compose --env-file .env.production -f docker-compose.prod.yml ps

Cuando el DNS esté correctamente apuntado, Caddy solicitará y renovará HTTPS automáticamente.

Abre:

    https://app.tudominio.com

Prueba también desde un teléfono usando datos móviles, no solo Wi-Fi.

## 7. QR

PublicWebBaseUrl utiliza automáticamente:

    https://TU_DOMINIO

Por lo tanto, los QR generados en el servidor abrirán una dirección pública y podrán ser escaneados desde celulares fuera de la red local.

Los QR impresos anteriormente mientras la aplicación usaba localhost deben reimprimirse para el piloto en Internet.

## 8. Respaldo en producción piloto

    ./scripts/prod-backup.sh

Haz un respaldo antes de cada actualización. Durante el piloto se recomienda respaldo diario.

Idealmente copia periódicamente los archivos de la carpeta backups fuera del mismo servidor.

## 9. Actualizaciones

Antes de cambiar de versión:

    ./scripts/prod-backup.sh

Luego sustituye los archivos de la aplicación y ejecuta:

    ./scripts/prod-up.sh

No uses:

    docker compose down -v

porque eliminaría el volumen de PostgreSQL.

## 10. Qué probar desde Internet

- login Administrador;
- login Técnico;
- QR desde teléfono con datos móviles;
- actualizar kilometraje;
- estado de mantenimiento;
- registrar servicio;
- reportes;
- PDF;
- Excel;
- Sin kilometraje hoy;
- comportamiento en Android y iPhone;
- volver a escanear el QR después de varias horas/días.

## Antes de considerarlo producción

El piloto no equivale todavía a producción comercial. Antes de vender el servicio se recomienda añadir como mínimo:
- backups externos automatizados;
- monitoreo de disponibilidad;
- política de privacidad y tratamiento de datos;
- rotación/gestión de secretos;
- actualización periódica del sistema operativo y contenedores;
- mecanismo de recuperación documentado y probado.
