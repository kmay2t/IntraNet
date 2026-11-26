# Reporte de entrega — 2026-10-12

**Alcance:** continuación y optimización del sistema existente (sin proyecto nuevo ni
reemplazo) + **pantalla única de Inventario de equipos** (pestaña *Ingresos*, selector de unidad
Almacén/HES/HGR/HGO/HI, botón *Añadir* y **hoja de entrada y de salida en cada fila**,
*Descargar Excel* solo Admin) + **hojas de entrada/salida con el formato oficial**
(`Entrada.docx` / `Salida.docx`) con firmas en blanco sin nombres + ajustes de calendario
(checkbox de fecha límite por elemento y panel de pendientes por ingeniero).

El antiguo módulo independiente **Almacén de equipos se retiró**: su tarjeta desapareció de los
menús y `/almacen/` redirige a la pantalla única. El backend conserva el espacio de nombres
`/api/almacen/*` y las claves de permiso `almacen.*` por compatibilidad.

Todo se implementó **sobre el proyecto actual** (`Intranet_Unificada`), se compiló sin errores
(0 advertencias / 0 errores), se probó en desarrollo (`localhost:5099`) y se verificó en
navegador real. La publicación al servicio de Windows (`localhost:5023`) requiere ejecutar
**`actualizar.bat` como administrador** (aprobación de UAC); el código y la carpeta `publish\`
quedan listos para aplicarlo.

---

## 1. Funcionalidades nuevas

### Inventario de equipos (`/gestion/inventario-equipos/`) — pantalla única
- **Pestaña única *Ingresos*** con **selector de unidad**: `Almacén`, `HES`, `HGR`, `HGO`, `HI`.
  La opción *Almacén* representa el **almacén central** y se guarda como hospital `General`
  (`Hospitales.ConGeneral`); en las hojas se imprime **ALMACÉN CENTRAL**. Buscador por n.º de
  inventario, serie, nombre, marca o modelo, y contador de equipos.
- **+ Añadir equipo (solo Admin):** captura unidad, nombre, categoría, marca, modelo, serie,
  número de inventario, cantidad, servicio, ubicación, estado, procedencia y notas. Al aceptar se
  **crea el lote**, se genera y **confirma su entrada** (que suma la cantidad), se sella su
  `docEntrada` y se **abre la hoja de entrada**.
- **Acciones por fila:** **Hoja entrada** (reimpresión de la hoja de la entrada del lote),
  **Hoja salida** (descuenta stock, genera y confirma la **hoja de salida** y, si se indica
  destino, **re-ingresa** el equipo en esa otra unidad) y **Editar** (solo Admin).
- **Descargar Excel (solo Admin):** existencias de la unidad y el filtro actuales en `.xlsx`.
- **Existencias = lotes** del catálogo de equipos ampliados con número de inventario, cantidad,
  ubicación, estado, fecha de ingreso, procedencia y documento de entrada. **Una sola fuente de
  verdad para el stock** (sin tabla paralela).
- **Traslado entre unidades** se modela como *salida + re-ingreso* (dos hojas). El motivo
  **"Desecho / baja definitiva"** descuenta sin re-ingresar en ningún sitio.

### Reglas de integridad del stock
- **Incrementar un lote existente sí se permite:** una entrada cuya línea apunta a un lote ya
  existente (`detalles[].equipoId`) le **suma cantidad** en vez de rechazarse como duplicado.
  Solo se rechaza (`400`) una serie o número de inventario que ya exista **en un lote distinto**
  de la misma unidad (`Services/Almacen.cs`: `dup is not null && dup.Id != d.EquipoId`). La misma
  serie puede existir en unidades distintas (es otro activo físico).
- **Sin stock negativo:** toda disminución se valida en **dos fases** (pertenencia al origen y
  stock suficiente) antes de aplicar nada.
- **Sin borrado físico de movimientos** desde la aplicación: un movimiento equivocado se
  **cancela o rechaza** (queda el rastro en historial y bitácora).
- **Máquina de estados (backend):** `Borrador → Confirmado` (entrada/salida aplican stock) y
  `Borrador → EnTransito → Recibido` (traslado); `Cancelado`/`Rechazado` como cierre terminal.

### Hojas con el formato oficial (`Entrada.docx` / `Salida.docx`)
- La hoja **replica exactamente** el documento oficial: título (*ENTRADA / SALIDA DE EQUIPO*),
  recuadro de encabezado (razón social, *UNIDAD MÉDICA*, *AREA*, *NÚMERO DE CONTRATO*, *FECHA DE
  INICIO/TERMINO DE VIGENCIA*), **fecha larga en español + FOLIO**, tabla
  *EQUIPO / NUMERO DE SERIE / UNIDADES* con la fila *OBSERVACIONES* dentro, y bloque de firmas.
- **UNIDAD MÉDICA** toma la unidad del movimiento (origen en salidas, destino en entradas); si es
  `General` se imprime **ALMACÉN CENTRAL**.
- **Firmas en blanco y sin nombres:** tres líneas de subrayado (30/28/29 guiones bajos) y los
  tres puestos (jefe de servicio o coordinador clínico, jefe de finanzas, supervisor del
  proveedor). La hoja **no imprime ningún nombre** en ese bloque.
- Una sola función (`HojaAlmacenHtml`) genera el HTML en línea (`/hoja`) y el Word (`/word`);
  `/almacen/hoja.html` muestra la hoja con **Imprimir / PDF**, **Descargar Word** y **Cerrar**.

### Seguridad (interfaz + servidor)
- Los botones **+ Añadir**, **Descargar Excel** y **Editar** se ocultan si el rol no es Admin
  (`Gestion.esAdmin()`), **y el servidor vuelve a validar** el permiso en cada endpoint
  (`SoloAdmin` para alta/edición/baja de lotes; `almacen.*` para movimientos y exportación).
- Permisos del grupo **"Inventario de equipos · entradas y salidas"** (claves `almacen.*`,
  catálogo v3): consultar/registrar/exportar para Admin e Ingeniero; **cancelar o rechazar
  movimientos ajenos o en tránsito** exige `almacen.autorizar` (por defecto solo Admin).
- Las matrices de permisos ya guardadas se completan solas con los valores por defecto del rol
  (`CatalogoPermisos.NuevasDesde`): nadie pierde acceso tras la actualización.

### Calendario
- **Casilla "Deshabilitar fecha límite" por elemento** dentro de los diálogos de crear, editar y
  asignar: apaga y limpia el campo de fecha de **ese** elemento (en lugar de un ajuste global).
- **Panel derecho "Pendientes por ingeniero":** agrupa por responsable las actividades e
  incidentes abiertos y, con el conmutador *Reportes*, los reportes de estatus pendientes,
  ordenando primero lo vencido. Cada renglón abre el detalle del elemento.

### Frontend compartido
- `Gestion.formDialog` admite campos tipo **checkbox** (con `desactiva` para apagar otro campo)
  y la opción `contenidoDespues` para colocar un bloque interactivo debajo de la cabecera.

---

## 2. Funcionalidades conservadas (sin regresiones)

- Login único, roles Admin/Ingeniero y matriz de permisos editable.
- Calendario, histórico único con Excel, reportes de estatus y periodicidad.
- Los inventarios de **IPs y AETitle** con filtro por hospital y Excel.
- Playbook (subida/administración por Admin, consulta por Ingeniero).
- Repositorio de Patología y visor DICOM (sin cambios).
- Bitácora de accesos y historial de cambios append-only.
- Respaldo en archivos `App_Data/` cuando MySQL no responde.
- Verificado en navegador (5099): el menú muestra la tarjeta *Inventario de equipos* y **no** la
  de *Almacén*; `/almacen/` redirige a la pantalla única; el calendario conserva el panel derecho.

---

## 3. Archivos

**Creados (esta fase):**
- `docs/INVENTARIO-EQUIPOS.md` — documentación del módulo unificado.

**Modificados (esta fase):**
- `Models/Catalog.cs` — tarjeta única *Inventario de equipos* en Admin e Ingeniería; sin *Almacén*.
- `Models/Permisos.cs` — etiqueta del grupo → *Inventario de equipos · entradas y salidas*.
- `Models/Inventario.cs`, `Models/Almacen.cs` — unidad *Almacén* (`General`) permitida en equipos
  y movimientos (`permiteGeneral: true`).
- `Services/Inventario.cs`, `Services/Inventarios.cs`, `Services/AlmacenStore.cs` —
  `Hospitales.Normalizar(..., permiteGeneral: true)`.
- `Services/Almacen.cs` — permitir el incremento sobre el lote existente en una entrada.
- `Program.cs` — `HojaAlmacenHtml`: `General` → **ALMACÉN CENTRAL**.
- `wwwroot/gestion/inventario-equipos/index.html` — pantalla única reescrita (pestaña *Ingresos*).
- `wwwroot/almacen/index.html` — redirección a la pantalla única.
- `README.md`, `docs/REGISTRO-CAMBIOS.md`, este reporte — re-documentación.

**Creados en fases previas (siguen vigentes):**
- `Models/Almacen.cs`, `Services/AlmacenStore.cs`, `Services/Almacen.cs` — tipos, estados,
  motivos, `MovimientoInventario`, folios, `EnsureSchema`, stock y reversas.
- `wwwroot/almacen/hoja.html` — hoja imprimible/PDF + descarga Word.

**Eliminados:**
- `docs/ALMACEN.md` — sustituido por `docs/INVENTARIO-EQUIPOS.md`.

No se borró información de producción, datos de usuarios ni registros históricos.

---

## 4. Cambios de base de datos / migraciones

- **Tabla** `almacen_movimientos` en `meditechnosys_intranet`, creada automáticamente al arrancar
  (`MySqlAlmacenStore.EnsureSchema`): `id`, `folio UNIQUE`, `tipo`, `motivo`, origen/destino
  (hospital + ubicación), `estado`, `fecha`, responsables, `usuario_registra`, `doc_ref`,
  `observaciones`, `creado`, `modificado`, `fecha_autorizacion`, `usuario_autoriza`, `detalles`
  (JSON). Índices `idx_almacen_tipo_estado` y `idx_almacen_fecha`. **Sin cambios de esquema en
  esta fase.**
- **Sin columnas nuevas** en `inventario`: los campos de los lotes viven dentro del JSON `datos`
  (esquema dirigido por `InventarioCatalogo`).
- **Catálogo de permisos v3:** las matrices ya guardadas se completan solas (`NuevasDesde`).
- Las transacciones de stock se aplican de forma **atómica por movimiento** (validación en dos
  fases antes de escribir).

---

## 5. Pruebas realizadas y resultados

Ejecutadas contra la instancia de desarrollo (`localhost:5099`, misma base MySQL que el servicio)
vía API y vía navegador:

| Prueba | Resultado |
|---|---|
| Pantalla única carga con pestaña *Ingresos* y selector Almacén/HES/HGR/HGO/HI | ✔ en navegador |
| Menú Admin/Ingeniería muestra *Inventario de equipos* y **no** *Almacén* | ✔ confirmado |
| `/almacen/index.html` redirige a `/gestion/inventario-equipos/index.html` | ✔ confirmado |
| **+ Añadir equipo** → lote + entrada confirmada + `docEntrada` + hoja de entrada | ✔ lote con cantidad 3, folio `ENT-2026-0002`, hoja correcta |
| Hoja de entrada: *UNIDAD MÉDICA: ALMACÉN CENTRAL*, tabla, OBSERVACIONES, firmas sin nombres | ✔ formato oficial |
| Fila muestra **Hoja entrada**, **Hoja salida** y **Editar** | ✔ los tres botones |
| **Hoja salida** con destino → descuenta origen y re-ingresa en destino | ✔ Almacén 3→2, HES +1, folio `SAL-2026-0001` |
| Hoja de salida: título, encabezado, OBSERVACIONES con texto, firmas sin nombres | ✔ formato oficial |
| Entrada que incrementa el **mismo lote** (`equipoId`) | ✔ suma cantidad (ya no se rechaza) |
| **Descargar Excel** de existencias | ✔ `.xlsx` válido (200, 7190 bytes, ClosedXML) |
| Botones Añadir/Excel/Editar visibles solo para Admin | ✔ `hidden = !esAdmin()` + validación en servidor |
| Datos reales preservados (lote DELL/HES, movimiento `ENT-2026-0001`) | ✔ intactos tras las pruebas |
| Servicio publicado (5023): menú, pantalla, hojas | ⚠ pendiente de aplicar `actualizar.bat` como administrador (UAC); verificado en desarrollo (5099) |

**Defecto encontrado y corregido durante las pruebas:**
1. La comprobación de **duplicado** de una entrada rechazaba también el **incremento sobre el
   lote existente** al que apuntaba la línea (`equipoId`), impidiendo sumar cantidad a un lote ya
   dado de alta. Se corrigió en `Services/Almacen.cs` para excluir ese caso
   (`dup is not null && dup.Id != d.EquipoId`): ahora solo se rechaza un duplicado real en un lote
   distinto.

**Limpieza posterior:** todos los registros de prueba creados durante esta verificación (lotes de
equipos y movimientos de entrada/salida/re-ingreso) se eliminaron; solo quedaron los datos reales
(lote DELL/HES y movimiento `ENT-2026-0001`). No se borró información de producción.

---

## 6. Pendientes, errores conocidos y limitaciones

- **Publicación al servicio (5023) pendiente de UAC:** el código está compilado (0 errores) y la
  carpeta `publish\` lista, pero aplicar los cambios al servicio de Windows exige ejecutar
  **`actualizar.bat` como administrador** (el servicio bloquea el ejecutable mientras corre, por
  eso el script lo detiene primero). En esta sesión la aprobación de UAC no se concedió, así que
  la versión nueva quedó **verificada en desarrollo (5099)** y pendiente de publicarse con clic
  derecho → *Ejecutar como administrador* sobre `actualizar.bat`.
- **Registros de prueba anteriores en la base compartida:** quedan **dos actividades de prueba**
  de sesiones previas (`#1 "Prueba"` y `#2 "Prueba checkbox limite"`, del usuario `ing`). No se
  eliminaron automáticamente porque la acción de borrado fue bloqueada en su momento. Se pueden
  quitar desde la interfaz: **Calendario → abrir el elemento → Eliminar**, o indicarlo para
  eliminarlas por API.
