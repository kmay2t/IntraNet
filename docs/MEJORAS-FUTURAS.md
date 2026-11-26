# Mejoras futuras y puntos de atención

Lista priorizada de cosas que **hoy funcionan** pero que conviene mejorar después, con el motivo y
por dónde empezar. No es código roto: es deuda conocida y oportunidades de evolución.

---

## Prioridad alta

### 1. Unificar el tono dorado entre subsistemas
Hoy la intranet core usa `--gold: #d4a838` y los subsistemas (dicom, patologia) usan
`--dorado: #c9a227`. El logo IMSS toma el color del `:root` de cada página, así que se ve ligeramente
distinto entre pantallas.
**Por dónde:** definir un único token (p. ej. `#d4a838`) y referenciarlo en los tres `:root`, o
mover los estilos compartidos de cabecera a un CSS común (ver punto 2).

### 2. Extraer el CSS/JS duplicado de los subsistemas
`dicom/index.html`, `patologia/consulta/index.html` y `patologia/subida/index.html` repiten casi
idénticos la cabecera, el fondo de logo, los botones de navegación y helpers de `fetch`. Las páginas
de panel (`/admin/usuarios/`, `/admin/bitacora/`, `/admin/roles/`, `/admin/reportes/`,
`/admin/config/`, `/calendario/` y las cinco de `/gestion/…`) **ya comparten** `css/panel.css`, y el
módulo Gestión además comparte `js/gestion.js` y `js/inventario.js`, que nació justo con ese formato DICOM.
**Por qué importa:** un cambio de diseño hay que repetirlo en 3 archivos (riesgo de que se olviden).
**Por dónde:** migrar dicom y patología a `panel.css` (o un `subsistemas.css` derivado) y dejar en
cada `index.html` solo lo específico. Cuidado: son páginas autónomas por diseño; al extraer,
mantenerlas servidas sin caché como hoy.

### 3. Contraseñas: política más rica y cambio propio
La regla actual es solo "mínimo 3 caracteres" (`UserRules.PasswordError`, umbral en
`UserRules.PasswordMin`). No hay complejidad,
caducidad ni **cambio de contraseña del propio usuario** (solo un admin puede cambiarla desde Admin).
**Por dónde:** añadir un endpoint `/api/usuarios/{yo}/password` con verificación de la contraseña
actual y una pantalla/modal "Cambiar mi contraseña" en el layout.

### 4. Bitácora: retención, purga y exportación  *(la bitácora base ya existe)*
La bitácora real ya está implementada (`IAuditLog`, sección 7 de
[`USUARIOS-Y-MYSQL.md`](USUARIOS-Y-MYSQL.md)): registra inicios de sesión, cierres, operaciones de
usuarios y actividades y movimientos por página/API, con filtros por usuario y día en su página
propia **`/admin/bitacora/`**. Lo que **falta** es gestionarla en el tiempo: hoy crece sin límite
(JSONL o tabla MySQL) y solo se consulta en pantalla.
**Por dónde:** una tarea programada (`IHostedService`) que purgue o archive eventos de más de N
días, y un botón "Descargar Excel" en `/admin/bitacora/` reutilizando `Services/Excel.cs`
(el mismo writer `.xlsx` que ya usan el Histórico de actividades y los tres inventarios).

---

## Prioridad media

### 5. Reconciliación JSON ↔ MySQL tras una degradación larga
Si MySQL cae y se crean usuarios en JSON, al volver MySQL **solo se migran si la tabla está vacía**.
Si la tabla ya tenía usuarios, hay que reconciliar a mano.
**Por dónde:** en el arranque, detectar `users.json` no vacío con MySQL activo y ofrecer/automatizar
un merge (respetando el más reciente por `creado`), en vez de ignorarlo.

### 6. Sondeo de MySQL periódico (no solo al arrancar)
El proveedor se decide **una vez al arrancar**. Si MySQL cae con el servicio ya arriba, la app sigue
en el proveedor elegido hasta reiniciar.
**Por dónde:** un `IHostedService` que reintente la conexión y conmute el `IUserStore` en caliente
(o al menos avise). Requiere que `IUserStore` se resuelva por fábrica, no como singleton fijo.

### 7. Paginación y búsqueda en la tabla de usuarios
La tabla pinta todos los usuarios de una vez. Con cientos de cuentas será incómoda.
**Por dónde:** añadir un buscador cliente (como el de Ingeniería) y/o paginación servidor en
`GET /api/usuarios`.

### 8. Confirmación de eliminación más segura
Hoy `confirm()` nativo. Funciona, pero un modal propio con el nombre del usuario escrito evitaria
borrados accidentales y permitiría mostrar consecuencias (sesiones abiertas).
**Por dónde:** reutilizar el patrón `dialog.modal` de `panel.css` (ya usado en
`/admin/usuarios/index.html`).

### 8b. Limpiar los estilos legacy de `intranet.css`
La gestión de usuarios y la bitácora se mudaron a páginas autónomas con `panel.css`, pero en
`intranet.css` quedaron los bloques `.tablehead`, `.store-line`, `.aviso`, `.modal`, `.btn-gold`…
de la antigua tabla dentro de Admin. Ya no los usa ninguna pantalla.
**Por dónde:** borrarlos (o moverlos a un `legacy.css`) tras confirmar con una búsqueda que ninguna
página Razor los referencia.

### 9. ~~Los `.html` de documentación están desactualizados~~ (resuelto)
`README.html`, `docs/ARQUITECTURA.html` y `docs/SERVICIO-WINDOWS.html` eran instantáneas generadas
de los `.md` y se **eliminaron** (2026-10-08): la fuente única de documentación es Markdown.
Si algún día se quiere una versión navegable, se regenera bajo demanda con
`pandoc README.md -o README.html` sin versionarla.

