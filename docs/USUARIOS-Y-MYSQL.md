# Usuarios, roles y MySQL

Este documento explica **cómo se crean, editan y validan los usuarios** de la intranet, cómo se
conectan a **MySQL**, y qué pasa cuando la base **no está disponible**.

---

## 1. Roles dinámicos y permisos efectivos

Desde la actualización de **2026-10-15** los roles **ya no son solo dos fijos**: son **dinámicos y de
nombre libre**. Existen dos **roles de sistema** que siempre están y no se pueden renombrar ni eliminar:

| Rol | Constante | Notas |
|---|---|---|
| Administrador | `Roles.Admin` (`"Admin"`) | Rol de sistema. **Siempre** conserva `roles.administrar` (anti-bloqueo). |
| Ingeniero | `Roles.Ingeniero` (`"Ingeniero"`) | Rol de sistema. |

Además, el administrador puede **crear, renombrar y eliminar** cualquier otro rol desde `/admin/roles/`
(`IRoleStore`: `List`, `Existe`, `Crear`, `Renombrar`, `Eliminar`). El nombre de rol se valida con
`ReglasRol` (hasta 20 caracteres, coincide con la columna `usuarios.rol VARCHAR(20)`); `NormalizeRole`
(`Services/UserRules.cs`) acepta cualquier nombre válido, no solo Admin/Ingeniero.

**Qué puede hacer cada quien** ya no lo define el nombre del rol sino sus **permisos efectivos**:

- **Matriz del rol** — **46 permisos en 13 grupos** (`CatalogoPermisos.Version = 15`), editables por rol
  en `/admin/roles/` y guardados en la tabla `permisos` (o `App_Data/permisos.json`).
- **Ajustes por usuario (overrides)** — en `/admin/usuarios/ → Permisos`, cada cuenta puede **conceder (+)**
  o **revocar (−)** permisos concretos sobre los heredados del rol (`IUserPermissionStore`; tabla
  `usuario_permisos` o `App_Data/usuario_permisos.json`).
- **Efectivos = matriz del rol + overrides**, calculados por `IPermissionResolver` (`PermissionResolver.Calcular`).
  Admin siempre conserva `roles.administrar`. Cuando se agregan claves nuevas el catálogo sube de versión
  y las claves faltantes se otorgan al guardar según la matriz por defecto, para que nadie pierda acceso.

Los permisos efectivos se **recalculan en cada petición**: en `OnValidatePrincipal` se adjuntan a la
sesión como claims `perm` (`GestionService.PermClaim`), así que un cambio de matriz, de rol o de override
aplica **de inmediato** a las sesiones ya abiertas (las operaciones que cambian permisos llaman a
`resolver.Invalidar()`). La interfaz los consulta con `GET /api/permisos/mios` y oculta lo no permitido;
el servidor los hace cumplir **además** con guards de ruta y políticas por permiso.

Las políticas de autorización (`Program.cs`) dejaron de ser por rol:

- `SoloAdmin` e `Ingenieria` → ahora son simplemente `RequireAuthenticatedUser`; la autorización fina vive
  en los guards de ruta y en los servicios.
- **`P:<clave>`** → una política por cada permiso del catálogo (`RequireClaim(perm, clave)`); por ejemplo
  `/api/usuarios` exige `P:usuarios.administrar`, `/api/roles` exige `P:roles.administrar`, `/api/bitacora`
  exige `P:bitacora.ver`.
- **Guards de ruta** por permiso: `/patologia`→`patologia.consultar`, `/dicom`→`dicom.ver`,
  `/calendario`→`calendario.acceder`, `/almacen`→`almacen.consultar`, y los subpaths de `/admin/*` y
  `/gestion/*` mapeados a su permiso; si falta el permiso se redirige a `/AccesoDenegado` (o `/Login` sin
  sesión). `/Admin` e `/Ingenieria` (Razor) **redirigen a `/Index`**, el inicio único filtrado por acceso.
- **`GET /api/usuarios/asignables`** devuelve una lista ligera (`{usuario, rol}` de cuentas activas) para
  los diálogos de asignación/edición y el filtro de bitácora; se protege con una **aserción** que admite
  varios permisos (`usuarios.administrar`, `tickets.asignar`, `tickets.cambiarIngeniero`,
  `supervision.porIngeniero`, `historico.actividades`, `bitacora.ver`, `actas.administrar` o
  `credenciales.administrar`), de modo que un rol personalizado
  pueda elegir ingeniero sin ver el panel completo de cuentas.


---

## 2. El almacén de usuarios (`IUserStore`)

