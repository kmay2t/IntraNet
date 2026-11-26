# Arquitectura del sistema unificado

Este documento explica **cómo funciona por dentro** la aplicación, para que cualquier persona pueda darle mantenimiento. Todo vive en **un solo proceso** ASP.NET Core (.NET 8) que sirve pantallas, APIs estáticas y el motor DICOM a la vez.

---

## 1. Vista general

```
                         ┌──────────────────────────────────────────────┐
   Navegador  ──HTTP──►  │  Meditechnosys.Intranet (un solo programa)    │
   (5023)                │                                                │
                         │  • Pantallas Razor (Login, Index, Ingenieria…) │
                         │  • API /api/patologia/*  (repositorio)         │
                         │  • API /api/dicom/*      (visor DICOM)         │
                         │  • API /api/actividades, /api/inventario/*,    │
                         │    /api/playbook/*       (módulo Gestión)      │
                         │  • Motor fo-dicom (C-FIND / C-MOVE)            │
                         └───────┬───────────────┬───────────────┬────────┘
                                 │               │               │
                          MySQL (3306)    Red hospital      Disco local
                          meditechnosys_   Nodos PACS       PDF: Desktop\Pato
                          intranet + RepoPato  (104)         DICOM: publish\dicom_downloads
                                              ▲
                                         puerto 11113
                                      (receptor C-STORE)
```

- **Un solo login** protege todo: pantallas y APIs.
- **Dos bases MySQL**: `meditechnosys_intranet` (usuarios) y `RepoPato` (reportes + auditoría).
- **Un puerto HTTP** (5023, configurable) y **un puerto DICOM** (11113) para recibir imágenes.

El punto de entrada es **`Program.cs`**: ahí se configura la autenticación, se registran los servicios y se define el orden del "pipeline" (qué middleware atiende cada petición).

---

## 2. Autenticación y autorización (el login único)

### Cookie de sesión
- Se usa **autenticación por cookie** (`AddCookie`). La cookie se llama `meditechnosys.intranet`, es `HttpOnly`, `SameSite=Lax`, dura **8 horas** con *sliding expiration* (se renueva con cada uso). Lo que corta la sesión antes de esas 8 horas es la **ventana de inactividad** que cada quien elige en el botón ⚙ del inicio (ver §9 «Dónde tocar cada cosa» y [`FRONTEND.md`](FRONTEND.md) §5 quinquies); la caducidad que se escribe al renovarla es `SesionPreferencias.VidaCookie` (8 h + 10 min), deliberadamente más larga que cualquier ventana.
- Al iniciar sesión, `POST /api/login` valida credenciales y guarda en la cookie el **nombre** y el **rol** del usuario.

### Validación en cada petición
- `OnValidatePrincipal` revisa en **cada** request que el usuario siga existiendo en la base y con el mismo rol. Si lo borran o le cambian el rol, la sesión abierta **se ajusta o se cierra al instante**.
- `OnRedirectToLogin` / `OnRedirectToAccessDenied`: si la ruta empieza por `/api`, devuelve **401/403** (JSON) en vez de redirigir a la página de login. Las pantallas normales sí redirigen a `/Login`.

### Roles y políticas (`Models/Roles.cs`, `Models/Permisos.cs`, `Services/RolesStore.cs`)
- **Roles dinámicos y de nombre libre** (desde 2026-10-15). Dos **roles de sistema** no editables:
  **`Admin`** e **`Ingeniero`**; el resto se crea/renombra/elimina desde `/admin/roles/` (`IRoleStore`,
  tabla `roles`). La matriz de permisos por rol (**catálogo v15: 46 claves en 13 grupos**) se edita ahí
  mismo, y cada usuario puede tener **ajustes (overrides)** conceder/revocar (`IUserPermissionStore`,
  tabla `usuario_permisos`).
- **Permisos efectivos** = matriz del rol + overrides, calculados por `IPermissionResolver` y adjuntados
  a la sesión como claims `perm` en `OnValidatePrincipal` (se **recalculan en cada petición**; los cambios
  llaman a `resolver.Invalidar()` y aplican a sesiones abiertas). **Admin siempre conserva
  `roles.administrar`** (anti-bloqueo).
- Políticas (`AddAuthorization`):
  - **`SoloAdmin`** e **`Ingenieria`** → ahora son solo `RequireAuthenticatedUser` (la autorización fina
    vive en los guards de ruta y en los servicios, no en el nombre del rol).
  - **`P:<clave>`** → una política por permiso del catálogo (`RequireClaim(perm, clave)`); p. ej.
    `/api/usuarios` usa `P:usuarios.administrar`, `/api/roles` usa `P:roles.administrar`, `/api/bitacora`
    usa `P:bitacora.ver`.
  - **`GET /api/usuarios/asignables`** se protege con una **aserción** que admite varios permisos
    (`usuarios.administrar`, `tickets.asignar`, `tickets.cambiarIngeniero`, `supervision.porIngeniero`,
    `historico.actividades` o `bitacora.ver`).
- En la práctica: lo que cada quien puede hacer **ya no depende del nombre del rol** sino de sus permisos
  efectivos, validados **en la interfaz y en el servidor**. El borrado y cada escritura siguen protegidos
  **en el servidor** por su permiso, no solo ocultando el botón.

### Rutas protegidas
- Por convención, **toda la carpeta `/` exige sesión**, salvo `/Login` y `/Error`.
- Las carpetas estáticas **`/patologia`** y **`/dicom`** tienen un cerrojo extra (`app.UseWhen`): si no hay sesión → redirige a `/Login`; si el rol no es Admin/Ingeniero → **403**. Así, ni los HTML ni los PDF/ZIP se sirven sin estar logueado.
- **`/admin/*`** (páginas autónomas `admin/usuarios/`, `admin/bitacora/`, `admin/roles/`,
  `admin/reportes/` y `admin/config/`) tiene el mismo cerrojo pero **exclusivo para Admin**.
- **`/calendario/*`** exige sesión con rol **Admin o Ingeniero** y el permiso `calendario.acceder`.
- **`/gestion/*`** (histórico único, los tres inventarios y el Playbook) exige sesión con rol
  **Admin o Ingeniero**; lo que cada rol puede hacer dentro se decide con el catálogo de permisos.
- Estos estáticos usan la función local `CacheEstatico`: `.html`, `.js` y `.css` se revalidan con ETag/Last-Modified (`Cache-Control: no-cache`) y las imágenes/fuentes se conservan siete días en el navegador.

---

## 3. Usuarios (login) — `Services/`

- Interfaz **`IUserStore`** con dos implementaciones intercambiables:
  - **`MySqlUserStore`** (recomendada, `Auth:Store = "mysql"`): tabla `usuarios`. **Crea la base y la tabla solas** (`EnsureSchema`) en el primer arranque.
  - **`UserStore`** (archivo `App_Data/users.json`): solo para arrancar sin base de datos.
- **Migración automática**: si MySQL está vacío pero existe `users.json`, los usuarios del JSON **se importan solos** a MySQL la primera vez.
- **Semilla**: si aun así no hay usuarios, se crean los de `Auth:Users` (en desarrollo, `admin`/`ing`). En producción el primer admin se crea con variables de entorno `Auth__Users__0__User/Password/Role`.
- **Contraseñas**: `PasswordHasher` usa **PBKDF2‑SHA256 con 210.000 iteraciones** y sal aleatoria. Nunca se guarda la contraseña en claro. La verificación usa comparación de tiempo constante (no delata si el usuario existe).
- **Reglas** (`UserRules`): usuario de 3–40 caracteres `[A-Za-z0-9._-]`, contraseña mínima 3 caracteres (`PasswordMin`), rol válido Admin/Ingeniero. No se puede eliminar ni degradar al **último administrador**.
- **Estado activo/inactivo**: cada usuario tiene un campo `activo`. Un usuario desactivado **no puede iniciar sesión** y, si ya tenía sesión abierta, `OnValidatePrincipal` la rechaza en la siguiente petición. La validación usa `CachedUserStore` con una ventana corta (`Auth:SessionCacheSeconds`, 15 s por omisión): los cambios hechos por esta aplicación invalidan la entrada al momento y un cambio externo se ve como máximo al terminar esa ventana. No se puede desactivar al último administrador activo ni a uno mismo.
- **Degradación graceful** (`AuthStoreInfo` + `Program.cs`): al arrancar se **sondea** MySQL (timeout 3 s). Si no responde, la app **no se cae**: registra el almacén JSON como respaldo y deja `AuthStoreInfo.Degraded = true` con el motivo. Ese estado se publica en `/api/status`, se muestra como aviso en la pantalla Admin y se registra en bitácora. Al volver MySQL, el siguiente arranque lo retoma.
- **Interfaz de gestión** (página autónoma **`wwwroot/admin/usuarios/index.html`**, formato DICOM): tabla de usuarios (usuario, **nombre**, **cargo**, rol, alta, estado) y ventanas emergentes (`<dialog>`) para **crear** (con rol Admin/Ingeniero, nombre y cargo) y **editar** (rol, nombre, cargo, estado activo y/o contraseña), más botones por fila para **desactivar/activar** y **eliminar**. Todo contra la API `/api/usuarios` (política `SoloAdmin`). El botón *Gestión de usuarios* de la pantalla Admin navega a esta página. Detalle en [`USUARIOS-Y-MYSQL.md`](USUARIOS-Y-MYSQL.md).
- **Bitácora (accesos + quemador en una sola hoja)** (`Services/Bitacora.cs`, interfaz `IAuditLog`): registro real de inicios y **intentos fallidos** de sesión, cierres, operaciones de usuarios/actividades/inventarios/permisos y **movimientos** (páginas y APIs visitadas, con método, ruta, código e IP). Dos destinos según el almacén: tabla MySQL `bitacora` o archivo `App_Data/bitacora.jsonl`. Desde el 2026-11-03 se muestra **unificada** con la del Quemador DICOM (`IQuemadorLog`, tabla `bitacora_quemador` / `App_Data/bitacora_quemador.jsonl`): `Models/BitacoraUnificada.cs` traduce ambas filas a un solo `Movimiento` **al consultar** —no hay migración ni tabla nueva, cada almacén conserva su historia y su forma de escribirse— y normaliza el diccionario de «Actividad» (Inicio de sesión, **Intento de sesión fallido**, Visita, Consulta, Descarga, Quemado en USB, Quemado en CD/DVD, Modificación…). La página autónoma **`wwwroot/admin/bitacora/index.html`** pinta Fecha · Hora · Usuario · Actividad · IP · Ubicación geográfica · Qué fue lo que hizo, con el filtro `fuente` para ver solo una de las dos. API: `GET /api/bitacora/unificada` y `GET /api/bitacora/unificada/export` (Excel), ambas con `bitacora.ver` **o** `quemador.ver`; `puedeAccesos`/`puedeQuemador` dicen qué renglones le tocan a quien entra, así que un permiso suelto solo ve su mitad. `GET /api/bitacora` sigue vivo para la lista plana. La antigua `wwwroot/admin/quemador/index.html` es ahora una página que **redirige** a `/admin/bitacora/index.html?fuente=quemador` (conserva `usuario`, `dia`, `lote` y `texto`). La página de la bitácora **ya no tiene tabla general de movimientos**: es la tarjeta de usuarios (`GET /api/bitacora/resumen`) y **al hacer clic en una persona se abre una ventana flotante** (`dialog#ventanaUsuario`) con sus 2.000 movimientos más recientes, filtros y Excel propios —la hoja de **periodo** (`#vCal`) es propia —no el calendario que pinta el sistema— y ofrece cuatro modos (**Día**, **Mes**, **Año**, **Rango**) sobre los días que de plano dejaron renglones; la de **hora** (`#vHoraCal`) deja elegir una hora suelta o un tramo. Lo elegido viaja como `desde`/`hasta` (`yyyy-mm-dd`, los dos extremos incluidos) más `hora`/`horaHasta`: el servidor recorta antes de devolver y la pantalla vuelve a comprobar el filtro sobre la lista; `GET /api/bitacora/periodos?usuario=` es el índice «día → cuántos renglones dejó en toda su historia» que alimenta esas hojas, y solo cuenta los almacenes que el permiso del visitante puede ver—; quien solo tiene `quemador.ver` no puede leer `/resumen`, así que la misma tarjeta se arma agrupando los renglones del mostrador. Detalle en [`FRONTEND.md`](FRONTEND.md) §6b.
- **Escritura de auditoría fuera de la petición**: `QueuedAuditLog` captura usuario, IP y dispositivo y encola el evento; `AuditWorker` lo escribe en MySQL o JSONL. La cola es acotada (`Audit:QueueCapacity`, 5.000 por omisión) y, si se llena, se escribe directo para no perder el registro. Al detener el servicio se intenta vaciar lo aceptado.
- **Ubicación de cada acceso** (`Services/Ubicaciones.cs` + `Services/Lugares.cs`): la columna «Ubicación» se resuelve **al consultar** (los renglones de la bitácora no guardan dirección, así que no hay nada que migrar), en este orden: rango de IP con ubicación escrita a mano → calibración del sitio fijada en el mapa → dirección que reportó el propio equipo **por GPS del navegador** → dirección deducida de la IP del equipo → dirección automática del sitio. Un renglón sin IP queda vacío y `127.x` dice «Este equipo (el servidor)».
  - **Dónde se guarda**: `bitacora_lugar` (rango → ubicación escrita) y `bitacora_sitio` (el punto y la dirección del edificio, con quién y cuándo lo fijó). `Ubicaciones` los lee con caché corta; `UbicacionResolver` toma la decisión y expone `UbicacionDe(ip)` y `CoordenadasDe(ip)`, que son las columnas «Ubicación» y «Coordenadas» de la API y del Excel.
  - **El geocodificador** (`GeocodificadorLugar`, OpenStreetMap/Nominatim) tiene **dos lecturas del mismo punto**, cada una en su propio hueco de caché: `LeerExactoAsync`/`DireccionExactaAsync` (`zoom=18`) arma la **dirección postal completa** —calle con número, colonia, C.P., alcaldía, ciudad y, si el punto cayó en un sitio con nombre, ese nombre abre la dirección— en `DesmenuzaExacta`, que es lógica pura sin red; `DireccionAsync` (`zoom=16`) da solo la zona. La lectura fina **solo** se pide para el punto que marcó el administrador o para coordenadas reales del aparato: deducir calle y número de una IP daría una dirección finísima pero falsa, y eso no sirve para justificar una falta. Cortesía con OpenStreetMap: una petición por segundo, User-Agent identificable, caché 30 días por punto y modo, reintento a las 6 horas de un fallo y presupuesto de 4 segundos.
  - **API**: `GET /api/bitacora/ubicaciones` (rangos y equipos, con `coordenadas` por equipo) y `PUT`/`DELETE` sobre ese mismo camino para la ubicación escrita a mano (`configuracion.administrar`); `GET /api/bitacora/sitio` (`bitacora.ver`; devuelve la dirección calibrada, el punto y `coordenadas`, o 204 si el sitio no está calibrado); `POST`/`DELETE /api/bitacora/sitio` (`configuracion.administrar` — el POST **guarda el texto que el administrador dejó en el campo** y solo calcula la dirección cuando viene vacío); `GET /api/bitacora/sitio/preview?lat=&lon=` (`configuracion.administrar`; **lee sin guardar**). Los equipos reportan su propio punto con `POST /api/ubicacion` (anónimo; `fuente = navegador` si trajo coordenadas GPS, `ip` si no). **Desde el 2026-11-02 la pantalla de la bitácora ya no expone ni el mapa ni el panel de rangos** (el usuario pidió quitarlos: «el chiste es que se tenga la ubicación geográfica de manera automática»), así que los endpoints de `/sitio` y `/ubicaciones` siguen vivos en el servidor pero no se usan desde `wwwroot`: hoy la columna «Ubicación» sale solo de lo que reporta cada equipo. Ojo con lo que esto implica: mientras la intranet se sirva en HTTP el navegador no entrega el GPS real (solo en HTTPS), así que el reporte automático es la geolocalización por IP y da **zona, no edificio**.