---

## Prioridad baja / ideas

### 10. Pruebas automatizadas
No hay suite de pruebas. Los almacenes (`UserStore`, `MySqlUserStore`) y `UserRules` son muy
testeables (devuelven códigos de estado).
**Por dónde:** proyecto `tests/` con xUnit probando reglas, códigos de error y el "último admin".

### 11. ~~Roles más granulares y más roles~~ (resuelto — 2026-10-15)
**Hecho.** Los roles ahora son **dinámicos y de nombre libre** (crear/renombrar/eliminar desde
`/admin/roles/`, tabla `roles`), la matriz por rol subió al **catálogo v6 (40 claves en 13 grupos)** —que
**incluye Patología y DICOM** (`patologia.consultar`, `dicom.ver`)—, y se añadieron **ajustes de permisos
por usuario** (conceder/revocar sobre los heredados del rol, tabla `usuario_permisos`). La autorización
fina ya **no depende de `RequireRole`**: las políticas `SoloAdmin`/`Ingenieria` son solo
`RequireAuthenticatedUser` y cada ruta/endpoint se protege por **permiso** (políticas `P:<clave>` +
guards de ruta). Las pantallas de Patología y DICOM también se gobiernan desde `/admin/roles/`.
Ver `docs/REGISTRO-CAMBIOS.md` (2026-10-15) y `docs/USUARIOS-Y-MYSQL.md` §1.
**Resta (opcional):** suite de pruebas automatizadas del resolver (ver punto 10).

### 11b. Inventarios: más colecciones y campos calculados
Los tres inventarios ya se construyen desde un esquema (`Models/Inventario.cs` + `js/inventario.js`),
así que añadir una cuarta colección (p. ej. *UPS*, *red inalámbrica*) es declarar campos y una ruta.
**Por dónde:** agregar la colección al catálogo; la pantalla se copia de una existente cambiando
`data-coleccion`. Pendiente: validaciones más ricas (p. ej. IP única por hospital, CIDR válido)
y campos calculados en el Excel.

### 11c. Playbook: histórico de versiones
Hoy cambiar la versión estampa la fecha de actualización y se puede sustituir el archivo, pero **no
se conservan las versiones anteriores** ni su PDF.
**Por dónde:** tabla `playbook_versiones` (documento, versión, archivo, quién y cuándo) y una pestaña
*Versiones* en el detalle; conservar los PDF viejos en una subcarpeta `_historico`.

### 12. Internacionalización / textos centralizados
Los mensajes de error viven repartidos entre `UserRules` (C#) y los `.html`/`.js`. Centralizarlos
facilitaría cambiar redacción o soportar otro idioma.

### 13. HTTPS en producción
El servicio escucha en `http://localhost:5023`. Dentro de la red del hospital puede ser aceptable,
pero si se expone más allá conviene terminar TLS (reverse proxy o Kestrel con certificado).

### 14. `appsettings.json` contiene credenciales
Las contraseñas de MySQL están en texto claro en el repo/carpeta. Para endurecer:
`Secrets de usuario` (dev) o variables de entorno / un gestor de secretos en producción.

### 15. Calendario: vistas y recordatorios
El calendario solo tiene **vista mensual** y filtro por **hospital**. Ideas: vista semanal/diaria,
arrastrar actividades a otro día, más filtros en la barra superior (ingeniero, tipo, estado) y avisos
de actividades próximas.
**Por dónde:** `wwwroot/calendario/index.html` (rejilla y `<script>`); la API ya devuelve todo lo
necesario (`GET /api/actividades`).

### 16. Adjuntos en actividades, incidentes y tickets
El seguimiento es solo texto (avance, informe, observaciones y reportes). Sería útil adjuntar
evidencia (fotos, PDFs) a un elemento, igual que el Playbook guarda sus archivos.
**Por dónde:** reutilizar el patrón de `Services/Playbook.cs` (validación de tipo/tamaño, GUID en
disco, ruta relativa en la fila) con una tabla `actividades_adjuntos`.

---

## Cómo está organizado para facilitar estos cambios

- La lógica de usuarios está tras la interfaz `IUserStore`, la bitácora tras `IAuditLog` y el módulo
  Gestión tras `IActivityStore`, `IReportStore`, `IHistorialStore`, `IPermissionStore`,
  `IConfigStore`, `IInventarioStore` e `IDocumentoStore`: cambiar el almacenamiento o añadir
  caché/auditoría no toca la API ni la interfaz.
- Las **reglas de negocio** viven en servicios (`GestionService`, `InventarioService`,
  `PlaybookService`, `HistoricoExportador`), no en los endpoints: los permisos y validaciones se
  comprueban una sola vez y para todos los clientes.
- La **exportación a Excel** está centralizada en `Services/Excel.cs`: añadir una nueva descarga es
  declarar columnas y filas.
- Los **inventarios** y el **Playbook** se construyen desde un esquema/catálogo
  (`Models/Inventario.cs`), así que ampliar campos no toca la pantalla.
- El frontend no tiene build: cualquier cambio de CSS/JS se prueba recargando (servido sin caché).
- Los modales usan `<dialog>` nativo: añadir una nueva ventana emergente es copiar el patrón de
  `#dlgCrear` en `wwwroot/admin/usuarios/index.html` + `dialog.modal` en `css/panel.css`, o usar
  directamente `Gestion.formDialog` / `Gestion.confirmar` en las pantallas del módulo Gestión.