La lógica de negocio **no depende** de dónde se guarden los usuarios: todo pasa por la interfaz
`IUserStore` (`Services/UserStore.cs`), que tiene dos implementaciones:

| Implementación | Dónde guarda | Cuándo se usa |
|---|---|---|
| `MySqlUserStore` | MySQL `meditechnosys_intranet.usuarios` | `Auth:Store = "mysql"` **y** el servidor responde. |
| `UserStore` | Archivo `App_Data/users.json` | `Auth:Store = "json"`, o como **respaldo** si MySQL no responde. |

Ambas devuelven los **mismos códigos de estado** para que la API y la interfaz se comporten igual:

| Operación | Éxito | Errores |
|---|---|---|
| `Create` | `201` | `400` datos inválidos · `409` ya existe |
| `Delete` | `204` | `404` no existe · `400` es el último admin |
| `SetPassword` | `204` | `404` · `400` contraseña inválida |
| `SetRole` | `204` | `404` · `400` rol inválido o dejaría sin admin |
| `SetActive` | `204` | `404` no existe · `400` dejaría sin administrador activo |
| `SetDatos` | `204` | `404` no existe · guarda nombre de persona (máx. 120) y cargo (máx. 80) |

### Reglas compartidas (`Services/UserRules.cs`)

- Usuario: 3–40 caracteres, solo `A-Z a-z 0-9 . - _`.
- Contraseña: mínimo 3 caracteres (`UserRules.PasswordMin`).
- Nombre de persona: hasta 120 caracteres; cargo: hasta 80 (`LimpiarTexto` recorta y limpia).
- Rol: se normaliza a `Admin` o `Ingeniero` (sin distinguir mayúsculas); cualquier otro valor es error.
- **Nunca** se puede eliminar al último administrador ni quitarle el rol.
- **Nunca** se puede **desactivar** al último administrador activo (ni a uno mismo desde la API).

### Contraseñas (`PasswordHasher`)

PBKDF2 (RFC 2898) con SHA-256, 210 000 iteraciones y sal aleatoria de 16 bytes. Se guarda como
`iteraciones.sal.hash` en Base64. La verificación usa comparación de tiempo constante y, si el
usuario no existe, compara contra un hash de relleno para que el tiempo de respuesta no revele
si el usuario existe.

---

## 3. Esquema MySQL

`MySqlUserStore.EnsureSchema()` crea la base y la tabla **solas** en el primer arranque:

```sql
CREATE DATABASE IF NOT EXISTS `meditechnosys_intranet`
  CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `meditechnosys_intranet`.`usuarios` (
  `usuario` VARCHAR(40)  NOT NULL PRIMARY KEY,
  `rol`     VARCHAR(20)  NOT NULL,
  `hash`    VARCHAR(255) NOT NULL,
  `creado`  DATETIME(6)  NOT NULL,
  `activo`  TINYINT(1)   NOT NULL DEFAULT 1,
  `nombre`  VARCHAR(120) NOT NULL DEFAULT '',
  `cargo`   VARCHAR(80)  NOT NULL DEFAULT ''
) ENGINE=InnoDB;
```

Si la tabla **ya existía** de una versión anterior, `EnsureSchema()` **agrega sola** las columnas que
faltan con `ALTER TABLE ... ADD COLUMN` (comprobando antes en `information_schema.COLUMNS`): `activo`
(los usuarios previos quedan activos), `nombre` y `cargo` (los previos quedan vacíos), sin tocar ningún
dato existente.

La cadena de conexión sale de `ConnectionStrings:Usuarios` en `appsettings.json`.