---

## 3b. Módulo Gestión — actividades/incidentes/tickets, reportes, permisos

Un **único elemento** (`record Actividad`, `Models/Actividad.cs`) con campo `Tipo`
(`Actividad` | `Incidente` | `Ticket`) representa todo lo gestionable. Los tickets llevan
`Numero` consecutivo y **todo elemento pertenece a un hospital** (`Hospital`, obligatorio:
HES | HGR | HGO | HI, catálogo en `Models/Hospitales.cs`). Propiedades calculadas que se
serializan a JSON y usan todas las pantallas:
`Etiqueta` (`#NNN · título` para tickets), `Vencida` (fecha límite pasada y sigue abierto),
`ProximoReporte` y `ReportePendiente` (según periodicidad y último reporte).

- **Estados** (`EstadosActividad`): **solo tres** — `Pendiente`, `Completada` y `Cancelada`.
  «En proceso» y el cierre formal se retiraron de toda la intranet; los registros viejos con esos
  estados se traducen al leer (`ActivityStoreBase.Normalizar`: `EnProceso → Pendiente`,
  `Cerrada → Completada` con `Avance = 100`). La única vuelta a `Pendiente` es
  `GestionService.Reabrir` (permiso `items.reabrir`, Admin): exige `Completada`, deja
  `Avance = 0` y `Cerrado = null`, **conserva la evidencia** y anota el motivo en historial y bitácora.
  `CambiarEstado` sigue rechazando `→ Pendiente` por cualquier otra vía.
  `Completada` exige **al menos un archivo de evidencia** —el informe es **opcional**, solo se vigila su
  tope de 2000 caracteres— (ver más abajo).
  `Cancelada` solo con el permiso `items.cancelar` (Administrador por defecto) y con justificación
  obligatoria; **lo cancelado se congela**: no se reabre, no se edita, no se reasigna, no se elimina,
  no admite comentarios, evidencia ni reportes — solo queda el registro y se puede consultar.
- **Recordatorio de seguimiento cada 3 días** (`Actividad.DiasParaRecordar`, `LeTocaReporte`): mientras el
  elemento siga abierto y hayan pasado 3 días o más desde el último comentario u observación del historial
  —o desde la fecha de inicio, si todavía no tiene ninguno—, la API publica `ultimoComentario` y
  `leTocaReporte = true`, y la tarjeta y el chip del calendario avisan, en naranja, «Favor de registrar
  avances en comentarios.». Cada
  comentario nuevo reinicia la cuenta. `UltimoComentario` **no se guarda**: lo decora
  `GestionService` al leer (`Visibles`/`Find`) con `IHistorialStore.UltimosComentarios()`.
- **Alta del Ingeniero en un solo paso** (`GestionService.CrearYCerrada`): cuando quien registra una
  **Actividad** no tiene `tickets.asignar` —el Ingeniero—, la actividad **nace ya `Completada`**: la primera
  ventana solo junta los datos (no guarda nada) y la ventana de la evidencia hace el alta multipart
  (`tipo`, `hospital`, `titulo`, `descripcion`, `fecha`, `informe`, `archivos[]`) que crea, adjunta y
  completa en la misma petición. El informe es opcional y la evidencia obligatoria; sin evidencia el alta
  se rechaza y no queda nada a medias (rollback: `evidencias.Descartar` por archivo y `DeshacerAlta`).
  Un `POST /api/actividades` en **JSON** de quien no puede asignar y con tipo Actividad se rechaza con
  **400** («las actividades se crean ya completadas»). Los **Incidentes** del Ingeniero siguen naciendo
  `Pendiente` por JSON, como siempre.
- **Prioridades** (`Prioridades`): Baja / Media / Alta / Urgente. **Avance**: 0–100 (heredado: ya no se
  registra a mano, solo lo sellan `Completada = 100` y `Reabrir = 0`).
- **Reglas de negocio** en **`GestionService`** (`Services/Gestion.cs`), no en los almacenes:
  permisos por operación, transiciones de estado, asignación formal (solo Admin),
  reportes, observaciones y registro en historial + bitácora. Los endpoints son wrappers finos.
- **Almacenes duales** (MySQL o JSON, elegidos al arrancar por el sondeo `useMysql`):
  `IActivityStore` (`Services/Actividades.cs`), `IReportStore` (`Services/Reportes.cs`),
  `IHistorialStore` (`Services/Historial.cs`, append-only), `IPermissionStore`
  (`Services/Permisos.cs`), `IConfigStore` (`Services/Config.cs`), `IInventarioStore`
  (`Services/Inventarios.cs`), `IDocumentoStore` (`Services/Documentos.cs`), `IRoleStore` /
  `IUserPermissionStore` (`Services/RolesStore.cs`), `IAvisoStore` (`Services/Avisos.cs`),
  `IActaStore` (`Services/Actas.cs`) e `ICredencialStore` (`Services/Credenciales.cs`), más
  `IEvidenciaStore` (`Services/Evidencias.cs`: tabla `evidencias` o `App_Data/evidencias.json`, con el
  archivo en disco dentro de `Gestion:EvidenciasDir` —por defecto `App_Data/evidencias—`).
- **Evidencia del trabajo** (`Models/Evidencia.cs` + `Services/Evidencias.cs`): lo que se adjunta como foto o
  archivo al marcar un elemento como `Completada`. `EvidenciaRules` es la única lista de lo aceptado —hasta
  **5 archivos por envío** de **20 MB** cada uno, extensiones de foto y de oficina (jpg, jpeg, png, gif, bmp,
  webp, tif, tiff, pdf, doc, docx, xls, xlsx, ppt, pptx, txt, csv, log, zip, rar, 7z) y **verificación de
  firma** contra lo que dice la extensión—; el servicio escribe con nombre GUID, valida el recorrido de ruta
  (`RutaAbsoluta`), rechaza un elemento ya cerrado, registra `AdjuntarEvidencia` en la bitácora y un renglón
  «Evidencia» en el historial, y sirve el archivo con el MIME de esa extensión validada, `nosniff` y
  `Content-Disposition` ASCII + UTF-8. `GestionService.CambiarEstado` a `Completada` **rechaza sin evidencia**,
  por la vía nueva (`POST /completar`) o por el viejo `PUT /estado`, así que el requisito no se puede saltar
  llamando a la API a mano.
- **Avisos del inicio** (`Services/Avisos.cs`, tabla `avisos` / `App_Data/avisos.json`): el panel lateral
  *Avisos* de `/Index` lo ven **todos** los usuarios con sesión; escribir (`POST/PUT/DELETE /api/avisos`
  y `GET /api/avisos/todos`) exige el permiso `avisos.administrar` (política `P:avisos.administrar`).
  `GET /api/avisos` devuelve solo los activos. Cada aviso tiene bandera `activo` (pausado = oculto a
  lectores pero editable por quien administra). Alta/edición/baja quedan en la bitácora.
- **Administrativos: actas y credenciales** (`Services/Actas.cs` tabla `actas` / `App_Data/actas.json`;
  `Services/Credenciales.cs` tabla `credenciales` / `App_Data/credenciales.json`). Página única
  `/admin/administrativos/` con dos pestañas, protegida por un guard que admite `actas.administrar` **o**
  `credenciales.administrar`; cada pestaña y cada API exige su propia clave (`P:actas.administrar`,
  `P:credenciales.administrar`). Las **actas** llevan folio autogenerado (`ACTA-{año}-{n:D4}`), **tipo**
  (`conducta` = mala conducta, `faltas` = faltas injustificadas; `ReglasActa.NormalizarTipo` cae en
  `conducta` si viene vacío/inválido), persona, puesto, lugar, **hora**, fecha de hechos, hechos, acuerdos
  y testigos. La impresión usa la **plantilla legal oficial según el tipo** (`cuerpoActa(a)` en
  `administrativos.js`): *mala conducta* reproduce el formato general en mayúsculas (cláusulas de reserva
  legal y de testigos, firmas de involucrado/testigos/elaboró) y *faltas* el formato por inasistencias
  (narrativa, sanción prevista, firmas de jefe directo/elaboró/involucrado/testigos); empresa y domicilio
  del preámbulo son fijos de Meditechnosys. Al **elegir el usuario ligado** (acta o credencial) el
  **nombre y el puesto se autocompletan** con el `nombre` y el `cargo` guardados en la ficha del usuario
  (`/api/usuarios/asignables` los trae); el selector muestra "usuario — Nombre (rol)". Las
  **credenciales** van **ligadas a un usuario** (`/api/usuarios/asignables`) y guardan nombre, puesto, NSS,
  cédula, vigencia (`MM/AAAA`) y una **foto tamaño infantil** (2.5×3 cm, recortada en el navegador). Las
  fotos se validan en el servidor (content-type + magic bytes JPEG/PNG, ≤2 MB) y se guardan en
  `App_Data/credenciales/foto-{id:D5}.ext`; la credencial se dibuja sobre la plantilla oficial
  `wwwroot/img/credencial/{frente,reverso}.png`. La **impresión sale en una sola hoja** (frente a la
  izquierda, reverso a la derecha, línea punteada de doblez entre ambos; `page-break-inside: avoid`) y el
  **PNG descargado trae las dos caras** en una sola imagen (`lienzoAmbas`); `htmlCred`/`lienzoAmbas` quedan
  expuestos en `window.Administrativos` para vista previa. Alta/edición/baja quedan en la bitácora.
- **Historial de cambios** (`record Cambio`): cada diferencia de campo se registra con antes/después
  y comentario; **nunca se borra**. Por eso `Eliminar` solo aplica a `Pendiente` sin reportes. **Verlo es
  un permiso, no una consecuencia de ser el dueño**: la pestaña «Historial de cambios» de la tarjeta y el
  endpoint correspondente los gobierna `tickets.historialCambios` (`GestionService.HistorialDe` devuelve
  vacío sin él), de modo que el Ingeniero se queda solo con sus **reportes de estatus**.