- **PDF de la hoja:** se genera con el diálogo *Imprimir → Guardar como PDF* del navegador (no hay
  renderizador PDF del lado del servidor). La versión **Word** sí es un archivo descargable.
- **Word `.doc`:** es HTML con cabecera Word (compatible y editable en Word), no un binario
  `.docx` nativo.
- **Espacio de nombres `/api/almacen/*`:** se conserva por compatibilidad aunque el módulo se
  llame *Inventario de equipos*; renombrarlo implicaría migrar permisos e integraciones.
- En **desarrollo**, el listener DICOM del puerto `11113` puede fallar al bind si ya está ocupado
  por el servicio; es esperado e inocuo para el resto del sistema.

---

## 7. Ejecución y configuración

- **Compilar:** `dotnet build -c Release` (0 errores).
- **Desarrollo:** para no chocar con el servicio:
  `ASPNETCORE_URLS=http://localhost:5099 dotnet run -c Release`.
- **Publicar / actualizar el servicio:** clic derecho en **`actualizar.bat`** → *Ejecutar como
  administrador* (detiene `MeditechnosysIntranet`, republica y reinicia). Una vez ejecutado,
  `localhost:5023` servirá la versión nueva (menú con *Inventario de equipos*, pantalla única con
  *Añadir* y hojas por fila, y *Descargar Excel* solo Admin).
- **Configuración (opcional) en `appsettings.json`:**
  - `Almacen:Contrato` (`D260012SI`), `Almacen:VigenciaInicio` (`01/JULIO/2026`) y
    `Almacen:VigenciaFin` (`31/DICIEMBRE/2026`) → textos de las hojas.
  - `Auth:AlmacenFile` → ruta del respaldo `App_Data/almacen.json` si MySQL no responde.
- **Documentación:** `README.md` (secciones y tabla de pantallas actualizadas),
  `docs/INVENTARIO-EQUIPOS.md` (módulo completo) y `docs/REGISTRO-CAMBIOS.md` (entrada
  2026-10-12 del rediseño).

---

*Reporte generado el 2026-10-12 como cierre del alcance solicitado.*