> **La misma base** `meditechnosys_intranet` aloja el resto de las tablas del sistema: `bitacora`
> (§7) y las del módulo Gestión —`actividades`, `reportes_estatus`, `historial_cambios`,
> `permisos`, `configuracion`, `inventario`, `playbook`, `avisos`, `actas` y `credenciales`—, creadas por el `EnsureSchema` de
> cada almacén con el mismo mecanismo (`CREATE TABLE IF NOT EXISTS` + columnas nuevas por `ALTER TABLE`).
> El detalle de las columnas está en `docs/ARQUITECTURA.md` §6.
>
> **Roles dinámicos y ajustes por usuario (2026-10-15).** `Services/RolesStore.cs` crea además dos
> tablas y siembra los roles de sistema:
>
> ```sql
> CREATE TABLE IF NOT EXISTS `roles` (
>   `nombre`    VARCHAR(20) NOT NULL PRIMARY KEY,   -- coincide con usuarios.rol
>   `esSistema` TINYINT(1)  NOT NULL DEFAULT 0,     -- 1 = Admin/Ingeniero (no se borran/renombran)
>   `creado`    DATETIME(6) NOT NULL
> ) ENGINE=InnoDB;
>
> CREATE TABLE IF NOT EXISTS `usuario_permisos` (   -- overrides por usuario
>   `usuario`   VARCHAR(40) NOT NULL,
>   `clave`     VARCHAR(60) NOT NULL,
>   `concedido` TINYINT(1)  NOT NULL DEFAULT 1,     -- 1 = conceder, 0 = revocar
>   PRIMARY KEY (`usuario`, `clave`)
> ) ENGINE=InnoDB;
> ```
>
> La **matriz por rol** sigue guardándose en `permisos` (clave `rol` + claves concedidas). Con respaldo
> JSON en `App_Data/` (`roles.json`, `usuario_permisos.json`, `permisos.json`) si MySQL no responde.
>
> La base de Patología (`RepoPato`) es **otra** conexión (`ConnectionStrings:Patologia`) y no se
> crea sola: debe existir con sus tablas. Ver `docs/ARQUITECTURA.md`.

---

## 4. Elección del almacén y degradación graceful

En `Program.cs`, al arrancar:

1. Lee `Auth:Store` (`mysql` recomendado).
2. Si es `mysql`, **sondea** la conexión (timeout de 3 s para no retrasar el arranque).
3. Si el sondeo falla, **no tumba el servicio**: registra `UserStore` (JSON) como respaldo y crea
   un `AuthStoreInfo` con `Degraded = true` y el motivo.
4. Si el sondeo pasa, registra `MySqlUserStore`.

`Services/AuthStoreInfo.cs` guarda qué se pidió, qué quedó activo y por qué. Se muestra en:

- **`/api/status`** → entrada `Usuarios (almacén)` en estado `ok` o `warn`.
- **Página `/admin/usuarios/`** → línea "Almacén activo" y, si hay degradación, un recuadro amarillo de aviso.
- **Bitácora** → `LogWarning` al arrancar degradado.

> La **misma decisión** (`useMysql`) se aplica a todos los almacenes del módulo Gestión: bitácora,
> actividades, reportes, historial, permisos, configuración, inventarios y Playbook. Si MySQL no
> respondió al arrancar, todos escriben en su archivo de `App_Data/` y el sistema sigue operando.

### Migraciones automáticas

- **JSON → MySQL**: si al arrancar con MySQL la tabla `usuarios` está vacía, se importan los
  usuarios de `App_Data/users.json` (`MySqlUserStore.Import`).
- **MySQL → JSON (degradación)**: los usuarios creados mientras MySQL estuvo caído quedan en
  `users.json`; al volver MySQL con la tabla vacía se migran de vuelta. Si la tabla **no** está
  vacía, no se pisa: habrá que reconciliar a mano.

---

## 5. La API de usuarios, permisos y roles

El grupo `/api/usuarios` está protegido por el permiso efectivo **`usuarios.administrar`**
(política `P:usuarios.administrar`). Definido en `Program.cs`:

| Método y ruta | Cuerpo | Efecto |
|---|---|---|
| `GET /api/usuarios` | — | Lista `{ usuario, rol, creado, activo, nombre, cargo }`. |
| `POST /api/usuarios` | `{ usuario, password, rol, nombre?, cargo? }` | Crea la cuenta (valida que el rol exista). |
| `PUT /api/usuarios/{u}/password` | `{ password }` | Cambia contraseña. |
| `PUT /api/usuarios/{u}/rol` | `{ rol }` | Cambia rol (no el propio); valida que exista e invalida la caché de permisos. |
| `PUT /api/usuarios/{u}/activo` | `{ activo }` | Activa (`true`) o desactiva (`false`). No permite desactivarse a uno mismo. |
| `PUT /api/usuarios/{u}/datos` | `{ nombre, cargo }` | Guarda nombre de persona y cargo (recortados a 120/80 en el servidor). |
| `DELETE /api/usuarios/{u}` | — | Elimina (no el propio). |
| `GET /api/usuarios/{u}/permisos` | — | Devuelve `{ usuario, rol, grupos, rolPermisos, ajustes, efectivos }` para el editor de overrides. |
| `PUT /api/usuarios/{u}/permisos` | `{ ajustes:[{ clave, concedido }] }` | Guarda los overrides del usuario e invalida la caché. |
| `GET /api/usuarios/asignables` | — | Lista ligera `{ usuario, rol, nombre, cargo }` de cuentas activas (ver aserción en §1). |

**Roles y matriz** (protegidos por `P:roles.administrar`):