- **Permisos** (`Models/Permisos.cs`, `CatalogoPermisos`): **46 claves en 13 grupos**, con **versión de
  catálogo** (`Version = 15`; la 13 trajo `items.reabrir`, la 14 trajo `items.cancelar` y retiró
  `items.registrarAvance` y `tickets.cerrar`, y la 15 es cuando `tickets.historialCambios` **empieza a
  consultarse de verdad** y sale de los defectos del Ingeniero). Matriz editable por rol (`IPermissionStore`) **y ajustes por usuario**
  (`IUserPermissionStore`); los **permisos efectivos** los calcula `IPermissionResolver` (matriz +
  overrides). Por defecto Admin = todas, Ingeniero = subconjunto de seguimiento + inventarios + Playbook
  de consulta —y **sin** `items.reabrir` ni `items.cancelar`, que también faltan en `IngenieroPorDefecto` junto con
  `items.editar`, `items.eliminar`, `configuracion.fechaLimite` y, desde la 15, `tickets.historialCambios`. Los roles son dinámicos (`IRoleStore`, tabla `roles`). El Admin siempre conserva
  `roles.administrar` al guardar (anti-bloqueo). Cuando se agregan claves
  en una versión nueva, `NuevasDesde` completa las matrices ya guardadas con el valor por defecto del
  rol, de modo que nadie pierde acceso tras una actualización. **Matiza ese "nadie"**: solo se completan
  las claves que **están en el defecto del rol**, así que una clave nueva se le tiene que declarar un
  `Desde` acorde —una matriz de Admin guardada en la versión 13 recibe `items.cancelar` sola al arrancar
  el servicio porque el Admin la tiene por defecto; en cambio `tickets.historialCambios` necesitó
  `Desde: 15` para que la matriz de Admin ya guardada la recibiera, porque nació en la versión 1 con valor
  `true` para Admin y sin esa marca la clave no le llegaría nunca. A un rol **propio** guardado antes del
  cambio tampoco le llega: si se quiere que vea el historial, se le concede la clave en «Roles y permisos».
  Las claves
  retiradas no se borran de las matrices guardadas: al leerlas se filtran con `CatalogoPermisos.EsClaveValida`,
  así que un registro viejo no revive un botón que ya no existe.
- **Periodicidad de reportes**: por elemento (`PeriodicidadDias`, fijada al asignar) y valor por
  defecto global en `IConfigStore` (`reportes.periodicidadDias`, default `7`).
- **Fecha límite (solo administrativa)**: la clave `items.fechaLimiteHabilitada` (default `true`)
  habilita o deshabilita el uso de la fecha límite. `GestionService.FechaLimiteEfectiva` la aplica:
  **un Ingeniero nunca la ve ni la fija**, y si está deshabilitada tampoco el Admin (los datos ya
  guardados se conservan, pero no se solicitan, no se validan y no se muestran).
  `/api/permisos/mios` publica el booleano `fechaLimite` y todas las pantallas lo respetan.

- **API del módulo** (todo exige sesión; cada operación queda en bitácora):

| Método y ruta | Rol / permiso | Qué hace |
|---|---|---|
| `GET /api/actividades` | `Ingenieria` | Lista lo visible: Admin todo; Ingeniero lo suyo o lo que creó. |
| `GET /api/actividades/{id}` | `Ingenieria` | Detalle (dueño, creador o Admin). |
| `POST /api/actividades` | `Ingenieria` + `items.crear*` | Alta. Acepta **JSON** (Admin: crea `Pendiente` y puede asignar; Ingeniero: solo **Incidentes**, `Pendiente` asignado a sí mismo, Media y sin fecha límite — una Actividad en JSON de quien no puede asignar se rechaza con **400**) y **multipart** (`tipo`, `hospital`, `titulo`, `descripcion`, `fecha`, `informe`, `archivos[]` → `CrearYCerrada`, el elemento **nace `Completada`** con su evidencia; 201; sin evidencia, 400 y no queda nada a medias). |
| `PUT /api/actividades/{id}` | `Ingenieria` + `items.editar` | Edita solo elementos abiertos. |
| `DELETE /api/actividades/{id}` | `Ingenieria` + `items.eliminar` | Solo `Pendiente` sin reportes. |
| `PUT /api/actividades/{id}/estado` | según transición | Solo dos destinos: `Completada` (dueño + `items.actualizarEstatus`, informe **opcional** + evidencia ya adjuntada) y `Cancelada` (justificación obligatoria + permiso `items.cancelar`). Cualquier otro estado —incluidos `EnProceso`, `Cerrada` y volver a `Pendiente`— responde **400**. |
| `POST /api/actividades/{id}/completar` | `Ingenieria` (dueño o con `tickets.asignar`) | **multipart**: `archivos` (hasta 5, 20 MB c/u) + `informe` (**opcional**, puede ir vacío). Es la ventana de evidencia del alta del Ingeniero y el botón *Completar* de la tarjeta. Adjunta la evidencia y pasa a `Completada` en la misma petición; si la evidencia se rechaza, el elemento no cambia de estado. |
| `POST /api/actividades/{id}/reabrir` | `Ingenieria` + `items.reabrir` (lo revisa `GestionService.Reabrir`, no una política) | JSON `{ motivo }` (opcional, ≤1000). Solo sobre `Completada`: devuelve a **`Pendiente`** con `Avance = 0`, `Cerrado = null` y **la evidencia guardada intacta**. Anota historial y `AuditEvents.ReabrirActividad`. 403 sin el permiso, 400 si el elemento sigue abierto **o está cancelado** (lo cancelado no se reabre). |
| `GET /api/actividades/{id}/evidencias` | `Ingenieria` (quien puede ver el elemento) | Lista la evidencia del elemento para la tarjeta (nombre, tipo, peso, quién y cuándo). |
| `GET /api/evidencias/{id}` | `Ingenieria` (dueño, creador o Admin) | El archivo en disco: `inline` si es foto, `attachment` si es documento, MIME de la extensión validada al subir, `nosniff` y `Cache-Control: private, max-age=120`. |
| `PUT /api/actividades/{id}/asignar` | `SoloAdmin` + `tickets.asignar` | Ingeniero, prioridad, fecha límite, periodicidad. |
| `POST /api/actividades/{id}/observaciones` | dueño o Admin con `tickets.observaciones` | Comentario u observación al historial; reinicia el recordatorio de 3 días. Sobre una cancelada, 400. |
| `GET /api/actividades/{id}/historial` | dueño o Admin **+ `tickets.historialCambios`** | Historial de cambios. Sin el permiso responde **`[]`** aunque se sea el dueño. |
| `GET/POST /api/actividades/{id}/reportes` | dueño o Admin / dueño + `reportes.entregar` | Consulta / entrega reportes de estatus. |
| `GET /api/reportes` | `SoloAdmin` + `reportes.consultar` | Reportes globales con filtros, etiqueta y **hospital** del elemento. |
| `GET /api/roles` | `P:roles.administrar` | Catálogo (`grupos`) + `roles` (nombre/esSistema/creado) + `matriz` activa. |
| `POST /api/roles` | `P:roles.administrar` | Crea un rol nuevo (nombre libre). |
| `PUT /api/roles/{nombre}/renombrar` | `P:roles.administrar` | Renombra un rol no-sistema. |
| `DELETE /api/roles/{nombre}` | `P:roles.administrar` | Elimina un rol no-sistema. |
| `PUT /api/roles` | `P:roles.administrar` | Guarda la matriz de un rol (Admin retiene `roles.administrar`). |
| `GET /api/usuarios/{u}/permisos` | `P:usuarios.administrar` | `grupos` + `rolPermisos` + `ajustes` + `efectivos` del usuario. |
| `PUT /api/usuarios/{u}/permisos` | `P:usuarios.administrar` | Guarda los ajustes (overrides) del usuario. |
| `GET /api/usuarios/asignables` | aserción (ver §Roles y políticas) | Lista ligera `{usuario, rol, nombre, cargo}` de cuentas activas. |
| `GET /api/avisos` | sesión | Avisos **activos** del panel del inicio (los ven todos). |
| `GET /api/avisos/todos` | `P:avisos.administrar` | Todos los avisos (incluye pausados) para el editor. |
| `POST /api/avisos` | `P:avisos.administrar` | Publica un aviso. |
| `PUT /api/avisos/{id}` | `P:avisos.administrar` | Edita título/etiqueta/texto/activo. |
| `DELETE /api/avisos/{id}` | `P:avisos.administrar` | Elimina un aviso. |
| `GET /api/actas` | `P:actas.administrar` | Lista las actas administrativas. |
| `POST /api/actas` | `P:actas.administrar` | Crea un acta (folio autogenerado `ACTA-{año}-{n:D4}`). |
| `PUT /api/actas/{id}` | `P:actas.administrar` | Edita un acta. |
| `DELETE /api/actas/{id}` | `P:actas.administrar` | Elimina un acta. |
| `GET /api/credenciales` | `P:credenciales.administrar` | Lista las credenciales (con URL de foto por id). |
| `POST /api/credenciales` | `P:credenciales.administrar` | Crea una credencial ligada a un usuario (multipart con foto). |
| `PUT /api/credenciales/{id}` | `P:credenciales.administrar` | Edita los datos de una credencial. |
| `POST /api/credenciales/{id}/foto` | `P:credenciales.administrar` | Reemplaza la foto (borra el archivo anterior). |
| `GET /api/credenciales/{id}/foto` | `P:credenciales.administrar` | Sirve el archivo de la foto. |
| `DELETE /api/credenciales/{id}` | `P:credenciales.administrar` | Elimina una credencial y su foto. |
| `GET /api/permisos/mios` | autenticado | Permisos activos de la sesión + `fechaLimite`, `hospitales` y `hospitalesConGeneral` (la UI oculta lo no permitido). |
| `GET/PUT /api/config` | `SoloAdmin` + `configuracion.administrar` | Lee / guarda configuración del módulo. El interruptor de fecha límite exige además `configuracion.fechaLimite`. |
| `GET /api/historico/excel` | `Ingenieria` + `historico.actividades` + `historico.exportar` | Descarga el histórico filtrado en **.xlsx** (mismos filtros que la pantalla). |
| `GET /api/inventario` | `Ingenieria` + `inventario.consultar` | Esquema de las tres colecciones (campos, tipos, opciones y límites). |
| `GET /api/inventario/{coleccion}` | `Ingenieria` + `inventario.consultar` | Registros por colección (`equipos`/`ips`/`aetitles`), con `?hospital=` y `?q=`. |
| `GET /api/inventario/{coleccion}/excel` | + `inventario.exportar` | Descarga el inventario filtrado en **.xlsx**. |
| `POST /api/inventario/{coleccion}` | `SoloAdmin` + `inventario.administrar` | Alta de un registro. |
| `PUT /api/inventario/{coleccion}/{id}` | `SoloAdmin` + `inventario.administrar` | Edición de un registro. |
| `DELETE /api/inventario/{coleccion}/{id}` | `SoloAdmin` + `inventario.administrar` | Baja de un registro. |
| `GET /api/playbook/documentos` | `Ingenieria` + `playbook.consultar` | Lista de documentos con `?hospital=`, `?q=` y `?estado=`. |
| `GET /api/playbook/documentos/{id}/pdf` | `Ingenieria` + `playbook.consultar` | Entrega el PDF en línea (`inline`, con rangos y cabeceras de seguridad). |
| `POST /api/playbook/documentos` | `SoloAdmin` + `playbook.administrar` | Sube un documento (multipart; valida que sea PDF real, máx. 20 MB). |
| `PUT /api/playbook/documentos/{id}` | `SoloAdmin` + `playbook.administrar` | Edita nombre, versión, hospital, fecha de publicación o estado (nunca toca el PDF). |
| `POST /api/playbook/documentos/{id}/archivo` | `SoloAdmin` + `playbook.administrar` | Reemplaza el archivo por una versión nueva y sella la fecha de actualización. |
| `DELETE /api/playbook/documentos/{id}` | `SoloAdmin` + `playbook.administrar` | Elimina el documento y su archivo. |

- **Pantallas** (formato DICOM vía `css/panel.css`, lógica compartida en `js/gestion.js`):
  `/calendario/` (panel de tickets a la izquierda + rejilla mensual + filtro por hospital),
  `/admin/roles/`, `/admin/reportes/`, `/admin/config/`, `/gestion/historico/`,
  `/gestion/inventario-equipos/`, `/gestion/inventario-ips/`, `/gestion/inventario-aetitles/` y
  `/gestion/playbook/`. `gestion.js` aporta `Gestion.init/api`, `formDialog` (diálogos genéricos),
  `abrirDetalle` (barra de estatus, metadatos, pestañas Reportes/Historial y acciones según
  rol/permiso/estado), `abrirAsignar/abrirEditar/abrirCrear`, el catálogo de hospitales
  (`HOSPITALES`, `opcionesHospital`, `campoHospital`), el interruptor de fecha límite
  (`usaFechaLimite`) y las utilidades de transferencia (`descargar`, `subir`).

