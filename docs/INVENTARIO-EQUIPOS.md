# Inventario de equipos (entradas y salidas)

**Pantalla única de equipos** del sistema, en `/gestion/inventario-equipos/index.html`.
Sustituye al antiguo módulo independiente *Almacén de equipos*: ya **no existe una tarjeta de
"Almacén"** en los menús y la ruta vieja **`/almacen/` redirige** a esta pantalla. Todo el alta,
la consulta y los movimientos de equipos viven aquí, en una **única pestaña: *Ingresos***.

> **Nota de compatibilidad:** el backend conserva el espacio de nombres `/api/almacen/*` y las
> claves de permiso `almacen.*` (no se renombraron para no romper sesiones, matrices de roles ni
> integraciones). En la interfaz y en la documentación el módulo se llama **Inventario de
> equipos**; el grupo de permisos aparece como **"Inventario de equipos · entradas y salidas"**.

---

## Qué se ve en la pantalla

Encabezado **Inventario de equipos**, la pestaña fija **Ingresos** y una barra con:

- **Selector de unidad**: `Almacén`, `HES`, `HGR`, `HGO`, `HI`. La opción *Almacén* representa
  el **almacén central** y se guarda internamente con el valor de hospital `General`
  (`Hospitales.ConGeneral`); en las hojas se imprime como **ALMACÉN CENTRAL**.
- **Buscador** por n.º de inventario, serie, nombre, marca, modelo, etc. (filtra la tabla en el
  cliente, sin recargar).
- **+ Añadir equipo** (solo Admin): crea el lote con su **número de inventario generado
  automáticamente** (`EQ-0001`, `EQ-0002`, …), genera y confirma su **entrada** y abre la
  **hoja de entrada**. El diálogo ya **no** pide el número de inventario.
- **Descargar Excel** (solo Admin): existencias de la unidad y filtro actuales en `.xlsx`.
- Contador de equipos y tabla con columnas **N.º inv. / Nombre / Marca / Modelo / Serie /
  Unidad / Cant. / Estado / Hojas y acciones**. **Solo se listan los lotes con cantidad mayor a
  0**: en cuanto un equipo se queda sin existencias desaparece de la tabla y del Excel.

Cada **fila** ofrece sus acciones:

| Botón | Quién | Qué hace |
|---|---|---|
| **Hoja entrada** | todos con `almacen.consultar` | Reabre la hoja institucional de la entrada de ese lote (reimpresión). |
| **Hoja salida** | todos con `almacen.registrar` | Abre el diálogo de salida: descuenta stock, genera y confirma la **hoja de salida** y, si hay destino, re-ingresa el equipo allá. |
| **Editar** | solo Admin | Edita los datos del lote (marca, modelo, serie, cantidad, estado, ubicación…). |

Los botones **+ Añadir**, **Descargar Excel** y **Editar** se ocultan si el rol no es Admin
(`Gestion.esAdmin()`), **y además el servidor vuelve a validar** el permiso en cada endpoint: la
seguridad no depende de la interfaz.

---

## Principio de diseño: una sola fuente de verdad para el stock

Las **existencias no viven en una tabla aparte**: son los **lotes del catálogo de equipos**
(`Models/Inventario.cs`, colección `equipos`) ampliados con los campos `numInventario`,
`cantidad`, `ubicacion`, `estado`, `fechaIngreso`, `procedencia` y `docEntrada`. Cada lote
representa **un grupo de unidades físicas iguales en una unidad concreta** (mismo n.º de
inventario / serie / marca / modelo) y su campo `cantidad` es el stock disponible.

Los **movimientos** (`almacen_movimientos`) son **documentos**: no guardan stock, guardan la
**cabecera** (tipo, motivo, origen, destino, responsables, observaciones, estado, fechas y quién
autoriza) y una **foto (snapshot) de las líneas** (`detalles`) tal como estaban al registrarse.
Así el historial nunca se deforma aunque el catálogo cambie después.

Consecuencias:

- **Incrementar un lote existente sí se permite.** Al registrar una entrada cuya línea apunta a
  un lote ya existente (`detalles[].equipoId`), el servidor **suma cantidad** a ese lote en vez
  de rechazarla por "duplicado". Solo se rechaza (`400`) cuando la serie o el n.º de inventario
  ya existe en esa unidad **en un lote distinto** al de la línea
  (`Services/Almacen.cs`: la comprobación de duplicado excluye `dup.Id == d.EquipoId`). La misma
  serie **sí** puede existir en unidades distintas (es otro activo físico).