| Método y ruta | Cuerpo | Efecto |
|---|---|---|
| `GET /api/roles` | — | `{ grupos, roles:[{nombre,esSistema,creado}], matriz }`. |
| `POST /api/roles` | `{ nombre }` | Crea un rol nuevo (nombre libre). |
| `PUT /api/roles/{nombre}/renombrar` | `{ nombre }` | Renombra un rol no-sistema. |
| `DELETE /api/roles/{nombre}` | — | Elimina un rol no-sistema. |
| `PUT /api/roles` | `{ rol, permisos:[…] }` | Guarda la matriz del rol (Admin siempre retiene `roles.administrar`). |

Además, `GET /api/permisos/mios` devuelve los permisos efectivos de la sesión (y `fechaLimite`).

Los errores devuelven `{ "error": "mensaje" }` con el código de estado correspondiente; la
interfaz muestra ese mensaje tal cual dentro de la ventana emergente.

Cada operación de estas APIs queda registrada en la **bitácora de accesos** (ver sección 7) con el
usuario que la ejecutó, el evento y la IP.

---

## 6. La interfaz (página autónoma `/admin/usuarios/`)

La gestión de usuarios tiene **página propia**: `wwwroot/admin/usuarios/index.html`, con el mismo
formato del repositorio DICOM (cabecera negra, logo IMSS dorado, marca de agua, estilos en
`wwwroot/css/panel.css`). Se llega desde la **tarjeta *Usuarios* del inicio único `/`** (visible solo
con `usuarios.administrar`); la ruta está protegida en el servidor (ver sección 1).

- La tabla se refresca desde la API con `GET /api/usuarios` al cargar y después de cada cambio.
- La cabecera muestra el almacén activo (y aviso amarillo si está degradado) leído de `/api/status`.
- **+ Crear usuario** abre el `<dialog id="dlgCrear">`: campos usuario, **nombre completo**, **cargo**,
  contraseña y un `<select>` de rol **dinámico** (poblado desde `GET /api/roles`, no limitado a
  Admin/Ingeniero). Al enviar hace `POST /api/usuarios`.
- **Editar** (por fila) abre el `<dialog id="dlgEditar">`: permite cambiar el rol (lista dinámica), el
  **nombre completo** y el **cargo**, la casilla *Usuario activo* y, opcionalmente, la contraseña. Envía
  `PUT .../rol` solo si el rol cambió, `PUT .../datos` solo si nombre o cargo cambiaron,
  `PUT .../password` solo si se escribió una contraseña nueva y `PUT .../activo` solo si la casilla
  cambió. El botón **Guardar cambios** aplica todo lo modificado de una vez.
- **Permisos** (por fila) abre el `<dialog id="dlgPermisos">`: carga `GET .../permisos` y pinta la matriz
  agrupada con un `<select>` por permiso **[Heredado del rol / Conceder (+) / Revocar (−)]**, la columna
  base *Rol* y una columna *Efectivo* en vivo (refleja el resolver: Admin siempre conserva
  `roles.administrar`). **Guardar** hace `PUT .../permisos` con solo los ajustes no heredados.
- **Desactivar / Activar** (por fila) conmuta el estado con `PUT .../activo` sin abrir modal. Un
  usuario desactivado aparece con estado `Inactivo` y **no puede iniciar sesión**.
- **Eliminar** (por fila) pide confirmación y hace `DELETE`.
- Los errores de la API se muestran dentro del propio modal (`#errCrear` / `#errEditar`); el modal
  no se cierra hasta que la operación tiene éxito.
- Se usa el elemento nativo `<dialog>` (`showModal()` / `close()`): da fondo oscurecido, cierre con
  `Esc` y foco atrapado sin librerías externas. Los estilos están en `wwwroot/css/panel.css`
  (`dialog.modal`).

---

## 7. Bitácora de accesos (`IAuditLog`)

Registro **real** (no datos de ejemplo) de quién entra, qué hace y desde dónde. Interfaz
`IAuditLog` en `Services/Bitacora.cs` con dos implementaciones, elegidas según el almacén de
usuarios:

| Implementación | Dónde guarda | Cuándo se usa |
|---|---|---|
| `MySqlAuditLog` | MySQL `meditechnosys_intranet.bitacora` | `Auth:Store = "mysql"` y la base responde. |
| `JsonAuditLog` | Archivo `App_Data/bitacora.jsonl` (una línea JSON por evento) | `Auth:Store = "json"` o degradación. |

`Register(...)` **nunca lanza**: si el destino falla, solo escribe un `LogWarning` y la aplicación
sigue. `Query(usuario?, dia?, limite)` filtra por usuario (sin distinguir mayúsculas) y por día
local, y devuelve los eventos más recientes primero.

