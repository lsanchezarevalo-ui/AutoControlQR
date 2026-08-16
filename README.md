# AutoControl QR V30.1 — Centro de Control

Agrega registro real de mantenimiento, historial, snapshots de intervalos, cálculo del siguiente ciclo y mejora visual de tarjetas.

## Actualizar desde V5.1 sin perder datos
1. En V5.1: `docker compose down` (NO usar `-v`).
2. Descomprimir V30.1.
3. En la carpeta V30.1: `docker compose up --build`.
4. Abrir http://localhost:3000

La V30.1 conserva intencionalmente el nombre Compose/volumen de la línea estable V4/V5 para reutilizar PostgreSQL.


V30.1 añade predicción de mantenimiento usando el promedio de kilometraje diario de los últimos 30 días. Requiere al menos 3 días con lecturas válidas; de lo contrario muestra datos insuficientes.


V30.1 añade historial visible de kilometraje, carga histórica administrativa con fecha seleccionable y calendarios HTML con botón Hoy para fechas de mantenimiento/historial base. Las lecturas QR públicas conservan fecha/hora automática e inalterable.


V30.1 añade nivel de confianza predictiva (Alta/Media/Baja) según cantidad de días y variabilidad del uso. El Centro de Control prioriza vencidos y, entre próximos, los de menor número de días estimados.


V30.1 conserva km/día, días estimados y fecha estimada, pero elimina de la interfaz los niveles de confianza Alta/Media/Baja. Prioridad: vencidos primero y próximos después.


V30.1 añade Reportes de mantenimiento por rango de fechas y vehículo, con historial de flota y vista preparada para imprimir/guardar como PDF desde el navegador.


V30.1 añade filtro por Servicio en Reportes. La lista de servicios se adapta al vehículo y periodo seleccionados.


V30.1 añade el reporte Últimos servicios: una sola fila por vehículo y tipo de servicio, correspondiente a la ejecución más reciente. Conserva filtros por vehículo y servicio.


V30.1 añade gestión de usuarios y roles. Administrador gestiona usuarios, vehículos, planes y reportes. Técnico consulta vehículos y registra mantenimiento, sin acceso a configuración de planes ni gestión administrativa. El conductor continúa usando el QR público sin login.


V30.1 cambia el flujo del Técnico: tras iniciar sesión debe buscar un vehículo por placa/interno o escanear su QR. La API emite una sesión de trabajo ligada a ese vehículo y bloquea el acceso del técnico a otros vehículos hasta usar Cambiar vehículo.


V30.1 añade arquitectura multiempresa. Cada empresa tiene administrador, usuarios, vehículos, planes y reportes aislados por company_id. Se añade un Administrador de Plataforma para crear/desactivar empresas y generar el primer administrador de cada cliente.


V30.1 genera automáticamente el código de empresa con formato EMP-0001, EMP-0002, etc. La creación de empresa ya no solicita código. Se añade Configuración de Empresa para nombre comercial, razón social, NIT/identificación, teléfono, correo y dirección.


V30.1 añade logo por empresa y etiqueta QR individual para impresión. El administrador puede cargar/quitar logo desde Empresa. Cada vehículo incluye Imprimir etiqueta QR con AutoControl QR, empresa/logo, placa, interno opcional, QR grande e instrucción para actualizar kilometraje.


V30.1 compacta la etiqueta QR: elimina la línea final de marca/modelo/versión, reduce márgenes y altura total, y conserva el QR en 52 mm para no sacrificar facilidad de lectura.


V30.1 reduce la etiqueta física a 70 mm de ancho aproximado, compacta encabezado/márgenes, elimina el eslogan y baja el QR de 52 mm a 44 mm manteniendo una zona limpia alrededor para lectura confiable.


V30.1 elimina el logo de la etiqueta física, conserva solo el nombre de empresa en texto pequeño, reduce el ancho a ~64 mm y el QR a 42 mm para una etiqueta interior aún más compacta.


V30.1 compacta la identificación de la etiqueta: placa y número interno aparecen en una sola línea como ABC-123 / 254, sin las palabras PLACA ni INTERNO.


V30.1 elimina la altura mínima fija de la etiqueta para que termine inmediatamente después del contenido y reduce el margen inferior.