- **Nunca hay stock negativo.** Toda disminución se valida en **dos fases**: primero que la línea
  pertenezca a la unidad de origen y haya stock suficiente; solo si pasa, se aplica.
- **Los lotes en cantidad 0 no se listan.** `AlmacenService.Existencias` filtra `CantidadDe(i) > 0`,
  de modo que un equipo que se queda sin existencias **desaparece de la tabla y del Excel**. El
  registro **no se borra** (sigue en el catálogo con su historial de movimientos y su `docEntrada`);
  si vuelve a entrar stock sobre ese lote, **reaparece** automáticamente. Así la lista muestra solo
  lo que hay disponible.
- **Número de inventario automático.** Al dar de alta un equipo sin indicar número, el servidor
  (`InventarioService.Crear`) le asigna el siguiente `EQ-####` correlativo. El máximo se calcula
  sobre **todos** los equipos (incluidos los que están en 0 y ya no se listan) para **no reutilizar
  nunca** un número dado de baja.
- **Ampliar el catálogo no toca este módulo.** Los campos de los lotes salen del esquema dirigido
  por `InventarioCatalogo`, igual que los otros inventarios.

---

## Flujo "+ Añadir equipo" (Admin)

El diálogo captura **unidad, nombre, categoría, marca, modelo, serie, cantidad, servicio,
ubicación, estado, procedencia y notas** (el **número de inventario se genera solo**). Al aceptar
se encadena:

1. `POST /api/inventario/equipos` → crea el **lote** en la unidad elegida con `cantidad: 0`,
   `docEntrada: ''`, `fechaIngreso` = hoy y el **`numInventario` autogenerado** (`EQ-####`).
2. `POST /api/almacen/movimientos` → crea una **Entrada** (motivo *Recepción de equipo nuevo*)
   con una línea que referencia el lote (`equipoId`) y la cantidad capturada.
3. `PUT /api/almacen/movimientos/{id}/confirmar` → **aplica el stock** (el lote suma la cantidad).
4. `PUT /api/inventario/equipos/{id}` → **sella** `docEntrada` con el id del movimiento y fija la
   `cantidad`, de modo que el botón *Hoja entrada* de la fila always apunte al documento correcto.
5. `window.open('/almacen/hoja.html?id={mov}')` → abre la **hoja de entrada** y recarga la tabla.

---

## Flujo "Hoja salida" (por fila)

El diálogo indica el **stock disponible** del lote y captura **cantidad que sale** (1…stock),
**motivo**, **unidad de destino (opcional)**, **responsable que entrega**, **responsable que
recibe** y **observaciones**. Al aceptar:

1. `POST /api/almacen/movimientos` → **Salida** con `hospitalOrigen` = la unidad del lote.
2. `PUT …/confirmar` → **descuenta** el stock del lote de origen.
3. Si hay **destino** y el motivo **no** es *Desecho / baja definitiva*:
   `POST` + `confirmar` de una **Entrada** en la unidad de destino (**re-ingreso**), que suma allá
   el mismo equipo. Esto modela un **traslado entre unidades** como *salida + entrada*.
4. `window.open('/almacen/hoja.html?id={salida}')` → abre la **hoja de salida** y recarga.

- **Desecho / baja definitiva:** las unidades **no** se re-ingresan en ningún sitio; el lote de
  origen queda reducido o en estado `Baja`.
- El tipo **Traslado** (`TRA-`, con estado *En tránsito* → *Recibido*) sigue existiendo en el
  backend (`Models/Almacen.cs`, máquina de estados completa) por si se registra desde otra
  integración, pero **esta pantalla** usa el par *salida + re-ingreso*, que genera las dos hojas
  (la de salida y la de entrada en destino) por separado.

---

## Tipos de movimiento y máquina de estados (backend)

| Tipo | Prefijo | Qué hace al confirmarse |
|---|---|---|
| **Entrada** | `ENT-` | **Suma** stock en el destino (incrementa el lote existente o crea uno nuevo en *Disponible*). |
| **Salida** | `SAL-` | **Resta** stock del origen. |
| **Traslado** | `TRA-` | **Resta** del origen y lo deja *En tránsito*; al **recibirse** suma en el destino. |

El folio es **único por tipo y año** (`ENT-2026-0001`, `SAL-2026-0002`, …) y lo asigna el
servidor al guardar. Estados (`EstadosMovimiento`):