---

## 3c. Histórico único y exportación a Excel

**Una sola pantalla** (`wwwroot/gestion/historico/index.html`) consulta actividades, incidentes y
tickets; el tipo se distingue con el filtro **Tipo**. No existe ningún otro apartado para consultar
tickets (el panel izquierdo del calendario es sólo un panel de trabajo).

- **Filtros**: texto libre, hospital, tipo, ingeniero responsable, quién registró, estatus (incluida
  la opción *Vencida* sólo cuando la fecha límite está habilitada), prioridad, fecha de referencia
  (inicio / asignación / límite / finalización) y rango desde-hasta. Se combinan entre sí.
- **Resumen por ingeniero** (asignadas, pendientes, en proceso, terminadas, vencidas, tickets e
  incidentes) y tabla con Elemento, Tipo, Hospital, Ingeniero, Inicio, Límite (condicional),
  Prioridad, Estatus y Avance. Cada fila abre el detalle completo con seguimiento.
- **Descargar Excel** (`Services/Historico.cs` + `Services/Excel.cs`): `HistoricoExportador.Aplicar`
  reproduce **en el servidor** exactamente los filtros de la pantalla, de modo que el archivo contiene
  lo mismo que se ve. Columnas: ID, Elemento, Tipo, Hospital, Fecha, Registrado por, Ingeniero
  responsable, Descripción, Prioridad, Estatus, Fecha de creación, Fecha límite (sólo si está
  habilitada), Fecha de finalización, % avance, Último reporte, Próximo reporte y Observaciones.
- **Siempre `.xlsx` real** (ClosedXML, con encabezado, anchos, filtro automático y primera fila
  congelada). **Nunca se genera CSV.** El nombre del archivo refleja los filtros
  (`Historico_actividades_HES_Ticket_20261001_20261031.xlsx`).
- La misma capa `Excel.Tabla` genera los Excel de los inventarios; la descarga la hace el navegador
  con `Gestion.descargar`, que usa la cookie de sesión y respeta el `Content-Disposition`.

---

## 3d. Inventarios (equipos, IPs y AETitle)

Tres módulos **independientes, cada uno en su propia pantalla** (no se mezclan), con la misma
mecánica: consulta, filtro por hospital, búsqueda libre, visualización ordenada y descarga en Excel.

- **Esquema por colección** (`Models/Inventario.cs`, `InventarioCatalogo`): cada colección declara sus
  campos (`CampoInventario`: clave, etiqueta, tipo `texto|area|numero|lista`, obligatorio, longitud
  máxima, opciones y validación). `GET /api/inventario` publica el esquema y la pantalla construye
  sola las columnas y el formulario, así que **ampliar campos** es editar sólo ese archivo.
- **Reglas** (`InventarioRules`): hospital obligatorio y válido, campos obligatorios presentes y
  validaciones propias (por ejemplo formato de IP). `InventarioStoreBase.Limpiar` descarta claves
  ajenas al esquema y recorta longitudes antes de guardar.
- **Servicio** (`Services/Inventario.cs`): `Filtrar` exige `inventario.consultar`; alta, edición y
  baja exigen Admin + `inventario.administrar` y quedan en bitácora; `Exportar` exige
  `inventario.exportar` y respeta hospital y búsqueda.
- **Persistencia**: tabla única `inventario` discriminada por `coleccion` (MySQL) o
  `App_Data/inventario.json`. Los campos propios de cada colección viajan en `datos` (JSON), por lo
  que agregar un campo no requiere migrar la tabla.

---

## 3e. Playbook (documentación técnica en PDF)