V30.1 fija la etiqueta de rollo a 60 x 60 mm y configura la página de impresión al mismo tamaño. El QR queda en 36 x 36 mm para conservar encabezado, empresa, placa/interno e instrucción dentro del formato.


V30.1 elimina Versión/Referencia de la interfaz de Vehículos y Planes. Marca y Modelo usan autocompletado con los datos ya existentes de la empresa; se puede seleccionar un valor existente o escribir uno nuevo. Los modelos sugeridos se filtran según la marca seleccionada. La columna variant se conserva internamente solo por compatibilidad con datos anteriores y los registros nuevos guardan null.


V30.1 normaliza captura: Placa e Interno se convierten siempre a MAYÚSCULAS; Marca y Modelo se guardan con capitalización de título; Kilometraje acepta únicamente dígitos enteros sin puntos, comas ni separadores.


V30.1 añade edición de vehículos (placa, interno, marca, modelo) y edición de planes (nombre, marca, modelo). No permite modificar kilometraje desde Editar vehículo: el kilometraje conserva su flujo y trazabilidad independiente.


V30.1 integra la asignación del plan al crear el vehículo. El plan es obligatorio en alta de vehículo. Se elimina el selector de asignación separado de cada tarjeta; el plan puede cambiarse posteriormente desde Editar vehículo, conservando el historial de asignaciones.


V30.1 corrige el error de compilación CS8803: el endpoint PATCH de edición de vehículo fue movido antes de app.Run() y antes de las declaraciones record.


V30.1 añade archivado seguro. Archivar un vehículo conserva historial, cierra su plan activo y revoca su QR. Archivar un plan conserva historial y solo se permite si ningún vehículo activo lo utiliza.


V30.1 añade recuperación de archivados. Vehículos y Planes tienen acceso a Ver archivados. Los planes pueden reactivarse directamente; los vehículos requieren seleccionar un plan activo y reciben un nuevo token QR al volver a operación, manteniendo intacto el historial previo.


V30.1 completa gestión de usuarios: crear, editar nombre/correo/rol, activar/desactivar y restablecer contraseña. Protege al administrador conectado para que no pueda desactivarse ni quitarse su propio rol administrador.


V30.1 realiza barrido de seguridad por roles. Las sesiones autenticadas se invalidan inmediatamente si el usuario o la empresa son desactivados. La configuración de empresa queda restringida a COMPANY_ADMIN. Los endpoints de vehículo mantienen aislamiento por company_id y el técnico continúa limitado al vehículo seleccionado mediante CanAccessVehicle. El acceso público QR sigue limitado a tokens activos de vehículos activos y se endurece la validación de kilometraje.


V30.1 inicia el barrido final de diseño por Centro de control: jerarquía de KPIs priorizando Vencidos y Próximos, textos más directos, tabla de prioridades más legible, estado vacío más limpio y ajustes responsive. No cambia lógica ni datos.


V30.1 mejora Centro de Control: título capitalizado, logo de empresa en encabezado y KPIs interactivos. Vencidos, Próximos, Al día y Sin historial filtran servicios por estado; Total vehículos muestra todos los servicios. El estado inicial prioriza Vencidos, luego Próximos, Al día y Sin historial.


V30.1 continúa el barrido final en Vehículos: formulario de alta con etiquetas claras, conteo de activos, tarjetas más compactas y jerarquizadas, placa/interno en una línea, kilometraje y plan destacados, acciones operativas separadas de edición/archivo y QR más discreto. Sin cambios de lógica.


V30.1 añade búsqueda instantánea de vehículos por placa o número interno y permite crear vehículos sin plan. El plan queda opcional al alta para soportar tanto flotas como usuarios de un solo vehículo; los vehículos sin plan se mantienen visibles como pendientes de configuración.


V30.1 realiza el barrido visual de Planes: creación más explicativa para flotas o usuarios particulares, búsqueda por nombre/marca/modelo, tarjetas simplificadas y gestión de servicios en pantalla en lugar de alertas. Agregar servicio usa formulario guiado con intervalo y prealerta. Sin cambios al modelo de mantenimiento.


V30.1 permite editar servicios ya creados desde la vista de Servicios. Se pueden cambiar nombre, intervalo por kilometraje, prealerta y especificación. Los registros históricos de mantenimientos conservan sus snapshots anteriores; la modificación aplica a la configuración futura del plan.