### Qué se registra

| Evento | Dónde se emite |
|---|---|
| Inicio de sesión / fallido / cierre | `Pages/Login.cshtml.cs`, `Pages/Logout.cshtml.cs` (detalle `Web`) y `/api/login`, `/api/logout` (detalle `API`). |
| Alta, cambio de rol, cambio de contraseña, activación, desactivación, baja de usuario | Los endpoints de `/api/usuarios`. |
| Alta (actividad/incidente/ticket), edición, baja, completado con su evidencia, cancelación, reapertura, asignación, observación/comentario y entrega de reporte | `Services/Gestion.cs` (`GestionService`), que es donde vive la lógica del módulo Gestión; los endpoints de `/api/actividades` solo la invocan. El porcentaje de avance ya no se registra: el seguimiento queda en los comentarios del historial. |
| Alta, edición y baja en los inventarios | `Services/Inventario.cs` (`InventarioService`), con la colección, el hospital y un resumen del registro. |
| Alta, edición, reemplazo de archivo y baja de documentos del Playbook | `Services/Playbook.cs` (`PlaybookService`), con nombre, hospital, versión y archivo. |
| Alta, edición y baja de actas administrativas | Los endpoints de `/api/actas`, con folio y persona. |
| Alta, edición (incluida la foto) y baja de credenciales | Los endpoints de `/api/credenciales`, con el usuario ligado y el nombre. |
| Descarga de Excel (histórico e inventarios) | `GET /api/historico/excel` y `GET /api/inventario/{coleccion}/excel` en `Program.cs`, con el número de registros o el hospital filtrado. |
| Cambio de la matriz de permisos de un rol y de la configuración del módulo | `PUT /api/roles` y `PUT /api/config` en `Program.cs`. |
| Acceso (movimientos) | Un middleware en `Program.cs` tras `UseAuthorization`: anota cada página visitada (`/`, `/Admin`, `/Ingenieria`, `/admin/*`, `/gestion/*`, `/calendario`, `/patologia`, `/dicom`) y cada acción/consulta a las APIs, con método, ruta y código de respuesta. |

Los recursos estáticos (CSS/JS/imágenes) no se registran: `UseStaticFiles` corta la petición antes
del middleware. Tampoco `/api/status` (sondeo) ni `/api/usuarios`, `/api/actividades`,
`/api/inventario`, `/api/playbook`, `/api/roles`, `/api/config`, `/api/actas` o `/api/credenciales`
(ya tienen eventos propios), para no duplicar ruido.

> La bitácora **no sustituye** al historial del módulo Gestión: cada cambio sobre un elemento queda
> además en el historial append-only (`historial_cambios` o `App_Data/historial.jsonl`), con valor
> anterior y nuevo, que es lo que se ve en la pestaña *Historial de cambios* del detalle —solo para
> quien tiene el permiso `tickets.historialCambios`—.

### Consulta desde la interfaz

`GET /api/bitacora?usuario=&dia=` (política `SoloAdmin`) devuelve los eventos en JSON. La bitácora
tiene **página propia** en el mismo formato DICOM: `wwwroot/admin/bitacora/index.html`, con un
desplegable de usuario, un selector de día, botón *Limpiar* y la tabla de resultados (momento,
usuario, evento, detalle e IP). El botón *Bitácora de accesos* de la pantalla `/Admin` navega ahí.

---

## 8. Primer arranque y credenciales semilla

- Si el almacén queda **vacío**, se crean los usuarios de `Auth:Users` (ver
  `appsettings.Development.json`: `admin`/`admin` e `ing`/`ing`).
- En producción no hay semilla: el primer admin se crea con variables de entorno
  `Auth__Users__0__User`, `__Password`, `__Role`, o directamente en MySQL.
- Cambia las credenciales de prueba desde la pantalla Admin en cuanto puedas.

---

## 9. Seguridad de la sesión (relacionado)

- Cookie de sesión `meditechnosys.intranet`, `HttpOnly`, `SameSite=Lax`, 8 h con renovación deslizante.
- En cada petición, `OnValidatePrincipal` vuelve a leer el usuario en el almacén: si fue dado de
  baja, **desactivado** o le cambiaron el rol, la sesión abierta se rechaza o se actualiza **al instante**.
- Un usuario con `activo = false` no supera la validación de credenciales: el login lo rechaza igual
  que una contraseña incorrecta (sin revelar el motivo).
- Las rutas `/api/*` responden `401/403` en lugar de redirigir al login (para clientes tipo Postman).