Biblioteca de manuales, procedimientos y guías en **PDF**, clasificados por hospital
(HES/HGR/HGO/HI/**General**).

- **Modelo** (`Models/Documento.cs`): nombre, nombre del archivo, versión, hospital, fecha de
  publicación, fecha de actualización, quién lo subió, fecha de carga, tamaño, ruta, estado
  (`Vigente`/`Retirado`). `DocumentoRules` valida nombre, versión, hospital, estado y el archivo
  (extensión `.pdf`, tamaño máximo **20 MB** y firma real `%PDF-` en los primeros bytes).
- **Servicio** (`Services/Playbook.cs`): `Subir`, `Editar`, `Reemplazar`, `Eliminar`, `RutaPdf` y
  `RutaAbsoluta` (a prueba de traversal). **Editar metadatos nunca modifica el contenido del PDF**;
  sólo un cambio de versión o un reemplazo explícito sellan `FechaActualizacion`.
- **Archivos**: se guardan con nombre GUID dentro de `Playbook:StorageDir` (por defecto
  `App_Data/playbook`); al eliminar el documento se borra también su archivo, y si el almacén rechaza
  el registro el archivo recién escrito se revierte.
- **Permisos**: el Administrador (`playbook.administrar`) sube, renombra, cambia la versión,
  reemplaza el archivo, consulta la información y elimina. El Ingeniero (`playbook.consultar`)
  **sólo** ve nombre/versión/fecha de actualización, abre y descarga; en la interfaz no aparecen los
  botones de administración y el servidor los rechaza con 403.
- **Borrado**: el diálogo usa exactamente el texto requerido
  («¿Está seguro de que desea eliminar este documento?» / «Esta acción eliminará el documento del
  Playbook.» / *Cancelar* / *Eliminar*).

---

## 4. Repositorio de Patología — `Patologia/`

Es el puerto a C# del antiguo sistema Python (que quedó **retirado**). Áreas: **`esp`** (Especialidades) y **`gineco`** (Ginecología).

### Componentes
- **`PatoDb`** — conexión a `RepoPato`, mapa de áreas, rutas de archivos (`RutaRelativa` = `área/00000123.pdf`), normalización de texto (quitar acentos para buscar) y **auditoría** (`Auditar` registra cada acción en la tabla `auditoria`).
- **`Extractor`** — con **PdfPig** lee el texto del PDF y extrae los campos: **folio, fecha, nombre, NSS, agregado médico y modalidad** (reconoce una lista de modalidades). Si no los encuentra en el contenido, deduce el folio del nombre del archivo.
- **`Repositorio.Guardar`** — valida (máx. **20 MB**, que sea PDF real), **deduplica** (si el folio ya existe en el área, lo omite/avisa), guarda el archivo en disco y la fila en `reportes`. Devuelve un `ResultadoGuardado` con ok/error/aviso y los datos extraídos.
- **`Importador`** — dos vías de carga masiva:
  - **Carpetas "inbox"**: `ImportadorService` (un `BackgroundService`) revisa cada **30 s** las carpetas de entrada, procesa los PDF nuevos y los mueve a `procesados` o `errores`.
  - **Excel**: `ImportarExcel` (con **ClosedXML**) carga una plantilla `.xlsx` con muchos reportes a la vez.
- **`PatoApi`** — la API `/api/patologia/*` (política `Ingenieria`).

### Endpoints de Patología
| Método y ruta | Qué hace |
|---|---|
| `GET /api/patologia/me` | Devuelve `{ usuario, rol }` de la sesión. La UI lo usa para **no pedir usuario** y para mostrar/ocultar el botón *Eliminar* según el rol. |
| `GET /api/patologia/areas` | Lista las áreas (esp/gineco). |
| `GET /api/patologia/reportes` | Búsqueda paginada con filtros (texto, NSS, nombre, folio, modalidad, desde/hasta). |
| `GET /api/patologia/concentrado` | Exporta la búsqueda a **Excel** (.xlsx). |
| `GET /api/patologia/reportes/{id}` | Detalle de un reporte. |
| `GET /api/patologia/reportes/{id}/pdf` | Descarga/visualiza el PDF (con cabeceras de seguridad y caché privado). |
| `POST /api/patologia/reportes` | **Subida** de uno o varios PDF (el usuario sale de la sesión). |
| `PATCH /api/patologia/reportes/{id}` | Edita campos (folio, fecha, nombre, NSS, agregado, modalidad). |
| `DELETE /api/patologia/reportes/{id}` | Borra reporte y su archivo. **Solo Admin** (`.RequireAuthorization("SoloAdmin")`). |
| `POST /api/patologia/importar-excel` | Importación masiva por Excel. |

> La búsqueda replica los criterios del sistema Python (coincidencias por folio con/sin guiones, NSS solo dígitos, nombre normalizado por palabras, etc.).

### Pantallas web de Patología (`wwwroot/patologia/`)

Ambas comparten la **misma paleta y diseño** (encabezado negro con detalle dorado, botones azules) y el **combo box de Área** (Especialidades / Ginecología) en lugar de pestañas.

- **`consulta/index.html`** — pantalla **principal** de Patología:
  - Combo box **Área**, filtros (texto, tipo de estudio, fechas) y paginación.
  - Botón **Descargar Excel** (exporta la búsqueda) y, a su lado, **Subir** (lleva a `subida/` con el área elegida).
  - En cada fila: **Ver** (visor de PDF) y **Editar**.
  - **Editar** abre una **ventana emergente (modal)** centrada (`#modal-editar`) con el formulario; guarda con `PATCH`. Se cierra con *Cancelar*, clic fuera o `Esc`.
  - Dentro del modal, el botón rojo **Eliminar documento** solo se muestra si `/me` devuelve rol `Admin`
    (y el servidor lo vuelve a validar con `SoloAdmin`).
- **`subida/index.html`** — **solo carga**: zona de arrastre de PDFs e importación de Excel, con el combo box de Área.
  **Ya no incluye** la sección de edición (esa vive en `consulta/`). **Ya no tiene botón «Salir»**: arriba quedan
  únicamente **Regresar** y **Home**, y la etiqueta del usuario debajo de la cabecera; cerrar sesión se hace desde
  la intranet, que conserva su propio **Salir**.
- **Catálogo de la intranet** (`Models/Catalog.cs`): la pantalla Ingeniería muestra *Consulta de reportes* y el *Visor DICOM*;
  **no** hay acceso directo a `subida/` (se entra desde Consulta → **Subir**).

---

## 5. Visor DICOM — `Dicom/`

Es el antiguo proyecto **DicomQrApi** fusionado aquí (usa la librería **fo-dicom**). Permite **buscar estudios** en los PACS del hospital y **descargarlos**.

### Motor: `DicomQrService`
- **C‑FIND (buscar)**: `SearchAsync` consulta uno o varios **nodos PACS** (de `PacsNodes`) y devuelve la lista de estudios (`StudyInfo`: paciente, fecha, descripción, modalidad, UID, nodo de origen). Antes de enviar el C‑FIND **normaliza los criterios** (`NormalizarId` / `NormalizarTexto`): los IDs se recortan, se pasan a mayúsculas y, si el usuario escribió un valor **parcial sin comodines**, se les añade `*` al final para que coincida sin tener que teclearlo; un valor entre `"comillas"` fuerza **coincidencia exacta** y uno que ya traiga `*`/`?` se respeta. Una consulta **sin ningún criterio** se rechaza (`SinCriterio` → `400`) para no volcar el PACS entero, y los resultados se **deduplican** por (AE Title + StudyInstanceUID) y se **ordenan** por fecha descendente y nombre.
- **C‑MOVE / C‑GET (descargar)**: `DownloadAsync` trae las imágenes. En modo **`move`**, el PACS envía las imágenes a un **receptor C‑STORE propio** (`StoreScp`) que la app mantiene abierto en el puerto **11113** (`StartListener`), así varias descargas simultáneas no chocan.
- **Control de concurrencia**: un **semáforo por nodo** (`MaxParallelPerNode`) limita cuántas asociaciones se abren contra cada PACS a la vez, sumando todas las PC.
- **Descargas por lotes**: procesa en lotes (`BatchSize`) para poder reportar progreso y no abrir asociaciones eternas.
- **ZIP en partes**: al terminar se empaqueta en `dicom_downloads/_zips`. Si la descarga es grande —más de `ZipMaxStudies` estudios o más de `ZipMaxMBPorParte` MB— **no se rechaza**: se corta en **partes numeradas** (`dicom_<fecha>_<sesión>_parte1de3.zip`) respetando que **un estudio nunca quede partido entre dos ZIP** (la unidad de corte es la carpeta del estudio) y que cada parte cumpla el tope por separado. Cada parte devuelve su `downloadUrl` = **`/api/dicom/files/<nombre>.zip`** y la pantalla las baja una tras otra. Antes, al pasar de `ZipMaxStudies`, directamente no había ZIP. Los ZIP se conservan `ZipRetentionHours` y luego se borran; el repaso horario de `LimpiadorDescargas` es el que de verdad vigila ese plazo (antes solo se revisaba cuando alguien iniciaba otra descarga).
- **Verificación de integridad** (`VerificarDescargas`, por defecto encendida): el PACS responde *Success* aunque una imagen se pierda en el camino, así que al terminar se espera a que la carpeta se asiente (`EsperarAsentamiento`, 2 s sin cambios, tope 30 s), se cuenta **lo que llegó de verdad** y se compara contra **lo que el PACS anuncia** — el `expectedInstances` que ya traía la búsqueda o, si venía vacío, un C‑FIND por estudio leyendo `NumberOfStudyRelatedInstances` (topado a `VerificarConPacsMaxEstudios` = 300 para no castigar al PACS). Lo que falte se **vuelve a pedir** hasta `ReintentosMaximos` (**2**) rondas, y el resultado (`verified`, `missingInstances`, `retriedStudies`, `incompleteStudies` con paciente, fecha y nodo) viaja al progreso y a la pantalla. Contar **imágenes distintas** (hash FNV‑1a del SOP UID por estudio, en `DownloadSession`) es lo que evita el falso «todo bien» cuando un reintento vuelve a mandar las mismas: los contadores crudos sí se duplicarían. Pedir de más nunca se marca como falta; los archivos se sobrescriben por su nombre de SOP InstanceUID, así que re-pedir es idempotente. En el **reenvío** entre nodos la verificación va apagada (`verificar: false`) porque se llama estudio por estudio y la espera de asentamiento + el C‑FIND sumarían segundos a cada uno.
- La lógica de las dos decisiones (en cuántas partes se corta y qué falta) está aislada en **`Dicom/IntegridadDescarga.cs`**: recibe listas y devuelve listas, sin red ni disco, para poder comprobarla suelta (`../.harness-integridad`).
- **Sesiones aisladas**: cada descarga tiene su propia `DownloadSession` (contadores, carpetas y mensajes independientes), por lo que dos usuarios descargando a la vez no se mezclan.

### Progreso: `DownloadJobService`
- Registro **en memoria** de trabajos con su progreso (`pending → running → completed/failed`), **fase** (`queued | retrieving | verifying | retrying | zipping | done`), etiqueta legible (estudios, paciente(s) y nodos) y mensajes. Los trabajos se limpian a las ~6 h.
- **Varias descargas a la vez**: `Encolar(jobId, cuerpo)` reparte con un `SemaphoreSlim` de `MaxDescargasSimultaneas` (**3**, recortado a 1–8). Las que llegan con el cupo lleno se quedan en `pending`/`queued` —la pantalla dice «En cola» y la barra sigue en 0 %— y arrancan solas cuando se libera un hueco. El límite existe para no saturar el PACS: cada descarga abre sus propias asociaciones y el tope real por nodo lo pone `MaxParallelPerNode`, **compartido entre todas**. Si ya hay más de `MaxTrabajosEnCola` (**20**) trabajos vivos, `Encolar` devuelve `false` y el controlador responde **429** («inicia menos descargas»). `POST …/download/start` responde de inmediato con `jobId`, `statusUrl` y `startedNow`.
- `GET /api/dicom/studies/download/activos` devuelve los trabajos vivos para que la pantalla **se reenganche** a las descargas en curso después de recargar la página o abrir el apartado desde otra ventana; `POST /api/dicom/studies/download/{jobId}/cancel` cancela **uno** sin tocar los demás (lo ya bajado se conserva, solo se pierde el ZIP).
- `TieneTrabajoActivo()` (`TrabajosVivos() > 0`) le dice al depurador si conviene o no meter la mano en la carpeta.

### Depuración de descargas: `LimpiadorDescargas` + `DepuradorDescargas`
- **`LimpiadorDescargas`** es un `BackgroundService` (mismo patrón que `ImportadorService`): primer repaso **20 s** después de arrancar —así se recoge la basura que deja un reinicio a mitad de descarga— y luego uno cada `Local:DepurarCadaMinutos` (**60**). Nunca lanza: un fallo se registra y se reintenta en el repaso siguiente. Deja el resultado en `Ultima`, que alimenta la tarjeta **Depuración de descargas** del panel SISTEMAS (`/api/status`), la cual **no vuelve a medir el disco**, solo reporta lo que dijo el último repaso.
- **`DepuradorDescargas.Ejecutar`** son las reglas puras (sin temporizador, para poder probarlas sobre cualquier carpeta): borra `.dcm` y `.tmp` mayores de `Local:ConservarHoras` (**3 h**), ZIP de `_zips` mayores de `Local:ZipRetentionHours` (**24 h**), poda las carpetas de paciente/estudio vacías (conserva la raíz y `_zips`) y, si la carpeta sigue pasando de `Local:MaxMB` (**4096**), elimina lo más viejo primero hasta quedar debajo — **sin tocar nunca los ZIP**, que tienen su propia regla.
- **Dos rieles de seguridad:** con **algún trabajo vivo** en verdadero **no borra nada** — se consultan **los dos registros**, `DownloadJobService.TieneTrabajoActivo()` **y** `ReenvioJobService.TieneTrabajoActivo()`, porque un reenvío entre nodos también escribe dentro de `dicom_downloads` (en `reenvio/{jobId}`) y el repaso lo podía dejar a medias. Y jamás toca un archivo modificado en los últimos **2 minutos** (`MargenProteccion`), porque una transferencia puede estar escribiendo ahora mismo aunque el registro aún no lo refleje. Los archivos bloqueados (visores abiertos) se cuentan, se avisan y se reintentan.
- **Se apaga sin republicar** con `Local:DepurarDescargas=false`.

### Endpoints DICOM (controladores MVC, política `Ingenieria`)
| Método y ruta | Qué hace |
|---|---|
| `GET /api/dicom/status` | Estado: AE local, modo y **lista de nodos PACS** (para el selector de la UI). |
| `GET /api/dicom/studies` | Búsqueda (C‑FIND) por parámetros en la URL. |
| `POST /api/dicom/studies/search` | Búsqueda con el criterio en el cuerpo (JSON). |
| `POST /api/dicom/studies/download/start` | **Inicia** la descarga en segundo plano y devuelve `jobId`, `statusUrl` y `startedNow`. Varias a la vez; con la cola llena responde **429**. |
| `GET /api/dicom/studies/download/activos` | Trabajos en curso o en cola, para que la pantalla se reenganche tras recargar. |
| `GET /api/dicom/studies/download/{jobId}/progress` | Progreso de la descarga (barra, fase, verificación y partes del ZIP). |
| `POST /api/dicom/studies/download/{jobId}/cancel` | Cancela **ese** trabajo; los demás siguen. |
| `POST /api/dicom/studies/download` | Descarga síncrona (compatibilidad; espera a terminar). |
| `POST /api/dicom/studies/download/{studyInstanceUid}` | Descarga un solo estudio. |
| `GET /api/dicom/files/{nombre}.zip` | Descarga el ZIP generado (con rangos HTTP, reanudable). |

La pantalla **`wwwroot/dicom/index.html`** consume estas rutas. Como está detrás del login, el navegador ya lleva la cookie y las peticiones funcionan sin pedir usuario.

> **Ojo con `/api/status`**: la intranet ya tenía un `GET /api/status` propio (estado de sistemas del catálogo). Por eso el DICOM se renombró a **`/api/dicom/status`** y no chocan.

### Lista de trabajo (MWL) — `WorklistService` + `Pages/ListaTrabajo.cshtml`

Apartado **Sistemas médicos → Consulta lista de trabajo** (`/dicom/lista-trabajo`), para el equipo de ingeniería clínica:
consulta la **agenda** de un nodo (C‑FIND sobre `ModalityWorklistInformationModelFind`) y manda la imagen de prueba al
**mismo nodo consultado**, para comprobar si lo recibe y qué queda guardado de ese estudio en su archivo. **No** toca el
PACS de descarga ni `dicom_downloads`.

- **Formato de panel.** La página usa `Layout = "_LayoutPanel"` —la plantilla Razor con el formato de
  `css/panel.css`, igual que `/dicom/` y los demás paneles— en lugar de `_Layout` con `intranet.css`. El
  `Layout` solo cambia cómo se pinta: los permisos, los endpoints y el `WorklistService` son los mismos.
  Ver [`FRONTEND.md`](FRONTEND.md) §2.

- **Solo programados.** La petición lleva `ScheduledProcedureStepStatus = SCHEDULED`, el periodo como rango DICOM
  `AAAAMMDD-AAAAMMDD` (o abierto `AAAAMMDD-` / `-AAAAMMDD`) y la modalidad como clave; `ScheduledStationAETitle` se deja
  vacío para no consultar solo al AE local. El **AE local es editable y arranca en `MEDI`**; viaja como *calling AE*, no
  como filtro.
- **Defensa ante SCPs que responden de más:** el servicio **re‑filtra la modalidad** sobre lo recibido, **excluye** lo que
  no esté `SCHEDULED` y cuenta por separado las respuestas sin secuencia o sin estado. Devuelve `omitidosSinEstado`,
  `omitidosNoProgramados` y `limitado` (tope `MaxResults = 5000` entradas / 20 000 respuestas), y la pantalla los dice.
  Límites: **60 s** la consulta, **15 s** el C‑ECHO, **30 s** la prueba y **15 s** la consulta de verificación; si el nodo
  no termina, **504** con mensaje claro.
- **Una sola conexión en la pantalla.** El bloque «Conexión del nodo» (AE local, Host/IP, Puerto, AE remoto y su Ping) es
  **el único sitio donde se escriben parámetros**: con esos mismos datos se hace el C‑FIND de la agenda, el C‑ECHO del
  Ping y el C‑STORE de la prueba. Antes había un segundo bloque «Conexión de destino Storage» con su propio host, puerto
  y AE; **ya no existe**, así que no puede darse el caso de consultar una lista en un nodo y mandar la prueba a otro sin
  querer. La línea `#lt-destino` repite siempre el destino real (`AE@host:puerto · AE local …`) y avisa cuando arriba
  todavía no hay una conexión válida escrita.
- **Filtros de pantalla** (fieldset «Filtros de las entradas programadas»): **Nombre**, **ID del paciente**,
  **Número de acceso**, **Periodo** y **Hora** —los dos, cajas de la hoja compartida `js/hoja-fecha.js` (`instalar` y
  `instalarHora`, con el mismo formato de la Bitácora)—, **Modalidad** y **Procedimiento**. Modalidad y fechas **se piden
  al nodo**; nombre, ID, **acceso**, hora y procedimiento filtran además lo ya recibido, en el navegador: **ningún filtro
  de pantalla borra entradas**, solo deja de mostrarlas, y si ninguna coincide la tabla se queda en su sitio con un
  renglón que lo dice (`tr.lt-row-vacia`) en lugar de desaparecer. El acceso se busca sin mayúsculas, sin acentos y por
  partes (`textoBuscado()`), sobre los hasta 16 caracteres del VR `SH` que devolvió el nodo. La hoja de hora escribe
  `'HH:00'`/`'HH:59'` en dos campos ocultos y **no viaja al nodo**: solo recorta lo recibido, y cada celda avisa cuántas
  entradas caen en esa hora. Una entrada sin fecha u hora legible **queda fuera** cuando se pide periodo u hora. Cambiar modalidad o periodo después
  de consultar deja el aviso «pulsa Consultar para pedir al nodo esta modalidad y este periodo».
- **Prueba de envío.** Tres orígenes de datos: `anonima` (`PRUEBA^CIRCULO`), `seleccion` (la fila elegida en la tabla) y
  `manual` (nombre, ID y acceso escritos a mano). Con `seleccion` hay **un menú de siete casillas** —Nombre, ID del
  paciente, Número de acceso, Modalidad, Procedimiento, Fecha y hora programadas, UID del estudio— todas marcadas por
  defecto. La pantalla manda la lista de claves marcadas en `datos.campos` y **el servidor decide dato por dato**: lo
  marcado viaja **tal cual lo devolvió el nodo** (nombre, ID, acceso, `Modality`, `StudyDescription`, `StudyDate`,
  `StudyTime` y el `StudyInstanceUID` programado); lo desmarcado —o lo que el nodo no trajo, o lo que no cabe en el tag—
  se queda con el valor técnico por defecto. `Incluye()` responde `verdadero` si `Campos` no llega, así que las
  peticiones antiguas, la anónima y la manual conservan su comportamiento. Nombre e ID solo se exigen si su casilla está
  marcada. El destino es **la conexión de arriba** —no hay campos propios de Storage ni selector de nodo preconfigurado—
  y el envío exige `confirmar: true`.
- **La imagen nunca es clínica** (`Dicom/ContrastTestImage.cs`): Secondary Capture 512×512 `MONOCHROME2`,
  `ConversionType = SYN`, píxeles generados aquí. Sus valores por defecto son `Modality = OT` y
  `StudyDescription = PRUEBA TECNICA - NO DIAGNOSTICO`, y **ambos tags pueden llevar la modalidad y el procedimiento de la
  entrada si el operador marcó esas casillas**: la imagen sigue marcada como prueba (`SYN`) y la fecha/hora **de toma**
  (`ContentDate`/`InstanceCreationDate`) sigue siendo la de ahora. **La serie y la instancia (SOP) son nuevas
  en cada envío**, así que no se reemplaza ninguna imagen existente. El **estudio** es el `StudyInstanceUID` programado
  cuando el origen es `seleccion`, la casilla está marcada y el UID llega con formato DICOM válido (`^[0-9.]{1,64}$`, se
  valida en el servidor); en `anonima` y `manual` —aunque el cuerpo traiga un UID— o sin la casilla, el estudio es nuevo.
  Con una entrada de la lista, por tanto, la imagen de prueba **sí queda dentro del estudio real del paciente**, que es
  lo que hace un equipo real y lo que evita que el destino abra un segundo registro bajo el mismo número de acceso;
  borrarla después se hace desde ese estudio. `GET …/patron` devuelve el BMP exacto que se manda.
- **Qué viajó realmente.** `WorklistTestResult` lleva, además del estado y el mensaje, el array `campos` (`dato`,
  `etiqueta`, `valor`, `viajo`, `motivo`) que `ContrastTestImage.Create` va llenando y los `tags` que quedaron escritos en
  la imagen (`estudioUid`, `serieUid`, `instanciaUid`, `acceso`); ambos se devuelven también en `advertencia`, `fallida` e
  `incierta`.
- **Verificación en el nodo después de cada envío** (`WorklistService.ConsultarPacsAsync`). Terminado el C‑STORE, el
  servicio abre **una segunda asociación contra el mismo destino** y hace un **C‑FIND de Study Root a nivel STUDY**
  (`SOP Class 1.2.840.10008.5.1.4.1.2.2.1`) buscado **solo por el `StudyInstanceUID` que escribió en la imagen** —si
  también se exigiera el acceso, un «no hay nada» podría ser mentira—, con `AccessionNumber`, `StudyDate`, `StudyTime`,
  `ModalitiesInStudy`, `StudyDescription` y `NumberOfStudyRelatedInstances` como claves de retorno. Devuelve
  `PruebaVerificacion(Soportada, Mensaje, Estudios)` con **tope de 15 s y sin lanzar nunca**: el resultado del envío no
  depende de que el nodo conteste. Cada caso tiene su frase honesta: sin UID aprovechable; el nodo **no ofrece la consulta**
  (excepción de asociación o **DICOM 0122** *SOP class not supported*); **rechazó** la consulta (otro código, se cita);
  **no terminó en 15 s** —el plazo se cumple y `SendAsync` vuelve sin respuesta: se dice que no terminó, nunca que «no
  tiene nada»—; respondió y **no devuelve ninguna entrada** con ese UID; o **dice tener N entradas**, con su acceso, fecha,
  hora, modalidades, descripción y el número de instancias que él cuenta. La pantalla lo pinta en el aviso del envío
  («En el nodo: …») y en el registro.
- **Registro permanente de las pruebas** (`Dicom/DicomPruebaStore.cs`). Cada prueba que pasa validación y permiso deja
  **una línea JSON** en `App_Data/dicom-pruebas.jsonl` (ruta configurable con `Dicom:PruebasFile`, misma convención que los
  demás almacenes de respaldo), con `id`, `momento`, `usuario`, `estado`, `mensaje`, nodo destino (`host`, `puerto`,
  `aeRemoto`, `aeLocal`), los `tags`, los `campos` dato por dato y la `verificacion` completa. `Registrar` **nunca lanza**
  —un fallo al apuntar no puede romper un envío que ya ocurrió—, los textos vienen recortados (usuario 40, estado 20,
  mensaje 300, host 253, AE 16) y `Listar(limite)` devuelve del más reciente hacia atrás con `limite` acotado a 1–200. Lo
  lee `GET /api/dicom/worklist/envios` y la pantalla lo muestra en la tabla «Registro de pruebas enviadas»
  (Hora · Usuario · Nodo destino · Resultado del envío · Qué dice el nodo que tiene) con botón **Actualizar**, contador de
  asientos y **sin botón de borrado**: sobrevive a recargar la página y a reiniciar la intranet. La antigua **ventana de
  logs en memoria** de la pestaña se retiró con su panel y su botón Limpiar; no la sustituye ningún otro control.
- **Privacidad:** los datos del paciente viajan solo en el cuerpo del POST (`RequestSizeLimit` 8 KB), no van en la URL ni
  se guardan en el navegador; respuestas con `NoStore` y POST con token anti‑falsificación en la cabecera
  `RequestVerificationToken`. **Excepción pedida explícitamente por el usuario:** el registro de pruebas **sí** guarda en el
  servidor los valores que viajaron en cada envío (nombre, ID, acceso y los tres UID) porque es la única forma de demostrar
  después qué salió y qué contestó el nodo. Vive en `App_Data`, no en la bitácora de accesos, no en la base de datos y no en
  el navegador, y solo lo leen usuarios con `DicomVer`; no se envía a ningún sitio externo.

| Método y ruta | Qué hace | Permiso |
|---|---|---|
| `GET /api/dicom/worklist/opciones` | AE local por defecto (`MEDI`) y si el usuario puede enviar (`puedeEnviar`). | `DicomVer` |
| `POST /api/dicom/worklist/buscar` | C‑FIND MWL con conexión, modalidad y periodo en el cuerpo. | `DicomVer` |
| `POST /api/dicom/worklist/eco` | C‑ECHO al nodo indicado → `{ conectado: true/false }`. | `DicomVer` |
| `GET /api/dicom/worklist/patron` | Vista previa BMP del círculo sintético (lo mismo que se envía). | `DicomVer` |
| `POST /api/dicom/worklist/prueba` | C‑STORE de **una** imagen de prueba al nodo escrito arriba y, después, C‑FIND de Study Root para decir qué guarda ese estudio. | `DicomEnviar` |
| `GET /api/dicom/worklist/envios?limite=50` | Registro permanente de las pruebas enviadas (`App_Data/dicom-pruebas.jsonl`), del más reciente hacia atrás. | `DicomVer` |

El registro es `IWorklistService` como singleton (`Program.cs`); no guarda estado por petición: cada llamada abre su
asociación con su propio `DicomClient` y sus timeouts. El registro de pruebas es `IDicomPruebaStore` →
`JsonDicomPruebaStore`, también singleton, con su propio bloqueo para escribir y leer el archivo JSONL.

---

## 6. Bases de datos (MySQL)

### `meditechnosys_intranet` (login) — se crea sola
```
usuarios(
  usuario  VARCHAR(40) PRIMARY KEY,
  rol      VARCHAR(20),          -- Admin | Ingeniero
  hash     VARCHAR(255),         -- PBKDF2 (iteraciones.sal.hash)
  creado   DATETIME(6),
  activo   TINYINT(1) DEFAULT 1, -- 0 = desactivado (no puede entrar)
  nombre   VARCHAR(120) DEFAULT '', -- nombre de persona (actas/credenciales)
  cargo    VARCHAR(80)  DEFAULT ''  -- puesto/cargo
)
bitacora(
  id       BIGINT PK AUTO_INCREMENT,
  momento  DATETIME(6),          -- UTC
  usuario  VARCHAR(40),
  evento   VARCHAR(60),          -- Inicio de sesión, Acceso, Alta de usuario…
  detalle  VARCHAR(255),
  ip       VARCHAR(45),
  KEY idx_bitacora_usuario_momento (usuario, momento)
)
actividades(
  id                BIGINT PK AUTO_INCREMENT,
  tipo              VARCHAR(20) DEFAULT 'Actividad',  -- Actividad|Incidente|Ticket
  hospital          VARCHAR(10) NOT NULL DEFAULT '',  -- HES|HGR|HGO|HI (obligatorio al crear)
  titulo            VARCHAR(120),
  descripcion       VARCHAR(1000),
  usuario           VARCHAR(40),     -- ingeniero responsable (vacío = sin asignar)
  fecha             DATETIME,        -- día de inicio en el calendario
  fecha_limite      DATETIME NULL,   -- vencida si pasa y sigue abierto
  estado            VARCHAR(20) DEFAULT 'Pendiente',  -- Pendiente|Completada|Cancelada (EnProceso y Cerrada solo en renglones viejos: se traducen al leer)
  prioridad         VARCHAR(20) DEFAULT 'Media',      -- Baja|Media|Alta|Urgente
  avance            INT DEFAULT 0,   -- 0..100, heredado: ya no se edita (el seguimiento va en los comentarios del historial)
  informe           VARCHAR(2000),   -- opcional al completar; lo obligatorio es la evidencia
  justificacion     VARCHAR(500),    -- obligatoria al cancelar
  creado_por        VARCHAR(40),
  creado            DATETIME(6),
  cerrado           DATETIME(6) NULL, -- cuándo se completó/cerró
  numero            BIGINT DEFAULT 0, -- consecutivo de ticket (#001…)
  periodicidad_dias INT DEFAULT 0,   -- cada cuántos días toca reporte (0 = sin reportes)
  asignado          DATETIME(6) NULL, -- cuándo se asignó el responsable
  ultimo_reporte    DATETIME(6) NULL,
  relacion_id       BIGINT NULL,     -- elemento relacionado (opcional)
  KEY idx_actividades_usuario_fecha (usuario, fecha),
  KEY idx_actividades_tipo_numero (tipo, numero)
)
reportes_estatus(
  id               BIGINT PK AUTO_INCREMENT,
  item_id          BIGINT,          -- actividades.id
  momento          DATETIME(6),
  autor            VARCHAR(40),
  estado           VARCHAR(20),
  avance           INT,
  descripcion_avance VARCHAR(2000),
  problemas        VARCHAR(2000),
  pendientes       VARCHAR(2000),
  proximas_acciones VARCHAR(2000),
  fecha_estimada   DATETIME NULL,
  comentarios      VARCHAR(2000),
  KEY idx_reportes_item (item_id)
)
historial_cambios(
  id        BIGINT PK AUTO_INCREMENT,
  item_id   BIGINT,
  momento   DATETIME(6),
  usuario   VARCHAR(40),
  campo     VARCHAR(60),   -- qué cambió (Ingeniero, Prioridad, Estatus…)
  antes     VARCHAR(255),
  despues   VARCHAR(255),
  comentario VARCHAR(1000),
  KEY idx_historial_item (item_id)
)
permisos(
  rol   VARCHAR(20),
  clave VARCHAR(60),       -- p. ej. tickets.asignar
  PRIMARY KEY (rol, clave)
)
configuracion(
  clave VARCHAR(60) PRIMARY KEY,   -- p. ej. reportes.periodicidadDias | items.fechaLimiteHabilitada
  valor VARCHAR(255)
)
inventario(
  id          BIGINT PK AUTO_INCREMENT,
  coleccion   VARCHAR(20),        -- equipos | ips | aetitles
  hospital    VARCHAR(10),        -- HES|HGR|HGO|HI
  datos       TEXT,               -- JSON con los campos propios de la colección
  creado_por  VARCHAR(40),
  creado      DATETIME(6),
  actualizado DATETIME(6) NULL,
  KEY idx_inventario_coleccion (coleccion, hospital)
)
playbook(
  id                  BIGINT PK AUTO_INCREMENT,
  nombre              VARCHAR(150),
  nombre_archivo      VARCHAR(200),   -- nombre original con el que se subió
  version             VARCHAR(20) DEFAULT 'v1.0',
  hospital            VARCHAR(10) DEFAULT 'General',  -- HES|HGR|HGO|HI|General
  fecha_publicacion   DATETIME NULL,
  fecha_actualizacion DATETIME NULL,  -- se sella al cambiar versión o reemplazar el archivo
  subido_por          VARCHAR(40),
  subido              DATETIME(6),
  tamano              BIGINT,         -- bytes del PDF
  ruta                VARCHAR(300),   -- nombre GUID dentro de Playbook:StorageDir
  estado              VARCHAR(20) DEFAULT 'Vigente',  -- Vigente|Retirado
  KEY idx_playbook_hospital (hospital, estado)
)
```
- Si `usuarios` ya existía sin columnas nuevas, `EnsureSchema()` las agrega solas (`ALTER TABLE`):
  `activo`, `nombre` y `cargo` (comprobadas antes en `information_schema.COLUMNS`).
- `bitacora` la crea `MySqlAuditLog.EnsureSchema()` al arrancar; en modo JSON/degradado se usa en su
  lugar `App_Data/bitacora.jsonl` (una línea JSON por evento).
- `actividades`, `reportes_estatus`, `historial_cambios`, `permisos`, `configuracion`, `inventario`,
  `playbook` y `evidencias` las crean sus respectivos `EnsureSchema()` al arrancar (con `ALTER TABLE ADD COLUMN` por
  columna faltante al actualizar — así se agregó `actividades.hospital`); en modo JSON/degradado se
  usan `App_Data/actividades.json`, `reportes.json`, `historial.jsonl`, `permisos.json`,
  `configuracion.json`, `inventario.json`, `playbook.json` y `evidencias.json`.
- Los **PDF del Playbook** no viven en la base de datos: se guardan en disco bajo
  `Playbook:StorageDir` (por defecto `App_Data/playbook`) con nombre GUID, y la tabla conserva la ruta.
- La **evidencia del trabajo** (foto o archivo adjunto al completar) tampoco: se guarda en disco bajo
  `Gestion:EvidenciasDir` (por defecto `App_Data/evidencias`) también con nombre GUID, y la tabla `evidencias`
  guarda la ruta relativa, el nombre original, el MIME validado, el peso y quién la subió. La carpeta se crea
  al arrancar (`EvidenciaService.AsegurarCarpeta()`).

### `RepoPato` (patología) — debe existir
```
reportes(
  id BIGINT PK AUTO_INCREMENT, area, folio, fecha, nombre, nombre_norm,
  nss, agregado_medico, modalidad, archivo_original, ruta_pdf, subido_en, …
)
auditoria( id, usuario, accion, detalle, ip, fecha )
```
- `nombre_norm` = nombre sin acentos/en minúsculas, para búsquedas tolerantes.
- `ruta_pdf` = ruta relativa del archivo (ej. `especialidades/00000123.pdf`), resuelta contra `Patologia:StorageDir`.
- Cada acción relevante (consulta, subida, edición, borrado, ver PDF, importar) queda en **`auditoria`** con usuario e IP.

---

## 7. Arranque y ciclo de vida (`Program.cs`)

Orden aproximado al iniciar:
1. **fo-dicom** se inicializa una sola vez (`DicomSetupBuilder`).
2. Se decide la *content root*: si corre **como servicio de Windows**, es la carpeta del `.exe`; si no, la carpeta actual.
3. Se registran: Razor Pages, autenticación/autorización, almacén de usuarios, los almacenes del módulo Gestión (`IActivityStore`, `IReportStore`, `IHistorialStore`, `IPermissionStore`, `IConfigStore`, `IInventarioStore`, `IDocumentoStore`, `IEvidenciaStore`; MySQL o JSON según el mismo sondeo), los servicios `GestionService`, `InventarioService`, `PlaybookService`, `EvidenciaService` y `HistoricoExportador`, núcleo de Patología y **núcleo DICOM** (nodos, `LocalOptions`, `DicomQrService`, `DownloadJobService`, `LimpiadorDescargas`, controladores MVC).
4. Se ajustan **límites de Kestrel** (cuerpo hasta 32 MB y sin corte por "poca velocidad") para permitir descargas/ZIP grandes y la subida de PDF al Playbook. Antes de eso, si la sección `Https` está activa, `CertificadoIntranet.Asegura` deja listo **un certificado propio** en `App_Data\https` (se autofirma con `localhost`, el nombre del equipo, todas las IPs locales y los alias de `Https:Nombres`; se renueva solo si vence en menos de un mes o si falta uno de los **fijos** —`localhost`, el nombre del equipo y los alias—, nunca por una IP que cambió el DHCP) y, antes de resignarse a firmar uno nuevo, **busca el espejo** del trío en `C:\ProgramData\Meditechnosys\Intranet\https` (`INTRANET_RESPALDO_CERTIFICADO` cambia el destino): si `App_Data\https` se perdió o se dañó —una republicación, un cambio de ruta del proyecto— el mismo certificado de siempre vuelve desde el espejo, que se mantiene al día en cada arranque. Por eso un **reinicio del servidor no cambia la huella**: las PC y el Quemador siguen confiando en la hojita que ya tienen. Cada arranque deja su renglón —«certificado … reutilizado / restaurado del respaldo / creado (huella …, vence …)»— en `C:\ProgramData\Meditechnosys\Intranet\https\arranque.txt`, que es donde se compara la huella de hoy con la de ayer (al visor de eventos solo llegan los mensajes del servicio, iniciar y detener, no los `Information` de la aplicación). Con el certificado en mano **se anexa** `https://0.0.0.0:5024` a las direcciones de escucha escribiendo la llave de configuración `urls` por memoria —es lo único que gana sobre el `--urls` del servicio—, con `ConfigureHttpsDefaults` para entregar ese certificado. El `http://…:5023` de siempre no se toca; el salto obligatorio de http a https vive detrás de `Https:Redirigir` (apagado por omisión) y nunca aplica a `localhost` ni a `/intranet.cer`. **Sin https el navegador no entrega la geolocalización**, así que la posición real de cada aparato depende de ese puerto seguro.
5. `EnsureSchema` + migración/semilla de usuarios; `EnsureSchema` de bitácora y de los almacenes del módulo Gestión (si el almacén es MySQL); `PlaybookService.AsegurarCarpeta()` crea la carpeta de PDF y `EvidenciaService.AsegurarCarpeta()` la de la evidencia.
6. Se arranca el **receptor C‑STORE** (`StartListener`, puerto 11113) y se registra su liberación al cerrar. Al levantar el host arrancan los `BackgroundService`: **`ImportadorService`** (vigila las carpetas de entrada de Patología), **`LimpiadorDescargas`** (depura `dicom_downloads` cada hora) y **`AuditWorker`** (vacía la cola de bitácora).
7. Pipeline: cerrojos de `/patologia`, `/dicom`, `/admin`, `/calendario` y `/gestion` → compresión de texto → estáticos → routing → autenticación → autorización → límites de solicitudes → tema y **middleware de bitácora** (encola los movimientos de usuarios autenticados; excluye `/api/usuarios`, `/api/actividades`, `/api/roles`, `/api/config`, `/api/inventario` y `/api/playbook`, que tienen sus propios eventos de auditoría) → páginas Razor → health checks → `MapPatoApi` → `MapControllers` (DICOM) → APIs de login/usuarios/bitácora/Gestión/inventarios/Playbook.

> **Caché de estáticos:** los `UseStaticFiles` (raíz, `/patologia`, `/dicom`, `/admin`, `/calendario`, `/gestion`) usan la función local
> `CacheEstatico(...)`: `.html`, `.js` y `.css` llevan `Cache-Control: no-cache` y se revalidan con ETag/Last-Modified; imágenes y fuentes se guardan siete días.
> Así, un **F5** comprueba si cambió el diseño sin volver a bajar siempre el mismo archivo.

---

## 8. Qué cambió con la unificación

| Antes | Ahora |
|---|---|
| Repositorio en **Python** (carpeta aparte) | Portado a C# en `Patologia/`, dentro de la intranet. El Python queda **retirado** (respaldo). |
| **DicomQrApi** como servicio suelto en el puerto 5080, **sin login** | Fusionado en `Dicom/`, detrás del login, en `/dicom` y `/api/dicom/*`. El proyecto suelto queda **retirado**. |
| Subida de PDF **pedía usuario** | El usuario **se toma de la sesión** (`/api/patologia/me`). |
| Usuarios en `users.json` | En **MySQL** (`meditechnosys_intranet`), con migración automática. |
| Tres programas, tres logins | **Un programa, un login**, y los accesos en la pantalla **Ingeniería**. |

---

## 9. Dónde tocar cada cosa

- **Agregar/quitar un servidor PACS** → `appsettings.json`, sección `PacsNodes`.
- **Cambiar puertos** → HTTP: variable `URL` en `instalar-servicio.bat` (o `--urls`); HTTPS: sección `Https` del `appsettings.json` (`Puerto`, 5024; `Activo: false` apaga el puerto seguro y con él el permiso de ubicación del navegador; `Nombres` fija los puntos que obligan a renovar el certificado —ahí va la IP con la que se entra, hoy `10.252.252.200`—; `Redirigir` fuerza el salto); DICOM: `Local:MovePort`. Si se mueve el puerto seguro hay que abrirlo en el firewall (`abrir-puerto-seguro.bat`, una vez, con administrador) y recordar que cada PC confía el certificado con `confiar-certificado.bat` (el archivo es de `App_Data\https`, y `GET /intranet.cer` lo baja sin login).
- **Cambiar carpetas de PDF/descargas** → `Patologia:StorageDir` y `Local:OutputDirectory`.
- **Cambiar la depuración de descargas** (cada cuánto, cuánto conserva, tope de espacio, apagarla) → `Dicom/DicomQrService.cs` (`LocalOptions`) y las llaves `Local:DepurarDescargas`, `Local:DepurarCadaMinutos`, `Local:ConservarHoras`, `Local:MaxMB`, `Local:ZipRetentionHours`; las reglas están en `Dicom/DepuradorDescargas.cs`.
- **Cambiar cuántas descargas van a la vez** → `Local:MaxDescargasSimultaneas` (3, recortado a 1–8) y `Local:MaxTrabajosEnCola` (20; al rebasarlo la API responde 429). El tope real de asociaciones contra cada PACS sigue siendo `Local:MaxParallelPerNode`, compartido entre todas las PC.
- **Ajustar la protección de carga** → `RateLimiting:GeneralPermitLimit` (600/min por usuario o IP), `RateLimiting:PublicPermitLimit` (120/min por IP para la consulta pública), `RateLimiting:LoginPermitLimit` (10/min por IP para `/api/login`) y `RateLimiting:WindowSeconds` (60). `GET /health/live` no toca dependencias; `GET /health/ready` devuelve 503 si MySQL no responde.
- **Ajustar las cachés cortas** → `Auth:SessionCacheSeconds` (15 s) y `Cache:ConfigSeconds` (15 s). Un `Set` hecho por la aplicación invalida la entrada afectada.
- **Apagar/ajustar la comprobación de integridad** → `Local:VerificarDescargas` (verdadero), `Local:ReintentosMaximos` (2), `Local:VerificarConPacs` (verdadero) y `Local:VerificarConPacsMaxEstudios` (300). **Cambiar los topes del ZIP en partes** → `Local:ZipMaxStudies` (estudios por parte, 100) y `Local:ZipMaxMBPorParte` (2048); `0` en cualquiera de los dos significa «sin tope por ese lado».
- **Nueva tarjeta en la pantalla Ingeniería** → `Models/Catalog.cs` (`Apps`).
- **Nueva regla de usuarios** → `Services/UserRules.cs`.
- **Nuevo campo de reporte** → `Patologia/Extractor.cs` (extraer) + `Patologia/PatoApi.cs` (exponer) + tabla `reportes`.
- **Diseño/colores de Consulta o Subida** → el `<style>` de `wwwroot/patologia/consulta/index.html` y `.../subida/index.html` (comparten paleta).
- **Modal de edición / combo de Área / botón Subir** → `wwwroot/patologia/consulta/index.html` (HTML + `<script>`).
- **Permisos de borrado** → `Patologia/PatoApi.cs` (`.RequireAuthorization("SoloAdmin")` en el `DELETE`) y el `<script>` de consulta (oculta el botón si el rol no es Admin).
- **Color dorado de cabeceras** → el `:root` de cada subsistema (`--dorado`) o `--gold` en `wwwroot/css/intranet.css`. El logo del IMSS que antes se teñía con máscara CSS fue **retirado** de todas las pantallas (2026-10-16); ver [`FRONTEND.md`](FRONTEND.md).
- **Ventanas emergentes de usuarios (crear/editar)** → `wwwroot/admin/usuarios/index.html` (marcado `<dialog>` + `<script>` embebido) + `dialog.modal` en `wwwroot/css/panel.css`.
- **Desactivar/activar un usuario** → `IUserStore.SetActive` (ambas implementaciones) + `PUT /api/usuarios/{u}/activo` + botón por fila en `wwwroot/admin/usuarios/index.html`.
- **Nombre y cargo de un usuario (y su autocompletado en actas/credenciales)** → `UserRecord.Nombre/Cargo` + `IUserStore.SetDatos` (ambas implementaciones) + `PUT /api/usuarios/{u}/datos` + campos en `wwwroot/admin/usuarios/index.html`; el autocompletado usa el gancho `onCambio` de `G.formDialog` (`wwwroot/js/gestion.js`) en `wwwroot/js/administrativos.js`.
- **Bitácora unificada (qué se registra y dónde)** → `Services/Bitacora.cs` y `Services/BitacoraQuemador.cs` (destinos; su `DiasConMovimientos(usuario)` es el índice de días que pinta la hoja de periodo y su `RangoDePeriodo()` convierte el periodo elegido en un rango UTC de los dos extremos incluidos), `Models/BitacoraUnificada.cs` (mezcla y diccionario de actividades), `Program.cs` (middleware de movimientos, eventos de API y los endpoints `/api/bitacora/unificada*` y `/api/bitacora/periodos`), `Pages/Login.cshtml.cs` / `Pages/Logout.cshtml.cs` (login web y **intentos fallidos**) y `wwwroot/admin/bitacora/index.html` (pantalla; `/admin/quemador/` solo redirige).
- **Cómo se arma la «Ubicación» de la bitácora** → TODO el orden de decisión vive en un solo lugar, `Services/Ubicaciones.cs` (`UbicacionResolver.Decide`, y sus tres puertas `UbicacionDe` / `MarcaDe` / `CoordenadasDe`, que leen del mismo resultado para que columna, etiqueta y coordenada nunca se contradigan). El orden es: rango escrito a mano (`bitacora_ubicacion`) → **posición real del propio aparato** → **punto aprendido del rango /24** (`ArmaAprendidos`: el punto con permiso de navegador más nuevo de cualquier equipo del mismo rango; los reportes que llegan por `127.0.0.1` se reparten entre los rangos de las tarjetas del servidor, `PrefijosDeLaMaquina`) → edificio calibrado (`bitacora_sitio`) → **«Sin confirmar»** (`Ubicaciones.SinConfirmar`). La lectura del mapa está en `Services/Lugares.cs` (`GeocodificadorLugar`: `zoom=18` exacto vs `zoom=16` aproximado, y `DesmenuzaExacta` para el formato postal). Los tres insumos se refrescan como mucho cada minuto (`Lugares()`/`Rangos()`); cambiar un rango, o dar el permiso, acomoda también los registros viejos porque nada de esto se guarda en el renglón. Desde el 2026-11-02 la pantalla de la bitácora **no tiene ya ni mapa ni panel de rangos**, así que lo escrito a mano y la calibración siguen funcionando en el servidor pero nadie los llena desde la interfaz: para volver a ofrecerlos hay que reactivar esa parte de `wwwroot/admin/bitacora/index.html` o escribir directamente en las tablas.
- **De dónde saca cada equipo su ubicación (y cómo se marca)** → el aparato la reporta desde `wwwroot/js/ubicacion.js`: primero **posición real** con `navigator.geolocation` (solo funciona en https; cada 30 min) y, si no se permite o no hay señal, **zona aproximada por IP** con ipapi.co → ipwho.is → freeipapi.com (cada 6 h); si el permiso sigue sin contestarse sale una hojita con «Permitir ubicación». Se guarda en `bitacora_lugar` (`fuente` = `navegador`/`ip`, `precision_metros`). Al consultar, `UbicacionResolver.MarcaDe(ip)` devuelve la etiqueta que se ve en pantalla, en `/api/bitacora/resumen`, en `/api/bitacora/ubicacion-estado` y en la columna **«Ubicación obtenida»** del Excel; las columnas calculadas viajan en `AuditEntry.FuenteUbicacion` / `PrecisionMetros` y las llena `Ubicaciones.EnriquecerAsync`. Para tocar los tiempos, el aviso o el respaldo: `ubicacion.js`; para tocar el texto de las marcas: `Decide`. **Lo que sale de la IP ya no se pinta como ubicación** (`Ubicaciones.SinConfirmar` en la columna, y `Ubicaciones.SoloZona` solo dentro de la etiqueta: «sin permiso del navegador (su salida a Internet sale por Cuauhtémoc, Ciudad de México)»), porque la salida a Internet es UN punto compartido por todo el hospital y se midió a **4,523 m** del edificio, en otra alcaldía; la dirección postal completa solo se muestra cuando el punto vino del permiso del navegador —propio o heredado del rango—. Desde el 2026-11-02 (13:30) `Https:Redirigir` está en `true`, así que el http de siempre salta solo a la dirección segura para que la ventanilla del permiso alcance a salir.
- **Reglas del módulo Gestión (actividades/incidentes/tickets, estados, asignación, reportes, permisos)** → `Services/Gestion.cs` (`GestionService`); modelos en `Models/Actividad.cs`, `Models/Reporte.cs`, `Models/Cambio.cs` y `Models/Permisos.cs` (catálogo de permisos).
- **API del módulo Gestión** → `Program.cs`: `/api/actividades*` (incluidos `POST /api/actividades` cuando el alta del Ingeniero llega en `multipart`, `POST …/completar` con la evidencia en `multipart`, `POST …/reabrir` (Admin), `GET …/evidencias` y `GET /api/evidencias/{id}`), `/api/reportes`, `/api/roles`, `/api/config`, `/api/historico/excel`, `/api/inventario*` y `/api/playbook*` (endpoints delgados que delegan en `GestionService`, `EvidenciaService`, `HistoricoExportador`, `InventarioService` y `PlaybookService`). `POST /api/actividades` es la única firma que lee el cuerpo ella misma (`HttpContext` + `HasFormContentType`, con `ReadFromJsonAsync` en el lado JSON) porque el enlace por parámetro fuerza `application/json` y rechazaba el `multipart` con 415.
- **Almacenes del módulo Gestión (MySQL o JSON)** → `Services/Actividades.cs`, `Services/Reportes.cs`, `Services/Historial.cs`, `Services/Permisos.cs`, `Services/Config.cs`, `Services/Inventarios.cs`, `Services/Documentos.cs` y `Services/Evidencias.cs`; el sondeo y el registro están en `Program.cs`.
- **Pantallas del módulo Gestión** → `wwwroot/calendario/index.html` (rejilla + panel izquierdo de tickets + panel derecho de pendientes por responsable con sus dos pestañas por tipo, **Actividades / Incidentes** + filtro por hospital), `wwwroot/admin/{roles,reportes,config}/index.html` y `wwwroot/gestion/{historico,inventario-equipos,inventario-ips,inventario-aetitles,playbook}/index.html`; la lógica compartida de modales, filtros y tarjetas vive en `wwwroot/js/gestion.js`, la de los inventarios en `wwwroot/js/inventario.js` y el formato en `wwwroot/css/panel.css`.
- **Tema (Oscuro/Claro), botón ⚙ Configuración (letra, tiempo de sesión y parámetros del módulo)** → el botón y el `<dialog>` **solo se renderizan en el inicio**: `Pages/Shared/_Layout.cshtml` los pinta cuando la ruta pedida es `/` (o `/Index`) y `Pages/Shared/_LayoutPanel.cshtml` ya no los lleva, así que fuera del inicio no hay botón, ni diálogo, ni `wwwroot/js/preferencias.js`. El diálogo está escrito una sola vez en `Pages/Shared/_PrefDialog.cshtml`, su comportamiento está en `wwwroot/js/preferencias.js`, el tema/letra en `wwwroot/js/theme.js` (`window.MeditechnosysApariencia`, `data-theme` y `data-fuente` en `<html>`) y el formato en el bloque `.pref-*` de `wwwroot/css/intranet.css` con los colores en `wwwroot/css/theme.css`. **El interruptor Día/Noche de la cabecera (`#theme-toggle`) se retiró**: ya no está en las dos cabeceras, ni lo crea `theme.js`, ni queda CSS de él; el tema se escoge en el bloque «Tema» del ⚙ con las dos opciones **Oscuro** y **Claro**. **El tema y el tiempo de sesión son las dos preferencias que viven en la cuenta**: `Services/Tema.cs` (`TemaPreferencias`: opciones, clave `tema.<usuario>`, defecto **Oscuro** y la copia en la cookie `meditechnosys.tema`) con `GET`/`PUT /api/tema`, y `Services/Sesion.cs` (`SesionPreferencias`: opciones, clave `sesion.minutos.<usuario>`, vencimiento y auto-renovación) con `GET`/`PUT /api/sesion`. La cookie del tema **es solo una copia** para que el navegador pinte antes de llamar a nada: se escribe en el acceso (`Pages/Login.cshtml.cs` y `/api/login`), la reescribe el middleware de `Program.cs` que va justo después de `UseAuthorization` y la pone al día con lo que dice la cuenta en cada petición autenticada, la actualiza `PUT /api/tema` y la borra la salida (`Pages/Logout.cshtml.cs` y `/api/logout`) o cualquier petición sin sesión; `UseStaticFiles` corre antes, así que los paneles autónomos se valen de la copia que haya y `preferencias.js` la reconcilia al abrir el ⚙. Cada cambio deja su renglón en la bitácora (`AuditEvents.CambiarTema`, «Oscuro → Claro»). La ventana de inactividad de la sesión se aplica en `OnValidatePrincipal` (rechaza y manda a `/Login?sesion=expirada` cuando pasa la ventana; cuando ya va la mitad reescribe la marca de actividad `IssuedUtc` y deja la caducidad absoluta en `SesionPreferencias.VidaCookie` —8 h + margen, **siempre más larga que cualquier ventana**, para que el cierre lo decida esta app y no el vencimiento del framework, que no pone el aviso). La explicación del aviso de sesión terminada está en `Pages/Login.cshtml` / `Pages/Login.cshtml.cs`. Ver [`FRONTEND.md`](FRONTEND.md) §5 quinquies.
- **Tarjeta «Reportes» y sus pestañas (Bitácora · Histórico de actividades · Reportes de estatus)** → la tarjeta única vive en `Models/Catalog.cs` (grupo *Administración*); sus llaves de entrada son `bitacora.ver`, `quemador.ver`, `historico.actividades` y `reportes.consultar`, y `Catalog.Visibles` escoge a cuál de las tres rutas lleva a quien no puede ver la Bitácora. La barra que se ve debajo de la cabecera es `wwwroot/js/reportes-tabs.js` + el bloque `.reportes-tabs` de `wwwroot/css/panel.css`, y **son enlaces a las tres rutas de siempre**, no paneles dentro de una sola página: cada pantalla conserva su `<script>`, sus filtros, su Excel y su guard de `Program.cs` (`/admin/bitacora` con OR de los dos permisos, `/gestion/historico` con `historico.actividades`, `/admin/reportes` con `reportes.consultar`). Cambiar el orden o el rótulo de una pestaña se hace en la lista `apartados` de `reportes-tabs.js`; añadir un apartado nuevo exige además su `<nav>` en la página. Ver [`FRONTEND.md`](FRONTEND.md) §5 quater.
- **Campos de un inventario** → `Models/Inventario.cs` (`InventarioCatalogo`): la pantalla y el Excel se construyen solos a partir del esquema.
- **Hoja de periodo (Día · Mes · Año · Rango) de las pantallas que consultan fechas** → `wwwroot/js/hoja-fecha.js` + `wwwroot/css/hoja-fecha.css` (`window.HojaFecha.instalar({desde, hasta, titulo, cualquier})`). La hoja **no dueña del valor**: escribe `yyyy-mm-dd` en los dos `<input type="date">` de la página (que siguen siendo los que la consulta lee) lanza **un solo** `change`, así que no cambia el camino de filtrado ni del Excel de ninguna pantalla. Está instalada en `wwwroot/admin/reportes/index.html`, `wwwroot/gestion/historico/index.html`, `wwwroot/patologia/consulta/index.html`, `wwwroot/dicom/index.html`, `wwwroot/dicom/enviar/index.html` y `Pages/ListaTrabajo.cshtml`. La Bitácora **conserva su propia hoja** dentro de `wwwroot/admin/bitacora/index.html` porque además pinta el índice de días con movimientos (`Bitacora.DiasConMovimientos`); por eso el periodo se resuelve igual en los cinco almacenes (`RangoDePeriodo()` en `Services/Bitacora.cs` y `Services/BitacoraQuemador.cs`, extremo `hasta` **incluido** en todos los consumidores).
- **Hoja de hora (Una hora · Rango)** → la misma librería, con `window.HojaFecha.instalarHora({desde, hasta, titulo, cualquier, conteos})`: caja con icono de reloj, 24 celdas, atajos Todo el día/Mañana/Tarde y `×` para quitar el filtro. Escribe `'HH:00'`/`'HH:59'` en los dos campos de la página y dispara **un solo** `change`; `conteos` (opcional) pone en cada celda cuántas entradas caen en esa hora. **No añade CSS propio**: reutiliza las clases `hf-`. La usa `Pages/ListaTrabajo.cshtml` (`/dicom/lista-trabajo`); la Bitácora sigue con su copia dentro de su página. Antes de esto, filtrar por hora era exclusivo de la Bitácora. Ver [`FRONTEND.md`](FRONTEND.md) §5 ter.
- **Columnas de un Excel** → `Services/Historico.cs` (histórico) y `Services/Inventario.cs` (inventarios); el formato común (encabezado, anchos, filtros) está en `Services/Excel.cs`.
- **Hospitales admitidos** → `Models/Hospitales.cs` (`Todos`, `ConGeneral`, `Normalizar`, `EsValido`).
- **Reglas y almacenamiento del Playbook** → `Services/Playbook.cs` (negocio y archivos) y `Services/Documentos.cs` (almacén); validaciones en `Models/Documento.cs`; carpeta en `Playbook:StorageDir`.
- **Menú Gestión (tarjetas y grupos)** → `Models/Catalog.cs` (`Catalog.Apps` para Ingeniería, `AdminCatalog.Apps` para Administración) + `Pages/Ingenieria.cshtml` y `Pages/Admin.cshtml` (ambas agrupan por `Tag`).
- **Formato DICOM de las páginas de panel** → `wwwroot/css/panel.css` (cabecera negra, logo dorado, tarjetas, botones).
- **Almacén de usuarios y degradación MySQL** → `Program.cs` (sondeo y registro) + `Services/AuthStoreInfo.cs`.
- **Actualizar el sistema tras un cambio** → `actualizar.bat` (como administrador).