V30.1 mueve el acceso de edición de servicios al encabezado de la pantalla del plan, junto a + Agregar servicio. El botón Editar servicios activa/desactiva los controles Editar de cada fila, manteniendo la vista limpia por defecto.


V30.1 realiza el barrido visual de Usuarios: formulario etiquetado, explicación de roles, búsqueda por nombre/correo, tarjetas compactas, edición mediante modal, cambio de contraseña con confirmación y confirmación antes de activar/desactivar. Sin cambios en permisos ni seguridad.


V30.1 realiza el barrido visual de Reportes: pestañas más claras, filtros organizados, limpiar filtros, actualización automática al seleccionar vehículo/servicio, resumen de resultados, tablas más legibles y mejores estados vacíos. Mantiene Historial de servicios y Últimos servicios como reportes independientes.


V30.1 mejora Reportes: vehículo usa autocompletado por placa o número interno; se añade pestaña Por estado con filtros Vencidos, Próximos, Al día y Sin historial; los tres reportes permiten Descargar Excel además de Imprimir/PDF.


V30.1 realiza el barrido visual del flujo Técnico. Mantiene la selección obligatoria por placa/interno o QR antes de trabajar, refuerza visualmente el vehículo seleccionado, simplifica las acciones a Estado/registrar mantenimiento e Historial, y hace más clara la restricción de sesión a una sola unidad. Sin cambios en permisos.


V30.1 añade preaviso al registrar un mantenimiento cuyo servicio está AL DÍA. El técnico recibe confirmación antes de continuar, pero el sistema permite mantenimiento anticipado cuando sea necesario.


V30.1 realiza el barrido del flujo Conductor/QR: actualización de kilometraje como acción principal, lectura numérica limpia, confirmación reforzada para saltos excepcionales y consulta read-only del estado de mantenimiento. El acceso público no permite registrar servicios ni modificar planes.


V30.1 añade confirmación visual persistente después de actualizar el kilometraje por QR. Muestra la nueva lectura y aclara que ya fue guardada para evitar envíos repetidos por parte del conductor.


V30.1 inicia el barrido visual global final. El encabezado del administrador muestra el logo de la empresa junto al nombre; si no existe logo usa la inicial como fallback. También uniforma foco de campos, respuesta de botones, espaciado general y jerarquía tipográfica sin cambiar la lógica aprobada.


V30.1 elimina el logo duplicado de Centro de Control, incorpora iconos visuales junto a servicios y mejora las confirmaciones de actualización/registro. El conductor recibe confirmación con check, kilometraje, fecha/hora, autor y acceso al estado de servicios. El registro de mantenimiento del técnico/administrador muestra una confirmación equivalente. La foto del vehículo queda reservada para una fase posterior opcional.


V30.1 adopta la nueva identidad visual aprobada: navegación lateral azul, barra superior con empresa/usuario, Centro de Control reorganizado con indicadores, próximos servicios, actualizaciones de kilometraje de hoy, búsqueda rápida, resumen visual del estado de servicios y acciones rápidas. Los iconos de servicio pasan a SVG lineales según el tipo de mantenimiento. Se mantiene la lógica, permisos y flujos existentes.


V30.1 simplifica Centro de Control eliminando el panel de Actualizaciones de kilometraje hoy y amplía Buscar vehículo. También corrige impresión/PDF de reportes: oculta menú lateral, barra superior, filtros y botones; usa toda la hoja A4 horizontal y redistribuye las columnas para impresión.


V30.1 cambia el KPI Kilometraje hoy por Sin kilometraje hoy. Al tocarlo muestra vehículos con más de 24 horas desde su última lectura. También extiende la identidad visual del dashboard a Vehículos, Planes, Reportes, Usuarios y Empresa mediante paleta unificada, iconos de módulo, tarjetas, estados y formularios coherentes.


V30.1 incorpora la base del sistema de Notificaciones: módulo para administrador, activación/desactivación, alertas internas, preparación del canal correo, destinatario, reglas Próximo/Vencido, frecuencia de recordatorio e historial de avisos. Los envíos externos reales no se realizan todavía: correo requiere conectar un proveedor transaccional y WhatsApp queda preparado como fase posterior.


V30.1 corrige el literal SQL multilinea del endpoint /api/v1/company que impedía compilar la API en V30.