```
Borrador ──confirmar──► Confirmado            (Entrada / Salida: aplican stock)
   │
   ├──confirmar──► EnTransito ──recibir──► Recibido      (Traslado)
   │                      │
   └──cancelar/rechazar   ├──cancelar/rechazar (reversa el stock al origen)
            ▼                      ▼
        Cancelado / Rechazado   Cancelado / Rechazado
```

**Confirmado / Recibido / Cancelado / Rechazado** son terminales: un movimiento cerrado no se
puede volver a confirmar, recibir ni cancelar (`400 "El movimiento ya está cerrado."`).

---

## Permisos

Grupo **"Inventario de equipos · entradas y salidas"** (`Models/Permisos.cs`; claves `almacen.*`):

| Clave | Qué permite | Admin | Ingeniero (por defecto) |
|---|---|:---:|:---:|
| `almacen.consultar` | Ver existencias, movimientos, historial y hojas | ✔ | ✔ |
| `almacen.registrar` | Crear y confirmar entradas/salidas; cancelar/rechazar los **propios** movimientos no cerrados | ✔ | ✔ |
| `almacen.autorizar` | Cancelar/rechazar movimientos **ajenos** o **en tránsito** | ✔ | — |
| `almacen.exportar` | Descargar existencias e historial en Excel | ✔ | ✔ |

El alta/edición/baja de lotes usa los endpoints genéricos del catálogo
(`POST/PUT/DELETE /api/inventario/equipos`), protegidos con `SoloAdmin`.

La seguridad se cumple **en el servidor**, no solo ocultando botones:

- El bloque `app.UseWhen(... )` de las rutas de gestión exige sesión y rol; los endpoints
  `/api/almacen/*` llevan `.RequireAuthorization("Ingenieria")` y el servicio vuelve a comprobar
  el permiso concreto (`AlmacenService.Tiene`).
- Sin sesión → `401` / redirección a `/Login`; sin permiso → `403` / `/AccesoDenegado`.

Como el catálogo de permisos está en **versión 3**, las matrices ya guardadas se completan solas
con los valores por defecto del rol (`CatalogoPermisos.NuevasDesde`): nadie pierde acceso tras la
actualización.

---

## API

| Método y ruta | Qué hace | Permiso |
|---|---|---|
| `GET /api/inventario/equipos` | Lotes del inventario de equipos (existencias) | consultar (lectura) / `SoloAdmin` (alta, edición, baja) |
| `POST /api/inventario/equipos` | **Alta** de un equipo (marca, modelo, serie, cantidad…) | `SoloAdmin` |
| `PUT /api/inventario/equipos/{id}` | **Edición** / sellado de `docEntrada` y `cantidad` | `SoloAdmin` |
| `DELETE /api/inventario/equipos/{id}` | **Baja** de un equipo | `SoloAdmin` |
| `GET /api/almacen/existencias?hospital=&q=` | Lotes filtrados por unidad y texto | consultar |
| `GET /api/almacen/existencias/excel?hospital=&q=` | Existencias en `.xlsx` | exportar |
| `GET /api/almacen/movimientos?tipo=&estado=&hospital=&desde=&hasta=&q=` | Movimientos con filtros | consultar |
| `GET /api/almacen/movimientos/{id}` | Un movimiento con sus líneas | consultar |
| `POST /api/almacen/movimientos` | Crea un movimiento con folio | registrar |
| `PUT /api/almacen/movimientos/{id}/confirmar` | Confirma (aplica stock / pone en tránsito) | registrar |
| `PUT /api/almacen/movimientos/{id}/recibir` | Recibe un traslado en tránsito | registrar |
| `PUT /api/almacen/movimientos/{id}/cancelar?rechazo=true\|false` | Cancela o rechaza (reversa si estaba en tránsito) | registrar (propio) / autorizar |
| `GET /api/almacen/movimientos/{id}/hoja` | Hoja institucional en **HTML en línea** (imprimir / PDF) | consultar |
| `GET /api/almacen/movimientos/{id}/word` | La misma hoja como **Word (.doc)** descargable | consultar |
| `GET /api/almacen/historial/excel` | Historial completo en `.xlsx` | exportar |

Las operaciones de cambio y cada descarga quedan en la **bitácora de accesos** (`almacen.crear`,
`almacen.confirmar`, `almacen.recibir`, `almacen.cancelar`, `ExportarExcel`).

---

## Hojas de entrada / salida

La hoja **replica exactamente el formato oficial** de `Entrada.docx` y `Salida.docx`:

1. **Título** centrado según el tipo: *ENTRADA DE EQUIPO* o *SALIDA DE EQUIPO*.
2. **Recuadro de encabezado** (tabla): *MEDITECHNOSYS GROUP S.A. DE C.V.*, *UNIDAD MÉDICA*,
   *AREA*, y en dos columnas *NÚMERO DE CONTRATO*, *FECHA DE INICIO DE VIGENCIA* y *FECHA DE
   TERMINO DE VIGENCIA*.
3. **Fecha larga en español** (p. ej. *lunes, 12 de octubre de 2026*) seguida del **FOLIO**.
4. **Tabla de equipos** *EQUIPO / NUMERO DE SERIE / UNIDADES* con una fila por línea y, al final,
   la fila **OBSERVACIONES** dentro de la misma tabla.
5. **Bloque de firmas**: tres líneas de subrayado (30, 28 y 29 guiones bajos) y, debajo, los tres
   puestos: *jefe de servicio o coordinador clínico…*, *jefe del departamento de finanzas* y
   *supervisor por parte del proveedor*.

> **Las firmas van en blanco y sin nombres.** El formato oficial deja únicamente la línea y el
> puesto para firmarse a mano; la hoja **no imprime ningún nombre** en ese bloque.

- **UNIDAD MÉDICA** toma la unidad del movimiento (origen en salidas, destino en entradas); si es
  `General` se imprime **ALMACÉN CENTRAL** (`HojaAlmacenHtml` en `Program.cs`).
- Los datos se **autollenan desde la base** (cabecera + líneas); no hay recaptura.
- **Una sola fuente de verdad**: `HojaAlmacenHtml` genera tanto el HTML de `/hoja` como el `.doc`
  de `/word`; la versión imprimible y la de Word nunca divergen.
- `/almacen/hoja.html?id=<mov>` muestra la hoja con **Imprimir / PDF**, **Descargar Word** y
  **Cerrar**; al imprimir la barra de botones desaparece (`@media print`).
- **Reimpresión:** la hoja se abre tantas veces como se quiera (botón *Hoja entrada* de la fila o
  desde el historial); al generarse desde datos guardados, reimprimir no altera nada.
- Contrato y vigencias en `appsettings.json`: `Almacen:Contrato` (`D260012SI`),
  `Almacen:VigenciaInicio` (`01/JULIO/2026`) y `Almacen:VigenciaFin` (`31/DICIEMBRE/2026`).

---

## Base de datos

Tabla `almacen_movimientos` en `meditechnosys_intranet` (se crea sola al arrancar con
`MySqlAlmacenStore.EnsureSchema`):

```
almacen_movimientos(
  id, folio (UNIQUE), tipo, motivo,
  hospital_origen, ubicacion_origen, hospital_destino, ubicacion_destino,
  estado, fecha, resp_entrega, resp_recibe, usuario_registra,
  doc_ref, observaciones, creado, modificado,
  fecha_autorizacion, usuario_autoriza, detalles (JSON)
)
índices: idx_almacen_tipo_estado (tipo, estado), idx_almacen_fecha (fecha)
```

Los **lotes de existencias** viven en la tabla ya existente `inventario` (colección `equipos`);
**no** se creó una tabla paralela de stock. Si MySQL no responde al arrancar, el módulo usa el
respaldo en archivo (`App_Data/almacen.json`, configurable con `Auth:AlmacenFile`).

**No hay borrado físico de movimientos** desde la aplicación: el historial es consultable y
exportable siempre; un movimiento equivocado se **cancela o rechaza** (queda el rastro).

---

## Flujo de trabajo típico

1. **Añadir el equipo** (Admin): *+ Añadir equipo* → unidad, nombre, marca, modelo, serie, n.º de
   inventario y cantidad → se crea el lote, se confirma su entrada y se abre la **hoja de
   entrada**.
2. **Generar una salida** (Admin o Ingeniero con `almacen.registrar`): en la fila, *Hoja salida* →
   cantidad, motivo y destino opcional → se descuenta el stock, se genera la **hoja de salida** y,
   si hay destino, el equipo **re-ingresa** allá.
3. **Reimprimir** la hoja de entrada de un lote con el botón *Hoja entrada*.
4. **Editar** los datos del lote (solo Admin) con *Editar*.
5. **Descargar Excel** (solo Admin) con las existencias de la unidad y el filtro actuales.

---

*Detalle de la arquitectura general del sistema: [`ARQUITECTURA.md`](ARQUITECTURA.md).*