V31 cambia el título del Centro de Control de «Estado de servicios de la flota» a «Estado De Servicios», para que aplique tanto a empresas como a usuarios individuales. Notificaciones se mantiene sin cambios para revisión posterior.


V31 congela la aplicación para piloto y añade scripts seguros de respaldo/restauración de PostgreSQL junto con PILOT_GUIDE.md. No modifica la lógica funcional aprobada.


V31 añade despliegue piloto por Internet con Caddy/HTTPS, configuración de producción mediante variables de entorno y scripts separados para arranque y respaldo. La lógica funcional de la aplicación permanece congelada.


V31 — optimización móvil basada en las primeras pruebas reales en iPhone:
- Conductor: confirmación de kilometraje simplificada, botón Ver Estado De Servicios y Salir.
- Técnico: búsqueda simplificada y apertura directa cuando existe una sola coincidencia; botones de navegación mayores.
- Formularios/modales: desplazamiento vertical propio y bloqueo del fondo, corrigiendo el problema de Registrar mantenimiento en iPhone.
- Administrador móvil: menú lateral desplegable tipo ☰ y Acciones rápidas ocultas en pantallas pequeñas.
- QR: impresión/Guardar PDF adaptada para Safari móvil con mensaje específico si bloquea la ventana.
- PWA: manifest y modo standalone para poder añadir AutoControl QR a la pantalla de inicio del teléfono.
Notificaciones permanece sin cambios funcionales.


V31.1 refuerza el contraste visual de los botones Cambiar vehículo y Salir en Modo Técnico, especialmente en iPhone. No modifica la lógica del flujo técnico ni otras funciones.


V31.2 — ajustes responsive de Administrador basados en pruebas reales en iPhone:
- Empresa y Planes de Mantenimiento aprovechan el ancho útil móvil.
- Encabezados y acciones se redistribuyen para evitar compresión horizontal.
- Ver archivados se adapta a móvil sin reducir el contenido principal.
- Campos de fecha y filtros de Reportes quedan contenidos dentro de sus tarjetas.
- Se establece un ancho/margen móvil común siguiendo el estándar visual aprobado de Centro De Control.
- Sin cambios funcionales en Conductor ni Técnico.


V31.3 añade Servicios Individuales por Vehículo para clientes particulares y talleres:
- Un vehículo puede funcionar sin Plan de Mantenimiento.
- El Administrador puede agregar servicios directamente desde Estado de mantenimiento.
- Cada servicio individual conserva intervalo, prealerta y especificación y pertenece solo a ese vehículo.
- Técnico, estados, historial y registro de mantenimiento usan el mismo flujo existente.
- Los planes internos usados para soportar esta función quedan ocultos del módulo Planes y no se presentan como planes al usuario.
- Se corrigen los anchos móviles de Ver archivados, títulos de Vehículos/Planes y filtros de Reportes.


V31.4 corrige el inicio de sesión:
- Correo y Contraseña abren vacíos.
- Se elimina la referencia visible a credenciales demo.
- Se mantiene compatibilidad con gestores de contraseñas del navegador, sin precargar credenciales desde AutoControl QR.
- Sin cambios en módulos, base de datos ni permisos.


V31.5
- Administrador General: botón Ver empresa.
- Vista de consulta de usuarios y vehículos por empresa.
- La vista de plataforma es de consulta para usuarios/vehículos; evita modificaciones operativas accidentales.
- Producción usa el volumen PostgreSQL externo permanente `autocontrolqr_prod_pgdata`, independiente del nombre de la versión.


V31.6 — administración de acceso:
- Todos los usuarios autenticados pueden cambiar su propia contraseña desde Mi cuenta.
- El Administrador de Empresa conserva el restablecimiento de contraseña para usuarios de su empresa.
- El Administrador General puede entrar a cada empresa, consultar usuarios/vehículos, ver estado, activar/desactivar empresa y restablecer la contraseña de Administradores de Empresa.
- Login incluye “¿Olvidaste tu contraseña?” con el flujo de soporte correspondiente al rol.
- Se eliminan del código las contraseñas iniciales predecibles Demo123! y Platform123!. En una instalación nueva se generan credenciales iniciales aleatorias (o pueden inyectarse por variables de entorno).
- El Administrador General no puede modificar mantenimientos, kilometrajes ni historiales del cliente.
