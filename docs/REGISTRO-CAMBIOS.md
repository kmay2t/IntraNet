# Registro de cambios

Bitácora de mantenimiento del proyecto. Cada entrada indica **fecha**, **módulo**, **qué cambió**,
**por qué**, **archivos creados / modificados / eliminados** y el tipo de cambio
(base de datos, API, frontend, backend, permisos, configuración).

---

## 2026-11-24 — **Calendario / Incidentes**: el formulario de incidente también ofrece **Servicio**, y el elegido queda visible en su título

**Módulo:** `wwwroot/js/gestion.js` (`G.abrirCrear`, compartido por calendario e histórico). **Tipo:** frontend puro.
**Base de datos:** ninguna. **API:** ninguna —`POST /api/actividades` recibe el mismo JSON de siempre—. **Permisos:** ninguno.

**Por qué:** la captura del diálogo «Nuevo elemento» con **Tipo = Incidente**, donde aparecían Región, Hospital y Título, pero no Servicio, y el pedido «para los incidentes, tiene que así como aparece en actividad, aparecer el hospital y servicio». El Hospital ya estaba; el Servicio existía en el formulario, pero `ajustarServicio()` lo ocultaba expresamente cuando el tipo no era Actividad.

**Qué cambió:**

1. **Servicio se ofrece para Actividad e Incidente.** `ajustarServicio()` ya no recibe `esActividad` ni pone `api.mostrar('servicio', esActividad && lista.length > 0)`: ahora lo muestra siempre que el hospital elegido tenga servicios configurados. `ocultoInicial` también dejó de depender del tipo. Si el hospital no tiene servicios, el campo sigue oculto, igual que antes en las actividades.
2. **El selector sigue ligado al Hospital.** Cambiar de Hospital reconstruye la lista con los servicios de ese hospital y borra la elección anterior; cambiar de Región borra el Hospital y deja Servicio oculto hasta elegir uno. Cambiar entre Actividad e Incidente conserva esa dependencia y no permite mezclar servicios de hospitales distintos.
3. **El servicio del incidente se guarda dentro del título**, con la misma convención que ya usaban las actividades: `Título escrito — Servicio`. Por ejemplo, «Falla de red en el HGO» + «Radiodiagnóstico» queda como **«Falla de red en el HGO — Radiodiagnóstico»**. Así se ve en el calendario, el panel de pendientes, el histórico y la tarjeta de detalle sin agregar una columna ni migrar datos.
4. **El tope de 120 caracteres ahora se valida para los dos tipos** después de armar el título completo. Si el texto del incidente más el servicio no caben, el formulario lo avisa antes de enviar.
5. **Los incidentes ya guardados no se modifican.** Solo las altas nuevas con un servicio elegido agregan ese texto al final; los títulos viejos quedan tal como están.
6. **Lo que NO cambió:** Región, Hospital, permisos, estados, fechas, asignación, evidencia y el alta completada del Ingeniero. El servicio sigue siendo opcional: «(Sin servicio)» deja el título exactamente como se escribió.

**Verificación:** `node --check wwwroot/js/gestion.js` sin errores. En Chrome real, con el HTML del calendario y el `gestion.js` de `wwwroot`, un Incidente con Hospital HGO abrió mostrando **Hospital visible** y **Servicio visible** con `(Sin servicio) | Urgencias | Radiodiagnóstico`; al cambiar a HES la lista pasó a `(Sin servicio) | Laboratorio` y el valor se borró; al cambiar el tipo a Actividad el servicio siguió visible. El envío real del formulario generó `tipo:"Incidente"`, `hospital:"HGO"`, `titulo:"Falla de red en el HGO — Radiodiagnóstico"`. Consola sin errores de JavaScript (solo los 404 esperados de `/api/*` porque la prueba usa servidor estático). La captura automática no estuvo disponible porque el navegador integrado no expuso viewport visible; la comprobación se hizo sobre el DOM real de Chrome.

**Despliegue:** copiado a `publish/wwwroot/js/gestion.js`; md5 de origen y publicación idénticos (`46ec7996b208c82fcef9eaef19dfdf9b`). `gestion.js` se sirve con `SinCacheHtml`, así que **no requiere recompilar ni reiniciar el servicio**. Respaldo de la versión publicada anterior: `Intranet_Unificada/respaldo_gestion_publicado_20261124.js`. En la PC del usuario basta recargar el calendario con **Ctrl+F5**.

---

## 2026-11-24 — **DICOM / Enviar estudios**: el botón de la carpeta deja de depender de `.click()` desde script; ahora se puede **soltar la carpeta** y la pantalla **avisa** si el navegador no abre la ventana

**Módulo:** `wwwroot/dicom/enviar/index.html` (pestaña «Enviar estudios»). **Tipo:** frontend puro.
**Base de datos:** ninguna. **API:** ninguna —`POST /api/dicom/enviar/archivos` no cambió—. **Permisos:** ninguno (sigue siendo `dicom.enviar`).

**Por qué:** la captura del usuario con la pantalla abierta y el pedido «en otra computadora no le deja subir archivos, debe permitirlo, no solo del servidor». Aclarado con él: en esa PC **al tocar «Agregar carpeta de estudios…» no abre nada**, entran **por IP o nombre del servidor**, y **no** quieren un botón extra de archivos sueltos —solo que funcione la carpeta—.

**Qué cambió:**

1. **El botón es ahora una `<label>` enlazada al `<input type=file>`.** Antes era `<button>` + `$("btnAddFolder").onclick = () => $("pickFolder").click()`, y ese `.click()` desde script es justo el que varios navegadores del parque no dejan ejecutar (el selector queda muerto y el clic no hace nada, sin error en consola). Con la etiqueta, el navegador abre la ventana de carpetas **sin JavaScript de por medio**, igual que si se tocara el cuadro nativo; el `<input>` sigue siendo `webkitdirectory directory multiple` y está oculto con la técnica `clip` (no `display:none`) para que siga siendo alcanzable por teclado: `label.btn-carpeta input[type=file] { position:absolute; width:1px; height:1px; clip:rect(0 0 0 0) }`.
2. **`disabled` ya no sirve en una etiqueta**, así que se agregó `bloqueoCarpeta(apagado)`: apaga el `<input>` (el que de verdad abre la ventana), le pone la clase `apagado` al botón y su `aria-disabled`. Las cuatro llamadas viejas (`$("btnAddFolder").disabled = …`, dos al leer la carpeta y dos al enviar) ahora pasan por esa función, así que durante un envío o un lectura en curso la carpeta sigue bloqueada como antes.
3. **Se puede soltar la carpeta encima de la pantalla.** `dragenter/dragover/dragleave/drop` sobre `#tabArchivos`, con la clase `soltando` (borde dorado punteado) mientras se arrastra. Los elementos bajan con `webkitGetAsEntry()` y `recorrerEntrada()` recorre subcarpetas hasta el fondo (`readEntries` entrega de 56 en 56, se pide hasta que regrese vacío); la ruta de la carpeta se usa como etiqueta, igual que `webkitRelativePath` del selector. Si el navegador no expone entradas, se toman `dt.files` como archivos sueltos. `DICOMDIR`, los archivos de 0 bytes y las instancias repetidas se descartan con la misma regla de siempre.
4. **`agregar()` cambió de firma:** ya no recibe `File[]` sino `[{ f, etiqueta }]`, porque lo que llega arrastrando no trae `webkitRelativePath`. Las dos vías (selector y arrastre) terminan en el mismo lazo de lectura de cabeceras, el mismo `CONC_CABECERAS = 8` y el mismo progreso.
5. **El silencio pasó a ser aviso.** Si a los 2 s del clic no ocurrió ni `change` ni `cancel` y la página **no** perdió el foco (la ventana nativa sí le quita el foco), se escribe en el registro de la pantalla que el navegador no abrió la ventana, si es que ese navegador soporta `webkitdirectory`, que se puede soltar la carpeta sobre la lista, y —si la sesión va en `http://`— que varias versiones de Chrome y Edge solo dejan elegir carpetas en la dirección segura. Al cargar, si el navegador no soporta elegir carpetas, el aviso sale de una vez.
6. **Lo que NO cambió:** el destino, el AE emisor, los lotes (`LOTE_MB = 350`, `LOTE_ARCHIVOS = 300`), la corrección de etiquetas por estudio, el reenvío entre nodos, los permisos y el endpoint de subida. La ayuda nueva (`#hintCarpeta`) solo explica el arrastre.

**Verificación:** en Chrome real, sirviendo el mismo archivo: consola limpia (solo los 404 esperados de `/api/*` al no haber backend en la prueba); el DOM muestra `LABEL` conteniendo el input con `webkitdirectory` soportado; **elegir carpeta** con 3 archivos → `2 estudio(s) · 3 imágenes`, renglones `PACIENTE, 000 / P000000 / SC / 2` y `PACIENTE, 001 / P000001 / SC / 1`, registro «Leídos 3 archivo(s) y armados 2 estudio(s).»; **soltar la misma carpeta otra vez** → «No venía ningún archivo nuevo en esa selección» (no duplica); **soltar un archivo repetido y uno nuevo** → «Leídos 1 archivo(s)… 1 imágenes repetidas se ignoraron»; el `dragover` pinta la clase `soltando` y el `drop` la quita; `bloqueoCarpeta(true/false)` deja el input `disabled` con la clase puesta y lo regresa limpio; y al tocar la etiqueta con el ratón la página **pierde el foco** (la ventana nativa se abrió) sin que salga el aviso de respaldo.

**Despliegue:** es solo un archivo estático y se sirve con `SinCacheHtml("/dicom")`, así que **no requiere recompilar ni reiniciar el servicio**: copiado a `publish/wwwroot/dicom/enviar/index.html` (md5 idéntico al de `wwwroot/`) queda vivo para las PCs que entran por IP. Respaldo de la versión anterior: `Intranet_Unificada/respaldo_dicom_enviar_20261124.html`. En la PC donde no abría hay que recargar con **Ctrl+F5**; si aún así no abre la ventana, la pantalla lo dice en el registro y la carpeta se puede soltar encima.

---

## 2026-11-23 — **Calendario / Alta del Ingeniero**: el perfil de Ingeniero **solo registra actividades del día de hoy**; el Administrador sigue fechando cualquier día

**Módulo:** `wwwroot/js/gestion.js` (fecha del alta en `G.abrirCrear`), `wwwroot/calendario/index.html` (el «+» de cada celda) y `Services/Gestion.cs` (validación en `Crear`). **Tipo:** frontend + backend.
**Base de datos:** ninguna. **API:** `POST /api/actividades` —rechaza ahora `400 "Solo puede registrar actividades del día de hoy."` si el alta no es de hoy—. **Permisos:** ninguno (el divisor es el mismo `tickets.asignar` de siempre).

**Por qué:** el pedido «el perfil de ingeniero solo puede subir las actividades del día de hoy y las que tenga asignadas o pendientes», aclarado con el usuario en dos puntos: **la restricción es solo para el alta** (registrar), y **completar con evidencia una actividad suya pendiente de cualquier fecha sigue permitido** —recortar eso dejaría elementos pendientes sin salida.

**Qué cambió:**

1. **La fecha del alta del Ingeniero es siempre hoy.** En `G.abrirCrear`, justo después de `var cierraAlCrear = !puedeAsignar;`, se calcula `var fechaAlta = puedeAsignar ? (opts.fecha || G.hoy()) : G.hoy();`, y esa `fechaAlta` es la que viaja tanto en el multipart del `alta:true` como en el cuerpo JSON. El campo «Fecha de inicio» del Ingeniero **no está a la vista** (`puedeAsignar` es el que lo pinta), así que aunque el diálogo se abra desde el «+» de otro día, la fecha nunca puede ser otra. El Administrador conserva su campo editable con la fecha del día elegido.
2. **El «+» deja de pintarse en los días que no son hoy** para quien no tiene `tickets.asignar`. En `pintarMes()` de `calendario/index.html` se añadió `function puedeFechaOtroDia() { return Gestion.tiene('tickets.asignar'); }` y la condición pasó de `if (puedeCrear())` a `if (puedeCrear() && (puedeFechaOtroDia() || k === hoy))`: la celda de hoy conserva su «+» y su botón «+ Nuevo» de la barra; las demás celdas quedan sin botón. No es solo esconder: el servidor repite la regla (punto 3).
3. **El servidor lo obliga.** Inmediatamente después de `var puedeAsignar = Tiene(s, CatalogoPermisos.TicketsAsignar);` en `Crear` (el embudo por el que también pasa `CrearYCerrada`): `if (!puedeAsignar && fecha.Date != DateTime.Today) return (400, "Solo puede registrar actividades del día de hoy.", null);`. Con esto, un POST fabricado a mano con fecha de otro día tampoco entra. El cierre con evidencia (`completar`) **no toca esta validación**: completa actividades pendientes de cualquier fecha.
4. **Lo que NO cambió:** el alta en dos pasos con ventana de evidencia, la fecha límite, los estados Pendiente/Completada/Cancelado, el recordatorio de 3 días y el permiso `tickets.asignar` con su asignación formal siguen intactos; el Administrador (o cualquier rol con ese permiso) puede fechar elementos de otro día como siempre.
5. **Marca de caché:** `gestion.js` ya cargaba `?v=20261123a` (subida en la entrada anterior el mismo día) y el calendario es HTML propio sin marca —basta Ctrl+F5—. **El backend sí exige recompilar**: la validación vive en `Services/Gestion.cs`.

**Verificación:** `dotnet build -c Release` sin advertencias ni errores. Sobre la arena (5097 → 5099) con curl, las cinco rutas: **A)** `tec1` incidente con fecha de ayer → `400 {"error":"Solo puede registrar actividades del día de hoy."}`; **B)** `tec1` con fecha de hoy → 201; **C)** multipart (alta atómica) de ayer → 400; **D)** multipart de hoy → 201 y nace `Completada`; **E)** `jefe` (Administrador) con fecha anterior → 201, sin restricción; **F)** `POST /api/actividades/8/completar` sobre una pendiente de hace días (2026-11-20) con `tec1` → 200, el cierre de cualquier fecha sigue abierto. En Chrome real: `guiones/guion-s-solo-hoy.js` deja a `tec1` con **un solo «+»** en toda la cuadrícula (`mas:["2026-11-23"]`, `celdaHoyConMas:true`, `asignar:false`) y a `jefe` con los 42; `guiones/guion-s2-alta-ui.js` abre el alta de `tec1` con `fecha:'2026-11-10'` y el elemento nace con `fechaGuardada:"2026-11-23"` (`esHoy:true`) sin ver el campo fecha, mientras `jefe` ve `fechaDelCampo:"2026-11-10"` y guarda esa fecha. Captura: `cap-alta-solo-hoy.png` (cuadrícula de noviembre con el «+» únicamente sobre el día 23 resaltado, para `x-perfil=tec1`). La limpieza revirtió lo escrito por las pruebas: los registros desechables salieron de `actividades.json`/`evidencias.json` con sus archivos, la actividad 8 volvió a `Pendiente` y el servidor y el proxy de la arena quedaron detenidos.

**Despliegue:** pendiente, igual que el resto del 20 y el 23: nada de esto está en `https://10.252.252.200:5024` hasta la publicación autorizada y el reinicio elevado del servicio (`MeditechnosysIntranet`), y la recompilación del backend es obligatoria para que la regla del servidor exista.

---

## 2026-11-23 — **Calendario / «+ Nuevo»**: quien tiene **una sola región** la ve escrita y **sin desplegable**; con dos o más, el desplegable vuelve

**Módulo:** `wwwroot/js/gestion.js` (`G.campoRegion`) más el CSS del campo bloqueado en `wwwroot/css/panel.css` y `wwwroot/css/intranet.css`. **Tipo:** frontend.
**Base de datos:** ninguna. **API:** ninguna —`/api/permisos/mios` ya mandaba `regiones`, `region` y `veTodasRegiones`—. **Permisos:** ninguno.

**Por qué:** la captura del diálogo «+ Nuevo» con su lista de Región abierta mostrando «Todas las regiones», «CMN LA RAZA» y debajo «Seleccione hospital», con el pedido «al solo tener asignada una región este usuario, debe aparecerle por default solo la región a la que se le asignó, sin que pueda desplegar la lista, a menos que tenga más de una región». Para una cuenta con una única región el desplegable no ofrecía nada útil: «Todas las regiones» lo único que hacía era llevarlo a un hospital vacío (los hospitales que le manda el servidor son los de su región).

**Qué cambió:**

1. **Una sola región = campo fijo.** `G.campoRegion` calcula `var unaSola = !G.VE_TODAS_REGIONES && mias.length === 1;` y con eso hace dos cosas: el desplegable se queda **con una única opción** (su región, sin «Todas las regiones» delante) y sale **`disabled`**, además de escribirse con esa región como valor inicial si el formulario no traía ninguna. No es solo cosmético: un `<select disabled>` no se abre ni con clic ni con teclado.
2. **Con dos o más regiones el campo se comporta como siempre** —«Todas las regiones» arriba, solo sus regiones debajo, y cambiarla filtra el Hospital (`ajustarHospital`)—, y **el Administrador / root / quien no tiene región asignada siguen con la lista abierta**: `VE_TODAS_REGIONES` **no** bloquea. Aquí estaba el defecto de la versión anterior, que bloqueaba también al que maneja varias regiones.
3. **El campo bloqueado se ve como un valor, no como una lista.** `panel.css` (`dialog.modal .modal-form select:disabled`) e `intranet.css` (`.modal-form select:disabled`) quitan la flecha nativa (`appearance: none; background-image: none`) y el cursor de puntero. Sin eso la caja conservaba su flechita ▾ y parecía clickable aunque estuviera deshabilitada.
4. **Lo que NO cambió:** la Región **no viaja ni se guarda** en el elemento (sigue siendo solo un filtro del desplegable de hospital, `POST /api/actividades` recibe lo de siempre); el alta del Ingeniero sigue en sus dos pasos con la ventana de evidencia; el aviso «la región «X» todavía no tiene hospitales» sigue activo; y en Usuarios la asignación de región se hace igual.
5. **Marca de caché:** `gestion.js?v=20261120f → 20261123a` y `panel.css?v=20261120f → 20261123a`, en los **16 HTML** que los referencian. `intranet.css` se sirve con `asp-append-version`, así que su hash cambia solo. Ningún `.cshtml` se tocó, pero el binario de la arena había que recompilarlo: la DLL que corría **no mandaba `regiones`** en `/api/permisos/mios`, y sin ese campo el frontend no puede saber cuántas regiones tiene la cuenta.

**Verificación:** `dotnet build -c Release` sin advertencias ni errores. En Chrome real por el proxy de la arena (5097 → 5099), con el catálogo desechable `CMN LA RAZA → HES, HGR` y `UMAE → HGO, HI`: **`tec1` con una sola región** —`guiones/guion-r-region-unica.js` reporta `region {opciones:[{v:"CMN LA RAZA",t:"CMN LA RAZA"}], disabled:true, value:"CMN LA RAZA"}` y Hospital con `Seleccione hospital | HES | HGR`—; **`tec1` con dos regiones** —`opciones: [Todas las regiones, CMN LA RAZA, UMAE]`, `disabled:false`—; **`jefe` (Administrador, ve todas)** —las tres opciones y `disabled:false`, Hospital con los cuatro hospitales—. El alta sigue funcionando con el campo bloqueado: `guiones/guion-r3-crear.js` crea un Incidente como `tec1` y devuelve `regionBloqueada:true, regionValor:"CMN LA RAZA", ventanaCerro:true, apareceEnLista:true`. Capturas: `cap-region-unica.png` (Región fija, sin flecha), `cap-region-dos.png` y `cap-region-admin.png` (desplegable con su flecha). La prueba escribió en la arena y se limpió: el registro desechable salió de `actividades.json`, la config de la arena volvió a su estado sin claves de región y el servidor de la arena quedó detenido.

**Despliegue:** pendiente, como todo lo del 20 y el 23 de noviembre: nada está en `https://10.252.252.200:5024` hasta la publicación autorizada y el reinicio elevado del servicio. Mientras no se publique, el sitio en vivo sigue sirviendo el `gestion.js` viejo —que es precisamente la razón por la que la captura del usuario aún muestra el desplegable—.

---

## 2026-11-20 — **Correo / Bandeja**: sin segundo correo enlazado la bandeja muestra **una sola pestaña**

**Módulo:** `wwwroot/admin/correos/index.html` (`pintarCuentas()` y sus textos) con su parte de servidor en `Models/Correo.cs` y `Services/Correo.cs` (`CuentaBuzon` / `Cuentas()`). **Tipo:** frontend + backend.
**Base de datos:** ninguna. **API:** `GET /api/correos/mio` — cada cuenta de `cuentas` ahora trae además `enlazado`. **Permisos:** ninguno.

**Por qué:** la captura de la bandeja con sus dos tarjetas —*BUZÓN DE SOPORTE* y *SEGUNDO CORREO* punteada con «Sin enlazar · lo conecta el administrador»— y el pedido «cuando no se tenga un correo enlazado que solo muestre una pestaña». La segunda tarjeta era un botón que no abría nada: ocupaba su sitio solo para decir que faltaba.

**Qué cambió:**

1. **La pestaña del segundo correo ya no se pinta cuando no hay enlace.** `pintarCuentas()` filtra con `if (!c.esSoporte && c.enlazado === false) return;`, así que sin buzón conectado la bandeja queda con **una única pestaña** (la de soporte, activa). El soporte se sigue pintando **siempre**, incluso sin configurar, para que la pantalla nunca se quede sin pestañas ni con un botón muerto.
2. **El servidor ahora distingue "está enlazado" de "se puede abrir".** `CuentaBuzon` ganó el campo `Enlazado` (`b is not null`) y `Disponible` deja de mezclar las dos cosas. Eso importa porque **un enlace roto sí conserva su pestaña** (punteada y con el motivo): ahí hay algo que arreglar, y ocultarla dejaría al usuario sin aviso. La comparación es `=== false` a propósito: si el navegador hablara con un servidor viejo que no manda el campo, la pestaña vuelve a salir como antes en vez de desaparecer.
3. **Textos que decían lo contrario.** El rótulo del chip punteado pasó de «Sin enlazar · lo conecta el administrador» a **«Enlazado pero no abre · lo arregla el administrador»**; la dirección, si el enlace viene incompleto, dice «Enlace incompleto» en lugar de «Sin enlazar»; el aviso de la lista cambió a **«El segundo correo no se puede abrir»** (ya no puede decir «todavía no está conectado», porque a esa pantalla solo se llega habiendo enlace); y la nota del editor perdió el «de las dos»: ahora «Ninguna cuenta se puede usar todavía…».
4. **Lo que NO cambió:** el selector **«Enviar con»** del editor sigue armándose con `disponibles()` (solo las cuentas que abren), la elección de bandeja se recuerda en `sessionStorage`, el enlace lo pone **solo** el administrador en `/admin/usuarios/`, y la lectura por carpetas sigue con `cuenta=soporte|propio`.
5. **Marca de caché:** no sube —el cambio vive en el HTML propio de la bandeja—, pero **el backend sí hay que recompilarlo** por el campo nuevo `enlazado`; mientras el servidor no tenga la nueva DLL, la bandeja muestra las dos pestañas como antes (comportamiento viejo, no roto).

**Verificación:** `dotnet build -c Release` sin advertencias ni errores. En Chrome real por el proxy de la arena (5097 → 5099), con la cuenta `tec1` y su buzón desechable (`buzon-falso.json`, `segundo.arena@ejemplo.mx`), los tres estados salen así: **sin enlazar** —`guiones/guion-q-una-pestana.js` reporta `chips` de 1 elemento `{cabeza:"Buzón de soporte", correo:"soporte@meditechnosys.mx", punteada:false, activa:true}`, `diceSinEnlazar:false` y `enviarCon` con una sola opción—; **enlazado y funcionando** —`chips` de 2, el segundo con `correo:"segundo.arena@ejemplo.mx"` y `punteada:false`, `enviarCon: ["Soporte · …","Segundo correo · …"]`—; **enlazado pero roto** (contraseña vaciada a mano en el `buzon.json` de la arena) —`guiones/guion-q-enlace-roto.js` da el segundo chip con `estado:"Enlazado pero no abre · lo arregla el administrador", punteada:true`, y al hacerle clic `aviso:"El segundo correo no se puede abrir…"` con el motivo del servidor, `tablaOculta:true` y `enviarCon` otra vez con solo soporte; al regresar a soporte con clic real, `avisoOcultoAlVolver:true` y `chipActiva:"Buzón de soporte"`. En los tres casos el editor de redacción se abrió y **se cerró con clic real** (`editorAbierto/editorCerrado: true`). Capturas: `cap-bandeja-una-pestana.png`, `cap-bandeja-dos-pestanas.png` y `cap-bandeja-enlace-roto.png`. (Dos ajustes del arnés: la página reescribe `document.title` al recargar la bandeja, así que el informe ahora se imprime también en un `<pre id="informeGuion">`; y `espera()` devuelve el **valor de la condición** —un booleano—, no el elemento, de ahí que el guion tuviera que leer el aviso fuera del lazo.) El buzón desechable se desvinculó al final (`PUT /api/usuarios/tec1/buzon` con `limpiar: true` → 200) y el almacén de la arena quedó en cero enlaces.

**Despliegue:** pendiente, junto con lo acumulado del día (recordatorio naranja, permiso del historial, ancho del calendario y los tres ajustes del panel de pendientes): nada de esto está en `https://10.252.252.200:5024` hasta la publicación autorizada y el reinicio elevado del servicio.

---

## 2026-11-20 — **Gestión / Calendario**: el panel «Pendientes por ingeniero» cambia la pestaña **«Reportes» por «Incidentes»**, sus renglones **se quitan las etiquetas redundantes** y el **nombre del responsable solo lo ve el Administrador**

**Módulo:** `wwwroot/calendario/index.html` (markup de `#filtrosUsuarios`, el texto de ayuda y `pintarUsuarios()`). **Tipo:** frontend.
**Base de datos:** ninguna. **API:** ninguna (los datos ya venían en el mismo `GET /api/actividades`). **Permisos:** ninguno.

**Por qué:** tres pedidos seguidos sobre el mismo panel. Primero, con la captura de las pestañas *Actividades | Reportes*: «quita la pestaña de reportes y pon incidentes». Después, con dos capturas que enmarcaban las etiquetas `ACTIVIDAD` de los renglones y la `INCIDENTE` de la pestaña de incidentes: «quita las palabras redundantes». Y por último, con la captura de la cabecera `Ingeniero` con su contador: «el nombre para un usuario que no es administrador, no tiene caso que lo vea».

**Qué cambió:**

1. **La pestaña «Reportes» se retiró sin sustituto.** Ya no existe el botón `data-fu="reportes"` ni el cubo de reportes dentro de `pintarUsuarios()`: se eliminaron su clave de ordenamiento, el renglón «Reporte … tocaba el …» y su mensaje de vacío propio. Los reportes de estatus siguen visibles donde siempre vivieron: la pestaña **«Reportes de estatus»** de la tarjeta del elemento y la pantalla `/admin/reportes/`.
2. **En su lugar entró «Incidentes».** Las dos pestañas de `#filtrosUsuarios` ahora separan **por tipo**: `filtroUsuarios === 'incidentes'` pinta solo `tipo === 'Incidente'` y cualquier otro valor pinta solo `tipo === 'Actividad'`. Antes «Actividades» mezclaba actividades **e** incidentes, así que el reparto nuevo también ordena la pestaña que ya existía.
3. **Los tickets siguen fuera de este panel** (viven en el panel izquierdo), y el orden dentro de cada pestaña no cambia: primero lo vencido y después la fecha límite. El texto de ayuda del panel se reescribió para describir las dos pestañas por tipo y recordar que los tickets están a la izquierda.
4. **Y salieron las etiquetas repetidas de cada renglón** («quita las palabras redundantes»). `.u-meta` ya no arma `Gestion.badgeTipo(a.tipo)` —la pestaña activa ya dice si son actividades o incidentes— ni `Gestion.badgeEstado(a)` —en este panel **todo** lo pintado está `Pendiente`, porque el filtro es `estadoAbierto()`—, así que las dos palabras desaparecieron del panel. **Se conservó únicamente la insignia `Vencida`**, que sí informa: se pinta solo cuando `a.vencida` y va acompañada del borde rojo del renglón (`.u-item.urgente`). El renglón queda en título + «Límite dd/mm/aaaa · HOSPITAL» o «Sin límite · HOSPITAL». Las insignias del panel de tickets (izquierda) y de la tarjeta de detalle **no se tocaron**.
5. **Y la cabecera con el nombre del responsable solo aparece para el Administrador** («el nombre para un usuario que no es administrador, no tiene caso que lo vea»). `pintarUsuarios()` calcula `muestraNombres = Gestion.tiene('tickets.asignar')` —el mismo divisor que separa al que ve la carga de varios del que solo ve la suya— y **omite el `<div class="u-cab">`** (nombre + contador) cuando no lo tiene: al Ingeniero le quedan los renglones directos, porque el servidor ya filtra por su cuenta y el nombre siempre sería el suyo. El Administrador conserva las tarjetas agrupadas con su nombre y su contador. Se ajustó además el texto de ayuda del panel: decía «agrupado por responsable», que ahora solo aplica al Admin, y pasó a hablar de las dos pestañas **por tipo**. La tarjeta de detalle **no cambió**: ahí el responsable se sigue mostrando (es justamente lo que el Admin edita).
6. **Marca de caché:** no sube. El cambio vive en el HTML propio de la página (markup, `<style>` y guion interno de `calendario/index.html`), no en `gestion.js`/`panel.css`, así que basta con recargar la pantalla (Ctrl+F5).

**Verificación:** sin cambios de backend, `dotnet build -c Release` sin advertencias ni errores. En Chrome real por el proxy de la arena (5097 → 5099), el guion `guiones/guion-n-pestana.js` sobre `/calendario/index.html?x-perfil=jefe` reporta `pestanas "Actividades | Incidentes"`, `activa "Actividades"`, `enActividades {conteo: 3, tipos: {Actividad: 3}, diceReporte: false}`, `enIncidentes {conteo: 1, tipos: {Incidente: 1}, renglones: ["Incidente abierto viejo | Incidente Pendiente Sin límite · HES"]}` y `volvioAActividades {conteo: 3}`; el guion fotográfico `guiones/guion-n-foto.js` deja la pestaña **Incidentes** activa en `cap-pestana-incidentes.png`. (Dos ajustes del arnés: el guion debía esperar a que apareciera `.usuario-card` o `.usuarios-vacio` —leer antes del `GET /api/actividades` daba `conteo: 0`— y, bajo `--virtual-time-budget`, el clic hay que mantenerlo vivo con un lazo de `setTimeout` porque la cola de microtareas se vacía y la captura se dispara antes.)

De la limpieza de etiquetas, `guiones/guion-o-sin-insignias.js` en Chrome real (misma arena) reporta en **Actividades** `{renglones: 3, insigniasTipo: 0, insigniasEstado: 0, dicePendiente: false, diceActividad: false, diceIncidente: false}` con renglones del tipo `"LEGADO EnProceso >> Sin límite · HES"`, y en **Incidentes** `{renglones: 1, insigniasTipo: 0, insigniasEstado: 0}`; `ticketsTienenTipo: true` confirma que el panel izquierdo conservó sus insignias. Para probar la única etiqueta que quedó se creó **en la arena** un incidente desechable con `fechaLimite` atrasada (`POST /api/actividades` → id 11, `vencida: true`) y el mismo guion pasó a `{renglones: 2, insigniasEstado: 1, diceVencida: 1, renglonesUrgentes: 1}` con `"PRUEBA VENCIDA (arena) >> Vencida Límite 10/11/2026 · HES"`; después se borró con `DELETE /api/actividades/11` (204) y el almacén de la arena volvió a 10 registros. Capturas: `cap-panel-sin-insignias.png` (Actividades), `cap-panel-incidente-vencida.png` (Incidentes, con el renglón vencido y su etiqueta roja) y las de página completa `cap-sin-insignias-actividades.png` / `cap-sin-insignias-incidentes.png`; los guiones `guion-o-recorte-act.js` / `guion-o-recorte-inc.js` son las fotos recortadas al panel (ocultan `.panel-tickets` y `.cal-scroll`).

Del nombre del responsable, `guiones/guion-p-sin-nombre.js` se corrió **en los dos perfiles** de la arena: `tec1` (Ingeniero) da `{puedeAsignar: false, cabeceras: [], tarjetas: 1, renglones: 3}` —cero `.u-cab`, y el texto del panel arranca directo en el primer título—, y `jefe` (Administrador) da `{puedeAsignar: true, cabeceras: ["tec13"], tarjetas: 1, renglones: 3}` —el `13` es el nombre `tec1` con su contador `3` pegados por el `textContent` de la cabecera, que sigue pintándose. En ambos `tarjetaTieneResponsable: true`, es decir la tarjeta de detalle sigue nombrando al asignado. Capturas: `cap-pendientes-ingeniero-sin-nombre.png` (sin cabecera) y `cap-pendientes-admin-con-nombre.png` (con `tec1` y su contador).

**Despliegue:** pendiente, junto con lo del mismo día (recordatorio naranja, permiso del historial y el ancho del calendario): nada de esto está en `https://10.252.252.200:5024` hasta la publicación autorizada y el reinicio elevado del servicio.

---

## 2026-11-20 — **Gestión / Calendario**: la pestaña **«Historial de cambios» queda gobernada por el permiso** `tickets.historialCambios`, **la página del calendario se amplía** y un **título largo ya no ensancha la celda**

**Módulo:** `Models/Permisos.cs`, `Services/Gestion.cs`, `wwwroot/js/gestion.js`, `wwwroot/css/panel.css` y `wwwroot/calendario/index.html`. **Tipo:** backend + API + frontend + permisos.
**Base de datos:** ninguna columna nueva ni migración (el historial ya vivía en `Historial.cs`). **Configuración:** ninguna.

**Por qué:** el pedido, sobre la captura de la pestaña, fue «solo deja reporte de estatus, el ingeniero no tiene por qué ver el de cambios a menos que se tenga el permiso, también amplía la tarjeta del calendario, ya que tienes bastante espacio de manera horizontal, también que el título no amplíe la celda del calendario».

**Qué cambió:**

1. **El permiso por fin manda.** La clave `tickets.historialCambios` (*Tickets · administración*) **existía en el catálogo pero no la consultaba nadie**, y estaba en los valores por defecto del Ingeniero, así que la pestaña salía para todo el mundo. Ahora el servidor la exige: `GestionService.HistorialDe` devuelve **lista vacía** si la sesión no tiene el permiso, **aunque sea la dueña** del elemento, y el frontend (`gestion.js`) ni siquiera pinta la pestaña «Historial de cambios» ni hace el `GET /api/actividades/{id}/historial`. La tarjeta del Ingeniero se queda solo con **«Reportes de estatus»**.
2. **Catálogo v15 y el retro-fit.** `CatalogoPermisos.Version` sube **14 → 15** (siguen **46 claves en 13 grupos**) y la clave pasa a `Desde: 15`; se retira de `IngenieroPorDefecto`. El `Desde: 15` no es un adorno: `PermissionStoreBase.Completar` rellena las matrices guardadas con las claves cuyo `Desde` es **mayor que la versión guardada** y que están en el defecto del rol. Con `Desde: 1` la matriz del Administrador ya guardada en producción **nunca** recibiría la clave y el Admin perdería la pestaña al actualizar; con `Desde: 15` la recibe solo. **Riesgo conocido:** un rol propio guardado antes de este cambio tampoco la recibe —si alguien más necesita el historial, se le concede la clave en **Roles y permisos**—.
3. **Lo que había que ampliar era EL CALENDARIO, no la ventana del detalle** (aclaración del mismo día, sobre la captura de la rejilla con barra horizontal y solo cinco días visibles): `panel.css` topa `main` y `.card` en **1200 px**, así que la rejilla semanal nunca tuvo el sitio suficiente y `.cal-scroll` sacaba la barra. La hoja de estilos propia de `calendario/index.html` levanta ese tope **solo en esta página** —`main { max-width: min(1780px, 96vw); }` y `main > .card { max-width: none; }`— y el `min-width` de `.semana`/`.mes-grid` baja de **700 px a 560 px** (80 px por día), que es lo que hace falta para que **los siete días quepan sin barra horizontal** incluso en un monitor de 1366. La ventana del detalle del elemento **sí** se aprovechó del ancho nuevo: `gestion.js` la marca `modal ancho detalle` y `panel.css` estrena `dialog.modal.ancho.detalle { max-width: min(1180px, 94vw); }`, de modo que solo la tarjeta del elemento llega a ~1180 px y los demás diálogos anchos (alta con evidencia, edición) siguen en 660 px; con ese sitio `.meta-grid` (`auto-fill, minmax(170px,1fr)`) reparte los metadatos en **6 columnas** y los botones de acción caben en una sola fila.
4. **El título ya no estira la rejilla.** `.semana` y `.mes-grid` pasan de `repeat(7, 1fr)` a **`repeat(7, minmax(0, 1fr))`**: la columna deja de crecer por lo que contiene (el mínimo por contenido era lo que hacía que un título sin espacios cortables empujara su celda —medido: una `.dia` de **536 px** frente a los 55 px mínimos del resto—). Se añade la cadena `min-width: 0` en `.dia`, `.chip` y `.chip .txt` para que el recorte con puntos suspensivos del chip entre en acción.
5. **Marca de caché.** Las 21 pantallas que cargan esas hojas —entre ellas el propio `calendario/index.html`— suben el sello `?v=20261120e → ?v=20261120f` en `gestion.js`, `panel.css` y `theme.css`. La segunda pasada (la página ancha) solo tocó el `<style>` interno de `calendario/index.html`, que viaja en el propio HTML, así que el sello no vuelve a subir: basta con recargar la página.

**Verificación:** `dotnet build -c Release` sin advertencias ni errores. Con el proxy de la arena `%TEMP%\estados-arena` (5097 → 5099) y Chrome real, guion `guiones/guion-k-tres.js` en los dos perfiles: jefe → `pestanas "Reportes de estatus | Historial de cambios"`; tec1 → `pestanas "Reportes de estatus"` con **cero peticiones** a `/historial`. Ambos: `tarjetaAncho = 1180`, `metaColumnas = 6`, `columnasIguales = true`, `anchoMin = anchoMax = 100`, `recortadoConPuntos = true`. `curl` sobre la arena: `GET /api/actividades/10/historial` → `200 []` para el Ingeniero y una fila para el Administrador; `/api/permisos/mios` confirma el retro-fit (jefe `true`, tec1 `false`). Capturas `cap-tarjeta-ampliada-admin.png` y `cap-tarjeta-ingeniero-sin-historial.png`.
Después de la aclaración («esa parte que ampliaste no era, debes ampliar lo de la imagen»), el guion `guiones/guion-m-calendario.js` mide **la página del calendario** en Chrome real y en dos tamaños: a 1984 px → `mainAncho = cardAncho = 1780`, `contenedorRejilla = 1114`, `rejillaReal = 1112`, **`desborda = false`**, `diasDeCabecera = 7` con `cabenTodos = true`, siete columnas de `159` px iguales y el título largo `recortadoConPuntos = true`; a 1350 px → `mainAncho = 1296`, `contenedorRejilla = 630`, `desborda = false`, `cabenTodos = true`, columnas de `90` px. Captura `cap-calendario-anchos.png`: los siete días visibles y **sin barra horizontal**.

**Despliegue:** pendiente — hace falta la compilación/publicación completa (cambia C#) y reiniciar el servicio con el clic «Ejecutar como administrador». Sin eso, en `https://10.252.252.200:5024` el Ingeniero seguirá viendo la pestaña, el calendario seguirá con la barra horizontal y la tarjeta del elemento seguirá en 660 px.

---

## 2026-11-20 — **Gestión / Calendario**: el recordatorio de los 3 días pasa a **naranja** y su texto queda en **«Favor de registrar avances en comentarios.»**

**Módulo:** `wwwroot/js/gestion.js`, `wwwroot/css/panel.css`, `wwwroot/css/theme.css` y `wwwroot/calendario/index.html` + las 21 pantallas que cargan esas hojas. **Tipo:** frontend.
**Base de datos:** ninguna. **API:** ninguna (sigue siendo `leTocaReporte` del servidor lo que lo enciende). **Permisos:** ninguno.

**Por qué:** el pedido, sobre la captura del recuadro dorado, fue «aqui cambialo a, favor de registrar avances en comentarios y coloca un color naranja». El aviso heredaba el dorado/ámbar del **reporte de estatus**, que vive en la misma tarjeta, y los dos avisos se confundían.

**Qué cambió:**

1. **Aviso propio.** El recordatorio deja de pintarse con `.aviso-reporte` y usa la clase nueva **`.aviso-avance`** en `gestion.js`, con el texto exacto **«Favor de registrar avances en comentarios.»** (ya no dice «Toca informar cómo va: … Escríbelo con el botón "Agregar comentario"»). `.aviso-reporte` queda solo para el reporte de estatus solicitado.
2. **Naranja en los dos temas.** `panel.css` estrena las variables `--naranja: #f97316` / `--naranja-t: #ea580c` y la regla base `.aviso-avance` (borde naranja, crema `#fff7ed`, texto óxido `#9a3412`). `theme.css` define `--aviso-avance-bg` / `--aviso-avance-text` por tema —claro: el mismo crema y óxido; nocturno: `rgba(249,115,22,.16)` con texto `#ffb266`— y `.theme-panel .aviso-avance` se pinta con esas variables, de modo que el texto nunca queda más claro que el fondo.
3. **El calendario sigue el mismo color.** La marca `!` del chip (`.chip .marca`) y su cuadro en la leyenda pasan de dorado a `var(--naranja)`, la leyenda dice ahora «Favor de registrar avances en comentarios» y los `title` del chip y de la marca llevan la frase nueva.
4. **Marca de caché.** Las 21 pantallas suben el sello a `?v=20261120e` en `gestion.js`, `panel.css` y `theme.css`.

**Verificación:** arena `%TEMP%\estados-arena` (proxy 5097 → servidor 5099) con Chrome real, guiones `guion-h-naranja.js` (nocturno), `guion-h2-claro.js` (claro) y `guion-i-foto.js` (capturas). Lecturas: `textoAviso = "Favor de registrar avances en comentarios."`, `claseAviso = aviso-avance`; nocturno `rgb(255,178,102) / rgba(249,115,22,.16) / rgb(249,115,22)`, claro `rgb(154,52,18) / rgb(255,247,237) / rgb(249,115,22)` sobre tarjeta blanca; leyenda y marca del chip en `rgb(249,115,22)` en los dos temas. Capturas `cap-aviso-oscuro.png` y `cap-aviso-claro.png`.

**Despliegue:** pendiente — el cambio vive en el `wwwroot` del proyecto; para verlo en `https://10.252.252.200:5024` hace falta publicar (o copiar las tres hojas y el HTML del calendario) y reiniciar el servicio con el clic «Ejecutar como administrador».

---

## 2026-11-20 — **Gestión**: el ciclo de vida se recorta a **Pendiente · Completada · Cancelada**, lo cancelado se congela, el alta del Ingeniero nace **Completada** en un solo envío y el avance se vuelve **comentario con recordatorio de 3 días**

**Módulo:** `Models/Actividad.cs`, `Models/Permisos.cs`, `Services/Gestion.cs`, `Services/Historial.cs`,
`Services/Actividades.cs`, `Program.cs` y `wwwroot/js/gestion.js` + las pantallas del módulo. **Tipo:** backend + frontend + API + permisos.
**Base de datos:** ninguna columna nueva ni migración. **Configuración:** ninguna.
Sustituye, el mismo día, los puntos 1 y 3 de la entrada de abajo (el alta del Ingeniero en dos pasos con
elemento `Pendiente` intermedio).

**Por qué:** el pedido fue «solo debe estar el estado pendiente, completado y cancelado, el estado cancelado
solo admin o quien tenga ese permiso la puede cambiar a dicho estado, las canceladas ya no se pueden reabrir y
solo queda el registro, TAMBIEN DE LADO DEL INGENIERO DIRECTAMENTE AL CREAR UNA ACTIVIDAD DEBE ESTAR EN
COMPLETO, NO PUEDE QUEDAR PENDIENTE», y al preguntar por los botones que se caían la respuesta fue «EL
REGISTRAR AVANCE QUEDA EN COMENTARIOS Y SE DEBE RECORDAR CADA 3 DIAS». Cuatro estados y un porcentaje que
nadie movía hacían que la pantalla prometiera movimientos que el flujo real ya no usa.

**Qué cambió:**

1. **Tres estados, y los viejos se traducen al leerlos.** `EstadosActividad` queda en `Pendiente`,
   `Completada` y `Cancelada`; las constantes `EnProceso` y `Cerrada` se conservan **solo** para reconocer
   registros antiguos, y `ActivityStoreBase.Normalizar` los convierte al cargarlos (`EnProceso` → `Pendiente`,
   `Cerrada` → `Completada` con avance 100), de modo que ninguna pantalla ni ningún filtro vuelve a verlos.
   `Abierto()` ahora significa únicamente `Pendiente`. En el frontend, `G.ESTADOS` y `G.ORDEN_ESTADOS` tienen
   tres claves: la barra `.estatus` de la tarjeta pinta tres tramos, los chips del calendario tres colores y la
   leyenda añade el color del recordatorio de 3 días (naranja, con el texto de la entrada de arriba).
2. **`PUT /api/actividades/{id}/estado` solo admite dos destinos.** `Completada` (informe opcional + evidencia
   ya adjuntada) y `Cancelada` (justificación obligatoria). Cualquier otro valor —incluidos `EnProceso`,
   `Cerrada` y volver a `Pendiente`— responde **400**. Desaparecen el botón **Iniciar**, el botón **«Cerrar
   formalmente»** y el endpoint **`PUT /api/actividades/{id}/avance`** (hoy responde 404).
3. **Cancelar es un permiso, no un rol.** Nace la clave **`items.cancelar`** («Cancelar actividades, incidentes
   y tickets (lo cancelado ya no se mueve)», `Desde: 14`) y el catálogo sube a **`Version = 14`** con **46
   claves en 13 grupos**. Se retiran `items.registrarAvance` y `tickets.cerrar`; las matrices guardadas que aún
   las traen no las reviven porque al leerlas se filtran con `CatalogoPermisos.EsClaveValida`, y
   `NuevasDesde(14, rol)` le da `items.cancelar` al Admin en **true** y al Ingeniero en **false** sin tocar la
   base. El botón **Cancelar** (rojo, solo con el elemento abierto) pide **Justificación** (tope 500) y anota
   `AuditEvents.CancelarActividad`.
4. **Lo cancelado se congela: solo queda el registro.** `GestionService` rechaza con 400 editar, eliminar,
   reasignar, completar, reabrir, comentar, adjuntar evidencia y entregar reportes sobre una `Cancelada`
   («Una actividad cancelada ya no admite comentarios: solo queda el registro.»). En la tarjeta,
   `congelado = item.estado === 'Cancelada'` apaga todos los botones de acción y deja únicamente «Cerrar»; a
   cambio se muestran la **Justificación de la cancelación** y la **Fecha de cierre**, y las pestañas
   *Reportes* e *Historial* siguen vivas. `Reabrir` exige `Completada` (400 sobre abierta **o** cancelada) y el
   botón del frontend se pinta solo con `item.estado === 'Completada'`.
5. **El alta del Ingeniero nace Completada y no pasa por Pendiente.** El paso 1 de `G.abrirCrear` **ya no hace
   fetch**: con `cierraAlCrear && esActividad` arma `{ alta: true, tipo, hospital, titulo, descripcion, fecha }`,
   devuelve `Promise.resolve(null)` y abre `G.dialogoCierre` en un `setTimeout(…, 0)`. Esa ventana —titulada
   **«Registrar y completar actividad»**, con botón **«Crear y completar»**— es la que envía el `FormData`
   completo (`tipo`, `hospital`, `titulo`, `descripcion`, `fecha`, `informe`, `archivos[]`) a
   **`POST /api/actividades`**, que entra por `CrearYCerrada`: crea, adjunta la evidencia y deja el elemento
   `Completada` en la misma petición (201), con `DeshacerAlta` revirtiendo todo si la evidencia se rechaza. Si
   el usuario cierra la ventana sin enviar, **no quedó nada en el servidor**. Del lado del servidor, `Crear`
   recupera el parámetro `exigirCierre` y **rechaza con 400 el JSON de una Actividad** cuando quien la manda no
   tiene `tickets.asignar`, así que no hay forma de que la actividad del Ingeniero nazca abierta. Los
   **incidentes** siguen naciendo `Pendiente` y el alta del **Admin** sigue siendo JSON.
6. **El avance se escribe en comentarios y se recuerda cada 3 días.** `Actividad` gana
   `DiasParaRecordar = 3`, `UltimoComentario { get; init; }` (**no** se persiste: lo decora
   `GestionService.Visibles`/`Find` con `IHistorialStore.UltimosComentarios()`, que cuenta las entradas de
   historial cuyo campo es `Comentario` u `Observación`) y
   `LeTocaReporte => Abierto(Estado) && (UltimoComentario ?? Fecha).Date.AddDays(3) <= DateTime.Today`. Cada
   comentario nuevo **reinicia** la cuenta; si no hay ninguno, se cuenta desde la fecha de inicio. La tarjeta
   sustituye la meta «Avance» (barra) por **«Último comentario»** y pinta el aviso del recordatorio (en esta
   entrada era dorado con `.aviso-reporte`; la entrada de arriba lo cambió ese mismo día a la clase naranja
   `.aviso-avance` con el texto «Favor de registrar avances en comentarios.»); el chip del calendario gana la
   marca `.marca` con un `!`. Los **reportes de estatus** sí
   conservan su porcentaje (`avanceHtml` y las columnas de `/admin/reportes/` y del Histórico no cambian), pero
   ya no mueven el estatus del elemento.
7. **Dos arreglos que salieron al verificar en Chrome real.** (a) Los diálogos **Observación** y **Cancelar**
   guardaban bien pero no refrescaban la tarjeta —el usuario veía la pantalla de antes y parecía que no había
   pasado nada—; ahora los dos terminan en `.then(refresco)`. (b) `ActividadRules.TextoError` armaba el aviso
   con género fijo («El justificación es obligatorio»); ahora entrecomilla el nombre del campo —«Falta el campo
   «justificación».», «El campo «comentario» debe tener al menos 5 caracteres.»— y sirve igual con palabras
   femeninas y masculinas.

**Archivos modificados:** `Models/Actividad.cs`, `Models/Catalog.cs`, `Models/Permisos.cs`,
`Services/Gestion.cs`, `Services/Historial.cs`, `Services/Actividades.cs`, `Program.cs`,
`wwwroot/js/gestion.js`, `wwwroot/css/panel.css`, `wwwroot/calendario/index.html`,
`wwwroot/gestion/historico/index.html` y las **12** pantallas que cargan `gestion.js` con la marca de caché
**`?v=20261120d`** (las 21 que cargan `panel.css`/`theme.css` siguen en `?v=20261120c`), además de
`README.md`, `docs/ARQUITECTURA.md`, `docs/FRONTEND.md` y `docs/USUARIOS-Y-MYSQL.md`.

**Verificación** (arena nueva en `%TEMP%\estados-arena`: servidor en `http://127.0.0.1:5099` con
`--Auth:Store json` y `--Https:Activo false` —sin eso intenta abrir el 5024 de producción—, cuentas
desechables `jefe`/`tec1` con **catálogo guardado en versión 13** para probar el retro-ajuste, elementos
sembrados en los cuatro estados —dos de ellos `EnProceso` y `Cerrada`— y el proxy `proxy.js` en 5097 para el
Chrome real; **la producción de 5024 no se tocó**):

- **`chequea-estados.js`: 73 correctas, 0 fallidas** (corridas dos veces, también después del arreglo del
  mensaje). Cubren: el retro-ajuste v13 → v14 (Admin recibe `items.cancelar` en true, Ingeniero en false y las
  claves retiradas no reviven); la traducción de `EnProceso`/`Cerrada` al leer; `leTocaReporte` y
  `ultimoComentario` (sin comentario cuenta desde la fecha de inicio, un comentario nuevo reinicia, sobre
  completada o cancelada no se marca); **400** al JSON de Actividad del Ingeniero sin guardar nada; multipart →
  **201 `Completada`** con informe opcional y evidencia obligatoria, con reversión si falta; `PUT /avance` →
  **404** y `PUT /estado` con `EnProceso`/`Cerrada`/`Pendiente` → **400**; cancelar sin `items.cancelar` →
  **403**, sin justificación → **400**, con justificación → **200**; la cancelada de solo lectura en
  observaciones, completar, reabrir, editar, asignar, eliminar y reportes (todos **400**) con el historial
  todavía visible; reabrir sobre `Completada` → **200 Pendiente** con la evidencia intacta y sobre `Cancelada`
  → **400**; y el alta JSON del Admin intacta.
- **Chrome real (headless, cuatro lecturas de pantalla):** la tarjeta del **Ingeniero** con tres tramos
  (Pendiente · Completada · Cancelada), el aviso del recordatorio de 3 días (entonces dorado; hoy naranja,
  ver la entrada de arriba), la meta **Último
  comentario** y los botones *Completar* / *Agregar comentario* / *Cerrar*; la tarjeta del **Admin** sobre una
  `Pendiente`, que añade *Cancelar*; la **cancelada congelada**, con su caja de justificación, su *Fecha de
  cierre* y únicamente *Cerrar* (más el chip rojo tachado y sin la marca del recordatorio); la ventana
  **«Registrar y completar actividad»** con el botón **«Crear y completar»**; y el **Histórico** con su resumen
  en Pendientes / Completadas / Canceladas y los renglones antiguos traducidos.
- **Retroceder en la ventana de la evidencia no registra nada** (`guion-g-cancelar-alta.js`, misma arena): con el
  botón **Cancelar** y con la tecla **Escape**, el alta del Ingeniero termina con **cero peticiones `POST`** y
  **cero elementos nuevos** en `/api/actividades`. Los elementos `Pendiente` sin evidencia que se vean son
  sobrantes del flujo antiguo de dos pasos, que creaba el elemento al pulsar *Crear* antes de pedir la
  evidencia; ese flujo ya no existe ni en el navegador ni en el servidor.
- `dotnet build -c Release` sin avisos nuevos. Los puertos 5099 y 5097 se liberaron al terminar.

**Despliegue:** **pendiente**, como el resto de los cambios acumulados —falta el `dotnet publish` autorizado,
la comparación del staging y el clic «Ejecutar como administrador» del usuario. Mientras tanto la producción de
`https://10.252.252.200:5024` sigue con el comportamiento anterior.

---


**Módulo:** `Services/Gestion.cs` y `wwwroot/js/gestion.js`. **Tipo:** backend + frontend.
**Base de datos / permisos / configuración:** ninguno. Sustituye, el mismo día, lo que la entrada de abajo
dejó para el alta del Ingeniero (su punto 3 y su punto 4): el envío único con informe obligatorio.

**Por qué:** el pedido fue «en el perfil del ing, quita la descripcion y despues de crear que arroje el
apartado de Informe del trabajo realizado (opcional)+evidencia (obligatorio)». La ventana de alta quedaba
larga y mezclaba dos momentos —decir qué se va a hacer y reportar que ya se hizo—; ahora el primer paso es
corto (Tipo, Hospital, Servicio, Actividad) y el reporte vive en su propia ventana, que se abre sola justo
después de crear.

**Qué cambió:**

1. **El servidor vuelve a aceptar el JSON del Ingeniero.** `Crear` pierde el parámetro `exigirCierre` y el
   400 que bloqueaba la Actividad de quien no tiene `tickets.asignar` (punto 3 de la entrada anterior): el
   Ingeniero crea por JSON y el elemento nace **Pendiente**, asignado a sí mismo, con prioridad Media y sin
   fecha límite. `CrearYCerrada` —el multipart atómico— ahora llama a ese mismo `Crear` y después adjunta y
   cierra, así que el camino heredado se conserva idéntico para los navegadores que aún tengan la página
   anterior en caché, y las reglas de permisos y de hospital viven en un solo lugar.
2. **El informe pasa a opcional en el cierre.** `CambiarEstado` hacia `Completada` ya no exige mínimo de
   caracteres: solo vigila el tope de 2000 y que **haya evidencia** (`evidencias.PorItem(id).Count == 0` →
   400 «Adjunta la evidencia…»). El `FormData` de la ventana manda `informe` vacío cuando no se escribe nada
   y el elemento queda con `informe: ""`.
3. **`+ Nuevo` del Ingeniero sin Descripción.** `G.abrirCrear` ya no empuja el textarea de Descripción cuando
   `cierraAlCrear` (el del Administrador se conserva, con prioridad, fechas e ingeniero), y tampoco informe
   ni archivos: el botón vuelve a decir **«Crear»** y el envío es JSON. El texto de arriba avisa del segundo
   paso: «Al guardar se abre enseguida la ventana de la evidencia: el informe es opcional y la evidencia
   (foto o archivo) es obligatoria. El Administrador es el único que puede reabrirlo y ponerle fecha
   límite.» Con **Incidente** no hay segundo paso: el incidente sigue naciendo Pendiente como siempre.
4. **La segunda ventana, `G.dialogoCierre(item, despues)`.** Helper nuevo a nivel de módulo: un `formDialog`
   **ancho** con *Informe del trabajo realizado (opcional)* (`textarea`, `maxlength: 2000`) y *Evidencia (foto
   o archivo)* (`file`, `required`, `multiple`, miniaturas), botón verde **«Marcar como completada»** y
   `FormData` hacia `POST /api/actividades/{id}/completar`. La abre el `.then` del POST de *+ Nuevo* sobre el
   elemento recién creado y también el botón *Completar* de la tarjeta (`dialogoCompletar`), de modo que el
   cierre es **una sola ventana en los dos caminos**. La bandera `listo` + `fin()` hacen que el refresco de
   `despues` se dispare una sola vez, porque `onClose` llega también después del envío correcto.

**Archivos modificados:** `Services/Gestion.cs`, `wwwroot/js/gestion.js` y las **16** pantallas
`wwwroot/**/index.html` + `almacen/hoja.html` con la marca de caché `?v=20261120b`, además de `README.md`,
`docs/FRONTEND.md` y `docs/ARQUITECTURA.md`.

**Verificación** (arena nueva en `%TEMP%\cierre2-arena`: servidor en `http://127.0.0.1:5099` con
`--Auth:Store json` y `--Https:Activo false` —sin eso intenta abrir el 5024 de producción—, cuentas
desechables `jefe`/`tec1` y el proxy `proxy-cierre.js` en 5097 para el Chrome real; **la producción de 5024
no se tocó**):

- **`chequea-cierre2.js`: 34/34 correctas.** El JSON del Ingeniero → **201 Pendiente**, asignado a sí mismo,
  Media, sin fecha límite y sin descripción obligatoria, y nace sin evidencia. `/completar` sin archivo →
  **400**; con archivo e **informe vacío** → **200 Completada** con `informe:""` y 1 evidencia; con informe →
  se guarda tal cual. El JSON del Admin respeta prioridad, fecha límite, ingeniero y descripción. El
  multipart heredado → 201 Completada y sin archivo 400. El incidente del Ingeniero → 201 Pendiente.
  `/reabrir`: **403** Ingeniero, **200** Admin → Pendiente con avance 0 y **la evidencia intacta**; volver a
  completar suma la segunda evidencia (quedan 2).
- **Clics reales en Chrome por el proxy** (guion `guion-dos-pasos-ing.js` sobre `/calendario/`): el `+ Nuevo`
  del Ingeniero muestra **solo Tipo, Hospital, Servicio y Actividad** —sin Descripción, sin informe, sin
  evidencia— y el botón dice **«Crear»**; al crear se abre sola **«Completar actividad»** con *Informe del
  trabajo realizado (opcional)* y *Evidencia (foto o archivo) (obligatorio)*; sin archivo avisa en la misma
  ventana («Adjunta evidencia… antes de continuar»); con la miniatura pintada y el envío, el elemento queda
  **Completada**, `informe:""`, 1 evidencia (`trabajo-realizado.png`), sin fecha límite y sin ventanas
  abiertas al final. Con el **Administrador** el `+ Nuevo` conserva **Descripción**, Prioridad, Fecha de
  inicio, Fecha límite e Ingeniero responsable (guion `guion-admin-campos.js`).
- **Dos capturas leídas:** `pantalla-crear-ingeniero.png` —el `+ Nuevo` corto del Ingeniero— y
  `pantalla-cierre-ingeniero.png` —la ventana de cierre con el informe escrito y la miniatura de la
  evidencia—.
- `dotnet build -c Release` limpio (0 advertencias) y `node --check wwwroot/js/gestion.js`.

**Despliegue:** pendiente, dentro del mismo paquete acumulado de `despliegue-regiones.cmd`; sigue vigente el
aviso «Pendiente de autorización del usuario» de la entrada de abajo (el staging hay que regenerarlo con el
binario de hoy antes del clic elevado).

---

## 2026-11-20 — **Gestión**: el Ingeniero registra la actividad **ya completada**, con su evidencia; solo el Administrador la **reabre**

**Módulo:** `Services/Gestion.cs` (con su constructor), `Program.cs`, `Models/Permisos.cs`, `Models/Audit.cs` y
`wwwroot/js/gestion.js`. **Tipo:** backend + API + permisos + frontend.
**Base de datos:** ninguna tabla nueva —la evidencia ya vivía en `evidencias`/`App_Data/evidencias.json`—; si
existen matrices de permisos guardadas, el arranque les agrega la clave nueva (véase el punto 2).

**Por qué:** el pedido fue «cuando el perfil de ingeniero asigna una actividad debe completarse en ese momento y
pedir la evidencia, solo el administrador debe poder reabrir la actividad y poner fecha limite». Antes el
Ingeniero daba de alta una actividad **Pendiente** y la cerraba después con el botón *Completar*; ahora el alta y
el cierre son **un solo envío**, y deshacer ese cierre queda del lado administrativo. Con las cuatro respuestas
que se pidieron para aterrizarlo: **solo las actividades** (los incidentes siguen naciendo pendientes),
**informe + evidencia, los dos obligatorios**, **Reabrir devuelve a «Pendiente»** y la evidencia **se queda, y se
le puede sumar más**.

**Qué cambió:**

1. **El alta que nace cerrada** — `GestionService.CrearYCerrada` crea el elemento, le adjunta la evidencia y lo
   pasa a **Completada** en la misma petición, y si algo falla después de crear **deshace el alta**
   (`DeshacerAlta`) en vez de dejar una actividad a medias. `POST /api/actividades` ahora acepta **dos cuerpos**:
   JSON (el camino de siempre, Admin e incidentes) y **multipart** (el cierre inmediato del Ingeniero, con los
   campos `tipo/hospital/titulo/descripcion/fecha/informe` y los archivos bajo `archivos`). La bifurcación es por
   `ctx.Request.HasFormContentType`; con el enlace escrito como `async (HttpContext ctx, …)` y el JSON leído a
   mano con `ReadFromJsonAsync`, porque el ligador automático de parámetros respondía **415** al llegar un
   formulario.
2. **El permiso `items.reabrir`** (`CatalogoPermisos.ReabrirItems`, «Reabrir actividades ya completadas o
   cerradas (las devuelve a Pendiente)») dentro del grupo *Tickets (administración)*, con
   `CatalogoPermisos.Version = 13` y su `Desde: 13`, de modo que una matriz del Administrador guardada antes de
   esta versión **recibe la clave sola** al arrancar y la del Ingeniero no. Su endpoint
   `POST /api/actividades/{id}/reabrir` exige el permiso, solo actúa sobre **Completada** o **Cerrada**, deja el
   elemento en **Pendiente** con `Avance = 0` y `Cerrado = null`, **no toca la evidencia ni el informe** y anota
   el motivo en el historial y «Actividad reabierta» (`AuditEvents.ReabrirActividad`) en la bitácora.
3. **El bloqueo por el lado del servidor**, para que no dependa del navegador: `Crear` ganó el parámetro
   `exigirCierre` y, cuando quien crea **no** tiene `tickets.asignar` y el tipo es **Actividad**, contesta 400
   «La actividad se registra ya completada, con su informe y su evidencia adjunta…». Así, un POST a mano (o un
   `gestion.js` viejo en caché) no puede colar una actividad pendiente del Ingeniero.
   *(Sustituido el mismo día por la entrada de arriba: el bloqueo se retiró y el JSON del Ingeniero vuelve a
   crear Pendiente; el multipart heredado conserva el cierre atómico.)*
4. **`+ Nuevo`** (`G.abrirCrear`) — para quien no hace la asignación formal y con el tipo **Actividad** — muestra
   ahora *Informe del trabajo realizado* (obligatorio) y *Evidencia (foto o archivo)* (obligatoria, varios
   archivos, la misma lista de extensiones que el botón *Completar*), cambia el botón a **«Crear y completar»**,
   se abre con el diálogo ancho y envía `FormData` por `G.subir`. Al pasar a **Incidente** los dos campos se
   ocultan y quedan inactivos —un campo oculto no se envía ni se pide—, y el texto de arriba lo explica:
   «La actividad se registra ya terminada… El Administrador es el único que puede reabrirlo y ponerle fecha
   límite».
   *(Sustituido el mismo día por la entrada de arriba: esos campos salieron de `+ Nuevo` y viven en la
   ventana `G.dialogoCierre`, que se abre sola después de crear; el informe pasó a opcional.)*
5. **El botón «Reabrir»** en la tarjeta de detalle (`G.abrirDetalle`), solo con `items.reabrir` y con el elemento
   Completado o Cerrado. Abre una ventana con *Motivo de la reapertura* (opcional), hace el POST y refresca; la
   tarjeta renovada vuelve a mostrar el bloque **Evidencia del trabajo** con los archivos anteriores.
6. **La fecha límite ya era administrativa** y quedó verificado: `FechaLimiteEfectiva` solo la guarda con
   `configuracion.fechaLimite`, el campo no aparece en `+ Nuevo` sin `tickets.asignar` y `Asignar` exige
   `tickets.fechaLimite`. No hizo falta cambiar nada, pero se comprobó con curl que un Ingeniero que la mande en
   el JSON queda **sin fecha límite**.
7. **Detalle del constructor:** `GestionService` recibía `IEvidenciaStore evidencias` (solo lee y borra renglones,
   lo usa `CambiarEstado` para comprobar que hay evidencia). El adjuntar y el descartar viven en
   `EvidenciaService`, así que el constructor ahora recibe también ese servicio como `altaEvidencias`. No hay
   ciclo de inyección: `EvidenciaService` no conoce a `GestionService`, solo a su registro `Sesion`.

**Archivos modificados:** `Services/Gestion.cs`, `Program.cs`, `Models/Permisos.cs`, `Models/Audit.cs`,
`wwwroot/js/gestion.js` y las **16** pantallas `wwwroot/**/index.html` + `almacen/hoja.html` con la marca de
caché `?v=20261120a`, además de `README.md`, `docs/FRONTEND.md` y `docs/ARQUITECTURA.md`.

**Verificación** (una instancia local en `http://127.0.0.1:5098` con `--Auth:Store json`, cuentas y almacenes
desechables en `%TEMP%\cierre-arena` y el proxy `proxy-cierre.js` en 5097 para el Chrome real; **la producción de
5024 no se tocó**. *Los resultados de abajo corresponden al código de esta vuelta; la entrada de arriba los
sustituyó el mismo día*):

- **`chequea-cierre.js`: 29/29 correctas.** El alta multipart sin evidencia y sin informe devuelven 400 y **no
  dejan elemento**; con informe y dos archivos → **201 Completada**, asignada al propio Ingeniero, sin fecha
  límite y con la evidencia adjunta. El JSON del Ingeniero con tipo Actividad → 400 con el aviso; con Incidente →
  **201 Pendiente** (comportamiento viejo intacto). El Admin por JSON → 201 Pendiente con su fecha límite guardada
  y la del Ingeniero ignorada. `/reabrir`: **403** para el Ingeniero, **200** para el Admin → Pendiente con avance
  0 y **la evidencia intacta (2 archivos)**; reabrir algo ya pendiente → 400. Después de reabrir, el botón
  *Completar* del Ingeniero suma un archivo y quedan **3**. Historial y bitácora con sus renglones.
- **Clics reales en Chrome con los dos perfiles** (guiones inyectados por el proxy, sobre la pantalla
  `/calendario/`): con el **Ingeniero** el `+ Nuevo` muestra *Informe* y *Evidencia*, el botón dice **«Crear y
  completar»**, al enviar sin archivo avisa «Adjunta evidencia (foto o archivo) antes de continuar», la miniatura
  se pinta y el envío crea la actividad **Completada**; al cambiar a **Incidente** los dos campos desaparecen. Con
  el **Administrador** el `+ Nuevo` **no** muestra ninguno de los dos y conserva Prioridad, Fecha límite e
  Ingeniero responsable; su tarjeta de una actividad Completada trae **«Reabrir»** junto a *Cerrar formalmente*, y
  tras usarla el elemento queda **Pendiente** con su evidencia. La tarjeta del **Ingeniero** sobre ese mismo
  elemento reabierto muestra *Completar* y *Agregar comentario* y **no** tiene *Reabrir*.
- **Dos capturas leídas**: `nuevo-ingeniero-cierre.png` —el `+ Nuevo` del Ingeniero con el informe escrito y el
  archivo adjunto— y `tarjeta-admin-reabrir.png` —la tarjeta Completada con **EVIDENCIA DEL TRABAJO** en
  miniaturas; los botones quedan debajo del pliegue, por eso se comprobaron leyendo el DOM—.
- `dotnet build -c Release` limpio (0 advertencias) y `node --check wwwroot/js/gestion.js`.

**Despliegue:** pendiente, dentro del mismo paquete acumulado de `despliegue-regiones.cmd`. Cambió **C#**
(`Gestion.cs`, `Program.cs`, `Models/*.cs`), así que hay que **recompilar y publicar**; el `gestion.js` y las
marcas de caché viajan con el `wwwroot`, pero sin el binario nuevo el alta multipart y `/reabrir` no existen. Al
arrancar el servicio con la matriz de permisos guardada, el Administrador recibe `items.reabrir` por la versión 13
y el Ingeniero no, sin tocar *Roles y permisos*.

**Pendiente de autorización del usuario:** `%TEMP%\publish-staging` sigue con el binario del 19-nov 22:26, **sin
los cambios de hoy**, así que antes de ejecutar el script elevado hay que regenerarlo —eso ya es publicar código de
producción y necesita su visto bueno—:

```
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o %TEMP%\publish-staging
```

Después, `node %TEMP%\regiones-prueba\compara-staging.js` debe dar «distintos: nada» y el clic elevado sobre
`despliegue-regiones.cmd` hace el resto; el script ahora también verifica `/api/actividades/1/reabrir` sin sesión
(debe contestar **401**, señal de que el endpoint ya vive en el servidor).

---

## 2026-11-19 — **Cabeceras**: la ventana ⚙ Configuración se queda **solo en el inicio**

**Módulo:** `Pages/Shared/_Layout.cshtml` y `Pages/Shared/_LayoutPanel.cshtml`, con una regla muerta de
`wwwroot/css/theme.css`. **Tipo:** frontend (plantillas Razor + CSS).
**Base de datos / API / permisos / configuración:** ninguno. `/api/tema`, `/api/sesion` y `/api/config` siguen
exactamente igual; lo único que cambió es **quién los muestra**.

**Por qué:** el pedido fue «la ventana de configuracion unicamente se debe tener en el inicio, las demas
retiralas». Como es un retiro y no una reubicación con red de seguridad, fuera del inicio no quedó botón
sustituto ni atajo provisional: la ventana se va literal, que es como se entiende en este proyecto
«ya no se usa, retírala».

**Qué cambió:**

1. **`_LayoutPanel.cshtml`** —la cabecera de formato de panel, que solo usa `/dicom/lista-trabajo`— perdió las
   tres piezas de golpe: el botón `#pref-btn` con su SVG de engranaje, el `<partial name="_PrefDialog" />` y la
   etiqueta `<script src="~/js/preferencias.js">`. Su barra vuelve a ser título + `badge` de usuario y rol +
   **Regresar, Home y Salir**, y el comentario de arriba del archivo explica ahora que la ventana vive solo en
   el inicio y que a esa pantalla se regresa con *Home*.
2. **`_Layout.cshtml`** —la cabecera normal de la intranet— decide **por la ruta**, no por una bandera que una
   página pueda olvidar poner o quitar: un bloque `@{ … }` al principio calcula
   `enInicio` sobre `ViewContext.HttpContext.Request.Path.Value` con `TrimEnd('/')`, de modo que `/`, `/Index`
   y `/Index/` cuentan igual y `/Admin` o `/calendario/` no; y de ahí salen tres `@if (enInicio)` que envuelven
   el botón, el partial y el script. Detalle que costó: dentro de una plantilla Razor escribir
   `HttpContext.Request…` a secas **no compila** (CS0120, «Se requiere una referencia de objeto…»), hay que
   pasar por `ViewContext.HttpContext`.
3. **`theme.css`** perdió la regla `@media (max-width: 1320px) { .cab-panel .logo-centro { display: none } }`.
   Esa excepción existía solo porque esa barra llevaba cinco mandos y el wordmark centrado los pisaba antes; al
   quedarse en tres, la cabecera de panel se rige por la regla general de **1180px** como la de los demás paneles,
   y su comentario quedó fusionado con ella.
4. **El tema no se movió.** Sigue siendo de la cuenta, viaja en la copia `meditechnosys.tema` y se pinta en
   todas las pantallas —incluidas las autónomas de `wwwroot` y la lista de trabajo—; lo que ya no existe fuera
   del inicio es el mando para cambiarlo. La letra, el tiempo de sesión y los parámetros del módulo se siguen
   guardando con las mismas llamadas de siempre, ahora desde una sola pantalla.
5. **`wwwroot/admin/config/index.html` no es esta ventana**: es la pantalla completa de parámetros del módulo,
   con su ruta, su guard y su propio formulario. Se dejó intacta porque el pedido hablaba de la ventana ⚙; si
   también quiere retirarse, es una decisión aparte.

**Archivos modificados:** `Pages/Shared/_Layout.cshtml`, `Pages/Shared/_LayoutPanel.cshtml`,
`wwwroot/css/theme.css`, `README.md`, `docs/FRONTEND.md`, `docs/ARQUITECTURA.md`.

**Verificación** (una instancia local en `http://127.0.0.1:5098` con `--Auth:Store json` y almacenes
desechables en `%TEMP%\tema-arena`; **la producción de 5024 no se tocó**):

- **curl contando piezas dentro del HTML:** `/` → 1 botón, 1 diálogo, 1 `preferencias.js`;
  `/dicom/lista-trabajo`, `/Login` (anónimo) y `/AccesoDenegado` → **0 / 0 / 0**. Ojo con `/Admin`,
  `/Ingenieria` y `/Login` teniendo sesión: contestan **302 → `/`**, y por eso su cuerpo trae el botón del
  inicio; revisando el código de estado y la redirección se ve que la página propia no lo pinta.
- **Chrome real a través de `proxy-tema.js`** (el arnés que reenvía la sesión capturada y reporta el DOM vivo):
  en el inicio `prefBtn:true, prefDialog:true, prefScript:true` con los radios del diálogo marcados desde el
  servidor; en `/dicom/lista-trabajo` los tres en **false**, y aun así `tema` pinta lo que dice la cuenta, con
  **`errores: []`** en las dos pantallas.
- **Clic real en el radio Claro del inicio** → `PUT /api/tema → 204`, la copia queda en `light` y al cargar la
  lista de trabajo llega `tema:"light"` con `prefBtn:false`. Terminada la prueba, la cuenta de prueba se regresó
  a **Oscuro** (`PUT {"tema":"dark"} → 204`, y el `GET` lo confirma).
- **Dos capturas leídas**: `pref-solo-inicio.png` —la cabecera del inicio con «⚙ Configuración» junto a
  *Salir*— y `pref-fuera-panel.png` —la barra negra de *Consulta lista de trabajo* solo con **Regresar, Home y
  Salir**, sin rastro del engranaje.
- `dotnet build -c Release` limpio.

**Despliegue:** pendiente, dentro del mismo paquete acumulado de `despliegue-regiones.cmd`. Son plantillas
Razor, así que hace falta recompilar y publicar —copiar `wwwroot` no basta—; el CSS tocado sí lleva marca de
caché propia (`theme.css?v=20261119e`, ya puesta en la entrada del tema).

---

## 2026-11-19 — **Tema**: se quitó el interruptor Día/Noche; ahora es **Oscuro / Claro** guardado en la cuenta

**Módulo:** el preferencia nueva `Services/Tema.cs` con sus endpoints en `Program.cs`, el bloque «Tema» del
diálogo ⚙ (`Pages/Shared/_PrefDialog.cshtml`), las dos cabeceras Razor, `wwwroot/js/theme.js`,
`wwwroot/js/preferencias.js`, `wwwroot/css/theme.css` y `wwwroot/css/intranet.css`.
**Tipo:** frontend + API + backend + bitácora + configuración (una clave nueva por usuario).
**Base de datos:** ninguna tabla nueva. El valor vive en el mismo almacén `IConfigStore` que ya guarda el
tiempo de sesión por usuario —`sesion.minutos.<usuario>`—, ahora también en `tema.<usuario>`; en producción
es la tabla de configuración de MySQL y en la prueba local un JSON.
**Permisos:** ninguno nuevo. Cada quien lee y escribe **su** tema: el nombre se toma del `ClaimsPrincipal`,
nunca del cuerpo de la petición, así que no hay forma de tocar la preferencia de otra persona.

**Por qué:** el pedido fue «QUITA EL MODO DIA, DEJALO EN MODO NOCHE». Al confirmar el alcance quedó en tres
piezas: el interruptor se retira de **toda** la intranet sin sustituto; el tema pasa a ser una opción más del
⚙ —«COLOCALO COMO OTRO TEMA, Y QUE SE GUARDE LA CONFIGURACION POR PERFIL», con las dos opciones **Oscuro** y
**Claro**—; y se guarda **en la cuenta de cada usuario**, no en el equipo. Como el propio pedido pide dejarlo
en noche, **Oscuro** es el valor con el que abre quien nunca eligió (`TemaPreferencias.PorDefecto`).

**Qué cambió:**

1. **El interruptor se retiró literalmente**, sin botón nuevo ni camino de respaldo: el `#theme-toggle` que
   escribían `_Layout.cshtml` y `_LayoutPanel.cshtml`, el código de `theme.js` que lo creaba e inyectaba donde
   no venía escrito (paneles autónomos y acceso), y sus reglas en `theme.css` (`.theme-toggle`,
   `.theme-toolbar`) e `intranet.css`. En cada sitio quedó un comentario de por qué ya no está. El
   `localStorage['meditechnosys.theme']` que aquel escribía se borra al cargar (`theme.js:73`) para que un
   recuerdo viejo del equipo no le gane a lo que dice la cuenta.
2. **`Services/Tema.cs` (`TemaPreferencias`)**: valores `dark` / `light`, `PorDefecto = Oscuro`, la clave
   `tema.<usuario>` normalizada a minúsculas (los nombres de cuenta se guardan sin distinción de mayúsculas),
   `Normalizar` —acepta «oscuro», «noche», «claro», «día» y devuelve `null` para cualquier basura—,
   `Valor(config, usuario)` que cae en Oscuro ante un valor ilegible, y las etiquetas que ve la gente.
3. **`GET /api/tema`** (`Program.cs:2559`) devuelve `{tema, opciones[], porDefecto}`, para que las opciones que
   se muestran en pantalla sean exactamente las que el backend acepta. **`PUT /api/tema`** (`2567`) normaliza,
   escribe `cfg.Set(TemaPreferencias.Clave(usuario), tema)`, reescribe la copia en la cookie, anota
   `AuditEvents.CambiarTema` («Oscuro → Claro») y contesta 204. Un valor fuera de la lista devuelve 400 con
   «Ese tema no está entre las opciones.».
4. **El bloque «Tema» del ⚙** son dos radios Oscuro / Claro **marcados desde el servidor** con el valor de la
   cuenta (`_PrefDialog.cshtml:23-24`, `checked="@(temaCuenta == … ? "checked" : null)"`), igual que se marcaba
   «General» cuando el tema era del equipo: la ventana sale correcta aunque el JavaScript no cargue.
5. **La cookie `meditechnosys.tema` es una copia, no el dato.** No es `HttpOnly` porque `theme.js` necesita
   leerla antes del primer pintado y las pantallas autónomas son archivos estáticos. La escribe el acceso
   (`Pages/Login.cshtml.cs:65` y `/api/login`, `Program.cs:716`), la refresca el `PUT`, la mantiene al día el
   middleware nuevo de `Program.cs:630` —justo después de `UseAuthorization`— en cada petición autenticada, y
   la borran la salida (`Pages/Logout.cshtml.cs:23`, `/api/logout`) y cualquier petición sin sesión, para que
   quien entre después en ese equipo no herede el tema del anterior. Manda siempre la cuenta.
6. **`theme.js`** ya no crea mando alguno: lee la cookie y, si falta o trae algo irreconocible, pinta
   **Oscuro**. Sigue exponiendo `window.MeditechnosysApariencia` (`tema()`, `cambiarTema(v)`) para el diálogo y
   despachando `meditechnosys:themechange` (lo consume `intranet.js:87`). El tamaño de la letra **no** se movió:
   sigue siendo preferencia del equipo, porque es cuestión del monitor y de la vista de quien está sentado.
7. **`preferencias.js`** confirma el marcado con `GET /api/tema` al abrir el diálogo y guarda con `PUT`, con su
   mensaje «Listo: tu pantalla se abre en Oscuro/Claro…» y, si la escritura falla, regresa al valor que el
   servidor realmente tiene. **Se cerró además una carrera que encontró el arnés**: el `GET` de confirmación
   podía contestar *después* del clic del usuario y su respuesta —el valor anterior— reescribía los radios y
   devolvía la pantalla al tema viejo, deshaciendo lo que se acaba de elegir. Ahora la bandera `guardandoTema`
   ignora esa lectura mientras hay un `PUT` en vuelo (`preferencias.js:127,145,172,181`).
8. **Los paneles autónomos de `wwwroot`** no llaman a ninguna API de tema: `UseStaticFiles` los sirve antes de
   tocar la sesión, así que usan la copia que el navegador ya trae puesta y quedan reconciliados en cuanto se
   abre el ⚙ —que desde la entrada siguiente de este registro **solo existe en el inicio**— o se navega a una
   pantalla Razor.
9. **Marca de caché `20261119e`** en `theme.js?v=` y `theme.css?v=` de los 21 HTML de `wwwroot`
   (`marca-tema.js`). `preferencias.js` no lleva marca manual: lo incluyen las cabeceras Razor con
   `asp-append-version="true"`, que la calcula sola.

**Archivos creados:** `Services/Tema.cs`.
**Archivos modificados:** `Program.cs`, `Models/Audit.cs`, `Pages/Login.cshtml.cs`, `Pages/Logout.cshtml.cs`,
`Pages/Shared/_PrefDialog.cshtml`, `Pages/Shared/_Layout.cshtml`, `Pages/Shared/_LayoutPanel.cshtml`,
`wwwroot/js/theme.js`, `wwwroot/js/preferencias.js`, `wwwroot/css/theme.css`, `wwwroot/css/intranet.css`,
los 21 `wwwroot/**/index.html` y `wwwroot/almacen/hoja.html` (solo la marca de caché), `README.md`,
`docs/FRONTEND.md`, `docs/ARQUITECTURA.md`.

**Verificación** (todo contra una instancia local en `http://127.0.0.1:5098` con `--Auth:Store json` y
almacenes desechables en `%TEMP%\tema-arena`; **la producción de 5024 no se tocó**):

- **curl sobre la API:** `PUT {"tema":"light"}` → 204 y el `GET` siguiente devuelve `light`;
  `PUT {"tema":"sepia"}` → 400 con su mensaje; `GET` y `PUT` sin sesión → 401; `PUT` sin cuerpo → 400.
  Con la copia vieja (`tema=light`) y la cuenta en `dark`, el middleware responde
  `Set-Cookie: meditechnosys.tema=dark` —también cuando la copia no existe—; el acceso escribe la copia con el
  valor de la cuenta y la salida la borra (`Set-Cookie: meditechnosys.tema=; expires=Thu, 01 Jan 1970`).
- **Clic real en Chrome** (arnés nuevo `proxy-tema.js`, que reenvía al 5098 con la sesión capturada y deja ver
  el DOM vivo): con la cuenta oscura, abrir el ⚙ y marcar **Claro** deja `data-theme="light"`, la copia en
  `light`, los radios `dark:false / light:true`, `PUT /api/tema → 204`, y al volver a cargar la cuenta sigue en
  `light`. Antes de arreglar la carrera del punto 7 este mismo camino regresaba a Oscuro.
- **Cargas frescas** en los dos temas, en `/`, en `/dicom/lista-trabajo` y en `/Login` (anónimo): el tema y los
  radios van con lo que dice la cuenta, `toggle:false`, `toolbar:false` —no queda ni un interruptor viejo en
  ninguna pantalla—, `--theme-card-bg` y las tarjetas coinciden con la paleta, y **`errores: []`** en todas.
  El acceso anónimo abre en `data-theme="dark"`.
- **Cuatro capturas leídas** con el código ya final: `shot-home-dark.png`, `shot-home-light.png`,
  `shot-dialogo-dark.png`, `shot-dialogo-light.png` (el bloque **TEMA** arriba del todo, con su nota
  «Se guarda en tu cuenta…»).
- Detalle del arnés, para que nadie lo lea como un defecto de la app: bajo `--virtual-time-budget` el
  `transition: background .15s` de `.app` no avanza, así que al medir justo después de un cambio la tarjeta
  devuelve el color del tema anterior. La variable y el siguiente fotograma dan el color correcto, y en las
  cargas frescas (sin transición) la medición ya cuadra.
- `dotnet build -c Release` limpio.

**Despliegue:** pendiente, dentro del mismo paquete acumulado de `despliegue-regiones.cmd`. Lleva `Program.cs`
y plantillas Razor, así que hace falta la recompilación publicada —no basta con copiar `wwwroot`— y, al
haberse cambiado la hoja y el script del tema, conviene el sello `20261119e` que ya va en el fuente.

---

## 2026-11-19 — **DICOM · Consulta lista de trabajo**: pasa al formato de panel de las demás pantallas

**Módulo:** `Pages/ListaTrabajo.cshtml` (`/dicom/lista-trabajo`) y su cabecera nueva
`Pages/Shared/_LayoutPanel.cshtml`. **Tipo:** frontend (plantilla Razor + CSS).
**Base de datos / API / permisos / configuración:** nada. La pantalla sigue siendo la misma
`[Authorize(Policy = "P:" + CatalogoPermisos.DicomVer)]` sobre los mismos endpoints.

**Por qué:** las dos capturas del pedido («qUIERO QUE LE DES EL MISMO FORMATO A EL APARTADO DE: Consulta
lista de trabajo QUE SE TIENE EN LAS DEMAS PAGINAS») comparaban `/dicom/` —barra negra fija con la marca
Meditechnosys centrada en dorado, tarjetas blancas translúcidas con el título subrayado en dorado, etiquetas
de campo en versalitas y botón azul— contra `/dicom/lista-trabajo`, que venía con la tipografía grande serif
y la cabecera de la intranet. Era la única página Razor con formato propio.

**Qué cambió:**

1. **Cabecera nueva `_LayoutPanel.cshtml`**: es el `Layout` de la pantalla. Copia el formato de los paneles
   autónomos —`panel.css` y luego `theme.css`, para que Día/Noche mande sobre la paleta igual que en
   `/dicom/`— con `body.theme-panel`, `header.cab-panel` (título + `badge` con usuario y rol +
   `logo-centro` + `.botones-nav`), el `.fondo-logo` de atrás y el fondo `header` con la línea dorada de
   3 px. Conserva los mandos que ya estaban en la cabecera de intranet: **Día/Noche**, **⚙ Configuración**,
   **Regresar**, **Home** y **Salir**.
2. **El diálogo ⚙ se escribió una sola vez** en `Pages/Shared/_PrefDialog.cshtml` y lo incluyen las dos
   cabeceras (`_Layout.cshtml` y `_LayoutPanel.cshtml`). Antes estaba copiado dentro de `_Layout.cshtml`;
   duplicarlo era la vía rápida para que el ⚙ de una pantalla se desviara del de la otra.
3. **`ListaTrabajo.cshtml`** cambió `Layout = "_Layout"` por `Layout = "_LayoutPanel"` y el título de página
   se envolvió en `<div class="lt-heading">`. Es un `div`, no un `header`: `panel.css` pinta **toda** etiqueta
   `<header>` como la barra negra fija, así que un `header` dentro de `<main>` habría sacado una segunda barra.
4. **`panel.css`**: `header .botones-nav form { display: contents; }` para que el `<form method="post">` de la
   salida no pinte caja propia y su botón quede alineado con Regresar y Home. Sin eso el «Salir» salía como una
   píldora dentro de otra píldora.
5. **`theme.css`**: bloque `.theme-panel .lista-trabajo …` con los equivalentes de Noche (`--theme-surface`,
   `--theme-control-border`, `--theme-primary`, `--theme-danger/warning/success-*`, el `::backdrop` del diálogo
   y el marco negro del visor de imagen). `lista-trabajo.css` está escrito en claro, igual que `panel.css`, así
   que sus valores planos se corrigen por especificidad desde la hoja del tema. Prefijo `.theme-panel` y no
   `.theme-intranet` porque la página ahora viaja con ese `body`.
6. **`theme.css`**: la marca centrada se oculta desde **1320 px** en `.cab-panel` (en los demás paneles es a
   1180 px), porque esta barra lleva cinco mandos a la derecha y el emblema empezaba a pisarlos antes.
   Se midió: la agrupación de botones empieza en 882 px y la marca termina en 811 px.
7. **`lista-trabajo.css`**: `.lista-trabajo .hf-titulo` en versalitas de 11 px. La hoja de calendario y la de
   horas (`js/hoja-fecha.js`) escriben su rótulo como `<span class="hf-titulo">`, no como `<label>`, y se quedaba
   en minúsculas. El ajuste va acotado a esta pantalla: la Bitácora, que usa la misma librería, conserva su aspecto.

**Archivos creados:** `Pages/Shared/_LayoutPanel.cshtml`, `Pages/Shared/_PrefDialog.cshtml`.
**Archivos modificados:** `Pages/ListaTrabajo.cshtml`, `Pages/Shared/_Layout.cshtml`,
`wwwroot/css/lista-trabajo.css`, `wwwroot/css/panel.css`, `wwwroot/css/theme.css`.

**Verificación:** arnés nuevo `server-lt-arness.js` + `lee-lt.js` + `lee-lt-clics.js` (ensamblan los `.cshtml`
reales y los sirven con `/api/*` en 401, en Chrome headless): **45/45 en verde en Día y en Noche** — barra
negra pegada arriba con su línea dorada, marca centrada, `h1` y `h2` subrayados en dorado, etiquetas en
versalitas de 11 px (incluidos «Periodo» y «Hora»), tarjetas de la paleta translúcida, botón primario azul,
columna de 1200 px, cero desborde horizontal, cero errores de JavaScript y, en Noche, ningún campo queda
blanco papel. Más **9/9 con clic real**: el interruptor pasa claro → noche → claro y repinta los campos, y el
⚙ abre y cierra el diálogo. Capturas leídas: `lt-panel-dia.png`, `lt-panel-noche.png`.
`dotnet build -c Release` limpio y la publicación de staging comprobada contra el fuente
(`compara-staging.js`: 51 archivos, cero diferencias).

**Nota de comportamiento (preexistente, no introducida aquí):** si el navegador no tiene salida a internet,
el `<link>` a Google Fonts deja `document.readyState === "loading"` hasta que expira y retrasa el
`DOMContentLoaded`, que es cuando `theme.js` engancha el interruptor. El arnés de clic lo detectó y lo esquiva
esperando a que la página esté lista; afecta a todas las páginas Razor por igual.

**Despliegue:** pendiente, en el mismo paquete acumulado de `despliegue-regiones.cmd`. Al llevar cambios de
Razor (`Pages/*.cshtml`) no basta con copiar `wwwroot`: hace falta la recompilación ya publicada en
`publish-staging`.

---

## 2026-11-19 — **Patología · Subida**: se quitó el botón «Salir» de la pantalla

**Módulo:** `wwwroot/patologia/subida/index.html`. **Tipo:** frontend (una pantalla autónoma).
**Base de datos / API / permisos / configuración:** nada.

**Por qué:** la captura anotada de la pantalla decía **«QUITA ESE BOTON DE SALIR»**. La salida de sesión
vuelve a quedar solo donde estaba antes: en la cabecera de la intranet, que conserva su botón **Salir**;
esta pantalla de carga ya tiene **Regresar** y **Home** arriba, así que no se le puso ningún sustituto.

**Qué cambió:**

1. Se eliminó el botón `<button class="secundario" id="btn-salir">Salir</button>` que estaba en la línea de
   usuario (`div.barra`), junto a la etiqueta del nombre.
2. Se eliminó su manejador completo —el que hacía `POST /api/logout` y redirigía a `/Login`—; no quedó
   sustituido por una guarda ni por otro camino de salida.
3. Se eliminaron las reglas `.secundario` y `.secundario:hover` del `<style>` de la página: ese estilo solo lo
   usaba ese botón, y verificar que no quedara CSS muerto es parte de retirarlo.
4. La etiqueta del usuario (`span#quien`) se queda donde estaba: `.barra` es `justify-content: space-between`
   y con un solo hijo el texto sigue alineado a la izquierda.

**Archivos modificados:** `wwwroot/patologia/subida/index.html`.

**Verificación:** arnés nuevo `server-subida-arness.js` + `lee-subida.js` (sirven la pantalla real de
`wwwroot` con una sesión fingida en `/api/patologia/me` y **leen el DOM vivo en Chrome headless**, no el HTML
fuente): **8/8 en verde** — la página se queda en `/patologia/subida/`, la vista de la app se pinta, el nombre
del usuario sigue visible, no existe `#btn-salir`, ningún botón dice «Salir» (quedan Día/Noche, Regresar, Home
e Importar), el CSS `.secundario` desapareció y no hubo errores de JavaScript. Captura leída:
`subida-sin-salir.png`.

**Despliegue:** pendiente, en el mismo paquete acumulado de `despliegue-regiones.cmd`.

---

## 2026-11-19 — **Calendario**: la tarjeta del Ingeniero se queda en Completar (con evidencia obligatoria) y comentario; el Administrador conserva la asignación

**Módulo:** tarjeta de detalle del Calendario (`Gestion.abrirDetalle` en `wwwroot/js/gestion.js`) y el cierre
de un elemento en el servidor (`Services/Gestion.cs`, `CambiarEstado`), más un módulo nuevo de evidencia
(`Models/Evidencia.cs`, `Services/Evidencias.cs`). **Tipo:** backend (modelo, almacén y API nuevos), frontend
(la tarjeta y los modales con archivos) y base de datos (tabla `evidencias`). **Permisos:** ninguno nuevo; la
tarjeta sigue dividiéndose con `tickets.asignar`.

**Por qué:** la captura anotada de la tarjeta de detalle decía «de lado de ingeniero debe quedar como
completado ya que solo es el reporte de la actividad que hizo, y solo puede agregar comentarios, de lado de
admin puede asignar las actividades y cuando el ingeniero la complete debe poner la evidencia ya sea foto o
archivo, unicamente». Con las respuestas del usuario quedó cerrado el alcance: al Ingeniero se le dejan
**Completar** y **Agregar comentario**; la **evidencia es obligatoria y se admiten varios archivos**; el
**comentario es solo texto** (la evidencia se adjunta únicamente al completar) y **«Entregar reporte» se retira
de la tarjeta**. El Administrador no pierde nada: sigue asignando y conservó Iniciar, Cancelar, Registrar
avance, Entregar reporte, Reasignar, Editar, Observación y el cierre formal.

**Qué cambió:**

1. **Módulo nuevo de evidencia.** `Models/Evidencia.cs` define el registro (`Id`, `ActividadId`,
   `NombreArchivo`, `RutaRelativa`, `Tipo`, `Tamano`, `SubidoPor`, `Subido`) y `EvidenciaRules`: hasta
   **5 archivos por envío** de **20 MB** cada uno, con una lista cerrada de extensiones
   (jpg, jpeg, png, gif, bmp, webp, tif, tiff, pdf, doc, docx, xls, xlsx, ppt, pptx, txt, csv, log, zip, rar,
   7z) y **validación de firma** —un `.jpg` que en realidad es un `.exe` se rechaza con
   «no es realmente lo que su nombre dice»—. `Services/Evidencias.cs` aporta `IEvidenciaStore` con sus dos
   implementaciones (`JsonEvidenciaStore` en `App_Data/evidencias.json` y `MySqlEvidenciaStore` en la tabla
   `evidencias`) y `EvidenciaService` (`Adjuntar`, `Listar`, `ParaAbrir`, `Descartar`). El archivo se guarda en
   disco con nombre GUID bajo `Gestion:EvidenciasDir` (por defecto `App_Data/evidencias`), con guarda de
   recorrido de ruta y carpeta creada al arrancar.
2. **Completar ya no se puede dar sin evidencia.** `CambiarEstado` a `Completada` rechaza con
   «Falta la evidencia: adjunta una foto o un archivo del trabajo realizado.» si el elemento no tiene ninguna,
   venga por la vía nueva o por el viejo `PUT /api/actividades/{id}/estado`, así que no hay forma de saltarse
   el requisito desde la API.
3. **Tres endpoints nuevos** (`Program.cs`, todos tras la autenticación de Ingeniería):
   `POST /api/actividades/{id}/completar` —un solo envío `multipart` con los archivos en `archivos` y el
   `informe`; adjunta y luego cierra, y si algo falla no queda medio elemento completado—,
   `GET /api/actividades/{id}/evidencias` —la lista que pinta la tarjeta— y `GET /api/evidencias/{id}` —el
   archivo, en línea si es foto y como descarga si es documento, con el MIME de la extensión validada,
   `X-Content-Type-Options: nosniff`, `Content-Disposition` en ASCII más su variante UTF-8 y
   `Cache-Control: private, max-age=120`—.
4. **La tarjeta del Ingeniero queda reducida a su reporte.** Con `tickets.asignar` desactivado desaparecen
   Iniciar, Cancelar, Registrar avance, Entregar reporte y las acciones administrativas, junto con el aviso de
   reporte pendiente; le quedan **Completar**, **Agregar comentario** y el botón que simplemente quita la
   ventana. El cierre formal («Cerrar formalmente») sigue siendo de `tickets.cerrar`, no del Ingeniero.
5. **Bloque «Evidencia del trabajo» en la tarjeta** (los dos perfiles lo ven): miniaturas de las fotos a
   tamaño real dentro de la tarjeta, documentos con su extensión en grande, y debajo el nombre con quien lo
   subió, la fecha y el peso. Sin archivos dice «Sin evidencia todavía: se adjunta (foto o archivo) al marcar
   como completada.».
6. **Los modales ahora aceptan archivos con vista previa.** `G.formDialog` gana el campo
   `{tipo:'file', multiple, accept, miniaturas}`: enseña lo elegido antes de enviar, avisa
   «Adjunta evidencia (foto o archivo) antes de continuar. Hasta 5 archivos de 20 MB…» y no deja salir el
   envío sin nada. La ventana de **Completar** pide informe + evidencia (ambos obligatorios); la de
   **Agregar comentario** es solo texto.
7. **Comentario del dueño.** `GestionService.Observacion` registra como «Comentario» lo que escribe el
   responsable del elemento y como «Observación» lo que escribe quien tiene `tickets.observaciones`; el
   historial de cambios muestra ambos.
8. **Marca de caché** a `?v=20261119d` en las doce pantallas que cargan `js/gestion.js` y en las dieciséis que
   cargan `css/panel.css` (los estilos nuevos viven ahí).

**Archivos creados / modificados:** creados `Models/Evidencia.cs` y `Services/Evidencias.cs`; modificados
`Models/Audit.cs` (evento `AdjuntarEvidencia`), `Services/Gestion.cs`, `Program.cs` (inyección, carpeta al
arrancar, `EnsureSchema` y los tres endpoints), `wwwroot/js/gestion.js`, `wwwroot/css/panel.css` y los `?v=` de
las pantallas de `wwwroot/admin/`, `wwwroot/gestion/`, `wwwroot/almacen/hoja.html` y `wwwroot/calendario/`.

**Verificación:** `dotnet build -c Release` sin advertencias ni errores. Arnés .NET nuevo
`%TEMP%\evidencia-prueba`, que compila los `Models` y `Services` reales: **58/58 correctas** (lista de
extensiones, firma, peso y nombre; los dos perfiles tal como los entiende la tarjeta; el rechazo a completar
sin evidencia; `Adjuntar` con 403 para otro ingeniero, todo-o-nada, tope de 5 y nombres GUID; historial y
bitácora; completar después de adjuntar y el rechazo a adjuntar ya cerrado; comentario como «Comentario» frente
a «Observación»; y la semántica del almacén JSON). Chrome real, sin parches, contra el `gestion.js` y el
`panel.css` que se publican (`node server-detalle.js <puerto> <sesión>` + `--dump-dom`): **28/28** como
Administrador (conserva Iniciar, Completar, Cancelar, Registrar avance, Entregar reporte y su asignación, ve el
bloque de evidencia y el cierre con sus dos archivos sale en un solo `multipart`) y **26/26** como Ingeniero
(tarjeta en Completar + Agregar comentario + Cerrar, sin el aviso de reporte, no puede enviar sin evidencia —y
el navegador no manda nada—, sus tres archivos salen en un solo envío, la tarjeta se reabre Completada con las
dos fotos cargadas de verdad y el comentario queda como «Comentario»). Capturas:
`%TEMP%\regiones-prueba\tarjeta-botones-ingeniero.png`, `tarjeta-evidencia-ingeniero.png` (la ventana con las
miniaturas antes de enviar), `tarjeta-botones-admin.png` y `tarjeta-detalle-admin.png`.

**Despliegue:** pendiente, en el mismo reinicio elevado ya preparado (`despliegue-regiones.cmd`); cambió el
backend y se sumó la tabla `evidencias`, así que `%TEMP%\publish-staging` se regenera con `dotnet publish` y
el script crea la carpeta `App_Data/evidencias` al arrancar el servicio.

---

## 2026-11-19 — **Calendario**: en «+ Nuevo» la prioridad, las fechas y el ingeniero responsable son solo de quien puede asignar

**Módulo:** diálogo **+ Nuevo** del Calendario (`Gestion.abrirCrear`) y la creación en el servidor
(`Services/Gestion.cs`, método `Crear`). **Tipo:** frontend (el formulario) y backend (lo que se guarda).
**Base de datos:** ninguna. **API:** ninguna nueva. **Permisos:** ninguno nuevo; se usa el que ya existe,
`tickets.asignar` («Asignar tickets a ingenieros»).

**Por qué:** la captura anotada marcaba el final del diálogo —Prioridad, Fecha de inicio, Fecha límite,
«Deshabilitar fecha límite» e Ingeniero responsable— y decía «Para el perfil de ingeniero estos campos no
deben ser visibles, unicamente el administrador o el que tenga ese permiso». Hasta ahora el único campo
realmente reservado era el **Ingeniero responsable**; la **Prioridad** y la **Fecha de inicio** se le
ofrecían al Ingeniero y la **Fecha límite** aparecia siempre que estuviera habilitada en Configuración, sin
mirar el permiso: el Ingeniero la veía, la llenaba y el servidor la tiraba, así que el campo parecía roto.

**Qué cambió:**

1. **El bloque final depende de `tickets.asignar`.** En `Gestion.abrirCrear` se calcula
   `puedeAsignar = G.tiene('tickets.asignar')` y con eso se pintan Prioridad, Fecha de inicio, Fecha límite
   con su casilla e Ingeniero responsable —el mismo permiso que ya gobierna el diálogo de asignación formal
   (§6d) y el que trae el Administrador por defecto—. Un rol personalizado los ve en cuanto se le active
   ese permiso en **Roles y permisos**; no se creó permiso nuevo ni se subió la versión del catálogo.
2. **Sin ese permiso el diálogo termina en la Descripción.** La cadena de Programador (Tipo → Región →
   Hospital → Servicio → Actividad → Aplicativo → Tipo de aplicativo) se queda exactamente igual: lo único
   que desaparece son los cuatro campos administrativos.
3. **El alta no se queda coja.** Viaja `prioridad: 'Media'`, `fechaLimite: null`, `usuario: ''` y
   `fecha: opts.fecha || G.hoy()`, o sea **el día del calendario desde el que se abrió la ventana** (hoy si
   fue con *+ Nuevo*). La ayuda del diálogo ahora lo dice: «La fecha de inicio es el día del calendario desde
   el que abres esta ventana; la prioridad y la fecha límite las fija el Administrador al asignar.»
4. **El servidor refuerza la misma regla** (`Crear`): `puedeAsignar` se calcula antes de validar y, si es
   falso, la prioridad recibida se ignora y queda en Media —igual que ya se hacía con el usuario asignado y
   la periodicidad—. La fecha límite ya se anulaba por `configuracion.fechaLimite`; ahora el formulario ni
   siquiera la ofrece. Un cuerpo forjado a mano no puede registrar una prioridad o una fecha límite que la
   pantalla no muestra.
5. **No cambia nada guardado.** Los elementos ya registrados conservan su prioridad y sus fechas; el cuerpo
   de `POST /api/actividades` sigue siendo el mismo de siempre.
6. **Marca de caché** de `js/gestion.js` a `?v=20261119c` en las doce pantallas que lo cargan.

**Archivos modificados:** `wwwroot/js/gestion.js`, `Services/Gestion.cs` y el `?v=` de
`wwwroot/calendario/index.html`, `wwwroot/admin/{administrativos,config,programador,reportes,roles}/index.html`,
`wwwroot/almacen/hoja.html` y `wwwroot/gestion/{historico,inventario-aetitles,inventario-equipos,inventario-ips,playbook}/index.html`.
Se documentó en `README.md` (la cadena de «+ Nuevo», el bloque de la fecha límite y una nueva entrada de
«si no lo veo» en la solución de problemas) y en `docs/FRONTEND.md` §6c.

**Verificación:** `dotnet build` sin advertencias ni errores. Chrome real, sin parches, contra el código que
se publica (`node server-fake.js 8897 <sesión>` + `--dump-dom`), con dos sesiones nuevas en el arnés:
**48/48** como `cal` (Administrador: los cuatro campos se ofrecen, en el orden Prioridad → Fecha de inicio →
Fecha límite → Ingeniero responsable, con las opciones de `/api/usuarios/asignables`, y el alta manda la
prioridad y el ingeniero elegidos) y **25/25** como `ing` (Ingeniero: ninguno de los cuatro aparece —ni
siquiera oculto—, la ayuda lo explica, el alta cierra y llega en Media, sin fecha límite, sin asignar y con
la fecha de hoy; desde el `+` de un día la fecha del alta es la de ese día). El arnés sin navegador
`dialogo-regiones.js` subió a **52/52** con una sección nueva para el perfil sin permiso. Capturas:
`%TEMP%\regiones-prueba\nuevo-admin.png` (el diálogo completo) y
`%TEMP%\regiones-prueba\nuevo-ingeniero.png` (la versión del Ingeniero, que termina en Descripción).

**Despliegue:** pendiente, en el mismo reinicio elevado ya preparado (`despliegue-regiones.cmd`, cuyo encabezado
ya enumera este cambio); cambió el backend, así que `%TEMP%\publish-staging` se regeneró con `dotnet publish`
y se confirmó que su `js/gestion.js` es idéntico al de la fuente y que las doce pantallas del staging cargan
la marca `?v=20261119c`.

---


**Módulo:** pantalla **Programador** (`/admin/programador/`) y diálogo **+ Nuevo** del Calendario
(`Gestion.abrirCrear`). **Tipo:** backend (catálogo nuevo en el almacén de configuración), API (un endpoint) y
frontend (las dos pantallas). **Base de datos:** ninguna. **Permisos:** ninguno nuevo.

**Por qué:** la petición fue «me hace falta que de aplicativos desplegue una opcion de tipo de aplicativo».
La cadena que se configura en Programador se detenía en el aplicativo: Capacitación ofrecía *Aplicativos
Fuji* y ahí terminaba la elección, sin poder registrar en qué tipo de aplicativo fue (una estación de
trabajo, un servidor…). Se eligió con el usuario el alcance: **una lista por aplicativo**, guardada
**dentro del título** y **opcional**.

**Qué cambió:**

1. **Catálogo nuevo `gestion.subopcionesTipos`** (`Services/Config.cs`): un objeto `aplicativo → arreglo de
   tipos`, vacío por defecto (`{}`). Como los demás catálogos, vive en el almacén de configuración —tabla
   `configuracion` de MySQL o `App_Data/configuracion.json` de respaldo—, así que **sobrevive a apagar o
   reiniciar el servidor** y no necesita columna nueva.
2. **Nuevo bloque «Tipos por aplicativo» en Programador**: un desplegable con los aplicativos y sus filas de
   tipos, con «+ Agregar tipo» y «Guardar tipos». El desplegable **solo ofrece los aplicativos que ya están
   guardados en «Subopciones por actividad»**, porque el servidor rechazaría una lista sobre un aplicativo que
   nadie registró. Al guardar se envía **el mapa completo** (no solo el aplicativo visible), igual que con los
   servicios por hospital, para que lo escrito en el aplicativo de enfrente no se pierda.
3. **Endpoint nuevo `PUT /api/programador/tipos`** (`Program.cs`, grupo `programador` con el permiso
   `programador.administrar`) y `GestionCatalogos.GuardarTiposPorSubopcion`, que devuelve
   «El aplicativo «X» no está en la lista de subopciones. Escríbalo primero en el bloque «Subopciones por
   actividad»…» si la clave no existe, deja fuera las listas vacías y registra el cambio en la bitácora
   (`AuditEvents.CambiarConfig`) con la cuenta y la IP.
4. **Cascada hacia abajo:** al guardar las subopciones se recortan los tipos de los aplicativos renombrados o
   borrados (`RecortarTipos`), y al guardar la lista de actividades se recortan las subopciones y, de ahí, los
   tipos. Ninguna lista puede quedar huérfana aunque ninguna pantalla ya pueda mostrarla.
5. **El calendario gana el cuarto campo de la cadena**: Tipo → Región → Hospital → Servicio → Actividad →
   Aplicativo → **Tipo de aplicativo**. El campo aparece **solo si el aplicativo elegido tiene lista propia**
   (la misma regla con que Región y Servicio dejan de mostrarse cuando no hay catálogo) y es **opcional**: su
   primera opción es «(Sin tipo de aplicativo)». Cambiar de aplicativo —o de actividad— lo reconstruye y limpia.
6. **Lo elegido se arma dentro del título**, no en un campo nuevo:
   `Capacitación — Aplicativos Fuji — Estación de trabajo — Anatomía Patológica`, con el mismo tope de 120
   caracteres y el mismo aviso si se pasa. El cuerpo que recibe `/api/actividades` sigue siendo idéntico: no
   viaja `subtipo` (ni región, ni servicio, ni aplicativo) como campo propio.
7. **Se actualizó la marca de caché** de `js/gestion.js` a `?v=20261119b` en las doce pantallas que lo cargan.

**Archivos creados / modificados:** `Services/Config.cs`, `Services/Catalogos.cs`, `Program.cs`,
`wwwroot/js/gestion.js`, `wwwroot/admin/programador/index.html` (bloque nuevo y su lógica), más el `?v=` en las
doce pantallas; `README.md`, `docs/FRONTEND.md` y este registro.

**Verificación:** `dotnet build` sin advertencias ni errores. Chrome real, sin parches, contra las pantallas y
el `gestion.js` que se publican: `%TEMP%\regiones-prueba\server-fake.js` en `http://localhost:8897` con
`--dump-dom` dio **40/40** en `/calendario-prueba` (la posición del campo, que solo aparezca con el aplicativo
que tiene tipos y desaparezca con el que no, que «(Sin aplicativo)» y cambiar de actividad lo oculten, que el
desplegable traiga «(Sin tipo de aplicativo)» primero y las opciones del catálogo, que el título llegue al
servidor con las cuatro piezas y que el cuerpo de `/api/actividades` no traiga campos nuevos) y **20/20** en
`/programador-prueba` (el bloque y su ayuda, el selector con los aplicativos guardados, las filas pintadas, el
cursor en la fila nueva, que lo escrito fuera de la vista se conserve, el aviso verde, lo que quedó en el
servidor, que sobreviva a un refresco, que `/api/permisos/mios` entregue los tipos recién guardados y que con
todo vacío la pantalla pida «Deje al menos un tipo en algún aplicativo» en lugar de borrar el catálogo). El
arnés sin navegador `dialogo-regiones.js` repite **41/41**. Capturas:
`%TEMP%\regiones-prueba\nuevo-tipo-aplicativo.png` (el diálogo con la cadena completa) y
`%TEMP%\regiones-prueba\programador-tipos.png` (el bloque nuevo).

**Despliegue:** pendiente, junto con lo ya acumulado (`despliegue-regiones.cmd`, con clic derecho →
*Ejecutar como administrador*). Como esta vez cambió el backend, el staging se regeneró con
`dotnet publish`; el comando vuelve a copiar `publish-staging` completo, no solo las pantallas.

---

## 2026-11-19 — **Calendario**: en «+ Nuevo» el Servicio va pegado al Hospital y el Aplicativo es opcional

**Módulo:** diálogo **+ Nuevo** del Calendario de actividades (`Gestion.abrirCrear`). **Tipo:** frontend
(solo el formulario). **Base de datos:** ninguna. **API:** ninguna. **Permisos:** ninguno.

**Por qué:** la captura anotada del diálogo pedía «Despues de hospital coloca el servicio, y en aplicativo
debe desplegar opciones que tambien se deben configurar en programacion». El servicio aparecía al final del
formulario, separado del hospital del que depende, y el aplicativo era obligatorio: no dejaba dar de alta una
capacitación cuando todavía no se sabe en qué aplicativo cae.

**Qué cambió:**

1. **El orden del formulario sigue ahora la cadena que se configura en Programador:** Tipo → Región → Hospital →
   **Servicio** → Actividad → Aplicativo → Descripción → Prioridad → Fechas. El servicio se colocó justo debajo
   del hospital, que es el que lo llena.
2. **La regla de cuándo se ve no cambia:** el Servicio solo aparece si el tipo es Actividad **y** el hospital
   elegido tiene servicios en su lista (Programador → «Servicios por hospital»). Con un hospital sin servicios,
   o al cambiar de región antes de elegir hospital, el campo sigue ocultándose para no estorbar.
3. **El Aplicativo deja de ser obligatorio.** Su primera opción pasó a decir «(Sin aplicativo)» —antes decía
   «(Selecciona el aplicativo)», que se leía como un paso forzoso— y el aviso «Selecciona el aplicativo de la
   actividad.» que bloqueaba el guardado se eliminó. Se puede dar de alta una Capacitación sin aplicativo.
4. **Sus opciones siguen viniendo del Programador**, del bloque «Subopciones por actividad» (etiqueta + lista de
   cada actividad): no se creó ningún catálogo nuevo ni se cambió la API. La etiqueta del desplegable sigue
   poniéndose con el nombre que se escribió allí —Aplicativo, Modelo, lo que sea— y ahora sin el sufijo
   «(obligatorio)».
5. **Lo que se guarda no cambia.** El aplicativo y el servicio elegidos se siguen armando dentro del título
   («Capacitación — Aplicativos Fuji — Anatomía Patológica»); sin aplicativo el título se arma sin esa pieza
   («Capacitación — Anatomía Patológica»). El registro que recibe `/api/actividades` es idéntico de siempre: no
   viaja región, ni servicio ni aplicativo como campos propios.
6. **Se actualizó la marca de caché** de `js/gestion.js` a `?v=20261119a` en las doce pantallas que lo cargan,
   para que nadie se quede con el formulario viejo abierto.

**Archivos modificados:** `wwwroot/js/gestion.js`, más el `?v=` en `wwwroot/calendario/index.html`,
`wwwroot/admin/{administrativos,config,programador,reportes,roles}/index.html`, `wwwroot/almacen/hoja.html` y
`wwwroot/gestion/{historico,inventario-aetitles,inventario-equipos,inventario-ips,playbook}/index.html`. El
bloque *Subopciones por actividad* de la pantalla Programador aclara ahora en su texto que esa segunda lista es
opcional.

**Verificación:** Chrome real, sin parches, contra la pantalla y el `gestion.js` que se publican
(`node server-fake.js 8899 cal` + `http://localhost:8899/calendario-prueba` con `--dump-dom`): **26/26**
(el orden campo por campo, que el servicio aparezca con HES y desaparezca con HI, que la región reconstruya el
desplegable de hospital, que el aplicativo traiga «(Sin aplicativo)» y las opciones del catálogo, que crear sin
aplicativo cierre la ventana y que el título llegue al servidor armado, y que un Incidente no muestre ni
servicio ni actividad). El arnés sin navegador `dialogo-regiones.js` repite **24/24**. Captura del diálogo con
el orden nuevo: `%TEMP%\regiones-prueba\nuevo-orden.png`.

**Despliegue:** pendiente, en el mismo reinicio elevado ya preparado (`despliegue-regiones.cmd`); el staging
lleva `js/gestion.js` y las doce pantallas con su marca nueva.

---

## 2026-11-19 — **Bandeja de correo**: la imagen pegada se ve y se ajusta desde la pantalla

**Módulo:** ventana «Redactar correo» de `/admin/correos/`. **Tipo:** frontend (solo la pantalla).
**Base de datos:** ninguna. **API:** ninguna (el contrato de `cid:` con `Services/Correo.cs` no se tocó).
**Permisos:** ninguno.

**Por qué:** la captura anotada mostraba el cuerpo del correo con un cuadrito de imagen rota y el nombre
«Synapse 3D (16).png» en lugar de la captura. El texto del reporte: «quiero que se vea que imagen estoy
poniendo y ajustarla». La causa es que el editor pintaba `src="cid:img1@meditechnosys"`, y `cid:` es un
esquema que solo entiende el correo: ningún navegador lo resuelve, así que siempre salía el icono roto.

**Qué cambió:**

1. **Se ve la imagen.** Al insertar o pegar una captura, el cuerpo la pinta con una *data URL* construida del
   base64 que ya está en memoria (`urlVista`). El `cid:` sigue siendo lo que viaja: `htmlParaEnviar()` clona el
   cuerpo antes de enviar y le regresa `src="cid:…"` a cada imagen, así que el `multipart/related` que arma
   MailKit y su `ContentId` quedan exactamente igual que antes. El HTML que se manda sigue pesando caracteres,
   no megas.
2. **Barra de la imagen.** Debajo de la barra de formato aparece `#barraImagen` solo cuando hay una imagen
   elegida: dice cuál es (su nombre), su medida en píxeles y qué porcentaje del ancho del mensaje ocupa, y trae
   **Original / 75% / 50% / 25% / Ancho** y **Quitar imagen**. La imagen recién insertada ya aparece elegida.
3. **Ajustar a mano.** El cuadrito azul (`#asaImagen`) se ancla a la esquina inferior derecha y se arrastra con
   el puntero; el ancho queda entre 60 px y el ancho del mensaje. Escribe el `width` en el atributo (lo respetan
   los clientes de correo, Outlook incluido) y en el estilo, y deja el alto automático para no deformarla. Vive
   dentro del `<dialog>`: fuera de una ventana modal el navegador no deja pasar los clics.
4. **Lo que se manda es el archivo completo.** Ajustar el tamaño no re-muestrea ni recorta la imagen: el
   adjunto base64 es el original, solo cambia cómo se muestra. Quitar la imagen del cuerpo la saca también de la
   lista de archivos y del correo; con `Supr`/`Retroceso` sobre la imagen elegida hace lo mismo, y al poner el
   cursor en el texto la barra se cierra, así que esa tecla nunca borra la imagen sin querer.
5. **Borradores y reenvíos.** Las imágenes de un borrador (`reincrustarInline`) y las del mensaje reenviado
   también se pintan ahora con su vista en lugar del `cid:`, conservando el tamaño que traían escrito.
6. **Se suelta al cerrar.** Al cerrar la ventana —o al enviar— la barra y el cuadro desaparecen; si una imagen
   termina de cargar cuando la ventana ya está cerrada, no se vuelve a elegir (`dlgRedactar.open`).

**Archivos modificados:** `wwwroot/admin/correos/index.html`.

**Verificación:** el arnés de navegador `server-bandeja.js` gana un escenario nuevo `?s=imagen` con **41/41**
(ver que `naturalWidth > 0` —es decir, píxel pintado, no icono roto—, que la barra sale con su nombre y su
medida, que cada botón deja el ancho esperado, que el arraste funciona y tope en sus dos extremos, que con dos
imágenes cada una se ajusta sola, y que el `POST /api/correos/enviar` que recibe el servidor lleva `cid:` y
`width=` pero ningún `data:`). Los escenarios previos se repiten: **24/24** con las dos cuentas y **10/10** sin
segundo correo. El arnés .NET **43/43** sigue vigente: el backend no se modificó en esta entrega. Captura de la
ventana con la imagen visible y al 50 %: `%TEMP%\regiones-prueba\imagen-ajustada.png`.

**Despliegue:** pendiente, en el mismo reinicio elevado ya preparado (`despliegue-regiones.cmd`); la carpeta de
staging lleva esta pantalla actualizada.

---

## 2026-11-19 — **Bandeja de correo**: fuera la banda «Buzón que estás viendo»

**Módulo:** pantalla `/admin/correos/`. **Tipo:** frontend (solo la pantalla). **Base de datos:** ninguna.
**API:** ninguna (los endpoints y el parámetro `cuenta` quedan igual). **Permisos:** ninguno.

**Por qué:** con las dos tarjetas de cuenta ya encima de las carpetas, el recuadro de arriba repetía lo mismo
con otras palabras. Su captura anotada decía literal: «retira esa parte, ya no tiene caso si tienes las
pestañas».

**Qué cambió:**

1. Se quita el bloque `<div class="banda-buzon">` con `#buzonCorreo` y `#buzonMarca`, sin dejar ningún sustituto.
2. Se quitan sus estilos: `.banda-buzon` (y sus hijos `.dato`, `.etiqueta`, `.valor`) y las píldoras `.marca`,
   `.marca.propio`, `.marca.soporte`, que ya no usan nada en la pantalla.
3. Se quita `pintarBuzon()` y sus dos llamadas (desde `activarCuenta` y desde `cargarBuzon`); con eso desaparece
   el texto `BUZÓN DE SOPORTE` / `TU SEGUNDO CORREO` / `SIN ENLAZAR` que pintaba. No se pierde información: la
   dirección ya viene escrita en cada tarjeta de cuenta y el estado «Sin enlazar · lo conecta el administrador»
   vive en la suya.
4. Se queda `correoDe(cuenta)`: no lo usaba solo la banda, también «Responder a todos» para no meter tu propia
   dirección en CC.

**Archivos modificados:** `wwwroot/admin/correos/index.html`, `README.md`, `docs/REGISTRO-CAMBIOS.md`.

**Verificación:** el arnés de navegador `server-bandeja.js` vuelve a salir **24/24** (con las tres lecturas que
dependían del rótulo reescritas sobre las tarjetas de cuenta, y una nueva que comprueba que `#buzonCorreo`,
`#buzonMarca` y `.banda-buzon` ya no existen en el DOM) y **10/10** en el escenario sin segundo correo. Captura
del resultado: `%TEMP%\regiones-prueba\bandeja-sin-banda.png`. Sigue verde el arnés .NET **43/43**, que no toca
la pantalla.

**Despliegue:** pendiente, en el mismo reinicio elevado ya preparado (`despliegue-regiones.cmd`); el staging se
recompiló para que lleve también este cambio.

---

## 2026-11-19 — **Bandeja de correo**: el buzón de soporte y un segundo correo, las dos cuentas a la vez

**Módulo:** bandeja de correo (`/admin/correos/`), su lógica en `Services/Correo.cs`, sus endpoints en
`Program.cs` y la pantalla de **Usuarios** (donde el administrador conecta el segundo buzón).
**Tipo:** backend + API + frontend. **Base de datos:** ninguna (sigue la tabla `usuario_buzones` y el
almacén JSON de respaldo). **Permisos:** ninguno nuevo.

**Por qué:** «En el apartado de correo deben tener el correo de soporte y aparte debe permitir conectar un
segundo correo», y al concretarlo: «se debe poder escoger si enviar correo con la cuenta de soporte u con la
otra cuenta», con **Soporte por defecto** y el segundo buzón enlazado **por el administrador en Usuarios**.

**Qué cambió:**

1. **Dejar de ser excluyentes.** Antes `Resolver(usuario)` elegía *una* cuenta: si el usuario tenía buzón
   enlazado, ese tapaba al de soporte. Ahora las dos conviven: `Cuentas(usuario)` devuelve siempre las dos
   (soporte primero) con su estado y su aviso, y cada petición dice cuál quiere con el parámetro `cuenta`
   (`soporte` | `propio`; sin parámetro, soporte). `NormalizaCuenta` es el único sitio donde se decide el
   valor por defecto.
2. **El soporte ya no depende del enlace.** Un enlace roto (contraseña ilegible, servidor inalcanzable) no
   puede dejar a nadie sin bandeja: el fallo se anota como `Aviso` de esa cuenta y el soporte sigue abierto.
   Y al revés: si el de soporte no está configurado, el segundo correo abre igual.
3. **API.** `GET /api/correos`, `GET/DELETE /api/correos/{uid}`, sus rutas `adjuntos`/`inline`,
   `POST /api/correos/{uid}/responder` aceptan `cuenta`; `enviar` y `borrador` la leen del cuerpo
   (`CorreoEnviarRequest.Cuenta`, parámetro final opcional). `GET /api/correos/mio` pasa de devolver un buzón
   a devolver `cuentas` + `predeterminada`, conservando los campos viejos para no romper a quien llegue por
   enlace directo. `POST /api/correos/probar` ya no exige que la contraseña de soporte esté escrita: parte de
   `BuzonOpciones.Copiar(Predeterminado)` y reutiliza la contraseña guardada del enlace.
4. **La pantalla.** Dos tarjetas encima de las carpetas (*Buzón de soporte* / *Segundo correo*), cada una con
   su dirección escrita y la activa marcada en azul, para elegir cuál se ve; en la ventana de redactar, el
   selector **Enviar con**. La bandeja que se lee es la preferida al redactar, y tras
   enviar o guardar un borrador la vista salta a la cuenta con la que salió, para que el correo se vea en su
   *Enviados* / *Borradores* real.
5. **Los uid no cruzan de bandeja.** `enRespuestaA`, `responderTodos` e `incluirOriginal` se sueltan cuando
   se envía con una cuenta distinta de aquella donde se leyó el mensaje: el identificador es propio del buzón
   y usarlo en el otro abriría otro mensaje o fallaría. La cita y el `RE:`/`RV:` ya van escritos en el cuerpo.
6. **Sin enlazar no es botón muerto.** La segunda tarjeta se pinta punteada con «Sin enlazar · lo conecta el
   administrador» y sigue siendo clicable: la bandeja entonces muestra el aviso del servidor con un enlace a
   **Usuarios**. En el editor, «Enviar con» solo ofrece cuentas disponibles.
7. **La elección no persiste entre entradas.** Se guarda en `sessionStorage`, no en `localStorage`: al volver
   a abrir la pantalla se arranca en soporte, que es lo que se pidió («Soporte por defecto»).
8. **Usuarios** presenta el buzón del perfil como lo que es ahora: «Segundo correo de la bandeja», con la
   nota de que el de soporte siempre está disponible y la casilla para quitarlo y dejar solo soporte. No
   cambió ningún `id` ni ningún cuerpo de petición.

**Archivos modificados:** `Services/Correo.cs`, `Program.cs`, `Models/Correo.cs` (tipos `CuentaBuzon` y el
parámetro `cuenta`), `Models/Buzon.cs` (`Cuenta` en `CorreoEnviarRequest`),
`wwwroot/admin/correos/index.html`, `wwwroot/admin/usuarios/index.html`, `Models/Catalog.cs` (texto de la
tarjeta), `README.md`, `docs/REGISTRO-CAMBIOS.md`.

**Verificación:** arnés .NET **nuevo** `%TEMP%\correo-prueba` **43/43** — compila el `Services/Correo.cs`
**real** (no una copia) con MailKit 4.18.1 y un `IBuzonStore` en memoria: normalización de `cuenta`, las dos
cuentas simultáneas, soporte abierto aunque exista enlace, usuario sin enlace solo con soporte, segundo correo
abiendo soporte sin configurar, enlace con contraseña ilegible que no contamina al soporte, `Cuentas()` sin
contraseña en claro ni cifrada ni clave de soporte en el JSON, y compatibilidad con lo que ya estaba. Arnés de
navegador **nuevo** `%TEMP%\regiones-prueba\server-bandeja.js`, que sirve la pantalla real contra una API falsa
y le da clics en Chrome: **24/24** con el segundo correo enlazado (cambio de cuenta recarga la lista con
`cuenta=propio`, las carpetas conservan la cuenta, «Enviar con» arranca en la bandeja que se ve, el envío sale
con la cuenta elegida y la vista se muda ahí, el borrador se guarda en su cuenta y se ve en sus Borradores,
ninguna petición lleva contraseñas) y **10/10** sin segundo correo (tarjeta punteada, aviso con enlace a
Usuarios, «Enviar con» con una sola opción y la petición honesta `cuenta=propio`). Captura de la pantalla con
las dos tarjetas: `%TEMP%\regiones-prueba\bandeja-dos-cuentas.png`.

**Despliegue:** pendiente, viaja junto con la 3.ª entrega de regiones en el mismo reinicio elevado
(`despliegue-regiones.cmd`); hay que regenerar el staging (`dotnet publish`) para que lleve también estos
archivos de correo.

---

## 2026-11-19 — **Ajuste en Usuarios**: fuera la casilla «Ver todas las regiones», dentro «Todas las regiones» en la lista

**Módulo:** diálogo **Editar** de la pantalla de usuarios y los dos mensajes del servidor que nombraban la
casilla. **Tipo:** frontend + backend (solo textos). **Base de datos:** ninguna. **Permisos:** ninguno nuevo.

**Por qué:** al ver la pantalla pidió, con captura del bloque marcado: «ESA OPCION QUITALA, SOLO EN LA LISTA
DEBE APARECER TODAS LAS REGIONES COMO OPCION». La casilla hacía exactamente lo mismo que el renglón vacío de la
lista y encima se podía dejar marcada sobre una región escrita, así que el mismo dato tenía dos respuestas.

**Qué cambió:**

1. **Desaparece `ediRegionTodas`** de la página: ya no hay `<input type="checkbox">` dentro del bloque
   *Región que ve*, ni su listener, ni su lectura al guardar. Se quita, sin botón ni control de respaldo.
2. **La lista pasa a arrancar con «Todas las regiones»** (valor vacío), que es como se llama la opción en el
   resto de la intranet; antes ese renglón decía «Sin región (ve todas)». Después siguen las regiones del
   Programador con su cuenta de hospitales.
3. **Al guardar se manda siempre `{ region, todas: false }`.** Elegir «Todas las regiones» manda la lista
   vacía y el servidor borra el renglón, que ya era el camino de «ve todas».
4. **Compatibilidad con lo ya guardado:** el campo `todas` sigue en `gestion.regionesPorUsuario`, en la API y
   en las reglas de `VeTodasRegiones`, porque los renglones de la entrega anterior pueden traerlo marcado. Un
   renglón así se abre en «Todas las regiones» —que es lo que de verdad estaba viendo— y la columna ya decía
   «Todas».
5. **La nota dice lo que el servidor contesta.** Con una región sin hospitales avisaba «no le limitaría», pero
   el `PUT` la rechaza; ahora avisa que no se puede asignar hasta que el Programador le escriba hospitales.
6. **Textos del servidor:** `GuardarRegionPorUsuario` habla de «elija «Todas las regiones» en la lista» y la
   bitácora de `AsignarRegion` anota «todas las regiones» cuando el renglón se quita (antes «sin región»).

**Archivos modificados:** `wwwroot/admin/usuarios/index.html`, `Services/Catalogos.cs`, `Program.cs`,
`README.md`, `docs/REGISTRO-CAMBIOS.md`.

**Verificación:** arnés **nuevo** `pantalla-usuarios.js` **40/40** (ejecuta el guion real de la página sobre un
DOM mínimo, con un servidor falso que espeja `GuardarRegionPorUsuario`: root rechazado, región inexistente,
región sin hospitales y lista vacía que borra el renglón). Sigue verde el arnés .NET **83/83** y los cuatro de
pantalla (**91 + 40 + 30 + 19**). En Chrome real contra `server-fake.js` (sesión root): la casilla ya no existe
en el DOM, la lista trae `Todas las regiones | CMN La Raza · 2 hospitales | HRAA · 2 hospitales`, la nota se
actualiza al cambiar la selección, el `PUT` que sale es `{"region":"CMN La Raza","todas":false}` (visto en el
log del servidor), volver a «Todas las regiones» deja la columna en «Todas», root sale con la lista bloqueada y
la consola queda sin errores. **Sin clic físico**: el navegador integrado no dio superficie visible
(`NATIVE_BROWSER_VIEWPORT_UNAVAILABLE`), los clics se dispararon sobre el DOM real con `evaluate_script` y la
captura se tomó con Chrome sin interfaz.

**Despliegue:** viaja con la 3.ª entrega, que sigue pendiente del reinicio elevado; hay que volver a armar el
staging (`dotnet publish`) antes del copiado.

---

## 2026-11-18/19 — **Regiones médicas (3.ª entrega)**: lista plegable, región por usuario y catálogos en todas partes

**Módulo:** pantalla del Programador, pantalla de Usuarios, calendario (+ Nuevo) e inventario de equipos, y
su lógica en el servidor (`Services/Catalogos.cs`, `Program.cs`, `wwwroot/js/gestion.js`).
**Tipo:** backend + API + frontend + configuración. **Base de datos:** ninguna tabla nueva (el catálogo sigue
en la tabla `configuracion`). **Permisos:** ninguno nuevo; la asignación de región la hace quien tiene
`usuarios.administrar`.

**Por qué:** pidió tres cosas de una vez: «SOLO APAREZCA LA REGION, AL SELECCIONAR LA REGION DESPLIEGUE LOS
HOSPITALES QUE TIENE», «QUE A LOS USUARIOS SE LES PUEDA ASIGNAR UNA REGION PARA QUE SOLO PUEDAN VER ESA REGION
O TODAS EN CASO DEL SUPERUSUARIO» y «TODO LO QUE VAYA LIGADO A HOSPITAL/SERVICIO USE LOS HOSPITALES Y SERVICIOS
QUE EXISTEN». Al acotar el alcance respondió: la región asignada debe notarse en listas y filtros, en las
exportaciones a Excel, al dar de alta y en la pantalla del Programador; que root ve todas y que eso se pueda
configurar en los perfiles; que la región se asigne **en la pantalla Usuarios**; y en el Programador «lista de
regiones; al seleccionarla salen sus hospitales».

**Qué cambió:**

1. **Programador: una línea por región.** La lista ya no pinta tarjetas con sus hospitales siempre a la vista:
   cada renglón lleva el nombre (con su lista de sugerencias), cuántos hospitales tiene y dos botones
   («Ver hospitales»/«Ocultar» y «Quitar»). Al hacer clic en el nombre —o con «Ver hospitales»— se despliega
   **debajo de la lista** el panel con los hospitales de esa región, uno solo a la vez; la línea activa se
   marca con el borde dorado. Elegir en el campo una región ya guardada trae sus hospitales. Sigue sin haber
   filas vacías de cortesía y se conservan las dos ventanas emergentes de la entrega anterior.
2. **Región por usuario (nueva clave `gestion.regionesPorUsuario`).** En el almacén de configuración se guarda
   `{ usuario: { region, todas } }`. Deliberadamente **no** está en `ConfigKeys.DeCatalogo`: tiene su propio
   escritor y `GuardarLista` la rechaza, para que nadie la borre al editar otra lista.
3. **Endpoints nuevos**: `GET /api/usuarios/regiones` (regiones + desglose, para el desplegable de Usuarios sin
   depender de `/api/programador/catalogos`, que es de root), `GET`/`PUT /api/usuarios/{usuario}/region`
   (lectura limpia y asignación con validación: la región tiene que estar en la lista del Programador, se guarda
   con su ortografía exacta y se rechaza una región sin hospitales), y `DELETE` de usuario borra también su
   renglón de región. Cada asignación queda en la bitácora con la acción nueva `AuditEvents.AsignarRegion`.
4. **La lista de usuarios ya trae la región**: `GET /api/usuarios` devuelve además `region`, `verTodas` (lo
   marcado en el perfil) y `veTodas` (lo que de verdad ve, cierto para root y para quien no tiene región).
5. **Quién ve qué** (`HospitalesPara`, `ErrorRegionDe`, `FiltraPorRegion`, `RegionDeLista` en
   `Services/Catalogos.cs`): root ve todas; un usuario sin renglón, con `todas: true` o con región vacía ve
   todas; **mientras el Programador no haya guardado ninguna región nadie está limitado** (despliegue seguro);
   una región cuyo desglose quedó vacío no limita; `General` (Almacén/Playbook) nunca se limita; y los códigos
   de hospital en blanco o fuera del catálogo siguen siendo visibles para no esconder datos históricos.
6. **Dónde se nota el filtro**: `/api/permisos/mios` manda `hospitales` ya recortados, `hospitalesConGeneral`,
   `region`, `veTodasRegiones` y `catalogos` pasado por `ParaFormularios(hospitales)` —así el desplegable de
   unidad, la lista de servicios por hospital y la derivación región→hospital que reciben todas las pantallas
   salen de lo que ese usuario puede ver—. En el servidor quedan filtrados además los listados y las
   exportaciones de `reportes`/Excel, `Gestion`, `Inventario`, `Almacen` y `Playbook`, y el alta rechaza un
   hospital de otra región con `ErrorRegionDe`.
7. **Pantalla Usuarios**: columna **Región** (nombre, o «Todas» —«Todas (root)» en el superusuario—, con el
   aviso «sin hospitales» cuando la región todavía no limita) y bloque en el diálogo Editar con **una sola
   lista de región** (ver el ajuste del 19: «Todas las regiones» es su primer renglón, ya no hay casilla) y
   una nota viva que dice cuántos hospitales verá y cuáles. A root se le muestra la lista bloqueada en
   «Todas las regiones». Una región guardada que ya no está en la lista se sigue mostrando, marcada
   «(ya no está en el catálogo)», para no borrarla de paso.
8. **Inventario de equipos** (`wwwroot/gestion/inventario-equipos/index.html`): el filtro de unidad, la etiqueta
   de la tabla, los destinos de la hoja de salida y los campos Unidad/Servicio de las ventanas de alta, edición
   y salida ya no llevan hospitales escritos a mano — salen de `Gestion.opcionesHospital`/`serviciosDe`, con
   «General» presentado como «Almacén» y delante. El servicio depende de la unidad elegida y se oculta donde no
   hay servicios configurados; un valor viejo fuera de catálogo viaja marcado y **no se pierde al guardar** (el
   campo oculto llega como `undefined` y se conserva lo anterior).
9. **Calendario (+ Nuevo)**: el desplegable Región solo ofrece las regiones que de verdad traen hospitales al
   usuario (`G.opcionesRegion` en `wwwroot/js/gestion.js`). Antes un usuario de una región podía elegir otra y
   se quedaba con el campo Hospital vacío.

**Por qué se movió una línea de Program.cs:** `var json` (opciones de serialización) nacía en la sección del
módulo Gestión y el endpoint nuevo de `/api/usuarios/regiones` lo usaba antes de existir —error CS0841, que
solo aparece al compilar el proyecto completo, no en el arnés—. Su declaración subió justo antes del grupo
`/api/usuarios`, con su comentario.

**Archivos modificados:** `Program.cs`, `Services/Catalogos.cs`, `Services/Config.cs`, `Services/Gestion.cs`,
`Services/Inventario.cs`, `Services/Almacen.cs`, `Services/Playbook.cs`, `Models/Audit.cs`,
`wwwroot/js/gestion.js`, `wwwroot/admin/programador/index.html`, `wwwroot/admin/usuarios/index.html`,
`wwwroot/gestion/inventario-equipos/index.html`.

**Verificación:** 83 comprobaciones del arnés .NET (`%TEMP%\regiones-prueba`, sobre los archivos reales del
proyecto: asignación, ortografía guardada, `HospitalesPara`, `ErrorRegionDe`, `FiltraPorRegion`,
`ParaFormularios`, root, «ver todas», desasignar, región sin hospitales y almacén ilegible) y 180 de pantalla
(`pantalla-regiones.js` 91, `inventario-equipos.js` 40, `gestion-regiones.js` 30, `dialogo-regiones.js` 19).
Además paso por Chrome real contra una API falsa en local (`server-fake.js`, sesiones root y «HRAA»): las
cuatro pantallas abren, la columna y el diálogo de región funcionan de punta a punta, el Programador pliega y
despliega, las dos ventanas aceptan varias filas, y el inventario y el calendario muestran solo las unidades y
servicios de la región de la sesión. Consola sin errores. **No hubo clic físico**: el navegador integrado no
dio superficie visible (`NATIVE_BROWSER_VIEWPORT_UNAVAILABLE`), así que los clics se dispararon sobre el DOM
real de Chrome con `evaluate_script`.

**Despliegue:** **pendiente, a un clic.** La compilación sale limpia y el `dotnet publish` ya está hecho y
apartado en `%TEMP%\publish-staging` (binario + `wwwroot`, sin `App_Data` para no pisar los catálogos vivos).
Falta solo el paso elevado — detener `MeditechnosysIntranet`, copiar ese staging sobre `publish\` y volver a
arrancar — que el entorno de trabajo no puede ejecutar por su cuenta (bloquea parar/reiniciar el servicio de
producción); hay que abrirlo como Administrador con clic derecho → *Ejecutar como administrador* sobre
`actualizar.bat` (republica él mismo) o sobre `%TEMP%\regiones-prueba\despliegue-regiones.cmd` (copia el
staging ya compilado y deja el detalle en `%TEMP%\regiones-prueba\despliegue.log`). Los cuatro archivos de
`wwwroot` **no** se copiaron todavía a `publish\wwwroot` a propósito: sin el backend publicado la pantalla de
Usuarios se queda sin `GET /api/usuarios/regiones` — comprobado en vivo, el build que corre hoy responde
`405` a esa ruta y `404` a `/api/usuarios/{usuario}/region`. Se copian en el mismo paso del reinicio.

---

## 2026-11-18 (noche) — **Regiones médicas**: los dos botones abren su ventana emergente

**Módulo:** pantalla del Programador — `wwwroot/admin/programador/index.html` (solo frontend; `Program.cs`,
`Services/Catalogos.cs` y `wwwroot/js/gestion.js` no tocaron en esta pasada).
**Tipo:** frontend. **Base de datos:** ninguna. **API:** ninguna (siguen `PUT /api/programador/lista` con la
clave `gestion.regiones` y `PUT /api/programador/regiones`). **Permisos:** ninguno nuevo.

**Por qué:** con la tarjeta ya sin filas vacías pidió «QUIERO QUE AL SELECCIONAR LA REGION DESPLIEGUE LOS
HOSPITALES Y AL AGREGAR LO MANDE A UNA VENTANA EMERGENTE». Al preguntarle el alcance respondió «Los dos
botones» —tanto **+ Agregar región** como **+ Agregar hospital**— y «Varios antes de aceptar» —en la ventana
se llenan varias filas y entran todas juntas.

**Qué cambió:**

1. **Ventana para cada botón** (`ventana(cfg)`): un `<dialog class="modal">` con su `<form class="modal-form">`
   —`h3`, intro `.hint`, cuerpo, `p.form-error[role=alert]` y `.modal-actions` con Cancelar y el botón azul—,
   con las mismas clases de `panel.css` que el diálogo **+ Nuevo** del calendario, así que se ve igual que el
   resto de la intranet. Se arma a mano y no con `G.formDialog` porque dentro no hay campos fijos sino filas
   que salen con su propio botón.
2. **«Nueva región médica»** (`ventanaRegion()`): campo nombre con `list="sugerenciasRegion"` y una fila lista
   por delante; «+ Agregar otro hospital» saca las demás. Al elegir una región guardada **se despliega su
   desglose dentro de la ventana** («Se trajo 1 hospital guardado de «X»…» / «Se trajeron los N hospitales
   guardados de «X»…», y «Región nueva: sus hospitales se escriben abajo» si no lo está). Al aceptar entra la
   tarjeta completa; **Cancelar no deja nada**.
3. **«Agregar hospitales a «X»»** (`ventanaHospitales(i)`): varias filas, una sola aceptación, y la nota dice
   cuántos lleva la región y cuántos caben de los 40. El botón de la tarjeta ya no agrega una fila: abre la
   ventana, y si la región ya llegó al tope avisa en pantalla sin abrirla.
4. **Los rechazos se quedan dentro de la ventana** (`cfg.aceptar` lanza y el `submit` no la cierra): sin
   nombre, sin ningún código, el mismo código escrito en otra región, la región ya presente en pantalla o más
   de 40 hospitales. Un código repetido dentro de la propia ventana no es un error: se une en una sola fila al
   aceptar. Nada de lo escrito se pierde y no se llama al servidor.
5. **Lo escrito en la ventana es un borrador**, como se pidió: las tarjetas no se tocan hasta aceptar, y al
   aceptar se reescriben con `pintaFichas()` y queda el aviso de qué entró. Teclear en una fila de la tarjeta
   sigue sin crear filas.
6. **Dos fallos que sacaron las pruebas:** (a) `api.filas` no se asignaba en `ventanaHospitales`, así que
   aceptar reventaba con `Cannot read properties of undefined (reading 'querySelectorAll')`; (b) la ventana
   cancelada se quedaba **colgada en el documento** porque su limpieza dependía solo del evento `close` —que
   el navegador manda al actualizar la pantalla y aquí no llegaba—: ahora `quita()` se llama también al cerrar
   y al aceptar, y quitarla dos veces no hace nada. Se aprovechó para escribir los avisos en español —«Se
   agregó 1 hospital…» / «Se agregaron 3 hospitales…», con el helper `pluralH`— en lugar de «hospital(es)».
7. **Comprobaciones.** El arnés de pantalla (`pantalla-regiones.js`) se reescribió en torno a las ventanas —12
   escenarios, **91/91 OK**—: ventana que no pinta tarjeta hasta aceptar, Cancelar limpio y sin nodo colgado,
   varias filas de una sola vez, un código repetido dentro de la ventana que cuenta una sola vez, los rechazos dentro de la ventana (sin nombre, sin códigos, el `HGO` de otra
   región, la región ya en pantalla y el tope de 40), desplegar el desglose eligiendo la
   región, el tope de 40 aguantado por la ventana y por la tarjeta, los envíos al guardar, el código nuevo que
   entra al catálogo y la carga con basura en el almacén. Se volvieron a pasar sin tocarlas: servidor .NET
   **56/56**, `gestion.js` **23/23** y diálogo **+ Nuevo** **19/19** —189 comprobaciones, todas OK—.
8. **Pasada en Chrome real** contra el servidor local de pruebas (puerto 8897) que sirve esta misma pantalla
   con catálogos falsos: los dos botones abren su `<dialog>`, Cancelar deja cero ventanas y cero nodos, la
   ventana de la región despliega «hraa» con su `HGO` y frena aceptar, la tarjeta sale con `HI,HMC` al aceptar
   dos filas juntas, la ventana de hospitales rechaza el vacío y el `HGO` ajeno y luego deja
   `HES,HGR,ZZZ,YYY`, y los `PUT` que anotó el servidor fueron exactamente
   `{"clave":"gestion.regiones","valores":[…]}` y `{"porRegion":{…}}`. Consola limpia. La captura con
   `take_screenshot` sigue sin poder tomarse (el navegador embebido no tiene viewport: `requestAnimationFrame`
   no llega a resolverse), así que la verificación fue DOM real + clics y eventos `change`/`submit` por
   `evaluate_script`.
9. **Publicado sin recompilar:** solo cambió un archivo estático, así que se copió
   `wwwroot/admin/programador/index.html` sobre `publish/wwwroot/admin/programador/index.html` (SHA-256
   `49266a4f8f2893d6…`, 52 929 bytes, ambas copias idénticas) y no hizo falta `dotnet publish` ni reiniciar el
   servicio. `https://10.252.252.200:5024/admin/programador/index.html` sigue respondiendo `302 → /Login`
   (la pantalla está tras el login). La pantalla no lleva versión en el nombre, así que en el equipo hay que
   abrirla con **Ctrl+F5** una vez.

---

## 2026-11-18 (tarde) — **Regiones médicas**: ni una fila vacía, y la región se despliega al elegirla

**Módulo:** pantalla del Programador — `wwwroot/admin/programador/index.html` (solo frontend; ni `Program.cs`,
ni `Services/Catalogos.cs`, ni `wwwroot/js/gestion.js` cambiaron en esta pasada).
**Tipo:** frontend. **Base de datos:** ninguna. **API:** ninguna (siguen `PUT /api/programador/lista` con la
clave `gestion.regiones` y `PUT /api/programador/regiones`). **Permisos:** ninguno nuevo.

**Por qué:** sobre la tarjeta de la entrega de la mañana —que salía con cuatro filas y las hacía crecer
solamente al teclear en la última— dijo: «NO DEBE GENERAR UNA FILA VACIA PARA ELLO ESTA EL BOTON DE
AGREGAR, Y QUIERO QUE CUANDO SE SELECCINE LA REGION DESPLIEGUE LOS HOSPITALES», y al preguntarle cómo
quería la tarjeta respondió «Cero filas: solo el botón» y «En Programador: elegir la región y que salgan
sus hospitales».

**Qué cambió:**

1. **Cero filas por delante.** Se quitó `FILAS_NUEVA = 4` y el oyente `input` que agregaba una fila al
   teclear en la última: la tarjeta nueva sale **solo con el nombre** y su botón **+ Agregar hospital**,
   que es ahora la única manera de sacar filas (una por clic, con el tope de 40 ya avisado en pantalla:
   «Una región no puede tener más de 40 hospitales.»). La ayuda del bloque se reescribió para decirlo:
   «Las filas nunca salen vacías por sí solas: para escribir otro hospital use + Agregar hospital».
2. **El nombre de la región se elige de una lista.** Campo nombre con `list="sugerenciasRegion"` (un
   `<datalist>` nuevo, alimentado con `gestion.regiones`), y al elegir —o escribir— una región ya
   guardada se **despliegan abajo sus hospitales** tal como están en el servidor, con su aviso: «Se
   desplegaron los hospitales guardados de «X». Use + Agregar hospital para escribir más.» Si el nombre no
   corresponde a ninguna región guardada, las filas escritas se quedan intactas.
3. **La pantalla ahora conserva lo que trajo el servidor** (`datos.regiones` y `datos.desglose`), y al
   guardar con éxito los refresca con la respuesta, así que elegir una región recién creada despliega lo
   que ella dejó sin recargar la página.
4. **Un fallo que sacó el arnés al reescribir la carga:** las tarjetas se armaban *dentro* del objeto
   `datos`, cuando todavía estaba vacío, así que las regiones guardadas salían sin sus hospitales. Se arma
   `datos` primero y las tarjetas después, con un helper único
   (`hospitalesDeRegionGuardada`, que busca el nombre sin distinguir mayúsculas y descarta los códigos que
   ya no están en el catálogo). El mismo criterio deja de poner la fila en blanco de cortesía al recargar.
5. **Comprobaciones.** El arnés de pantalla (`pantalla-regiones.js`) se reescribió con estos
   comportamientos —11 escenarios, **65/65 OK**—: tarjeta sin filas, una fila por clic, teclear en la
   última no crea nada, tope de 40 con aviso, elegir región despliega las suyas, nombre nuevo no toca lo
   escrito, repetida dentro y entre tarjetas, borrar fila sin correr las demás, código nuevo que entra al
   catálogo y carga con basura en el almacén. Se volvieron a pasar sin tocarlas: servidor .NET **56/56**,
   `gestion.js` **23/23** y diálogo **+ Nuevo** **19/19** —163 comprobaciones, todas OK—.
6. **Pasada en Chrome real** contra un servidor local que sirve esta misma pantalla con catálogos falsos
   (`server-fake.js`): las dos regiones guardadas salen cada una con sus hospitales y **ninguna fila
   vacía**; «+ Agregar región» pinta la tarjeta con 0 filas; «+ Agregar hospital» da exactamente 1 y 2
   filas; escribir en la última deja 2 filas (no crece); elegir «hraa» de la lista despliega `HGO` con su
   aviso; guardar con la región repetida avisa «está escrita dos veces» **sin llamar al servidor**; y el
   guardado correcto mandó `{"clave":"gestion.regiones","valores":["CMN La Raza","HRAA","CMN Sur"]}` y
   `{"porRegion":{"CMN La Raza":["HES","HGR"],"HRAA":["HGO"],"CMN Sur":["HMC"]}}`, con `HMC` ya en las
   sugerencias y en el desplegable de *Servicios por hospital*. Consola sin errores. Los clics se
   dispararon sobre el DOM real de Chrome (el panel del navegador integrado no tenía viewport adjunto),
   no con el arnés de Node.

**Verificación en producción:** es archivo estático — solo cambió `wwwroot/admin/programador/index.html`—,
así que se copió a `publish\wwwroot\admin\programador\index.html` sin `dotnet publish` y sin reiniciar el
servicio: las dos copias quedaron byte a byte iguales (SHA-256 `0bc29c3079710479…`, 40 357 bytes) y
`https://10.252.252.200:5024/admin/programador/index.html` sigue respondiendo 302 a `/Login` para quien no
tiene sesión. Al abrir la pantalla con Ctrl+F5 se ve la tarjeta sin filas y la lista del campo nombre.

**Nota para la entrada de la mañana:** su punto 5 («al agregar la tarjeta salen cuatro filas… y al teclear
en la última crece sola una más») quedó superado por esta pasada; el resto de esa entrada —claves, servidor,
API, `gestion.js` y el arreglo de `inp.list`— sigue vigente.

---

## 2026-11-18 — **Regiones médicas** en Programador: tarjetas abiertas con sus hospitales escritos

**Módulo:** catálogos de Gestión — `Services/Config.cs`, `Services/Catalogos.cs`, `Program.cs`,
`Models/Catalog.cs`, `wwwroot/admin/programador/index.html`, `wwwroot/js/gestion.js` y las doce pantallas
que cargan ese guion (`?v=20261118a`).
**Tipo:** backend + API + frontend + configuración. **Base de datos:** ninguna tabla nueva (el desglose
vive en la tabla `configuracion`, columna `valor` ya ampliada a `TEXT`). **API:** `PUT
/api/programador/regiones` y las claves `regiones` / `hospitalesPorRegion` / `regionPorHospital` en las
respuestas de catálogos. **Permisos:** ninguno nuevo; sigue `programador.administrar` (`SoloRaiz`).

**Por qué:** pidió «una opción de agregar región médica, después de ello se pueda configurar por
hospitales (ejemplo: CMN La Raza y su desglose son los hospitales que aparecen en la intranet)». Primero
salió con casillas por hospital; pidió después «sin necesidad de un check box», o sea escribiéndolos. Y
al ver la tarjeta funcionando pidió lo último de esta entrega: «cuando se agregue la región despliegue el
de agregar hospital, y si se edita despliegue editar región o editar hospital, y el apartado de
Hospitales quítalo», con la condición de que «esta configuración debe guardarse, incluso si se apaga el
servidor o reinicia».

**Qué cambió:**

1. **Dos claves nuevas de configuración** (`Services/Config.cs`): `gestion.regiones` (los nombres, en el
   orden en que se escriben) y `gestion.hospitalesPorRegion` (`{ "CMN La Raza": ["HES","HGR"], … }`).
   Vacías por defecto: mientras no se configure nada, el diálogo **+ Nuevo** se ve exactamente igual que
   antes y no existe el campo **Región**.
2. **La región se deriva, no se guarda.** `GestionCatalogos` invierte el desglose (`RegionPorHospital()`)
   y `ParaFormularios()` manda solo esa derivación —nunca el desglose completo—, así que el alta sigue
   escribiendo la misma fila de siempre con su hospital. Reorganizar una región reordena también lo ya
   capturado, sin tocar un registro. `Hospitales.cs` no cambió: HES/HGR/HGO/HI siguen viniendo del código.
3. **Un hospital, una sola región.** `GuardarHospitalesPorRegion` (`Services/Catalogos.cs`) rechaza el
   código repetido nombrando a las dos regiones, y la pantalla lo frena antes de llamar al servidor. Las
   regiones sin hospitales se dejan fuera del JSON (quedan «Sin región»); borrar todo el desglose se hace
   enviando las regiones con su lista vacía, nunca mandando un objeto vacío.
4. **Lo escrito en la región entra al catálogo.** Un código que no estaba en `gestion.hospitales` se
   agrega ahí mismo al guardar —con `cfg.Set` directo y no con `GuardarLista`, que recortaría el desglose
   que se está escribiendo—, se valida (hasta 10 caracteres, solo letras y números, en mayúsculas, y
   «General» rechazado porque es etiqueta del Playbook) y `AplicarHospitales()` lo deja vigente en el
   resto del servidor en el mismo guardado. La bitácora queda con dos renglones: el desglose y los
   códigos nuevos (`gestion.hospitales += … (desde el desglose de regiones)`).
5. **La pantalla, en tarjetas abiertas** (`wwwroot/admin/programador/index.html`): un bloque *Regiones
   médicas* encima de *Servicios por hospital*, una tarjeta por región con el nombre arriba y los
   hospitales escritos debajo. Al agregar la tarjeta salen **cuatro filas listas** (`FILAS_NUEVA = 4`) y al
   teclear en la última crece sola una más, hasta 40 (`MAX_HOSPITALES`); al editar una región guardada
   vuelven sus hospitales **más una fila en blanco**, así que nunca hay que desplegar nada. Cada fila
   ofrece las sugerencias del catálogo en un `<datalist>` pero acepta cualquiera. Un solo botón —**Guardar
   región y hospitales**— manda la lista de nombres y luego el desglose; la pantalla avisa de lo que
   agregó al catálogo y mantiene las sugerencias y el desplegable de *Servicios por hospital* al día sin
   recargar.
6. **El bloque *Hospitales* desapareció de la pantalla** (era lo que pidió): se quitaron su `<section>`,
   sus botones `data-agrega`/`data-guarda="hospitales"` y el aviso de «fijo» en cada fila. La lista de
   hospitales del sistema se gestiona ahora escribiendo en las tarjetas; el endpoint `PUT
   /api/programador/lista` con esa clave sigue vivo por si hace falta, y el servidor sigue recortando el
   desglose cuando un código sale del catálogo.
7. **`wwwroot/js/gestion.js`** pone las regiones al servicio de los formularios: `G.hayRegiones()`,
   `G.regionDe(hospital)`, `G.hospitalesDeRegion(región)`, `G.opcionesRegion()` y `G.campoRegion()`, y en
   **+ Nuevo** el flujo es región → hospital → servicio (elegir región filtra el desplegable de hospitales
   y el servicio sigue dependiendo del hospital). Sin regiones guardadas no se pinta ningún campo nuevo.
8. **Un fallo que costó encontrar:** la fila de hospital se hacía con `inp.list = 'sugerenciasHospital'`.
   En el navegador `list` de un `<input>` es de **solo lectura**, y con el guion en `'use strict'` eso
   lanza `TypeError` a mitad del repintado: la tarjeta desaparecía sin decir nada y el botón «+ Agregar
   región» parecía no hacer caso. Ahora es `inp.setAttribute('list', …)`. El arnés de pantalla no lo había
   visto porque su `<input>` falso sí dejaba asignar la propiedad; se corrigió el arnés para que se
   comporte como el navegador y se le agregó una comprobación directa al código de la pantalla.
9. **Lo guardado sobrevive al apagar el servidor:** vive en la tabla `configuracion` de MySQL (con
   `App_Data/configuracion.json` de respaldo si el almacén falla), no en el navegador; `GestionCatalogos`
   no guarda caché —cada lectura va al almacén— y el arranque vuelve a aplicar los códigos con
   `AplicarHospitales()` (`Program.cs`, antes de atender la primera petición). La pantalla lo dice también
   en su ayuda.

**Verificación:** cuatro arneses fuera del proyecto, sobre el código real publicado
(`%TEMP%\regiones-prueba`): servidor .NET **56/56**, pantalla del Programador **54/54** (DOM falso ahora
fiel al navegador), `gestion.js` **23/23** y el diálogo **+ Nuevo** **19/19** —152 comprobaciones, todas
OK—, más la pasada en Chrome real (servidor estático local): la tarjeta sale con sus cuatro filas, la
quinta aparece sola al escribir en la última, el bloque *Hospitales* ya no está en el DOM y la consola no
registra ningún error de guion.

**Verificación en producción:** publicado y con el servicio reiniciado a las 15:23:35 (`arranque.txt` deja
la misma huella `8FEF59E6…`, así que el despliegue de regiones tampoco movió el certificado). La pantalla
es archivo estático: bastó copiar `wwwroot/admin/programador/index.html` a `publish\wwwroot\...` —queda
byte a byte igual (SHA‑256 `48d63572f49f990c…`, 37 326 bytes)— y al abrirla con Ctrl+F5 se ve la tarjeta
nueva sin reiniciar nada. `https://10.252.252.200:5024/admin/programador/index.html` sigue respondiendo
302 a `/Login` para quien no tiene sesión, como debe ser.

**Queda pendiente de días anteriores (no es de este cambio):** las dos entregas del 17 —Programador con
catálogos configurables y servicios por hospital— siguen sin renglón propio en este registro.

---

## 2026-11-18 — El certificado https **sobrevive igual a un reinicio** del servidor (y a un App_Data perdido)

**Módulo:** arranque seguro — `Services/CertificadoIntranet.cs`.
**Tipo:** backend. **Base de datos:** ninguna. **API:** ninguna nueva. **Permisos:** ninguno.

**Por qué:** pedida la garantía de que «aunque el servidor se reinicie, la conexión o el certificado no se
vean afectados». El arreglo de la mañana (ya no rota por una IP del DHCP) deja el certificado quieto en un
reinicio, pero quedaba un hueco: el trío vive en `publish\App_Data\https`, y esa carpeta se aparta y se
devuelve en cada `publicar.bat`, se pierde si el proyecto cambia de ruta o de unidad, o se daña a medio
copiar. En cualquiera de esos casos el arranque firmaba **un certificado nuevo** —la huella cambia siempre,
porque la llave es otra— y dejaba fuera al Quemador (fijado a la hojita vieja) y a todas las PC con el
navegador ya confiado, justo el mismo síntoma que se había resuelto horas antes.

**Qué cambió:**

1. `Asegura` ya no pasa directo de «no se pudo leer el de App_Data» a «firmar uno nuevo»: antes de eso
   busca un espejo del trío y, si lo encuentra, lo copia de vuelta y reintenta cargarlo. El nuevo
   `TraerDelRespaldo` escribe con archivo suelto y cambio de nombre (`Copiar`), para no dejar un `pfx` a
   medio copiar, y `Respaldar` mantiene el espejo al día en cada arranque —solo reescribe lo que de verdad
   cambió (`Iguales` por largo y SHA‑256), así el arranque diario no gasta disco—. Un fallo escribiendo el
   espejo solo se avisa: la intranet sirve igual.
2. El espejo vive **fuera del proyecto**: `C:\ProgramData\Meditechnosys\Intranet\https` (`CarpetaRespaldo`,
   permisos heredados: `NT AUTHORITY\SYSTEM` tiene control total, que es la cuenta del servicio). Con
   `INTRANET_RESPALDO_CERTIFICADO` se puede poner otro destino —por ejemplo una carpeta de red donde también
   copie— sin recompilar.
3. Cada arranque deja su renglón —«certificado https reutilizado / restaurado del respaldo / creado
   (huella …, vence …)»— en `arranque.txt`, junto al espejo (`EscribirArranque`, un `AppendAllText` con
   fecha y hora que si no se puede escribir no rompe nada). Se puso ahí y no en el visor de eventos porque
   se comprobó que el visor **solo** guarda los mensajes del servicio («Service started/stopped
   successfully», 11 renglones en dos días, ningún Information de la aplicación), así que la nota que ya
   existía nunca llegaba a leerse. Antes, cuando todo estaba bien, el arranque no dejaba prueba alguna: la
   garantía de que un reinicio no cambió el certificado era el silencio. Ahora es una huella escrita.
4. Se ajustaron los avisos de `Intenta` («toca rehacerlo», «se busca en el respaldo») para que describan lo
   que de verdad intenta el arranque, y se documentó la regla nueva en el comentario de clase.

**Verificación:** arnés aparte (`%TEMP%\cert-reinicio`, compila el `CertificadoIntranet.cs` real contra el
trío copiado de producción y nunca toca el original; el espejo se desvía a una carpeta prestada con la
variable de entorno). Diecisiete comprobaciones, todas OK: arranque normal → reutiliza la huella
`8FEF59E6…` y llena el espejo con la misma hojita; App_Data borrado → **la misma huella**, con la nota
«restaurado del respaldo»; `intranet.pfx` corrompido → otra vez la misma huella y el archivo quedó reparado;
servidor nuevo sin espejo → crea (huella distinta), deja espejo y al segundo arranque lo reutiliza; pérdida
total → crea y recrea el espejo; y el renglón de `arranque.txt` sale con fecha, hora y huella.

**Verificación en producción (publicado y con el servicio reiniciado dos veces):** las SHA‑256 de
`publish\App_Data\https` (`3bfe6a09…` el `pfx`, `bf4d1ddf…` el `cer`) son las mismas antes del despliegue
(10:01), después de publicar, después del arranque de las 10:06 y después del de las 10:07 —no rotó— y
`arranque.txt` quedó con los dos renglones «certificado https reutilizado (huella 8FEF59E67B3AE0CD…, vence
16/11/2036)», huella idéntica en ambos. `https://10.252.252.200:5024/api/dicom/status` responde 401 (TLS
armado, falta login), `http://10.252.252.200:5023/intranet.cer` baja los mismos 856 bytes y el arnés del
Quemador pasa con su hojita fijada. La carpeta `publish` quedó limpia (solo `App_Data`, `dicom_downloads`,
`wwwroot`).

**Del lado del Quemador (paquete 1.2.18, `D:\VisorLigero_y_Quemador_17`):** el reinicio ya no deja el
indicador vendido. Mientras la conexión esté caída, un reloj revisa cada 20 s en silencio —sin ventanas, sin
molestar una grabación en curso— y el letrero vuelve solo a «Conexión exitosa» cuando la intranet regresa;
en modo silencioso no abre la ventaneta de sesión, solo dice en la barra que hace falta, y la ventaneta sale
cuando él pida algo. Desplegado en `DicomQrDesktop\publish` (el 1.2.17 quedó como
`DicomQrDesktop.exe.1.2.17`) y en `Instalador Meditech\archivos`, ambos con la `intranet.cer` vigente
(`bf4d1ddf…`); la conexión real con esa hojita fijada vuelve a dar 401 (TLS válido, falta login).

**Pendiente de esta entrega:** la prueba de un **reinicio completo de la PC del servidor** —acordada como
suficiente con reiniciar el servicio—; al hacerla solo hay que leer `arranque.txt` y confirmar que el
renglón nuevo trae la misma huella `8FEF59E6…`.

---

## 2026-11-18 — El certificado https **ya no se rehace** cuando cambia una IP que da el DHCP

**Módulo:** arranque seguro — `Services/CertificadoIntranet.cs`, `appsettings.json` (sección `Https`).
**Tipo:** backend + configuración. **Base de datos:** ninguna. **API:** ninguna nueva. **Permisos:** ninguno.

**Por qué:** el Quemador DICOM marca «No se pudo conectar» contra `https://10.252.252.200:5024`. La IP del
hospital (`10.252.252.200`, Ethernet, **manual**) no cambió nunca; lo que cambió solo, por arriendo DHCP, fue
la del wifi del servidor (`192.168.100.14` → `192.168.100.7`). `CertificadoIntranet` comparaba el certificado
guardado contra **todas** las direcciones de hoy y, al faltar la nueva, firmaba uno distinto (huella
`37181CF6…` → `8FEF59E6…`). El Quemador fija su confianza byte por byte en la hojita `intranet.cer` instalada
junto al ejecutable, así que rechazó el certificado nuevo; en cada PC con navegador había que volver a correr
`confiar-certificado.bat`.

**Qué cambió:**

1. Se separaron los dos conjuntos. `DireccionesDeseadas` (lo que entra en un certificado **nuevo**) sigue
   siendo localhost, el nombre del equipo, sus IPv4 y los alias. El nuevo `DireccionesFijas` —localhost,
   `127.0.0.1`, nombre del equipo y los alias de `Https:Nombres`— es el único que se le exige al certificado
   **guardado**; `Intenta` ahora recibe y compara esos fijos. Una IP efímera que desaparece se queda dentro del
   certificado sin molestar: sacarla no aporta nada y rehacerlo rompe la confianza de las PC.
2. `Https:Nombres` pasa de `[]` a `[ "10.252.252.200" ]`, para que la dirección con la que se entra a la
   intranet sí sea un punto fijo: si esa llegara a cambiar, el certificado se renueva como debe.
3. Comentarios de clase y de `OpcionesHttps.Nombres` reescritos con la regla nueva.

**Verificación:** programa aparte (`%TEMP%\cert-fija`) que compila el archivo real y prueba tres casos —wifi
sin cubrir con la fija cubierta (no reescribe, huella intacta), falta la fija (renueva y avisa por qué) y el
certificado real del servidor (no lo toca)—. Desplegado y reiniciado el servicio: `intranet.pfx` y
`intranet.cer` conservan las mismas SHA‑256 de antes del despliegue (`3bfe6a09…` / `bf4d1ddf…`), o sea no rotó;
`GET /intranet.cer` entrega ese mismo archivo y `https://10.252.252.200:5024/api/dicom/status` responde 401
(TLS válido, falta login) usando la hojita fijada como autoridad.

**Pendiente del mismo día (no es de este cambio):** las dos entregas del 17 (Programador y servicios por
hospital) no quedaron anotadas aquí.

---

## 2026-11-06 (decimoquinto cambio) — «Consulta lista de trabajo»: **una sola conexión** para consultar y enviar, **verificación en el PACS** después de cada prueba, **registro permanente** de envíos y **filtro por número de acceso**

**Módulo:** DICOM — `Dicom/WorklistService.cs`, `Dicom/WorklistModels.cs`, `Dicom/DicomPruebaStore.cs` (nuevo),
`Dicom/Controllers/WorklistController.cs`, `Program.cs`, `Pages/ListaTrabajo.cshtml`,
`wwwroot/js/lista-trabajo.js`, `wwwroot/css/lista-trabajo.css`.
**Tipo:** backend + frontend. **Base de datos:** ninguna. **Permisos:** ninguno nuevo. **API:** nueva ruta
`GET /api/dicom/worklist/envios`; `POST /api/dicom/worklist/prueba` **deja de aceptar** la segunda conexión de Storage
que traía y su respuesta añade el objeto `verificacion`.

**Por qué:** captura del bloque **Conexión de destino Storage** (Host/IP `10.252.252.73`, Puerto `104`, AE remoto
`SERVEX`) con la instrucción «quiero que solo en el apartado de arriba tenga que poner los parametros y ese sea el mismo
de envio, tambien los log que pusiste no me sirven, si puedes intercepta los que llegan al servidor pacs y en los filtros
ponme numero de acceso». Preguntadas las dos decisiones sueltas, respondió **«La misma conexión, sin bloque de Storage»**
y **«Las dos cosas»** (preguntar al nodo qué guarda **y** dejar constancia en el servidor). Los logs anteriores eran una
ventana en memoria de la pestaña: se perdían al recargar y solo decían qué habíamos *intentado*, nunca qué *quedó* en el
PACS.

**Qué cambió:**

1. **Un solo sitio para escribir parámetros.** El bloque «Conexión de destino Storage» se **retiró** del DOM con sus
   campos, su Ping y su selector de nodo preconfigurado; no se sustituyó por ningún control nuevo. `WorklistTestRequest`
   ya solo lleva `Conexion` —la misma que usa el C‑FIND de la agenda y el C‑ECHO del Ping— y `SendTestAsync` envía el
   C‑STORE por ese nodo. La pantalla repite siempre el destino real en `#lt-destino`
   (`AE@host:puerto · AE local MEDI`) y, mientras arriba no haya una conexión válida, dice que todavía no la hay.
2. **Después de cada envío se le pregunta al nodo qué guarda.** `WorklistService.ConsultarPacsAsync` abre una segunda
   asociación contra el **mismo destino** y hace un **C‑FIND de Study Root a nivel STUDY** por el `StudyInstanceUID` que
   quedó escrito en la imagen (solo el UID como clave: exigir también el acceso convertiría un «no hay nada» en mentira),
   pidiendo `AccessionNumber`, `StudyDate`, `StudyTime`, `ModalitiesInStudy`, `StudyDescription` y
   `NumberOfStudyRelatedInstances`. **Nunca lanza y no afecta al envío**: tope 15 s y una frase distinta para cada caso —
   sin UID aprovechable, el nodo **no ofrece** la consulta (excepción de asociación o **DICOM 0122** *SOP class not
   supported*), **rechazó** la consulta (se cita el código), **no terminó en 15 s** (si el plazo se cumple sin respuesta
   se dice que no terminó; nunca que «no tiene nada»), respondió y **no devuelve ninguna entrada**, o **dice tener N
   entradas** con su acceso, fecha, hora, modalidades, descripción y las instancias que él cuenta. El aviso del envío la
   muestra («En el nodo: …»).
3. **Registro permanente en el servidor** (`Dicom/DicomPruebaStore.cs`). Cada prueba que pasa validación y permiso deja
   **una línea JSON** en `App_Data/dicom-pruebas.jsonl` (ruta configurable con `Dicom:PruebasFile`) con usuario, momento,
   estado, mensaje, nodo destino, los **tags** que salieron en la imagen, los **siete datos** con su `viajo`/`motivo` y la
   **verificación completa**. `Registrar` **nunca lanza** —un fallo al apuntar no rompe un envío ya ocurrido—, el texto
   viene recortado y `Listar` ordena del más reciente hacia atrás con `limite` acotado a 1–200. Lo sirve
   `GET /api/dicom/worklist/envios` (permiso `DicomVer`) y la pantalla lo pinta en la sección «Registro de pruebas
   enviadas»: tabla de cinco columnas *Hora · Usuario · Nodo destino · Resultado del envío · Qué dice el nodo que tiene*,
   contador de asientos, botón **Actualizar** y **sin botón de borrado**. Cada renglón explica dato por dato qué viajó y
   qué respondió el nodo, y **sobrevive a recargar la página y a reiniciar la intranet**. La ventana de logs en memoria, su
   panel plegable y su botón Limpiar **se retiraron** (`#lt-logs`, `#lt-storage-*` ya no existen).
4. **Filtro de Número de acceso.** Caja nueva `#lt-f-acceso` (16 caracteres, el tope del VR `SH`) entre «ID del paciente»
   y «Periodo», con su ayuda. Filtra **en el navegador sobre lo recibido**, como nombre e ID: no se pide al nodo, así que
   **no puede perder entradas**; se busca sin mayúsculas, sin acentos y por partes. Y cuando los filtros no dejan ver
   ninguna entrada la tabla **ya no desaparece**: se queda en su sitio con un renglón (`tr.lt-row-vacia`) que dice
   «Ninguna de las N entradas recibidas coincide con los filtros. Siguen aquí: quita el filtro para volver a verlas.»

**Lo que no cambió:** la imagen sigue siendo el círculo sintético de 512×512 con `ConversionType = SYN`; un solo C‑STORE
por confirmación, sin reintentos; las siete casillas de datos y el UID programado; los permisos `P:DicomVer`/`P:DicomEnviar`,
el antiforgery y el límite de 8 KB; la hoja de Periodo/Hora; `POST /api/dicom/worklist/prueba` sigue siendo la misma ruta.
**Consecuencia asumida y avisada:** el registro guarda en el servidor los valores de paciente que viajaron en cada prueba
(nombre, ID, acceso y los tres UID) —es lo que permite demostrar después qué salió—; no van a la bitácora de accesos, ni a
la base de datos, ni al navegador, y solo los lee quien tiene `DicomVer`.

**Verificado** en el entorno desechable de siempre (SCP sintético en `127.0.0.1:11140`, un segundo SCP en `11142`
**sin** Study Root, app en `127.0.0.1:5110`, **solo datos ficticios**; 5023/5024/11113, el servicio instalado y la base
real sin tocar): suite de API **164/164** (antes 139/139: la verificación devuelve el estudio recién enviado con sus
datos, cuenta las instancias que el nodo tiene del estudio programado, dice «no admitió la consulta» contra el nodo sin
Study Root, «rechazó» con su código contra el que responde `A700`, «no devuelve ninguna entrada» contra el que responde
vacío, y el registro deja una línea por envío aceptado —ni 400 ni 403 escriben—, en orden, con `limite` acotado, con los
tres UID y **sin píxeles ni datos de más**), suite de UI **127/127** (antes 110/110: un solo bloque de conexión arriba,
el destino escrito cambia al editar el nodo, filtro de acceso que no pierde entradas y su aviso con la tabla visible,
registro del servidor con sus cinco columnas legible tras recargar y en otra pestaña, y los ids retirados sin rastro en
el DOM ni en el JS), **tiempos límite 4/4** (eco 15 s, C‑STORE 30 s, consulta 60 s y **verificación 15 s**, este último
con la frase «no terminó la consulta de verificación en 15 segundos»), **regresión de la hoja compartida 30/30** y
maquetación sin desbordes a 390 px —la tabla del registro conserva su ancho mínimo dentro de su caja con `overflow:auto`—.
Capturas nuevas: `filtros-01-inicio-acceso.png`, `filtros-02-filtros-y-lista.png`, `filtros-08-registro-de-envios.png`,
`filtros-09-movil-filtros.png`, `filtros-13-movil-registro.png`, `filtros-14-escritorio-registro.png`. Nota honesta: el
entorno desechable no es un PACS Meditech; lo verificado es que la pregunta de Study Root se hace al nodo, cómo se reporta
cada respuesta y qué queda apuntado, no cómo lo lista el visor del hospital.

**Archivos creados:** `Dicom/DicomPruebaStore.cs` (los registros `DicomPrueba`, `PruebaTags`, `PruebaVerificacion` y
`PruebaEstudioPacs`, la interfaz `IDicomPruebaStore` y el almacén `JsonDicomPruebaStore`).
**Archivos modificados:** `Dicom/WorklistService.cs` (`ConsultarPacsAsync` con sus seis mensajes y el plazo de 15 s,
`SendTestAsync` verificando después del C‑STORE), `Dicom/WorklistModels.cs` (`WorklistTestRequest` sin destino de Storage
y `WorklistTestResult` con `Tags` y `Verificacion`), `Dicom/Controllers/WorklistController.cs` (`Registrar` tras cada
envío aceptado y `GET envios`), `Program.cs` (`IDicomPruebaStore` singleton), `Pages/ListaTrabajo.cshtml` (fuera el bloque
de Storage, dentro `#lt-f-acceso`, `#lt-destino` y la sección `#lt-registro`), `wwwroot/js/lista-trabajo.js` (una sola
conexión, filtro de acceso, renglón de aviso, `pintaRegistro()`/`cargaRegistro()` y fuera el módulo de logs),
`wwwroot/css/lista-trabajo.css` (`.lt-registro-table`, `.lt-row-vacia`, `#lt-destino` y fuera los estilos `lt-logs` /
`lt-storage`).
**Archivos eliminados:** ninguno (el bloque de Storage y la ventana de logs se retiraron del HTML, el JS y el CSS).

---

## 2026-11-06 (decimocuarto cambio) — Menú de **casillas por dato** para elegir qué regresa de la lista y **ventana de logs** de la sesión en «Consulta lista de trabajo»

**Módulo:** DICOM — `Dicom/WorklistModels.cs`, `Dicom/ContrastTestImage.cs`, `Dicom/WorklistService.cs`,
`Pages/ListaTrabajo.cshtml`, `wwwroot/js/lista-trabajo.js`, `wwwroot/css/lista-trabajo.css`.
**Tipo:** backend + frontend. **Base de datos:** ninguna. **Permisos:** ninguno. **API:** las mismas rutas;
el cuerpo de `POST /api/dicom/worklist/prueba` acepta cuatro campos más (`modalidad`, `procedimiento`,
`fechaProgramada`, `horaProgramada`) y una lista `campos`, y su respuesta añade el array `campos` con lo que
realmente viajó.

**Por qué:** «HAZME UN MENU PARA QUE YO PUEDA ELEGIR QUE DATOS REGRESAR Y UNA VENTANA DE LOGS». Consultada
la forma con el usuario, eligió **casillas por dato** (una por cada dato que devuelve la lista, todas
marcadas por defecto), un **panel plegable en la misma pantalla** para los logs y que el log muestre
**el resultado técnico y los datos que regresaron**. Hasta ahora la prueba sobre una entrada devolvía
íntegros nombre, ID, acceso y UID del estudio, pero la modalidad, el procedimiento y la fecha/hora
programadas **no viajaban nunca**: la imagen salía siempre con `OT`, «PRUEBA TECNICA ‑ NO DIAGNOSTICO» y la
hora del reloj, así que no había nada que elegir y nada donde ver qué había salido.

**Qué cambió:**

1. **Siete casillas, un dato cada una.** Bajo «Entrada seleccionada de la lista» aparece el bloque
   «Datos que regresan de la entrada» con Nombre, ID del paciente, Número de acceso, Modalidad,
   Procedimiento, Fecha y hora programadas y UID del estudio, todas marcadas por defecto y visibles solo con
   origen `seleccion`. Lo marcado viaja **tal cual lo devolvió el nodo**; lo desmarcado se queda con el valor
   técnico de la prueba (identidad sintética, `OT`, descripción de prueba, fecha y hora de ahora, estudio
   nuevo). La decisión la toma el **servidor**: `WorklistPruebaDatos.Campos` es la lista de claves y
   `Incluye(clave)` responde `verdadero` cuando la lista no llega (peticiones antiguas, manual y anónima no
   cambian de comportamiento). Fecha y hora comparten casilla porque son la misma cita; la **fecha de toma de
   la imagen** (`ContentDate`/`InstanceCreationDate`) sigue siendo la de ahora, eso no se disfraza.
2. **Cuatro datos nuevos que ahora sí viajan.** `WorklistPruebaDatos` recibe `Modalidad` (VR `CS`),
   `Procedimiento` (hasta 200), `FechaProgramada` (`AAAAMMDD`) y `HoraProgramada` (`HH`/`HHMM`/`HHMMSS`), cada
   uno con su validación en español; `ContrastTestImage.Create` los escribe en `Modality`,
   `StudyDescription`, `StudyDate` y `StudyTime`. El procedimiento de más de 64 caracteres (tope del VR `LO`)
   **viaja recortado y el log lo avisa** en lugar de rechazar el envío. Nombre e ID dejan de ser obligatorios
   cuando su casilla está desmarcada, y el navegador no manda lo que no cabe en el tag: `valorDe()` devuelve
   vacío y el servidor lo explica con el valor por defecto.
3. **La confirmación se recalcula con las casillas.** `pintaConfirmacion()` escribe una fila por dato: el
   valor que viaja, o «no viaja · la imagen lleva …». El aviso añade la frase de que **viajar la modalidad,
   el procedimiento o la cita hace que la prueba se parezca más a un estudio real** en las listas del PACS
   (aunque siga marcada `ConversionType = SYN`) y recuerda que lo desmarcado sale con valor técnico. Si se
   cambia una casilla con la ventana abierta, la confirmación se rehace antes de enviar.
4. **Ventana de logs de la sesión.** Panel plegable al pie de la pantalla, con contador de asientos, botones
   **Ocultar/Mostrar** y **Limpiar** y una tabla *Hora · Operación · Detalle · Resultado*. Anota cada C‑ECHO
   (con su destino), cada C‑FIND (entradas recibidas, omitidas, si fue parcial, modalidad y periodo) y cada
   C‑STORE (origen, casillas marcadas, nodo, AE local, milisegundos, estado) **más la lista de datos que
   regresaron**, sacada del array `campos` de la respuesta: `Nombre: GÓMEZ^Ana María` o `Modalidad: no viaja ·
   No marcaste esta casilla…`. Guarda los 200 más recientes en memoria de la variable `registros`; **no se
   escribe en disco, no se envía a ningún sitio, no va a la bitácora y se pierde al recargar**. Por eso puede
   contener datos de paciente: la ayuda del panel lo dice y recomienda limpiarlo o cerrar la página al terminar.
   <!-- Desde el decimoquinto cambio este punto ya no está vigente: el usuario respondió «los log que pusiste no me
   sirven», así que la ventana en memoria se retiró y en su lugar quedó el registro permanente del servidor
   (`App_Data/dicom-pruebas.jsonl`) con la verificación que contesta el propio nodo. -->

**Lo que no cambió:** la imagen sigue siendo el círculo sintético de 512×512; un solo C‑STORE por
confirmación, sin reintentos; los permisos `P:DicomVer`/`P:DicomEnviar`, el antiforgery, el límite de 8 KB y
la política de no guardar nada en el almacenamiento del navegador; la hoja de Periodo/Hora; el envío anónimo
y el manual (la prueba manual no escribe dentro del estudio programado). **Consecuencia asumida:** con todas
las casillas marcadas la prueba lleva modalidad y procedimiento reales, así que en los listados del PACS se
parecerá a un estudio clínico; para una prueba que se vea como prueba hay que desmarcar esas casillas.

**Verificado** en el entorno desechable de siempre (SCP sintético en `127.0.0.1:11140`, app en
`127.0.0.1:5110`, **solo datos ficticios**; 5023/5024/11113, el servicio instalado y la base real sin tocar):
suite de API **139/139** (antes 100/100: sin casillas nada viaja y el motivo lo explica, cada casilla sola
escribe su dato y deja el resto técnico, el recorte del procedimiento a 64, nombre/ID que dejan de pedirse
sin su casilla, 400 con modalidad en minúsculas o de 17, fecha con guiones, hora con dos puntos, procedimiento
con barra inversa o de 201, y hora imposible que no viaja; el DICOM recibido se comprueba tag por tag y por
red solo viaja el UID programado en los dos envíos que lo llevan), suite de UI **110/110** (antes 84/84:
casillas visibles solo con la entrada elegida, confirmación con doce filas y sus «no viaja», desmarcar y
re‑marcar sin recargar, el panel de logs con contador, C‑ECHO/C‑FIND/C‑STORE, el asiento más arriba, el
detalle dato por dato, Ocultar/Mostrar, Limpiar y que vuelva a anotar; cero errores de consola y ningún dato
de paciente en `localStorage`/`sessionStorage`), tiempos límite sin fallos (eco 15 s, prueba 30 s, consulta
60 s), maquetación sin desbordes a 390 px —el panel de logs conserva su tabla de 720 px dentro de su caja con
`overflow:auto`— y **regresión de la hoja compartida 30/30**. Capturas nuevas:
`filtros-06-casillas-desmarcadas.png`, `filtros-08-ventana-de-logs.png`, `filtros-12-movil-casillas.png`,
`filtros-13-movil-logs.png`. Nota honesta: el entorno desechable no es un PACS Meditech; lo verificado es qué
tags salen con cada combinación de casillas y qué apunta el log, no cómo lo lista el visor del hospital.

**Archivos creados:** ninguno.
**Archivos modificados:** `Dicom/WorklistModels.cs` (los cuatro campos nuevos con su validación, `Campos`,
`Regresables`, `Incluye()`, el registro `PruebaCampo` y el `WorklistTestResult` con `Campos`),
`Dicom/ContrastTestImage.cs` (`Create` decide dato por dato y devuelve `PruebaImagen(Archivo, Campos)`),
`Dicom/WorklistService.cs` (`SendTestAsync` propaga `campos` en los cuatro estados de resultado),
`Pages/ListaTrabajo.cshtml` (bloque `#lt-data-fields` con las siete casillas y sección `#lt-logs`),
`wwwroot/js/lista-trabajo.js` (módulo de casillas, `registrar()`/`pintaLogs()`, confirmación y avisos),
`wwwroot/css/lista-trabajo.css` (rejilla de casillas, checkbox dorado y tabla de logs).
**Archivos eliminados:** ninguno.

---

## 2026-11-06 (decimotercer cambio) — La prueba sobre una entrada de la lista se manda **dentro del estudio programado**: el destino ya no abre una segunda fila bajo el mismo número de acceso

**Módulo:** DICOM — `Dicom/WorklistModels.cs`, `Dicom/ContrastTestImage.cs`, `Pages/ListaTrabajo.cshtml`,
`wwwroot/js/lista-trabajo.js`.
**Tipo:** backend + frontend. **Base de datos:** ninguna. **Permisos:** ninguno. **API:** la ruta
`POST /api/dicom/worklist/prueba` es la misma; el cuerpo acepta ahora un campo más (`estudioUid`) y lo valida.

**Por qué:** captura del panel **CHECK** del PACS Meditech con **dos filas bajo el mismo Nº de acceso
HE‑59835** —una «Programado / Conteo 0» y otra «Completed / Conteo 1»— con la indicación «mi prueba se
duplico». La pantalla no manda dos imágenes: se comprobó en el código y en el entorno de pruebas que cada
confirmación produce **un solo C‑STORE**. Lo que ocurre en el destino es otra cosa: la imagen de prueba
llevaba el **número de acceso real** de la entrada pero un **StudyInstanceUID nuevo**, así que el PACS
interpretaba «el mismo acceso con dos estudios distintos» y mostraba el programado vacío junto al estudio
nuevo de la prueba. Consultadas las dos decisiones con el usuario, respondió **«Todo dato que se recibe de
la lista de trabajo debes regresarlo igual y respetarlo»** y eligió **«Usar el UID programado (una sola
fila)»**.

**Qué cambió:**

1. **El UID programado viaja de vuelta.** `WorklistPruebaDatos` recibe `EstudioUid` (VR `UI`: solo dígitos
   y puntos, máximo 64, se valida en el servidor con `RegularExpression` y `StringLength` en español). El JS
   lo toma de la fila seleccionada y lo manda **tal cual lo devolvió el nodo**, sin recortarlo ni marcarlo.
   <!-- Desde el decimocuarto cambio este punto se lee así: el UID programado viaja **si su casilla está
   marcada** (viene marcada por defecto, así que el comportamiento por defecto es el mismo). -->
2. **Con origen `seleccion`, la prueba se guarda dentro de ese estudio.** `ContrastTestImage.Create` usa el
   UID programado como `StudyInstanceUID` cuando el origen es la entrada de la lista y el UID tiene formato
   válido; si el nodo no lo envió, se genera uno nuevo (comportamiento anterior). **`SeriesInstanceUID` y
   `SOPInstanceUID` siguen siendo nuevos en cada envío**, así que la prueba no reemplaza ninguna imagen que
   ya existiera. Con origen `anonima` o `manual` el UID que llegue **se ignora**: solo una entrada elegida
   en la tabla puede escribir dentro de un estudio real.
3. **La pantalla lo dice antes de enviar.** El resumen de la fila elegida ahora dice «UID programado … · la
   prueba se manda dentro de ese estudio» (o «el nodo no envió el UID del estudio: la prueba creará un
   estudio nuevo»). La ventana de confirmación muestra la fila **«UID del estudio»** con el aviso *(la
   imagen se guarda dentro de este estudio)* y un texto de advertencia específico: que la prueba entra al
   estudio programado del paciente, que así el destino no abre un segundo registro, que **después habrá que
   borrarla desde el estudio del paciente** y no como prueba aparte, y que serie e imagen son nuevas. Para
   anónima y manual el aviso habla de estudio nuevo.
   <!-- El decimocuarto cambio quitó el sufijo «(la imagen se guarda dentro de este estudio)» de esa fila:
   ahora la confirmación tiene una fila por cada una de las siete casillas, con el valor o un «no viaja». -->

**Lo que no cambió:** la imagen sigue siendo el círculo sintético (Secondary Capture 512×512, `Modality = OT`,
`ConversionType = SYN`, `StudyDescription = PRUEBA TECNICA - NO DIAGNOSTICO`); sigue habiendo **un solo
C‑STORE por confirmación**, sin reintentos; los filtros, la hoja de periodo/hora y el destino escrito a mano
quedan igual. **Consecuencia asumida:** con una entrada de la lista, la imagen de prueba queda dentro del
estudio real del paciente y se ve en el visor; borrarla se hace desde ese estudio.

**Verificado** en el entorno desechable de siempre (SCP sintético en `127.0.0.1:11140`, app en
`127.0.0.1:5110`, **solo datos ficticios**; 5023/5024/11113, el servicio instalado y la base real sin tocar):
suite de API **100/100** (antes 94/94; los comprobantes nuevos exigen que el DICOM recibido traiga
exactamente el `StudyInstanceUID` programado, que serie e instancia sigan siendo nuevas, que anónima y
manual ignoren el UID que les llegue, que un UID con letras o de más de 64 caracteres se rechace con 400 y
su mensaje en español, y que por la red solo viaje el UID del estudio programado), suite de UI **84/84**
(antes 81/81: el resumen de la fila, la fila «UID del estudio» de la confirmación, el aviso de que se guarda
dentro del estudio real y que se borra desde el paciente, y el DICOM recibido con el UID programado), tiempos
límite sin fallos (eco 15 s, prueba 30 s, consulta 60 s) y maquetación sin desbordes a 390 px. **Regresión de
la hoja compartida 30/30** en las cinco pantallas. Revisión de los archivos recibidos: los dos envíos sobre
`TEST‑0006` comparten el estudio programado `…9999.6006` con **series distintas**; los envíos anónimos y
manuales conservan estudios nuevos. Capturas refrescadas: `filtros-03-fila-seleccionada.png`,
`filtros-04-confirmacion-seleccion.png` (la ventana con la fila y el aviso nuevo) y
`filtros-05-modo-manual.png`. Nota honesta: el entorno desechable no es un PACS Meditech, así que **no**
reproduce el panel CHECK; lo verificado es que el DICOM que sale de la app lleva ahora el UID del estudio
programado, que es lo que hace que el destino agrupe la prueba en esa fila.

**Archivos creados:** ninguno.
**Archivos modificados:** `Dicom/WorklistModels.cs` (propiedad `EstudioUid` con su validación y comentario),
`Dicom/ContrastTestImage.cs` (estudio programado frente a estudio nuevo y el ayudante `EsUidValido`),
`Pages/ListaTrabajo.cshtml` (textos de ayuda y de la confirmación), `wwwroot/js/lista-trabajo.js`
(`datosDePrueba()` devuelve `estudioUid`, `pintaSeleccion` y `pintaConfirmacion` con los avisos nuevos).
**Archivos eliminados:** ninguno.

---

## 2026-11-06 (duodécimo cambio) — La **Hora** de «Consulta lista de trabajo» pasa a la hoja de la Bitácora (**Una hora · Rango**): ya no son dos cajas de reloj

**Módulo:** frontend compartido — `wwwroot/js/hoja-fecha.js` (nueva `HojaFecha.instalarHora`),
`Pages/ListaTrabajo.cshtml`, `wwwroot/js/lista-trabajo.js`, y el token de caché de la hoja en las
otras cinco pantallas que ya la usan.
**Tipo:** frontend. **Base de datos:** ninguna. **Permisos:** ninguno. **API:** ninguna: ni una ruta
ni un contrato JSON cambió.

**Por qué:** captura de los dos recuadros de la Bitácora —**PERIODO** con «Cualquier día» y **HORA** con
«Todas las horas»— con la indicación «en la parte de hora y fecha, pon esto como esta en la bitacora con
ese formato en el Consulta lista de trabajo». En esa pantalla el horario se filtraba con dos
`<input type="time">` visibles (Hora desde / Hora hasta), que se ven y se operan distinto del resto de la
intranet.

**Qué cambió:**

1. **La hoja de horas entra en el componente compartido.** `HojaFecha.instalarHora({desde, hasta, titulo,
   cualquier, conteos})` monta la misma caja (`hf-caja` con icono de reloj) y la misma hoja que la Bitácora
   usa para el horario: modos **Una hora** y **Rango**, las **24 horas** en rejilla de cuatro columnas,
   atajos **Todo el día / Mañana / Tarde**, botón **Cualquier hora**, la **×** para quitar el filtro y el
   pie con cuántas horas tienen entradas. `cfg.conteos` es opcional: si devuelve `{'09': 3, …}` cada celda
   explica en su título cuántas entradas caen en esa hora («3 entrada(s) en la hora de las 09:00»).
   Escribe `'HH:00'` y `'HH:59'` en los dos campos —la hora entera incluida— y dispara `change` en el
   primero, que es lo que las páginas ya saben escuchar. **No añade una sola regla de CSS**: reutiliza las
   clases `hf-` que ya existían.
2. **En la pantalla, las dos cajas de hora pasan a ser campos ocultos.** `#lt-f-hora-desde` y
   `#lt-f-hora-hasta` siguen siendo los dueños del valor (el filtrado por ventana de hora no se tocó), pero
   ahora nadie escribe en ellos: la hoja escribe y la caja los muestra como «Todas las horas» /
   «14:00 – 14:59». **Periodo** y **Hora** quedan como dos recuadros del mismo formato, en la misma fila de
   filtros, igual que en la Bitácora.
3. **La validación de la hora se elimina**, no se adapta: un campo que solo escribe la hoja no puede traer
   nada inválido, y el horario **no viaja al nodo** —solo recorta lo recibido—, así que ya no hay mensajes
   de «hora inválida» que mostrar. Se mantienen los dos `span` de aviso del marcado porque `fieldError()`
   los busca por id; `message()` ahora ignora un aviso sin destino en vez de romper la pantalla.
4. **El token de caché sube a `?v=20261106a`** en las seis pantallas que cargan `js/hoja-fecha.js`
   (Consulta lista de trabajo, Reportes, Histórico de gestión, Patología, DICOM consulta y DICOM reenvío),
   para que el navegador no se quede con la versión anterior del archivo compartido.

**Lo que no cambió:** la **Bitácora** sigue con su copia propia de la hoja dentro de su página (no carga el
componente compartido), y las otras cinco pantallas siguen con su hoja de **Día · Mes · Año · Rango** —
`instalarHora` es código nuevo añadido al final del archivo, no una modificación de `instalar`.

**Verificado** en el mismo entorno desechable (SCP sintético en `127.0.0.1:11140`, app en `127.0.0.1:5110`,
**solo datos ficticios**; 5023/5024/11113, el servicio instalado y la base real sin tocar): suite de UI con
Playwright **81/81** (antes 70/70; los 11 comprobantes nuevos cubren la caja «Todas las horas», los modos
Una hora/Rango, las 24 celdas, el conteo por hora, que elegir 14 deje una sola entrada y escriba
`14:00`/`14:59` en los campos ocultos, el rango 09→07 ordenado a `07:00 – 09:59` con sus 4 entradas, el
atajo Tarde, «Cualquier hora» y la ×). Suite de API **94/94**, tiempos límite y maquetación en 0 sin
desbordes a 390 px. **Regresión específica de la hoja compartida: 30/30** en las cinco pantallas que la
usaban (caja visible, hoja abierta, los cuatro modos, el día escribe la fecha y la cierra, sin errores de
consola). Capturas: `filtros-12-hoja-horas.png` (la hoja de 24 horas abierta sobre los filtros) y
refrescadas `filtros-02`, `filtros-07`, `filtros-08`, `filtros-09` y `filtros-11`.
Dos notas honestas: (a) el comprobante «UIDs nuevos para cada prueba» del arnés exigía `>= 8` archivos
recibidos y fallaba en un directorio limpio porque solo **7** envíos escriben imagen (los que responden
«fallida» e «incierta» no almacenan); se corrigió el arnés para contar las imágenes **nuevas de esta
ejecución**, no las acumuladas, y ahora pasa con las 7 y sus UIDs únicos —no era un defecto del producto—;
(b) en esa instancia desechable `/api/patologia/reportes` responde 500 por no llevar cadena de conexión
MySQL, así que ese error de consola concreto se excluyó de la regresión; la hoja de Patología sí se verificó.

**Archivos creados:** ninguno en el proyecto.
**Archivos modificados:** `wwwroot/js/hoja-fecha.js` (bloque nuevo de la hoja de horas y su exportación),
`Pages/ListaTrabajo.cshtml` (campos de hora ocultos + token), `wwwroot/js/lista-trabajo.js` (instalación de
las dos hojas, `horasRecibidas()`, sin validación de hora y con `message()` tolerante), y el token
`?v=20261106a` en `wwwroot/admin/reportes/index.html`, `wwwroot/gestion/historico/index.html`,
`wwwroot/patologia/consulta/index.html`, `wwwroot/dicom/index.html`, `wwwroot/dicom/enviar/index.html`.
**Archivos eliminados:** ninguno; lo retirado son las **dos cajas de hora visibles** y su validación.

---

## 2026-11-06 (undécimo cambio) — Nueva pantalla **Consulta lista de trabajo**: filtros por nombre, ID, fecha, hora, modalidad y procedimiento; la prueba se manda con la entrada seleccionada o con datos escritos a mano

**Módulo:** DICOM — `Dicom/WorklistService.cs`, `Dicom/WorklistModels.cs`, `Dicom/ContrastTestImage.cs`,
`Dicom/Controllers/WorklistController.cs`, `Pages/ListaTrabajo.cshtml(.cs)`,
`wwwroot/js/lista-trabajo.js`, `wwwroot/css/lista-trabajo.css`, acceso en `Models/Catalog.cs`.
**Tipo:** backend + frontend + API. **Base de datos:** ninguna. **Permisos:** ninguno nuevo (reutiliza
`DicomVer` para consultar y `DicomEnviar` para mandar la prueba).

**Por qué:** captura anotada sobre el selector **«Nodo Storage (opcional)»** con la indicación
«EN PRUEBA DE ENVIO TAMBIEN RETIRALO, Y ME DEBE DEJAR SELECCIONAR UN ESTUDIO Y MANDAR LA PRUEBA O MANDAR
UNA PRUEBA MANUAL CON DATOS DE UN PACIENTE, TAMBIEN APLICA UN FILTRO PARA VER LAS ENTRADAS PROGRAMADAS, POR
NOMBRE, id, FECHA, HORA, MODALIDAD Y PROCEDIMIENTO». El selector se retira **sin sustituto** (regla del
proyecto: «ya no se usa, retírala» significa quitarla literal); el destino de la prueba se escribe a mano en
sus propios campos, igual que el destino de la consulta.

**Qué cambió:**

1. **Consulta MWL real.** `WorklistService.SearchAsync` hace **C-FIND** sobre
   `ModalityWorklistInformationModelFind` (nunca Study Root) con `ScheduledProcedureStepStatus = SCHEDULED`
   y el periodo como rango DICOM `AAAAMMDD-AAAAMMDD` —con las formas abiertas `AAAAMMDD-` y `-AAAAMMDD` si
   se pone un solo extremo—. El **AE local es editable y arranca en `MEDI`**, y viaja como *calling AE*;
   `ScheduledStationAETitle` se deja vacío en la petición para no restringir la consulta al MEDI.
   Carácteres: `ISO_IR 192`, así los acentos del español salen bien.
2. **Defensa contra nodos que responden de más.** Muchos SCP ignoran las claves opcionales, así que el
   servidor **vuelve a filtrar** la modalidad sobre lo recibido y **excluye** lo que no esté programado:
   las respuestas sin `ScheduledProcedureStepSequence` o sin estado se cuentan como *omitidas sin estado*
   y las de otro estado como *excluidas no programadas*; la pantalla lo dice en su aviso, no lo esconde.
   Tope de `MaxResults = 5000` entradas y de 20 000 respuestas; al llegar al tope se aborta la asociación y
   se avisa de que la lista está recortada. Los tiempos límite son honestos: 60 s de consulta, 15 s el ping
   y 30 s la prueba; si el nodo no termina, se responde **504** con «no se muestra una lista incompleta como
   si fuera completa».
3. **Filtros de las entradas programadas.** Fieldset nuevo con **Nombre**, **ID del paciente**,
   **Periodo** (la hoja de calendario compartida `js/hoja-fecha.js`), **Hora desde / Hora hasta**,
   **Modalidad** y **Procedimiento**, más **Limpiar filtros**. Modalidad y fechas **se piden al nodo** en el
   C-FIND; nombre, ID, hora y procedimiento filtran además lo ya recibido en el navegador. La hora se
   compara como ventana (`from`/`to` del paso programado) y una entrada **sin fecha u hora legible se
   excluye** cuando se pide periodo u hora, porque no se puede afirmar que esté dentro. Cuando los filtros
   ocultan entradas recibidas, la barra avisa cuántas («`N` de `M` … coinciden con los filtros»); si además
   se cambió modalidad o periodo después de consultar, recuerda que hay que **pulsar Consultar** para
   pedírselo al nodo.
4. **Elegir un estudio y mandarle la prueba.** Cada fila de la tabla lleva su **casilla de selección**;
   al elegirla, el resumen dice paciente, ID, acceso, programado, modalidad, procedimiento y el UID
   programado (para comparar, nunca para reutilizar <!-- decimotercer cambio: con una entrada de la lista el
   UID programado SÍ se reutiliza como estudio de la prueba -->). El panel «Prueba técnica de envío» tiene ahora **tres
   orígenes de datos**: *sin datos de paciente* (`PRUEBA^CIRCULO`), *entrada seleccionada de la lista*
   (deshabilitada hasta elegir fila) y *datos de un paciente escritos a mano* (nombre, ID y acceso).
   El envío pide **confirmación** con la lista completa de lo que va a salir.
5. **La imagen sigue siendo sintética y siempre es un estudio nuevo.** `ContrastTestImage` genera un
   círculo de contraste de 512×512 `MONOCHROME2` guardado como **Secondary Capture**, `Modality = OT`,
   `ConversionType = SYN` y `StudyDescription = PRUEBA TECNICA - NO DIAGNOSTICO`. Los UIDs de estudio, serie
   e instancia **se generan nuevos en cada envío**: nunca se escribe dentro del estudio programado ni se
   <!-- Este punto lo sustituye el decimotercer cambio del mismo día: con una entrada de la lista la prueba
        se manda DENTRO del estudio programado (serie e instancia siguen siendo nuevas). -->
   reutiliza su `StudyInstanceUID`. El `GET /api/dicom/worklist/patron` devuelve el BMP de la vista previa,
   que es exactamente lo que se manda.
6. **Validación en español, sin filtrar datos al log.** Todos los mensajes de `DataAnnotations` —
   incluidos los de longitud y rango, que antes salían en inglés del framework— están escritos en español;
   los nombres e IDs rechazan barra inversa y caracteres de control, y el acceso admite solo el alfabeto de
   `SH`. Los datos del paciente viajan **solo en el cuerpo del POST** (`RequestSizeLimit` 8 KB): no van en
   la URL, no se registran y no se guardan en el navegador; las respuestas van con `NoStore` y los POST con
   token anti-falsificación por cabecera.
7. **Lo que corregir la verificación.** Tres defectos reales de la pantalla: (a) el aviso de filtrado
   aparecía también cuando el servidor ya había devuelto justo lo pedido; (b) al limpiar filtros quedaba
   pendiente el aviso de «vuelve a consultar»; (c) el **clic en una fila no seleccionaba nada** porque el
   `change` que el navegador lanza al salir del campo repintaba la tabla y sustituía la fila bajo el cursor
   entre *mousedown* y *mouseup* — se arregló con `estadoFiltros()`/`filtrosPintados`, que no repinta cuando
   el estado de los filtros no cambió—. Y la casilla de cada fila ahora lleva una etiqueta legible por
   lector de pantalla (`lt-sr-only`) con el paciente y el ID reales; si la entrada no trae nombre se dice
   «paciente sin nombre», sin inventarlo.

**Verificado** en entorno desechable **solo con datos ficticios** (`FICTICIO-000x`, `GÓMEZ^Ana María`):
SCP sintético propio en `127.0.0.1:11140` (AE `MWL_TEST`, pynetdicom) con C-FIND MWL y C-STORE receptor, la
app levantada en `127.0.0.1:5110` con su propio `App_Data` en JSON; **5023/5024/11113, el servicio instalado
y la base real sin tocar**. Suite de API **94/94**, suite de UI con Playwright **70/70**, y los repasos de
tiempos límite y de maquetación salen en 0. Comprobado por las propias suites: el selector retirado no está
ni en el DOM ni en el JS, `/opciones` ya no devuelve nodos, cada C-STORE llega con UID nuevo, los datos de la
fila elegida y los escritos a mano aterrizan en `PatientName`/`PatientID`/`AccessionNumber`, la vista previa
coincide con los píxeles enviados, no hay datos de paciente en consola/URL/almacenamiento y los permisos se
aplican (el que solo ve no puede enviar; sin permiso, 403). 11 capturas nuevas en `capturas-worklist/`
(`filtros-01`…`filtros-11`): acceso, filtros y lista, fila seleccionada, confirmación con la entrada
seleccionada, modo manual, tema noche, hoja de calendario, móvil a 390 px en las tres vistas y la tabla a
escritorio.

**Archivos creados:** `Dicom/WorklistService.cs`, `Dicom/WorklistModels.cs`, `Dicom/ContrastTestImage.cs`,
`Dicom/Controllers/WorklistController.cs`, `Pages/ListaTrabajo.cshtml`, `Pages/ListaTrabajo.cshtml.cs`,
`wwwroot/js/lista-trabajo.js`, `wwwroot/css/lista-trabajo.css`.
**Archivos modificados:** `Program.cs` (registro de `IWorklistService`), `Models/Catalog.cs` (acceso
«Consulta lista de trabajo» bajo *Sistemas médicos*, con su descripción y sus palabras clave), `README.md`,
`docs/ARQUITECTURA.md` (§5, apartado nuevo de la lista de trabajo con sus endpoints),
`docs/FRONTEND.md` (la hoja de fechas pasa de cinco a seis pantallas y la pantalla MWL entra en la tabla
de familias) y este registro.
**Archivos eliminados:** ninguno; lo retirado es el selector **Nodo Storage (opcional)** y su relleno
automático dentro de la pantalla.

---

## 2026-11-05 (décimo cambio) — El tema vuelve al interruptor **Día / Noche** de la cabecera; el bloque «Tema» del diálogo queda en **General**

**Módulo:** cabecera y diálogo compartidos (`Pages/Shared/_Layout.cshtml`), los dos scripts de preferencias
(`wwwroot/js/theme.js`, `wwwroot/js/preferencias.js`) y la hoja de la intranet (`wwwroot/css/intranet.css`).
**Tipo:** frontend. **Base de datos:** ninguna. **API:** ninguna nueva. **Permisos:** ninguno nuevo.

**Por qué:** dos capturas anotadas —en la ventana, el bloque del tema con una sola pastilla «General»; en la
cabecera, el interruptor «Día ● Noche»— con la indicación «quiero que los cambios queden como te lo pongo en las
imagenes, obviamente con el formato que ya tiene la pagina». Cuestionado sobre qué debía hacer esa opción, eligió
**«Solo General, manda la cabecera»** y el interruptor **en todas las pantallas**.

**Qué cambió:**

1. **El interruptor Día/Noche se sirve desde la página.** `_Layout.cshtml` escribe `#theme-toggle`
   (`role="switch"`, con sus tres trozos `Día` / pista / `Noche`) como primer elemento de `.fila-acciones`,
   antes de ⚙ Configuración. Al estar en el layout aparece en **todas** las pantallas Razor; en los paneles
   autónomos de `wwwroot` y en el acceso sigue creándolo `theme.js`, que es lo que ya hacía.
2. **`theme.js` engancha el interruptor que ya existe** en lugar de salirse: si encuentra `#theme-toggle` en el
   DOM solo lo sincroniza con `data-theme` y le pone el `click`; si no, lo crea y lo coloca como antes. Antes se
   salía cuando existía `#pref-btn` —eso era lo que había dejado la cabecera sin interruptor.
3. **El bloque «Tema» del diálogo ya no manda: informa.** Vuelve a llamarse **Tema** y lleva una única pastilla
   **General** marcada desde el HTML, con la nota «El tema —Día o Noche— se elige con el botón de arriba, en la
   cabecera…». `preferencias.js` quita el listener del grupo y deja de reescribir sus radios (sigue pintando la
   letra); pulsar «General» no hace nada, porque no hay nada que cambiar.
4. **El formato es el de la cabecera, no el del genérico.** `intranet.css` añade reglas para
   `.theme-intranet .fila-acciones .theme-toggle`: mono de 12 px, borde `--navy-line`, fondo transparente y el
   dorado solo en la opción activa; la pista se achica a 26×14 px. Los tokens se redefinen por tema, así que se
   ve bien en Día y en Noche.

**Verificado** con `dotnet build -c Debug` (0 advertencias, 0 errores), `node --check` en los dos scripts y una
instancia desechable (`Auth:Store=json`, contenido raíz propio, `127.0.0.1:5101` con proxy de sesión en `5102`;
5023/5024/11113 y la base real sin tocar):

- El interruptor vive en la cabecera a 1440 px (x 193, junto a ⚙ en x 328, misma línea y misma altura) y al
  pulsarlo `data-theme` pasa `light → dark`, `aria-checked` a `true` y `localStorage['meditechnosys.theme']`
  queda en `dark`.
- El diálogo abre con las leyendas **Tema**, Tamaño de la letra, Tiempo de sesión y Parámetros del módulo
  Gestión; en Tema hay **una** pastilla («General», marcada) y sigue mostrando las seis opciones de sesión con
  la guardada y la letra con su valor.
- Pulsar la pastilla «General» no cambia el tema ni la marca; la letra («grande» lleva el raíz de 16 px a
  18,88 px) y el tiempo de sesión siguen funcionando; sin errores en consola.
- En `/AccesoDenegado` (otra página Razor) el HTML también trae `#theme-toggle`, y en el panel autónomo
  `/admin/usuarios/` el script lo inyecta donde antes.
- 7 capturas nuevas en `capturas-pref/` (16 y 17 la cabecera en Día y Noche; 18 y 19 la ventana con el bloque
  Tema/General; 20 otra página Razor; 21 panel autónomo; 22 móvil a 390×844).

**Archivos creados:** ninguno. **Archivos modificados:** `Pages/Shared/_Layout.cshtml` (interruptor en la
cabecera y bloque Tema/General), `wwwroot/js/theme.js` (enganchar el interruptor servido),
`wwwroot/js/preferencias.js` (el tema deja de ser suyo), `wwwroot/css/intranet.css` (formato del interruptor en
cabecera), `README.md`, `docs/FRONTEND.md` (§5 quinquies) y `docs/ARQUITECTURA.md`.
**Archivos eliminados:** ninguno. **Base de datos:** ninguna estructura nueva.

> **Pendiente del usuario:** este cambio **no está en el servidor** hasta ejecutar `actualizar.bat`
> **como administrador**.

---

## 2026-11-04 (noveno cambio) — Se retira la tarjeta **Configuración** y nace el botón **⚙ Configuración** de la cabecera

**Módulo:** página de inicio y su catálogo (`Models/Catalog.cs`), el layout compartido
(`Pages/Shared/_Layout.cshtml`), la cookie de sesión (`Program.cs`) y tres archivos de estilos.
**Tipo:** frontend + backend + API + configuración. **Base de datos:** ninguna tabla nueva; se usan las
dos claves que ya guarda `IConfigStore`. **Permisos:** ninguna clave nueva.

**Por qué:** con la captura de la tarjeta, pidió «este botón no me sirve aquí, por favor retíralo, y
ayúdame a poner un botón de configuración en la parte superior de inicio, donde pueda configurar el tema,
tiempo de inicio de sesión, tamaño de fuente».

**Qué cambió:**

1. **La tarjeta sale del catálogo, la pantalla no se borra.** `AdminCatalog.Apps` ya no incluye
   «Configuración», así que el inicio deja de ofrecerla. `wwwroot/admin/config/index.html`, su guard
   (`/admin/config` sigue siendo `configuracion.administrar`) y `PUT /api/config` **se quedan igual**: quien
   llegue por marcador o enlace directo sigue entrando. Las dos cosas que vivían ahí
   (fecha límite y periodicidad) **no perdieron su mando**: ahora están dentro del diálogo nuevo.
2. **Un solo botón arriba, en todas las pantallas Razor.** `_Layout.cshtml` pone `#pref-btn` (⚙ + «Configuración»)
   junto a *Salir* y, debajo del `<header>`, el `<dialog id="pref-dialog">` escrito **en el servidor** con sus
   cuatro bloques: **Tema**, **Tamaño de la letra**, **Tiempo de sesión** y **Parámetros del módulo Gestión**.
   Se abre con el botón, con `Esc`, se cierra con la X o con un clic en el oscuro, y hay **enlace directo**
   `/?configuracion=1` (o `#configuracion`) que lo abre solo y limpia la URL.
   - El primer bloque se llamó un momento **General** (con Día/Noche dentro). **El décimo cambio lo deshizo**:
     el bloque volvió a llamarse **Tema** con su única pastilla **General**, y el mando del tema regresó al
     interruptor **Día / Noche** de la cabecera. Ver la entrada del 2026-11-05.
3. **Cada preferencia con un solo dueño.** Tema y letra se guardan **en el equipo** (`localStorage`,
   `data-theme` y `data-fuente` en `<html>`, aplicados antes del pintado para no parpadear); el tiempo de sesión
   se guarda **en la cuenta** (`sesion.minutos.<usuario>` dentro del almacén de configuración, MySQL o JSON) y
   los parámetros del módulo siguen en la configuración común. Nada se duplica ni se contradice.
4. **El tiempo de sesión es una ventana de inactividad, no la vida de la cookie.** `Services/Sesion.cs`
   (`SesionPreferencias`) define las seis opciones —15 min, 30 min, 1 h, 2 h, 4 h y **8 h**— y las tres reglas:
   `Minutos` (valor roto o viejo → 480), `Expiro` y `DebeRenovar`. `Program.cs` las aplica en
   `OnValidatePrincipal`: si pasó la ventana sin actividad, **rechaza la principal**, cierra la sesión y manda a
   `/Login?sesion=expirada`; si ya va la mitad, reescribe la marca de actividad (`IssuedUtc`) con `ShouldRenew`
   para que la ventana elegida sobreviva al deslizamiento de 8 h del framework. **8 h es el tope** porque es la
   ventana más larga que se ofrece; la cookie, en cambio, se escribe con `VidaCookie` (8 h + 10 min) para que el
   corte lo dé esta app y no el vencimiento del ticket.
   - **Detalle que costó encontrar:** al escribir la cookie —en el acceso (`Pages/Login.cshtml.cs` y
     `POST /api/login`), al cambiar el tiempo (`PUT /api/sesion`) y en cada renuevo— la caducidad absoluta
     **no** puede ser la ventana elegida. Si lo fuera, a los 15 min exactos el framework mataría el ticket
     *antes* de llamar a `OnValidatePrincipal`: la sesión sí se cerraba, pero sin el marcador, y la persona caía en
     el acceso sin saber por qué. Por eso las cuatro escrituras usan `SesionPreferencias.VidaCookie`
     (8 h + 10 min), siempre más larga que cualquier ventana; quien cierra la sesión es `Expiro`.
5. **API nueva, sin filtrar permisos desde el navegador.** `GET /api/sesion` devuelve `{minutos, opciones[]}` de
   **quien entra** (las opciones que se pintan son las que el backend acepta) y `PUT /api/sesion` solo acepta un
   valor de esa lista (lo contrario responde 400), guarda la clave, **re-emite la cookie de la sesión abierta**
   para que el cambio surta efecto sin volver a entrar, y anota `Cambio de tiempo de sesión` (`15 → 60 minutos`)
   en la bitácora. Como cada persona escribe solo su clave, el Ingeniero también puede usarla.
6. **El botón no regala nada.** El bloque «Parámetros del módulo Gestión» queda oculto hasta que
   `GET /api/permisos/mios` confirma `configuracion.administrar`, y el interruptor de fecha límite solo se puede
   mover con `configuracion.fechaLimite`; la escritura real la valida `PUT /api/config` en el servidor.
   De paso, `GET /api/config` **deja de devolver las claves personales** `sesion.minutos.*`: la lista común ya
   no expone cuánto aguanta la sesión de cada quien.
7. **La letra se agranda con `rem`, no con píxeles.** `theme.css` pone `html:root[data-fuente="chica"] { font-size: 88% }`
   y `="grande" { 118% }`, e `intranet.css` cambió su `body { font-size: 15px }` por `.9375rem` para que escale
   el texto de todas las páginas. El diálogo y sus botones se siguen midiendo en píxeles: agrandar la letra no
   infla la ventana que sirve para agrandarla.
8. **Aviso honesto al entrar.** `Pages/Login.cshtml` muestra, solo cuando la dirección trae `sesion=expirada`,
   un recuadro azul («Tu sesión terminó por inactividad… el tiempo se cambia en el botón de configuración de la
   página de inicio»), con su propio estilo `.aviso-sesion` en `login.css`. No se confunde con el error de
   contraseña mala.

**Verificado** con `dotnet build -c Debug` (0 advertencias, 0 errores) y **instancias desechables**
(`Auth:Store=json`, contenido raíz en la carpeta de pruebas, puertos `127.0.0.1:5077` y, tras la corrección,
del `5091` al `5099` con almacenes separados para que una prueba no pise la otra; dos cuentas sembradas y sin tocar
5023/5024/11113 ni la base real):

- **21 de 21 comprobaciones de API** (`pruebas-pref/api-test.cjs`): el inicio trae `#pref-btn` y `#pref-dialog`
  y **ya no** enlaza `/admin/config/index.html`; `GET /api/sesion` da las seis opciones y su propio valor;
  `PUT` con 15 deja `sesion.minutos.prueba=15` y **no** toca `sesion.minutos.inge` (30), ni escribe de paso
  ninguna clave del módulo; `PUT` con 45 responde 400; anónimo responde 401; tras el cambio la cookie se
  re-emite; `/api/config` ya no devuelve `sesion.minutos.*`; el Ingeniero **no** puede leer ni escribir los
  parámetros del módulo pero sí su propio tiempo; `/admin/config/` sigue 200 para Admin y 302 a
  `/AccesoDenegado` para Ingeniero; `/Login?sesion=expirada` muestra el aviso y `/Login` a secas no; la
  bitácora registra `Cambio de tiempo de sesión` con su `antes → después` y **sin** renglón duplicado de
  `Acceso` por el tráfico de `/api/sesion`.
- **Navegador real** (Chrome sin interfaz + navegador integrado, con sesión de verdad a través de un proxy de
  pruebas): abrir/cerrar con botón, X, `Esc`, clic en el oscuro y enlace directo; **tema** Día/Noche cambia
  `data-theme` y `localStorage` sin interruptor duplicado (en aquel momento `#theme-toggle` dejó de inyectarse en
  el inicio; el **décimo cambio** devolvió el interruptor a la cabecera y le quitó al diálogo el mando del tema);
  **letra** chica/normal/grande lleva el raíz de 16 px a 14,08 y 18,88 px; las **seis opciones de sesión**
  llegan del servidor con la guardada marcada, y al elegir «1 hora» el aviso dice «Listo: tu sesión ahora
  aguanta 1 hora sin actividad» y `GET /api/sesion` confirma 60; **Admin** ve los parámetros del módulo
  (periodicidad 12 y fecha límite, guardados por el botón) y el **Ingeniero no** (`#pref-sistema` oculto, con su
  propio tiempo de 30 minutos marcado); sin errores de consola; a 390×844 la caja mide 339 px, sin desbordamiento
  horizontal y con scroll vertical propio. 15 capturas en `capturas-pref/` en ese momento (la 12 es el acceso con
  el aviso; las 13-15, el bloque entonces llamado **General**). El **décimo cambio** añadió las 16-22.
- **Reloj real, y el detalle que obligó a corregir.** La primera pasada, con ventana de 15 min y dos sesiones
  simultáneas, sí cortó la sesión quieta a los 16 min y sí renovó la usada a los 8 min, **pero la redirección salió
  sin `sesion=expirada`**: la cookie caducaba en el mismo instante de la ventana, así que el framework mataba el
  ticket antes de entrar a `OnValidatePrincipal` y el aviso nunca se ponía (la prueba de API no lo detectaba porque
  pedía `/Login?sesion=expirada` directamente, sin pasar por el corte). Con `VidaCookie` ya en su sitio se midió
  de nuevo, en una copia del código final con una ventana de **1 minuto** para no esperar media hora: a los 40 s la
  sesión usada devuelve 200 y **reemite** la cookie; a los 65 s de silencio sale
  `302 → /Login?ReturnUrl=%2F&sesion=expirada` y el recuadro azul aparece en la pantalla de acceso. La **ventana de
  15 min también se volvió a medir en reloj real sobre el código final** (puerto 5095, almacén propio, dos sesiones
  de la misma cuenta): a los 8 min la sesión usada contestó 200 y reemitió la cookie; la sesión quieta salió
  `302 → /Login?ReturnUrl=%2F&sesion=expirada` a los 16 min; y la sesión ya renovada volvió a salir con el mismo
  `302` con marcador 16 min después. Las tres redirecciones traen el marcador, que es lo que antes faltaba.

**Archivos creados:** `Services/Sesion.cs`, `wwwroot/js/preferencias.js`.
**Archivos modificados:** `Models/Catalog.cs` (tarjeta fuera, con nota de dónde vive ahora), `Models/Audit.cs`
(`CambiarSesion`), `Program.cs` (ventana de inactividad en `OnValidatePrincipal`, salida con `sesion=expirada`,
`GET`/`PUT /api/sesion`, filtro de claves personales en `GET /api/config` y exclusión de `/api/sesion` del
middleware de movimientos), `Pages/Shared/_Layout.cshtml` (botón + diálogo + `preferencias.js`),
`Pages/Login.cshtml` y `Pages/Login.cshtml.cs` (el aviso y la caducidad con la que se emite la cookie al entrar),
`wwwroot/js/theme.js` (API de apariencia, letra, y el
interruptor viejo solo donde la página no lo sirve), `wwwroot/css/intranet.css` (bloque `.pref-*`, `body` en `rem`),
`wwwroot/css/theme.css` (`data-fuente` y la paleta del diálogo en los dos temas), `wwwroot/css/login.css`
(`.aviso-sesion`), `README.md`, `docs/FRONTEND.md` (nuevo §5 quinquies) y `docs/ARQUITECTURA.md`
(«Dónde tocar cada cosa»).
**Archivos eliminados:** ninguno. **Base de datos:** ninguna estructura nueva.

> **Pendiente del usuario:** este cambio, como el octavo, **no está en el servidor** hasta ejecutar
> `actualizar.bat` **como administrador**.

---

## 2026-11-04 (octavo cambio) — La tarjeta «Bitácora» pasa a llamarse **Reportes** y reúne sus tres consultas en pestañas

**Módulo:** página de inicio (`Models/Catalog.cs`) y las tres pantallas de consulta: Bitácora
(`/admin/bitacora/`), Histórico de actividades (`/gestion/historico/`) y Reportes de estatus
(`/admin/reportes/`).
**Tipo:** frontend + catálogo de la página de inicio. **Base de datos:** ninguna. **API:** ninguna ruta
nueva; la barra de pestañas solo lee `GET /api/permisos/mios`, que ya existía. **Permisos:** ninguna clave
nueva ni cambiada; **no se concedió acceso a nadie**.

**Por qué:** con la captura de las dos tarjetas de Gestión, pidió «estos dos apartados inclúyelos en la
bitacora, renombra el botón como Reportes, pon los apartados divididos por su pestaña respectiva».

**Qué cambió:**

1. **Una sola tarjeta.** La de *Administración* deja de llamarse «Bitácora» y se llama **Reportes**, con las
   tres consultas en su descripción y en sus palabras clave. Las tarjetas sueltas de *Histórico de actividades*
   y *Reportes de estatus* (grupo Gestión) **se quitan del catálogo**: no se borra ninguna pantalla, solo el
   acceso duplicado desde el inicio.
2. **Pestañas que son enlaces, no paneles ocultos.** Cada apartado conserva **su ruta, su `<script>` y su guard
   del servidor**. Meter las tres páginas en un solo documento habría mezclado identificadores (`#fDesde`,
   `#error`, `#cuerpo`), los `<dialog>` y las peticiones, y obligaría a reimplementar la autorización en el
   navegador. Con enlaces funcionan el atrás/adelante, abrir en otra pestaña, el `Esc` de la ventana de la
   Bitácora y los marcadores viejos. El HTML de cada página ya trae su propia pestaña marcada con
   `aria-current="page"`, así que la barra se lee aunque el JS no cargue.
3. **`wwwroot/js/reportes-tabs.js` (nuevo)** reconstruye la barra con `GET /api/permisos/mios`
   (`credentials: 'same-origin'`, `cache: 'no-store'`): Bitácora con `bitacora.ver` **o** `quemador.ver`,
   Histórico con `historico.actividades`, Estatus con `reportes.consultar`. No llama a ninguna API de datos, no
   toca los filtros de la página y no escribe en `localStorage`. **Si la consulta de permisos falla no inventa
   enlaces**: deja la pestaña actual —que ya pasó el guard del servidor— y ofrece *Reintentar*.
4. **La entrada de la tarjeta no regala permisos.** `Catalog.Visibles` elige a qué apartado lleva **Reportes**:
   Bitácora si tiene `bitacora.ver`/`quemador.ver`; si no, Histórico si tiene `historico.actividades`; si no,
   Reportes de estatus. `PuedeAbrir` sigue siendo «su permiso o cualquiera de los alternativos», ahora con las
   cuatro llaves de consulta como alternativos.
5. **Rótulos coherentes:** el `<h1>` y el `<title>` de las tres páginas pasan a **Reportes** (el título
   conserva el apartado, «Reportes · Histórico de actividades»), y el CSS nuevo va prefijado
   (`.reportes-tabs`, `.reportes-tab`) en `panel.css` para no rozarse con las pestañas `.tabs`/`.on` de los
   inventarios; abajo de 600 px cada pestaña ocupa su fila.
6. **De paso, un hueco real de la Bitácora.** El aviso «Cargando los movimientos de …» se armaba con
   `innerHTML` y el valor de `?usuario=` de la dirección: un enlace manipulado podía inyectar HTML y ejecutar
   script con la sesión del visitante autorizado. Ahora se crea el `<span>` y se escribe con `textContent`.
   No tiene nada que ver con lo pedido, pero estaba en el archivo que se tocó y se corrigió.

**Verificado** en un banco desechable (servidor Node **solo de pruebas** en `127.0.0.1:5088` que sirve los
archivos de `wwwroot` con respuestas **ficticias** —«DEMOSTRACIÓN · DATOS FICTICIOS», IPs `192.0.2.x` de
documentación—; nunca se conectó al servicio ni a la base, y `ubicacion.js` se sustituyó por un archivo vacío
para que la prueba no pidiera geolocalización ni reportara nada):

- **Catálogo, 16 combinaciones de permiso** con un programa aparte que compila `Catalog.cs` + `Permisos.cs` +
  `Roles.cs` sin tocar el proyecto: la tarjeta aparece con cualquiera de las cuatro llaves y **no aparece**
  con 0; nunca hay tarjetas duplicadas; el destino es el que corresponde; tener solo `historico.exportar` o
  `reportes.entregar` **no** abre Reportes; «Consulta de reportes» (Patología) sigue intacta.
- **Navegador, las mismas 15 combinaciones no nulas:** número de pestañas exacto, una sola activa, `<h1>`
  «Reportes», sin errores de consola y sin desbordamiento horizontal.
- **Funciones que ya existían, comprobadas después del cambio:** clic en un usuario abre su ventana; la hoja
  de Periodo de la Bitácora (Día/Mes/Año/Rango) y la de Hora se abren; el filtro por texto y por periodo del
  Histórico deja 1 renglón y su Excel pide `…desde=2026-11-01&hasta=2026-11-30`; lo mismo en Estatus con su
  filtro de hospital; el clic en un reporte abre su detalle; el enlace entre pestañas navega y marca la
  destinada.
- **Casos límite:** con `?usuario=<img src=x onerror=…>` no se inyecta HTML; con `/api/permisos/mios` caído la
  barra muestra solo la pestaña actual con el aviso y, al pulsar *Reintentar*, aparecen las tres; a 390×844 las
  pestañas caben sin scroll horizontal; tema claro y oscuro en las tres pantallas.
- `dotnet build -c Release` **sin advertencias ni errores** (compilación aparte, en la carpeta de pruebas; el
  servicio publicado no se tocó).
- 9 capturas en `capturas-reportes/`.

**Archivos creados:** `wwwroot/js/reportes-tabs.js`.
**Archivos modificados:** `Models/Catalog.cs` (tarjeta Reportes, alternativos y elección de destino en
`Visibles`), `wwwroot/admin/bitacora/index.html`, `wwwroot/gestion/historico/index.html`,
`wwwroot/admin/reportes/index.html` (`<h1>`/`<title>`, `<nav>` de pestañas, `panel.css?v=20261104d` y el
`<script defer>` nuevo; en la Bitácora además el arreglo del `textContent`), `wwwroot/css/panel.css` (bloque
`.reportes-tabs`), `README.md`, `docs/FRONTEND.md` (nuevo §5 quater), `docs/ARQUITECTURA.md`
(«Dónde tocar cada cosa»).
**Archivos eliminados:** ninguno (las tres rutas siguen existiendo y `/admin/quemador/` sigue redirigiendo a
la Bitácora con `?fuente=quemador`). **Base de datos:** ninguna.

---

## 2026-11-04 (séptimo cambio) — La hoja de periodo de la Bitácora se vuelve componente y llega a las cinco pantallas que consultan fechas

**Módulo:** Reportes de status (`/admin/reportes/`), Histórico de gestión (`/gestion/historico/`),
Consultas de patología (`/patologia/consulta/`), Búsqueda DICOM (`/dicom/`) y Envío/reenvío DICOM
(`/dicom/enviar/`), más dos archivos nuevos en `wwwroot`.
**Tipo:** solo frontend. **Base de datos:** ninguna. **API:** ninguna (ni una ruta nueva ni un parámetro
nuevo; se siguen usando los `desde`/`hasta` que cada pantalla ya mandaba). **Permisos:** ninguno.

**Por qué:** mandó «Ahora todo lo que consulte calendario y hora en la intranet, aplicale los cambios que se
hicieron en el calendario y hora respectivamente de bitacora». Al preguntar el alcance contestó
**«las cinco»** y, sobre la Bitácora, **«no, déjala como está»**: su hoja sabe qué días tienen movimientos y
esa hoja de cálculo no quiere perder ese índice, así que el componente nuevo va suelto.

**Qué cambió:**

1. **Dos archivos nuevos:** `wwwroot/css/hoja-fecha.css` y `wwwroot/js/hoja-fecha.js` (`window.HojaFecha`,
   con `instalar({desde, hasta, titulo, cualquier, alAplicar})`, `sincronizar(campo)` e `instancias`). Es la
   misma hoja de cuatro modos (Día · Mes · Año · Rango) con sus chips, su cabecera ‹ mes año ›, su rejilla de
   lunes a domingo, sus doce meses, su bloque de doce años y su pie de atajos, convertida en componente.
   **Todo va con prefijo `hf-`** para no rozarse con las clases `.cal-dia`/`.cd-*` de la Bitácora.
2. **Nadie cambió su manera de filtrar.** Cada pantalla conserva sus dos `<input type="date">` como dueños
   del valor (`yyyy-mm-dd`, extremos incluidos) y su código de siempre; la hoja los tapa
   (`.hf-origen`), escribe en ellos con el `set` nativo y dispara **un solo `change` en el campo inicial** —
   las cinco leen los dos valores en el mismo manejador, así que avisar también al final duplicaba la
   consulta (en patología eran dos `buscar(1)` por periodo)—.
3. **Se auditó el contrato de `hasta` en los cinco consumidores** antes de dar el cambio por bueno:
   `reportes` y `historico` filtran en el navegador con `d < desde` / `d > hasta` (rechazan, luego incluyen);
   `Services/Historico.cs` usa `EnRango` con `.Date`; `Services/Reportes.cs` hace
   `>= desde.Date` y `< hasta.Date.AddDays(1)`; `Patologia/PatoApi.cs` (`PrepFiltros`) trabaja con `DateOnly`
   inclusivo y valida `desde <= hasta`; las dos DICOM mandan `?desde=&hasta=` al C-FIND. **Todos incluyen el
   día final**, así que Mes y Año no pierden su último día.
4. **Las escrituras de cada página se siguen respetando:** los botones «Hoy», «Últimos 7 días», «Limpiar»,
   «Limpiar filtros», quitar la casilla «usar rango de fechas» y `form.reset()` de patología escriben el
   campo sin pasar por la hoja. Se vigilan tres caminos —interceptor sobre `.value` de los dos campos,
   `change`/`input`, y `reset` del `<form>` con `setTimeout 0`— para que el rótulo nunca mienta.
5. **La Bitácora queda igual** (así se mandó): conserva su copia dentro de su página, con el índice de días
   que apaga los que no tienen movimientos. Por eso la hoja compartida **no** consulta ningún historial:
   todos los días están habilitados, las flechas nunca se apagan y no existe el atajo «Todo».
6. **La hora no se movió.** Se revisó pantalla por pantalla: **la Bitácora es la única que filtra por hora**.
   El otro «hora» de la intranet es un campo de texto libre de las actas administrativas
   (`wwwroot/js/administrativos.js`), que es contenido del documento, no consulta. No había a dónde llevar la
   hoja de horas.
7. **Tres tropiezos de convivencia, encontrados al medir (y ya corregidos en el CSS):**
   (a) el estado vacío del cuadro se llamaba `vacio` y en patología `.vacio` tiene `padding: 28px 12px` para
   su aviso de «sin resultados» —el botón salía de 89 px de alto—; ahora todas las clases de estado van
   prefijadas (`hf-vacio`, `hf-puesto`, `hf-sel`, `hf-hoy`, `hf-enrango`, `hf-pendiente`, `hf-oculto`) y el
   CSS empieza con un `reset` de `span` dentro del componente.
   (b) `.date-row > div { flex: 1 }` de DICOM estiraba el cuadro a 930 px; `.hf-campo.hf-campo { flex: 0 0 auto }`
   (doble clase para ganarle en especificidad) lo arregló.
   (c) un ancho fijo de 224 px cortaba el rango con puntos suspensivos; el cuadro ahora mide lo que mide su
   rótulo, con piso de 172 px.

**Verificado** con el banco de pruebas de siempre (servidor de archivos solo-lectura en `127.0.0.1:5088`,
copias de las cinco páginas sin llamadas a API, un driver que manipula la hoja y deja `MEDIDA {…}` fuera de
pantalla, Chrome sin cabecera a 1366×900): **35 casos, `errores: []` y `rechazos: []` en todos**. En las
cinco pantallas: abrir en Día deja 30 celdas de noviembre de 2026 con `deshab: 0` y hoja de 272×358
(`fuera: false`, `desbordaX: false`); Mes julio → `2026-07-01`/`2026-07-31` y rótulo «julio de 2026»;
Año 2025 → `2025-01-01`/`2025-12-31` y rótulo «2025»; Rango cruzando meses navegando ‹‹ hasta septiembre →
`2026-09-10`/`2026-09-20`; rango 15/09–20/11 sombrea 19 días y marca el extremo; la escritura programática de
la página (`2026-05-02`) se refleja sola como «02/05/2026» sin abrir la hoja; y el tema oscuro pinta la hoja
con la paleta de la página. `cambios` siempre tiene **un solo** elemento (`fDesde`/`f-desde`/`dateFrom`),
nunca dos. La matriz larga de 16 casos (Hoy, Ayer, Mes pasado, 30 días, flechas, `×`, clic fuera, reapertura
con el modo recordado) se corrió completa en reportes. 30 capturas en `capturas-hoja-compartida/` (las cinco
pantallas × Día/Mes/Año/Rango/puesto/oscuro). *Aprendizaje del banco:* el extractor del `--dump-dom`
coincidía con el propio código inyectado del driver; la medida definitiva exige buscar el patrón
`MEDIDA {"paso"` y no cualquier `MEDIDA`.

**Archivos creados:** `wwwroot/css/hoja-fecha.css`, `wwwroot/js/hoja-fecha.js`.
**Archivos modificados:** `wwwroot/admin/reportes/index.html`, `wwwroot/gestion/historico/index.html`,
`wwwroot/patologia/consulta/index.html`, `wwwroot/dicom/index.html`, `wwwroot/dicom/enviar/index.html` (los
dos `<link>`/`<script>` y una llamada a `HojaFecha.instalar`, nada más), `README.md`, `docs/FRONTEND.md`
(nuevo §5 ter), `docs/ARQUITECTURA.md` («Dónde tocar cada cosa»).
**Archivos eliminados:** ninguno. **Base de datos:** ninguna.

---


**Módulo:** Bitácora (`/admin/bitacora/`) —su ventana flotante— y los dos almacenes que la surten.
**Tipo:** frontend **y** backend (consulta). **Base de datos:** ninguna (no hay tabla nueva ni columnas
nuevas; solo se consultan distinto). **Permisos:** ninguno nuevo —la hoja de periodo se sirve con
`bitacora.ver` **o** `quemador.ver` y cada quien recibe el índice de su mitad—.

**Por qué:** mandó la captura de la hoja de días del quinto cambio y escribió «asi como esta, que me deje
filtrar por mes, año, y por rango de tiempo, igual en la hora que me deje poner de una hora especifica o un
rango». Es decir: conservar el mismo lenguaje visual de esa hoja y extenderlo a los cuatro periodos, más una
hoja de hora que admita una hora suelta o un tramo.

**Qué cambió:**

1. **Backend: el periodo deja de ser un día suelto.** `GET /api/bitacora/unificada` y
   `GET /api/bitacora/unificada/export` aceptan ahora `desde` y `hasta` (`yyyy-mm-dd`, **los dos incluidos**),
   que `Program.cs` (`MovimientosUnificados`) empuja a los dos almacenes; allí `RangoDePeriodo()`
   (`Services/Bitacora.cs:79` y `Services/BitacoraQuemador.cs:75`) lo traduce a un rango UTC
   `[inclusive, exclusive)` sumando un día al extremo final. Sin `desde`/`hasta` la consulta queda igual que
   antes (sin límite de tiempo), así que ningún enlace ni pantalla vieja cambia de resultado.
2. **Nuevo índice de días: `GET /api/bitacora/periodos?usuario=`** (`Program.cs:1072`) devuelve
   `{ dias: {'yyyy-mm-dd': n}, desde, hasta, puedeAccesos, puedeQuemador }` —cuántos renglones dejó esa
   persona en **cada día de toda su historia**, sin bajar ningún renglón—. Lo producen
   `DiasConMovimientos(usuario)` en los cuatro almacenes: en MySQL agrupa con
   `DATE_FORMAT(CONVERT_TZ(momento,'+00:00',@off),'%Y-%m-%d')` (día **local**, no UTC) y en JSONL agrupa
   `Momento.ToLocalTime()`. Como solo suma los almacenes que el visitante puede ver, la cuenta
   `soloquema` recibe el conteo del mostrador y nada más.
3. **Hoja de periodo con cuatro modos** (`#vCal` + chips `#vCalModos`, `pintarCal()` reparte a
   `pintarDias` / `pintarMeses` / `pintarAnios`): **Día** como antes; **Mes** con los doce meses del año a la
   vista, apagados los que no tienen renglones y las flechas cambiando de año; **Año** con un botón por cada
   año real (sin cabecera ni fila de días); **Rango** con dos clics —el orden no importa, se acomoda solo— y
   las flechas **nunca** apagadas, porque un extremo puede caer en un mes vacío. El pie cambia con el modo:
   **Hoy / Ayer / Cualquier día**, **Este mes / Mes pasado / Cualquier mes**, **Este año / Cualquier año** y
   **7 días / 30 días / Todo / Cualquier día**. Todo se escribe en `#vModo`, `#vDia` y `#vDiaHasta`.
4. **Hoja de hora con dos modos** (`#vHoraCal`, `pintarHoras()`): rejilla de 24 celdas `HH:00`. En **Una
   hora** solo se puede pulsar una hora que de plano tuvo movimientos *dentro del periodo cargado*
   (`mapaHoras()` cuenta sobre lo que ya está en pantalla); en **Rango** están vivas las 24 para poder
   escribir cualquier tramo. Pie: **Todo el día** (00–23), **Mañana** (06:00–12:59), **Tarde**
   (13:00–19:59) y **Cualquier hora**; se guarda en `#vHora` / `#vHoraHasta` y lo compara `horaDe(m)`, con
   los dos extremos incluidos.
5. **Cambiar de periodo vuelve a preguntar al servidor** (`cargarVentana()`): la ventana ya no se conforma
   con los 2.000 renglones más recientes de toda la historia, sino que pide los 2.000 más recientes **del
   recorte** (`&desde=&hasta=`). Por eso un año con 2.100 movimientos se ve completo y 2024 sigue siendo
   alcanzable aunque quede detrás del tope. La llamada en vuelo se aborta con `_ventanaPeticion` y no se
   repite si el rango ya está pedido (`_cargadoDesde` / `_cargadoHasta`).
6. **El Excel y los enlaces siguen el mismo contrato:** `urlExportVentana()` manda
   `desde`/`hasta`/`hora`/`horaHasta` y el archivo se nombra con lo elegido
   (`bitacora_mostrador01_desde2025-03-01_hasta2025-03-31_07h_a12h.xlsx`); la dirección acepta
   `?dia=`, `?mes=yyyy-mm`, `?anio=aaaa`, `?desde=&hasta=`, `?hora=&horaHasta=` (además de
   `usuario`, `actividad`, `fuente`, `texto` y `lote`) y abre la ventana ya recortada. `normalizaDia()`,
   `normalizaMes()` y `normalizaAnio()` también admiten las formas largas `dd/mm/aaaa` y `mm/aaaa`.
7. **Dos avisos honestos, descubiertos al verificar:** (a) mientras el servidor contesta el recorte nuevo
   la ventana decía «Ningún movimiento… coincide con los filtros seleccionados» —ahora `_cargando` pone
   «Cargando los movimientos de X…» y un `(cargando…)` junto a los contadores—; (b) con el tope de 2.000
   alcanzado el recado decía «de este periodo» aunque no hubiera periodo —dice «de su historia» cuando no lo
   hay—. Y el hueco vacío distingue tres casos: sin índice de días «X no tiene movimientos registrados»; con
   periodo puesto «Ningún movimiento de X del 14/08/2026 al 20/08/2026. Quitar el periodo (×) muestra toda su
   historia»; si no, «…coincide con los filtros seleccionados».
8. **Las hojas siguen siendo hijas del `<dialog>`** (`position: fixed`, capa superior, `posicionarHoja()` las
   ancla bajo su caja y las voltea si no caben) y `_hojaAbierta` hace que abrir una cierre a la otra: la
   primera `Esc` o el primer clic en el fondo cierran la hoja, la segunda cierra la ventana.

**Verificado** en el entorno desechable (`meditechnosys_intranet_prueba`, app en `127.0.0.1:5077`, proxy de
sesión en `5098` y `5099`, Chrome real y Chrome sin cabecera) con datos sembrados en `bitacora` y
`bitacora_quemador`: **Día** —noviembre de 2026 con «3 día(s) con movimientos», 3 celdas libres de 30, hoja
de 272×358 pegada a la caja y dentro del visionado—, elegir 01/11 deja 2 renglones y la caja
`dia 2026-11-01..2026-11-01`. **Mes** —12 celdas, solo con historia las que la tienen; en 2025 marzo y
noviembre—, marzo de 2025 → 3 renglones `mes 2025-03-01..2025-03-31`. **Año** —`2025:6 2026:37`—, 2025 → 6
renglones. **Rango** —clic en 03/11/2026 y luego en 19/03/2025 (se acomoda) → `19/03/2025 – 03/11/2026`, 41
renglones—; 7 días → 19, Todo → 43, Mes pasado → 10, Ayer → 3, `×` → 43. **Hora** —16 de 24 celdas vivas,
coinciden con las 16 horas que dejaron renglones en el periodo—; rango 12 y luego 9 → `09:00 – 12:59` con 13
renglones en las horas 09–12; Mañana → 27, Tarde → 14, `×` → todas. **Rango escrito sobre días sin
historia** (14/08–20/08 de 2026): acepta los dos extremos y muestra el mensaje nuevo, no un falso «no tiene
movimientos». **Tope e historia lejana** (`historico01`, 2.100+ renglones): sin periodo 2.000 con el aviso
«(los 2000 más recientes de su historia; el Excel los trae todos)» y la rejilla de años sigue ofreciendo
`2024:1 2025:4 2026:2100` —elegir 2024 devuelve el renglón del 20/12/2024—. **Excel**: 200 en cada forma.
**Enlace** `?usuario=mostrador01&mes=2025-03&hora=7&horaHasta=12` abre la ventana ya recortada (3 renglones,
horas 07/10/12). **Cierre**: nunca hay dos hojas abiertas, `Esc` con hoja abierta es `preventDefault` y el
clic en el fondo atenuado cierra solo la hoja. **Permisos** (cuenta `soloquema`, solo `quemador.ver`): la
tarjeta se arma con el mostrador, la ventana muestra sus 8 renglones y la hoja marca solo días del quemador
(`2025:1 2026:7`, no los `2026:37` de root). `errores: []` en todas las corridas. 14 capturas en
`capturas-bitacora/` (las cuatro hojas de periodo y las dos de hora en claro y oscuro, más el día «Ayer», el
tramo de horas y el recorte vacío). *Aprendizaje del banco de pruebas:* `--virtual-time-budget` corre los
relojes mucho más rápido que la red y fabricaba «0 renglones» donde el navegador real sí los mostraba; las
medidas definitivas se tomaron con el navegador real esperando a `window.__cargando()`.

**Archivos modificados:** `Program.cs` (`MovimientosUnificados` con `desde`/`hasta`, `FiltraMovimientos` con
`hora`/`horaHasta`, el export con las mismas llaves y su nombre de archivo, y el nuevo
`GET /api/bitacora/periodos`), `Services/Bitacora.cs` y `Services/BitacoraQuemador.cs` (`RangoDePeriodo()`,
`DiasConMovimientos()` en las cuatro variantes MySQL/JSONL), `wwwroot/admin/bitacora/index.html` (chips y
rejillas `pintarCal`/`pintarDias`/`pintarMeses`/`pintarAnios`/`pintarBotonesCal`/`pulsarCal`, la hoja de hora
`pintarHoras`/`pulsarHora`, `fijarPeriodo`/`fijarHora`, `sincronizarPeriodo`/`sincronizarHora`,
`mapaDias`/`mapaHoras` sobre `_indiceDias`, `cargarIndice`, `cargarVentana` con `_cargando`,
`periodoDesdeUrl`, `urlExportVentana` y los tres mensajes del hueco vacío), `README.md`,
`docs/FRONTEND.md` (§6b), `docs/ARQUITECTURA.md` (§3 y «Dónde tocar cada cosa»).
**Archivos eliminados:** ninguno. **Base de datos:** ninguna.

---

## 2026-11-03 (noche, quinto cambio) — La ventana elige el día con su propio calendario

**Módulo:** Bitácora (`/admin/bitacora/`), solo la barra de filtros de la ventana flotante.
**Tipo:** frontend. **Base de datos:** ninguna. **API:** ninguna —el valor sigue viajando como
`?dia=yyyy-mm-dd` y los endpoints no se tocaron.

**Por qué:** mandó la captura del desplegable de Chrome («noviembre de 2026» con el año y los meses en
columnas) abierto sobre el campo **DÍA** y escribió «mejora esta parte de la intranet». Ese cuadro lo
pinta el sistema operativo: no lleva los colores del tema, no se puede estilizar y, lo más importante, no
sabe qué días dejaron movimientos —ofrece cualquier fecha del calendario, incluso las vacías—.

**Qué cambió:**

1. **El campo DÍA ahora es un botón** (`button#vDiaBtn`) con calendario y flecha dibujados por la página,
   su rótulo en dorado cuando hay día elegido («03/11/2026») y en cursiva gris cuando no («Cualquier
   día»), más una **`×`** (`#vDiaQuitar`) que aparece solo con un filtro puesto. El
   `<input type="date">` sigue existiendo pero oculto (`#vDia`, `tabindex="-1"`, `aria-hidden`): es el
   que guarda el `yyyy-mm-dd` que leen el recorte, el Excel y el enlace.
2. **Hoja de días propia** (`#vCal`, `pintarCal()`): rejilla de 7 columnas desde el **lunes**, con los
   días que **sí** tienen renglones de esa persona en dorado y con su conteo en el `title` («3
   movimiento(s) de mostrador01 el 03/11/2026»), los demás `disabled` y apagados, un punto dorado bajo el
   día de hoy y marco lleno en el elegido. Abajo: **Hoy**, **Ayer**, **Cualquier día** y la nota «N día(s)
   con movimientos» / «Ningún movimiento en este mes».
3. **No deja perderse en meses vacíos:** `hayEnMes()` deshabilita ‹ y › cuando el mes vecino no tiene un
   solo renglón (`abrirCal()` arranca en el mes del día elegido o, sin él, en el mes más reciente con
   datos) y el salto a un mes sin historia avisa «mes sin historia previa».
4. **No la recorta la ventana:** la hoja es `position: fixed` **hija del `<dialog>`**, así que vive en la
   capa superior y escapa al `overflow-y: auto` de `.modal-form`; `posicionarCal()` la ancla bajo la caja,
   la voltea hacia arriba si no cabe y la mete en la ventana gráfica.
5. **Las dos capas se cierran por orden:** con la hoja abierta, la primera `Esc` hace `preventDefault()` y
   cierra la hoja (la ventana cae con la segunda), y el primer clic en el fondo atenuado se consume con
   `_hojaRecienCerrada` para que no cierre las dos de golpe.
6. **Los contratos no cambiaron:** `fijarDia()` escribe en `#vDia` y llama a `pintarVentana()`, así que
   `filtradasVentana()`, `urlExportVentana()` y `?dia=` funcionan igual; `normalizaDia()` acepta además el
   formato largo `dd/mm/aaaa`, de modo que los enlaces viejos siguen entrando directo al día.

**Verificado** en el entorno desechable (`meditechnosys_intranet_prueba`, app en `127.0.0.1:5077`, proxy de
sesión en `5098`, Chrome sin cabecera) con datos sembrados en `bitacora` y `bitacora_quemador` para
`mostrador01` en 9 días repartidos entre octubre, noviembre y septiembre: `calendarioNativo: "none"` en
las tres medidas; la caja en (150, 186, 158×32) y la hoja en (150, 226, 272×318) —`pegadaALaCaja: true`,
`dentroDelVisionado: true`, `dentroDeLaVentana: true`—; noviembre «3 día(s) con movimientos», 3 celdas
libres de 36 y el día de hoy marcado; elegir 03/11 deja **7 renglones, todos fechados 03/11/2026**, el
rótulo pasa a `puesto`, la `×` aparece, `valorGuardado: 2026-11-03` y el Excel
`?usuario=mostrador01&dia=2026-11-03` contesta **200, 7.877 bytes, `application/…spreadsheetml.sheet`**;
noviembre → octubre (4 días) → noviembre (`vuelveAlAnterior: true`); atajos: Hoy 7, Ayer 4, `×` 25,
Cualquier día 25; `esc {"canceladoPorLaPagina": true, "hojaCerrada": true, "ventanaSigueAbierta": true}`,
primer clic fuera cierra solo la hoja y el segundo cierra la ventana. Casos límite: `sindatos` abre la
hoja en el mes actual con las 30 celdas apagadas, ambas flechas deshabilitadas y «Ningún movimiento en
este mes»; con la cuenta `soloquema` (`quemador.ver` solamente) la hoja ofrece **1 día** con movimientos —
los del mostrador—, ambas flechas apagadas y el resto del flujo igual (Hoy 10 renglones, Ayer 0 con su
aviso, Excel 200 de 8.604 bytes). Regresiones: modo `ventana` completo en verde (filtro por actividad 6,
chip de lote `USB-AYER-1` → 1 renglón, cerrar desmarca, `errores: []`) y modo `enlace` para
`dia=2026-10-26`, `dia=26/10/2026`, `fuente=quemador&dia=01/11/2026` y un día sin resultados
`16/09/2026`. Capturas en `capturas-bitacora/` (claro y oscuro, 1560 y 900 px, con y sin día elegido).

**Archivos modificados:** `wwwroot/admin/bitacora/index.html` (CSS de `.campo-dia`/`.cal-dia`, la caja
`#vDiaBtn` con `#vDia` oculto, la hoja `#vCal` dentro del `<dialog>` y su módulo de JS: `mapaDias`,
`hayEnMes`, `pintarCal`, `posicionarCal`, `abrirCal`, `cerrarCal`, `fueraDeCal`, `saltarMes`, `fijarDia`,
`sincronizarDia`, `normalizaDia`). **Archivos eliminados:** ninguno. **Base de datos:** ninguna.

---

## 2026-11-03 (noche, cuarto cambio) — Fuera la hoja de movimientos: la bitácora es la lista de usuarios y su ventana

**Módulo:** Bitácora (`/admin/bitacora/`), aviso del Quemador (`/admin/quemador/`) y catálogo de la
pantalla de inicio.
**Tipo:** frontend. **Base de datos:** ninguna. **API:** ninguna —los endpoints quedan intactos; lo que
ya no se usa desde la pantalla es la llamada sin `usuario=` y el Excel global.

**Por qué:** con la captura de la tarjeta «Todos los movimientos, en una sola hoja» pidió
«esta parte de la bitácora ya no se usa al tener la ventana flotante, por favor retírala». Al preguntar
cómo cubrir lo que esa tabla dejaba de mostrar, respondió «no pongas un botón, solo con el clic».

**Qué cambió:**

1. **Adiós la sección de movimientos:** desaparece `#seccionMovimientos` con su tabla, su barra de
   filtros (usuario, día, hora, actividad, «salió de», texto), sus contadores, su botón **Descargar
   Excel** y su aviso de corte. `main` queda con una sola `<section>`: la tarjeta de usuarios. Se
   borraron también `cargar()`, `consultar()`, `pintar()`, `filtradas()`, `urlExport()`,
   `rellenarActividades()`, `rellenarHoras()`, `rellenarUsuarios()` y `fijarLote()`; el guion pasó de
   ~690 a 543 líneas y ya no queda ninguna referencia colgada (se verificó por nombre de función y por
   `getElementById`).
2. **El aviso de ubicación no se perdió:** `#estadoUbicacion` se mudó al pie de la tarjeta de usuarios.
3. **El enlace ahora abre la ventana:** `?usuario=X` ya no recorta una tabla —ya no existe— sino que
   abre la ventana de esa persona cuando la lista terminó de pintarse, y `dia`, `hora`, `actividad`,
   `fuente`, `texto` y `lote` de la dirección se aplican dentro de ella (`abrirUsuario(usuario, filtros)`).
   El chip `usuario X ×` y el desplegable de usuario desaparecen con la hoja.
4. **Quien solo ve el quemador no se queda en pantalla vacía:** antes, con `quemador.ver` y sin
   `bitacora.ver`, `/api/bitacora/resumen` contestaba 403 y la tarjeta se ocultaba, dejando la página
   sin nada que tocar. Ahora `cargarResumen()` usa ese mismo 403 para armar la lista con
   `GET /api/bitacora/unificada?limite=2000`: agrupa por usuario, toma el renglón más nuevo para
   equipo/IP/ubicación y cambia los rótulos a **Registros** y **Último movimiento**, que es lo que de
   verdad puede ver. Sigue sin recibir ni un renglón de accesos: el servidor decide.
5. **Sin ventana no hay respaldo posible:** en un navegador que no conozca `showModal()` la tarjeta
   avisa que se necesita un navegador actual (antes el clic filtraba la hoja).
6. **Textos coherentes:** el aviso de `/admin/quemador/` y la descripción de la Bitácora en el catálogo
   (`Models/Catalog.cs`) dejan de prometer «una sola tabla» y explican la lista con su ventana.

**Verificado** en el entorno desechable (`meditechnosys_intranet_prueba`, app en `127.0.0.1:5077`, proxy
de sesión en `5098`, Chrome sin cabecera, 5 usuarios con accesos buenos y fallidos más 10 eventos del
quemador): `main > section` = solo `seccionUsuarios`; `hojaFuera: false` (ni `#cuerpo`, ni `#resumen`,
ni `#fUsuario`, ni `#btnDescargar` existen); rótulos Usuario · Estado · Última conexión ·
Equipo · IP · Ubicación · Dispositivo; 5 renglones; desborde horizontal 0 y sin errores de consola a
1560 y a 900 px. Clic en `mostrador01` → ventana abierta, 7 renglones (4 «contraseña mal»), cabecera
`sticky`, desborde 0, fila marcada; filtro por actividad → 1; filtro por día → 7; **Limpiar filtros** →
7; cerrar → `cerrada: true` y la fila vuelve a `fila-usuario`. `root` → 39 renglones con 7 enlaces de
lote; pulsar `Lote CD-20261103-1` → 1 renglón con su recordatorio dentro de la ventana y la tarjeta de
detrás sin cambiar. Excel de la ventana → 200, `.xlsx` de 7.879 bytes. Enlace `?usuario=patologia03` →
ventana abierta con 4 renglones y su fila marcada; `?usuario=root&fuente=quemador` → ventana con el
filtro de fuente aplicado (10 renglones, todos «Quemador DICOM»). Con la cuenta `soloquema`
(`quemador.ver` solo): `/resumen` = 403 y la tarjeta se llena igual —`root / 10 movimiento(s) / …` con
rótulos **Registros** y **Último movimiento**— y su ventana muestra sus 10 renglones del mostrador.
Regresiones: `/admin/quemador/` sigue redirigiendo y aterriza en 200, la página sin ninguno de los dos
permisos sigue mandando a Acceso Denegado, y `dotnet build` sale sin avisos ni errores.

**Archivos modificados:** `wwwroot/admin/bitacora/index.html`, `wwwroot/admin/quemador/index.html`,
`Models/Catalog.cs`, `README.md`, `docs/FRONTEND.md`, `docs/ARQUITECTURA.md`, `docs/REGISTRO-CAMBIOS.md`.

**Para aplicarlo:** ejecutar `actualizar.bat` como Administrador (detiene el servicio, publica y lo
reprende).

---

## 2026-11-03 (noche, tercer cambio) — Al hacer clic en un usuario se abre una ventana flotante con todas sus actividades

**Módulo:** Bitácora (`/admin/bitacora/`).
**Tipo:** frontend. **Base de datos:** ninguna. **API:** ninguna —la ventana consume el mismo
`GET /api/bitacora/unificada?usuario=&limite=` y el mismo `/export` que ya existían.

**Por qué:** justo después del cambio anterior pidió
«Quiero que en la bitácora cuando se haga clic al usuario se despliegue una ventana flotante de todas
las actividades que realizó dicho usuario». Filtrar la tabla de abajo tenía dos problemas: había que
bajar hasta ella y, sobre todo, **solo mostraba lo que cupiera en la hoja** —si la página venía
recortada por día o por fuente, el historial completo de la persona nunca aparecía.

**Qué cambió:**

1. **Ventana flotante:** nuevo `<dialog id="ventanaUsuario" class="modal ventana-bitacora">` abierto con
   `showModal()` (cabecera fija, tabla con desplazamiento propio, 88% de alto máximo y ancho de 1320 px).
   `abrirUsuario(usuario)` pide por su cuenta los **2000 movimientos más recientes de esa persona**, así
   que la ventana siempre está completa sea lo que esté mostrando la hoja. La fila de la tarjeta queda
   marcada en dorado mientras la ventana está abierta y **la tabla de atrás no se mueve**.
2. **Filtros propios:** la ventana trae su barra (día, hora, actividad, «salió de» y texto libre), sus
   contadores, el filtro de lote al pulsar «Lote …» de un renglón y tres botones —**Limpiar filtros**,
   **Descargar su Excel** (solo ese usuario con su filtro) y **Cerrar**—. Su estado (`_ventanaTodas`,
   `_ventanaLote`) está separado del de la hoja.
3. **Cierre limpio:** se cierra con el botón, con `Esc`, pulsando el fondo atenuado o desde el propio
   diálogo; las tres rutas llaman a `cerrarVentana()`, que aborta la petición en vuelo y desmarca la
   fila (antes, al cerrarla, la fila se quedaba marcada).
4. **Código compartido:** los renderizadores se factorizaron (`filaDe`, `contar`, `renglonesResumen`,
   `chipFijo`) para que la hoja y la ventana pinten idéntico con una sola copia del código; `filaDe`
   recibe `enVentana` para que los enlaces de lote afecten a la lista correcta.
5. **Sigue habiendo respaldo:** en navegadores sin `<dialog>` modal el clic filtra la hoja de abajo como
   antes. Y los enlaces `?usuario=` conservan el recorte `usuario X ×` sobre los contadores.

> **Actualización de la misma noche:** la entrada «cuarto cambio» retiró la hoja de movimientos, así que
> el respaldo del punto 5 y las frases «la tabla de atrás no se mueve» ya no aplican. La ventana flotante
> de esta entrada es exactamente la que sigue en pantalla.

**Verificado** en el entorno desechable (`meditechnosys_intranet_prueba`, app en `127.0.0.1:5077`, proxy
de sesión en `5098`, Chrome sin cabecera): clic en `mostrador01` → ventana abierta, título
*Movimientos de mostrador01*, 7 renglones (4 de «contraseña mal»), hoja de atrás sin recortar ni
desplazar (`hojaInalterada: true`); `patologia03` → 4; `root` → 50 con 7 enlaces de lote y 3 «Quemado en». Dentro
de la ventana: filtro por actividad → 1 renglón; pulsar `Lote CD-20261103-1` → 1 renglón y el recordatorio
`lote CD-20261103-1×` **solo dentro de la ventana**, la hoja siguió en 64 renglones. Al cerrar → la fila
vuelve a `fila-usuario` sin marcar. Excel de la ventana → `.xlsx` de 7.878 bytes, 8 renglones × 20
columnas. Sin desborde horizontal (`desborde: 0`), cabecera fija y sin errores de consola a 1544 px; a
900 px la ventana ocupa el ancho disponible y su tabla se desliza dentro de su caja.

**Archivos modificados:** `wwwroot/admin/bitacora/index.html`, `README.md`, `docs/FRONTEND.md`,
`docs/REGISTRO-CAMBIOS.md`.

**Para aplicarlo:** ejecutar `actualizar.bat` como Administrador (detiene el servicio, publica y lo
reprende).

---

## 2026-11-03 (noche, segundo cambio) — La hoja de usuarios abre la bitácora y manda sobre la tabla

**Módulo:** Bitácora (`/admin/bitacora/`).
**Tipo:** frontend. **Base de datos:** ninguna. **API:** ninguna (se aprovecha el filtro
`?usuario=` que ya existía).

**Por qué:** con la captura de la tarjeta «Usuarios · conexión, último acceso y desde dónde» pidió
«quiero que aparezca esto primero y al hacer clic despliegue la tabla por usuario y se tenga el
filtro». Estaba abajo de la tabla de movimientos y el clic solo movía el desplegable sin dejar
señal clara de qué se estaba viendo.

**Qué cambió:**

1. **Orden invertido:** la tarjeta de usuarios es ahora el primer `<section>` de la página
   (`#seccionUsuarios`) y la tabla de movimientos va debajo (`#seccionMovimientos`).
2. **Clic = abrir a ese usuario:** cada renglón de la tarjeta llama a `abrirUsuario(usuario)`, que
   suelta el filtro de servidor, pinta solo sus movimientos, **marca la fila** en dorado
   (`.fila-usuario.activa`) y baja suavemente a la tabla de movimientos.
3. **El filtro queda a la vista:** encima de los contadores aparece el recorte
   `usuario mostrador01 ×`; el `×` limpia el filtro, desmarca la fila y regresa a todos los
   movimientos. El desplegable **Usuario** de la barra de filtros sigue sincronizado, así que se
   puede cambiar o afinar con día, hora, actividad, fuente y texto sin perder el recorte.
4. **Arreglado el enlace directo `?usuario=`:** el valor se asignaba al `<select>` antes de que
   existieran sus opciones, así que el navegador lo tiraba y la hoja abría sin filtro (y el recorte
   mentía). Ahora el usuario pendiente viaja en `_pendienteUsuario`, se manda al servidor en la
   primera petición (`consultar()`) y se aplica al desplegable cuando ya tiene sus opciones.
   Es lo que usan los enlaces de Usuarios y la hoja vieja del quemador.

> **Actualización de la misma noche:** el punto 2 y el punto 3 (clic = filtrar la tabla de abajo) los
> sustituyó la **ventana flotante** de la entrada anterior, pedida expresamente por el usuario. Lo demás
> de esta entrada —el orden de las secciones, el recorte por enlace `?usuario=` con su
> `usuario X ×` y el arreglo de `_pendienteUsuario`— sigue vigente.

**Verificado** en el entorno desechable (base `meditechnosys_intranet_prueba`, app en `127.0.0.1:5077`,
proxy de sesión en `5098`, 4 usuarios de mostrador con entradas buenas y fallidos y 10 eventos del
quemador): `main > section` #1 = `seccionUsuarios` (top 81 px) y #2 = `seccionMovimientos` (490 px);
clic en `mostrador01` → 7 renglones, todos de ese usuario, fila con clase `fila-usuario activa`,
desplegable en `mostrador01`, recorte `usuario mostrador01×`; `×` del recorte → 46 renglones,
desplegable vacío y ninguna fila marcada. Enlace `?usuario=patologia03` → 4 renglones con su
intento fallido y el recorte puesto. Sin desbordo horizontal a 1560 px y con la tabla desplazándose
dentro de su caja a 900 px. Regresiones: lote `CD-20261103-1` sigue cerrando 59 → 1, el Excel del
quemador devuelve `.xlsx` (8.6 KB), y con permiso solo `quemador.ver` la tarjeta de usuarios sigue
oculta (`/api/bitacora/resumen` = 403) mientras que sin ninguno de los dos la página manda a
Acceso Denegado (302) y la API responde 403.

**Archivos modificados:** `wwwroot/admin/bitacora/index.html`, `README.md`, `docs/FRONTEND.md`,
`docs/REGISTRO-CAMBIOS.md`.

**Para aplicarlo:** ejecutar `actualizar.bat` como Administrador (detiene el servicio, publica y lo
reprende).

---

## 2026-11-03 (noche) — Las dos bitácoras se unen en una sola hoja

**Módulo:** Bitácora (`/admin/bitacora/`), Quemador (`/admin/quemador/`), catálogo de la página de
inicio y permisos.
**Tipo:** backend + API + frontend + permisos. **Base de datos:** ninguna — las tablas `bitacora` y
`bitacora_quemador` quedan exactamente igual; la mezcla se hace **al leer**.

**Por qué:** el pedido fue «quiero que me juntes estas dos bitácoras en una sola, y lo organices de
la siguiente manera: fecha, hora, actividad (inicio de sesión, error de sesión, modificó, descargó,
quemó, etc), IP, ubicación geográfica, qué fue lo que hizo… y en el caso que ingresaron su contraseña
mal que aparezca el intento fallido de sesión».

**Qué cambió:**

1. **`Models/BitacoraUnificada.cs` (nuevo).** Un `record Movimiento` con la forma común
   (Origen, Momento, Usuario, Actividad, Grupo, Módulo, Detalle, IP, Equipo, Ubicación, fuente de la
   ubicación, resultado, lote y los datos del estudio) y `Movimientos.DesdeWeb` / `DesdeQuemador`,
   que traducen cada renglón original. De ahí salen las etiquetas únicas de las dos bitácoras:
   *Inicio de sesión*, **Intento de sesión fallido** (une el «Inicio de sesión fallido» de la web con
   el «Intento de acceso fallido» del mostrador), *Cierre de sesión*, *Abrió una página*, *Búsqueda de
   estudios*, *Descarga de estudios*, *Quemado en USB*, *Quemado en CD/DVD*, *Descarga de Excel*,
   *Creación*, *Edición*, *Baja* y los eventos con nombre propio que ya venían escritos en español.
   Las frases de «qué fue lo que hizo» se arman aquí, con paciente, ID, acceso, modalidad, fecha,
   imágenes, medio y unidad.
2. **`Program.cs`.** `MovimientosUnificados` pide a cada almacén lo suyo, enriquece ambos con
   `UbicacionResolver` (nombre del equipo, ubicación, margen y coordenadas) y devuelve la lista
   mezclada por hora. Nuevos `GET /api/bitacora/unificada` (trae además `puedeAccesos`,
   `puedeQuemador` y las actividades que sí existen en los datos) y
   `GET /api/bitacora/unificada/export` (Excel de 20 columnas con los mismos filtros de la pantalla).
   Los endpoints viejos de `/api/bitacora` y `/api/quemador` no se tocaron.
3. **`wwwroot/admin/bitacora/index.html` (reescrita).** Una sola rejilla con las siete columnas
   pedidas, filtros de usuario y día en el servidor y de hora, actividad, «salió de», lote y texto en
   la pantalla; el chip de resultado (error / parcial / cancelado) y el enlace `lote …` que deja a la
   vista solo las filas de una misma acción del mostrador. Abajo sigue la tarjeta de usuarios
   conectados, que solo se oculta si quien consulta no tiene permiso de accesos.
4. **`wwwroot/admin/quemador/index.html`.** Ya no es una pantalla: es un aviso con
   `<meta http-equiv="refresh">` y un `location.replace` que manda a la Bitácora con
   `?fuente=quemador`, conservando en la dirección el usuario, el día, el lote y el texto que
   trajera el enlace antiguo.
5. **Permisos.** La hoja abre con `bitacora.ver` **o** `quemador.ver` (guarda del middleware en
   `Program.cs` y aserciones de los dos endpoints nuevos). Dentro, cada quien recibe solo lo suyo:
   con únicamente `quemador.ver` llegan los renglones del mostrador y la tarjeta de usuarios no se
   carga (su API responde 403). `Models/Catalog.cs` añade `AppLink.Alternativos` y `PuedeAbrir`, la
   tarjeta «Bitácora del Quemador» desaparece y la de accesos pasa a llamarse **Bitácora**.

**Verificado** en la instancia desechable (puerto 5077, base `meditechnosys_intranet_prueba`, proxy de
sesión 5098 y Chrome sin interfaz): se generaron entradas buenas, dos intentos con contraseña
mal, páginas abiertas, búsquedas DICOM, alta/edición/baja de un aviso, y diez eventos del mostrador
con sus lotes de descarga, USB y CD. La hoja mezcló 63 renglones sin desborde en 1560 px (la tabla
mide 1144 px y el `document` no tiene scroll horizontal) y en 900 px la rejilla se desplaza dentro de
su contenedor sin romperse. Con `?fuente=quemador` quedan los 10 del mostrador; al pulsar un lote, de
42 filas se pasa a 1; la cuenta del resumen (movimientos, entradas, contraseña mal, búsquedas,
descargas, quemados, con error) cuadra con lo sembrado. La exportación bajó 200/OK con las 20
cabeceras y las 11 filas esperadas. En el reparto de permisos: `soloquema` (solo `quemador.ver`)
recibió `puedeAccesos=false` y 10 renglones todos de origen «Quemador DICOM», su `resumen` devolvió
403 y la página 200; `sinbitacora` (Ingeniero sin ajustes) salió a `/AccesoDenegado` (302) y su API
respondió 403. Las dos cuentas y el aviso de prueba se borraron al terminar.

**Archivos creados:** `Models/BitacoraUnificada.cs`.
**Archivos modificados:** `Program.cs`, `Models/Catalog.cs`, `wwwroot/admin/bitacora/index.html`,
`wwwroot/admin/quemador/index.html`, `README.md`, `docs/FRONTEND.md`, `docs/ARQUITECTURA.md`,
`docs/REGISTRO-CAMBIOS.md`.

**Para aplicarlo:** ejecutar `actualizar.bat` como Administrador (detiene el servicio, publica y lo
reprende). No hay migración de datos: lo ya registrado aparece mezclado desde el primer momento.

---

## 2026-11-03 — El buzón lo enlaza solo el administrador, y la clave de cifrado deja de cambiar de versión

**Módulo:** Bandeja de correo (`/admin/correos/`), Usuarios (`/admin/usuarios/`), `Services/Correo.cs`,
`Services/BuzonStore.cs`.
**Tipo:** backend + API + frontend + configuración. **Base de datos:** ninguna (misma tabla
`usuario_buzones`; no se cambió ni se borró ningún dato).

**Por qué:** tres avisos seguidos del administrador. (1) «no debe aparecer ese botón de enlazar correo,
solo mediante la generación de usuarios». (2) «no me deja enlazar en usuarios el correo, ni guardar los
cambios». (3) «tiene ya la contraseña y me marca este error» — la bandeja de
`iramosh@meditechnosys.mx` decía que el buzón enlazado «perdió su contraseña».

**Qué cambió:**

1. **Fuera el auto-enlace.** Se eliminó el diálogo «Enlazar mi correo» de la bandeja y los endpoints
   `GET|PUT /api/correos/mio/buzon`; la cabecera ahora solo muestra el buzón que se ve. Queda una sola
   ruta para enlazar: `PUT /api/usuarios/{usuario}/buzon`, que usa el alta y la edición en Usuarios.
2. **Arreglado el guardado en Usuarios.** El diálogo Editar fallaba en silencio
   (`TypeError: Cannot read properties of null` al leer el buzón previo) y el alta con buzón moría contra
   una casilla (`creBuzLimpiar`) que no existe en ese formulario. Se leyeron ambas con tolerancia, se
   comparan los campos reales antes de mandar el `PUT` y la tabla se refresca *después* de guardar.
3. **Una sola clave de buzones.** `BuzonCrypto` tomaba `App_Data/buzon-clave.bin` **relativo a la carpeta
   donde arranca** el programa: `bin/Debug`, `bin/Release` y `publish` tenían tres claves distintas y cada
   una cifraba con la suya en la misma base, así que un enlace escrito por una versión era ilegible para
   la del servicio — eso era el «perdió su contraseña». `appsettings.json` fija ahora
   `Auth:BuzonKeyFile` (ruta absoluta, `App_Data/buzon-clave.bin` del proyecto), y se copió a esa ruta la
   clave que usa el servicio, así lo que ya estaba bien guardado sigue sirviendo.
4. **Los avisos dicen la verdad.** `CorreoService.FalloDelEnlace` separa las causas (enlace incompleto,
   sin contraseña, cifrado con otra clave, valor de una versión anterior) y `BuzonPublico` trae
   `contrasenaLegible`: la tabla de Usuarios muestra el distintivo «contraseña por reescribir» y el
   diálogo Editar quita «Mantener la guardada» y exige reescribirla. `GuardarBuzon` se niega a guardar si
   la clave no está en disco (`ClavePermanente`) o si la que se quiere conservar ya no descifra, para no
   volver a dejar un enlace muerto.
5. **La causa de fondo: el paquete del cifrado.** `Cifrar` codificaba en base64 el denso y la etiqueta
   **por separado y después los pegaba**. El `=` de relleno del primero caía en medio del texto y
   `Convert.FromBase64String` reventaba al descifrar: **cualquier contraseña cuyo número de bytes no fuera
   múltiplo de 3 se guardaba ilegible** (o sea, casi todas, incluida la de `iramosh`). Ahora se unen los
   **bytes** y se codifica una sola vez (`g1.b64(nonce).b64(denso+etiqueta)`); `Descifrar` no cambió, así
   que lo que sí se leía sigue leyéndose. `EsEmpaqueAntiguo` detecta los valores escritos con el paquete
   defectuoso para avisarlo como tal.

**Archivos modificados:** `Program.cs`, `Services/Correo.cs`, `Services/BuzonStore.cs`,
`Models/Buzon.cs`, `wwwroot/admin/correos/index.html`, `wwwroot/admin/usuarios/index.html`,
`appsettings.json`, `docs/FRONTEND.md`, `docs/REGISTRO-CAMBIOS.md`.

**Para aplicarlo:** ejecutar `actualizar.bat` como Administrador (detiene el servicio, publica y lo
reprende). Después, una sola vez por buzón afectado: **Usuarios → Editar → escribir la contraseña del
buzón → Guardar cambios**.

---

## 2026-11-03 (tarde) — Ventana de permisos legible, mostrar la contraseña en el login y mínimo de 3 caracteres

**Módulo:** Usuarios (`/admin/usuarios/`), inicio de sesión (`Pages/Login.cshtml`), `Services/UserRules.cs`.
**Tipo:** frontend + backend. **Base de datos:** ninguna.

**Por qué:** «esta ventana de permisos no me sirve visualizarla así, también permite que las contraseñas
se puedan ver al iniciar sesión si el usuario así lo quiere y que se pueda tener una contraseña de 3 dígitos
como mínimo». La ventana de Permisos venía con un `min-width: 760px` dentro de un diálogo de 460 px: el
contenido se salía por los dos lados, la primera columna (nombre y descripción del permiso) quedaba
recortada y aparecía una barra de desplazamiento horizontal.

**Qué cambió:**

1. **Permisos, sin tabla y sin recortes.** El `<table class="matriz">` se sustituye por una cuadrícula CSS
   (`.perm-cab` + `.perm-fila`): columna elástica para `clave` + descripción (con `overflow-wrap`), y
   columnas fijas para Rol / Ajuste / Efectivo. El diálogo usa `dialog.modal.permisos`
   (`max-width: min(920px, 100% - 20px)`) y ya no hay `min-width` interior, así que **no existe scroll
   horizontal**. Por debajo de 760 px el encabezado desaparece y cada control carga su propia etiqueta
   («Rol», «Ajuste», «Efectivo»), con el nombre y la descripción en su propia línea. Se conservan los
   grupos (`Sistemas médicos`, `Acceso`, …), los chips Sí/No, el `select` heredado/conceder/revocar y el
   candado de `roles.administrar` para el rol Admin.
2. **Mostrar la contraseña en el login.** El campo de contraseña pasa a `<div class="field-pwd">` con un
   botón de ojo (`#btnVerPwd`) que alterna `type="password"` / `type="text"`. La preferencia se recuerda en
   `localStorage` (`meditechnosys.login.verPassword`) **por equipo**, así que cada persona decide si la
   quiere siempre visible; si el navegador bloquea el almacenamiento el botón sigue funcionando durante la
   visita. El botón actualiza `aria-pressed` y su etiqueta («Mostrar/Ocultar la contraseña») y el icono se
   tacha al mostrar. No se cambia nada del envío: el `name="Password"` y el `POST` son los mismos.
3. **Contraseña de 3 caracteres.** `UserRules.PasswordError` usa `UserRules.PasswordMin = 3` (antes 8) y el
   mensaje dice «al menos 3 caracteres»; los dos campos de Usuarios bajaron a `minlength="3"` y el
   placeholder dice «Mínimo 3 caracteres». Los documentos (`ARQUITECTURA.md`, `USUARIOS-Y-MYSQL.md`,
   `MEJORAS-FUTURAS.md`) siguen la misma cifra. **Riesgo asumido y anotado:** con 3 caracteres la contraseña
   es fácilmente adivinable; el hash (PBKDF2-SHA256, 210 000 iteraciones) no cambia y no se relajó ningún
   otro control, pero la intranet queda expuesta si alguien prueba accesos desde fuera de la red.

**Archivos modificados:** `wwwroot/admin/usuarios/index.html`, `Pages/Login.cshtml`,
`wwwroot/css/login.css`, `wwwroot/css/theme.css`, `Services/UserRules.cs`,
`docs/ARQUITECTURA.md`, `docs/USUARIOS-Y-MYSQL.md`, `docs/MEJORAS-FUTURAS.md`, `docs/REGISTRO-CAMBIOS.md`.

**Verificado:** instancia desechable sobre una copia de la base de usuarios (puerto 5077) con Chrome sin
interfaz. La ventana de Permisos midió 45 filas y **cero píxeles de scroll horizontal** con la ventana a
1264, 684 y 500 px, sin desbordes en ninguna celda; revocar `patologia.consultar` desde la ventana guardó
`{"clave":"patologia.consultar","concedido":false}` en el servidor y cerró el diálogo; el candado de
`roles.administrar` sigue deshabilitado para Admin. En el login, el botón pasó de `type="password"` a
`type="text"` con `localStorage` en `1` y la siguiente visita arrancó ya en texto visible. Alta con
`abc` → 201, inicio de sesión con `abc` → 200, cambio a `123` → 204 y `12` → 400 con el mensaje nuevo.

**Para aplicarlo:** `actualizar.bat` como Administrador (detiene el servicio, publica y lo retoma).

---

## 2026-11-02 (noche) — La intranet aprende el punto real por rango de IP; la zona de la IP ya no se pinta como ubicación

**Módulo:** Bitácora de accesos (`/admin/bitacora/`) y su resolver (`Services/Ubicaciones.cs`).
**Tipo:** backend + API + frontend. **Base de datos:** ninguna (no se añadió ni se cambió ninguna tabla;
solo se leen las que ya estaban).

**Por qué:** «me sigue mostrando una ubicación errónea, yo me encuentro en Vallejo Poniente, Azcapotzalco,
02990 Ciudad de México y tú me mandas a Cuauhtémoc». La medida de la salida anterior ya lo probaba: el punto
que da la IP está a **4,523 m** del edificio y cae en otra alcaldía («Centro, Cuauhtémoc, 06060»). Acortar
ese texto a la zona no arreglaba el fondo —la zona *también* estaba mal—, así que se dejó de mostrar.

**Qué cambió — el nuevo orden de decisión** (antes: rango escrito → el reporte del equipo, fuera por IP o por
navegador → la zona del sitio; ahora todo vive en una sola función, `UbicacionResolver.Decide`, que alimenta a
la vez la columna, la etiqueta y la coordenada, para que nunca se contradigan):

1. lo escrito para su rango de IP (`bitacora_ubicacion`), si alguien lo escribió;
2. la **posición real del propio aparato** (reporte `navegador` con coordenadas válidas);
3. **nuevo:** el **punto aprendido del rango /24** — el punto real más reciente que dio cualquier equipo
   del mismo rango. Los puestos de `10.252.252.x` heredan el lugar del vecino que sí aceptó el permiso,
   marcado como «punto aprendido del mismo rango de IP (±N m)»;
4. el edificio calibrado en el mapa (`bitacora_sitio`), si es que alguien lo marcó;
5. y si nada de eso hay, **«Sin confirmar»**. La zona por IP solo aparece en la etiqueta explicativa
   («sin permiso del navegador (su salida a Internet sale por Cuauhtémoc, Ciudad de México)»), nunca en la
   columna de ubicación ni en el Excel.

**Los reportes que llegan por `127.0.0.1`:** cuando alguien abre la intranet desde el propio servidor
(`http://localhost:5023`), el navegador sí entrega el GPS —localhost es una excepción a la regla de
seguridad—, pero la fila queda anotada con la IP de retorno y la bitácora la muestra como «Este equipo (el
servidor)», así que ese punto no aprovechaba a nadie. Ahora se reparte entre los rangos /24 de las tarjetas
de red del servidor (leídos con `NetworkInterface`, sin `127.*`), que son los puestos de esa misma máquina.
En producción, la PC del servidor tiene `10.252.252.200` y `192.168.100.8`, así que el permiso que ya se dio
una vez en esa PC cubre los dos rangos donde están los equipos vistos (`10.252.252.68`, `10.252.252.126`,
`192.168.100.7`).

**API:** `GET /api/bitacora/ubicacion-estado` cambia los contadores. Ya no hay `conAproximacion` ni
`sinReporte`; ahora devuelve `conPosicionReal`, **`conPuntoAprendido`**, `conDireccionEscrita`,
`sitioCalibrado` y **`sinConfirmar`**, y cada equipo trae además `ubicacion` y `marca`. La pantalla
reescribe su línea con estos números.

**Archivos modificados:** `Services/Ubicaciones.cs` (nuevo `Prefijo`, `SinConfirmar`, `ArmaAprendidos`,
`PrefijosDeLaMaquina`, la caché `_aprendidos` y la decisión unificada `Decide`; se retiraron las tres
copias del orden de decisión), `Program.cs` (endpoint de estado), `Models/Audit.cs` (los textos que puede
llevar `FuenteUbicacion`), `wwwroot/admin/bitacora/index.html` (frase de estado, ayuda de la columna y su
comentario de estilos) y `.verify/mock-ubicacion.mjs` (creado: mock con los valores reales para probar la
pantalla sin tocar el servicio).

**Verificado:** compilación en limpio (0 advertencias, 0 errores) y `node --check` del guion de la pantalla
(356 líneas). **Tabla de decisión probada con los datos vivos** de `bitacora_lugar` (harness de reflexión
sobre el DLL compilado, en un proyecto desechable): los tres equipos con reporte por IP pasaron de
«Centro, Cuauhtémoc, 06060» a «Avenida Antonio Valeriano, Colonia Patrimonio Familiar, C.P. 02980,
Azcapotzalco, Ciudad de México · punto aprendido del mismo rango de IP (±93 m) · 19.466221, -99.145026»; un
puesto de `10.252.253.15` (rango que nadie aprendió) sale «Sin confirmar»; `127.0.0.1` sigue «Este equipo
(el servidor)»; el rango escrito a mano gana a todo; la posición real del propio aparato gana al punto
aprendido; un reporte `navegador` sin coordenadas no se hereda ni se pinta como real; y el calibrado del
mapa conserva su comportamiento. La pantalla, contra el mock en `http://127.0.0.1:5099`: la tabla de
usuarios y los movimientos de un usuario muestran la dirección aprendida con su etiqueta, la frase de estado
cuenta 3 heredados y 2 sin confirmar, y en consola solo queda el 404 de `/api/ubicacion` (el mock no lo
implementa; en el servidor real sí existe). **No se probó en vivo.**

**El límite honesto:** el punto heredado es del rango, no del puesto. Si un puesto de `10.252.252.x` estuviera
en otro edificio, saldría con el lugar de su vecino, y la etiqueta lo dice para que se pueda detectar. Lo que
no se puede heredar es el detalle de la calle: eso sigue pidiendo el permiso del navegador en ese aparato
(HTTPS ya está prendido, así que basta con aceptarlo una vez).

**Despliegue:** **no publicado.** Compilado en `bin\Release`; hace falta el republicado elevado de siempre
(`.verify\desplegar-sin-pausa.bat` o `actualizar.bat`, aceptando la ventana de administrador), que conserva
`publish\App_Data` —donde está el certificado— y `publish\dicom_downloads`. Antes de publicar hay que mirar
que no esté corriendo una descarga DICOM. Regla de la casa: **ningún cambio sale a producción sin el visto
bueno expreso del usuario.**

---

## 2026-11-02 (13:30, publicado) — Solo zona cuando el punto viene de la IP, y salto obligado a https

**Módulo:** Bitácora de accesos / resolver de ubicaciones (`Services/Ubicaciones.cs`) y configuración del
servidor. **Tipo:** backend + configuración. **Base de datos:** ninguna.

**Medición que decidió todo (con los datos reales de hoy):** el reporte con permiso del navegador de esta PC
(`19.466223, -99.145028`) cayó a **61 m** del Centro Médico Nacional La Raza — el margen que presumía el
navegador era de 92 m y acertó. El punto que da la IP (`19.428470, -99.127610`) está a **4,523 m** y dice
«Centro, Cuauhtémoc, 06060», una alcaldía que no es. O sea: la ubicación real ya era correcta; lo que salía
mal era la aproximación por IP, y salía mal **porque se mostraba como dirección postal** (con colonia y código
postal), con cara de saber dónde está el aparato cuando apenas sabe por dónde sale a Internet.

**Qué cambió:**

1. **`Ubicaciones.SoloZona(texto)`** (método nuevo): deja apenas las dos últimas partes de una dirección y
   quita los códigos postales. Se aplica **solo** a lo que viene de geolocalizar una IP —el reporte con
   `fuente = ip` y la dirección automática del sitio—; la posición real del navegador (`fuente = navegador`)
   **sigue mostrando la dirección postal completa**, porque esa sí es del puesto. Efecto en pantalla, en
   `/api/bitacora`, en `/api/bitacora/resumen` y en el Excel: «Centro, Cuauhtémoc, 06060 Ciudad de México» →
   **«Cuauhtémoc, Ciudad de México · zona aproximada por IP»**.
2. **`Https:Redirigir = true`** (el visto bueno fue a las 13:25: «publicar con el salto prendido»). Quien abra
   `http://…:5023` cae solo en `https://…:5024`, y ahí el navegador ya puede pedir el permiso de ubicación sin
   que nadie escriba nada. `localhost` y `/intranet.cer` siguen sin saltar, y el http directo de una petición
   que no es `GET` usa 307 para no perder el método. El costo aceptado: en una PC que todavía no corra
   `confiar-certificado.bat`, el navegador muestra su aviso de certificado hasta que se avance (una vez por PC).

**Por qué:** «sigue estando mal la ubicación» (13:15) y las dos respuestas de la encuesta: «mostrar solo zona,
sin calle ni C.P.» y «sí, que salte solo».

**Archivos modificados:** `Services/Ubicaciones.cs` (el método nuevo y sus dos usos dentro de `UbicacionDe`) y
`appsettings.json` (`Https:Redirigir`). La copia publicada quedó igual. No se tocó `Program.cs`, ni la pantalla,
ni el Excel: la columna ya existía y ahora simplemente trae texto más honesto.

**Verificado:** compilación 0 advertencias / 0 errores; `SoloZona` corrido contra las direcciones reales de la
base (Centro→«Cuauhtémoc, Ciudad de México»; Azcapotzalco→«Azcapotzalco, Ciudad de México»; un solo trozo se
queda igual; vacío queda vacío). En una instancia aparte con el salto prendido: `http://IP:5097/Login` → 302 a
la dirección segura, `localhost` sin salto, `/intranet.cer` sin salto y https en 200. **Ya publicado** a las
13:30:45 (`sc stop` → `dotnet publish` → `sc start`, código de salida 0, servicio `RUNNING` con PID nuevo) y
comprobado en vivo: `http://10.252.252.200:5023/Login` responde 302 a `https://10.252.252.200:5024/Login`, https
da 200, `localhost:5023` sigue sirviendo normal, `/intranet.cer` da 200 y el certificado de `publish\App_Data\https`
(no se tocó). El firewall de esta PC está desactivado en los tres perfiles, así que `abrir-puerto-seguro.bat` no
hizo falta. No se cortó ninguna descarga: `dicom_downloads` quedó intacto y antes de publicar ya no había
archivos moviéndose. Lo que **no** se pudo comprobar aquí es cómo se ve la columna ya publicada, porque leer la
bitácora necesita sesión; le toca al usuario con un F5.

**Siguiente paso propuesto (sin hacer todavía, pide visto bueno):** *punto aprendido por rango de IP*. Cuando
varios aparatos de un mismo rango (`10.252.252.x`, `10.252.253.x`) reportan su posición real, el promedio de ese
rango se usa para los escritorios de cable del mismo rango, marcándolo «punto aprendido de otros equipos del
mismo rango». Sirve porque un escritorio sin wifi ni GPS no se ubica bien solo —aunque hoy, con 61 m de error,
el permiso del navegador en esta PC demostró que sí funciona cuando el aparato tiene wifi.

---

## 2026-11-02 (12:40) — La ubicación real: https propio + permiso del navegador, sin escribir nada

**Módulo:** Bitácora de accesos (`/admin/bitacora/`), arranque del servidor y `ubicacion.js`.
**Tipo:** backend + frontend + configuración + desplegados. **Base de datos:** ninguna (la tabla
`bitacora_lugar` ya tenía `fuente` y `precision_metros`; no se agregó ni se borró columna).

**Qué cambió:** la intranet **sigue viéndose igual** —Usuarios · conexión, último acceso y desde dónde, y
Movimientos con sus filtros y su Excel—, pero ahora la ubicación puede ser **la posición real del aparato**,
obtenida con el permiso del navegador, sin que nadie escriba direcciones. Cuatro piezas:

1. **Dirección segura automática** (`Services/CertificadoIntranet.cs`, archivo nuevo, 230 líneas). Al arrancar,
   el servidor se firma a sí mismo un certificado (RSA 2048, 10 años) con `localhost`, `127.0.0.1`, el nombre
   del equipo y **todas las IPs locales de esa máquina**, y lo guarda en `App_Data\https` (`intranet.pfx` +
   `intranet.clave` + `intranet.cer`); la contraseña es aleatoria y no se escribe en ningún `appsettings.json`.
   Si ya existe y vence en menos de un mes, o si hoy hay una IP/nombre que no cubría, se arma uno nuevo solo.
   El puerto seguro (`https://0.0.0.0:5024`) **se anexa** a la lista de direcciones con
   `builder.Configuration.AddInMemoryCollection(["urls"] = …)`, que es lo único que le gana al `--urls` con el
   que arranca el servicio: `instalar-servicio.bat` no se toca y el `http://…:5023` de siempre sigue igual.
2. **El navegador ya puede pedir el permiso** (`wwwroot/js/ubicacion.js`, reescrito, 289 líneas). En https
   `navigator.geolocation.getCurrentPosition` abre la ventaneta de Permitir/Rechazar; si la aceptan, se reporta
   el punto con su precisión (con `enableHighAccuracy`, espera de 12 s, caché de 5 min) y se repite cada 30 min.
   Si se rechaza o el aparato no da posición, **queda el respaldo por IP** (cada 6 h) para que la columna nunca
   salga vacía. Si el permiso sigue sin contestarse, el propio aparato muestra una hojita discreta abajo a la
   izquierda («Permitir ubicación» / «Más tarde») —un clic, sin pantallas de configuración y sin campos a mano—;
   esa hojita deja de salir cuando ya hay respuesta del permiso.
3. **De dónde salió cada ubicación, visible** (`Services/Ubicaciones.cs` → `MarcaDe(ip)`; `Models/Audit.cs` →
   `FuenteUbicacion` + `PrecisionMetros`, calculados al consultar, no guardados por fila). Tres marcas:
   **posición real del equipo (±N m)** en verde, **zona aproximada por IP** y **escrita para su rango de IP** en
   cursiva. Se ven en la columna «desde dónde» de cada usuario, dentro de cada movimiento, en el Excel (columna
   nueva **«Ubicación obtenida»**, ahora 11 columnas) y en un resumen de arriba: «De 2 equipo(s) visto(s) en la
   red, 1 reportan su posición real; 1 siguen con la zona aproximada por IP…». Los equipos del propio servidor
   (`127.x`) no marcaron nada.
4. **Dos endpoints y dos hojitas nuevas.** `GET /intranet.cer` (sin login) baja el certificado para confiarlo, y
   `GET /api/bitacora/ubicacion-estado` (permiso de ver bitácora) dice si hay https, qué puerto, cuántos equipos
   traen posición real y cuáles no. Para el personal: **`confiar-certificado.bat`** (una vez por PC, sin
   administrador: baja `intranet.cer` y lo pone en «Autoridades de certificación raíz de confianza» del usuario).
   Para el servidor: **`abrir-puerto-seguro.bat`** (una vez, con administrador: regla de entrada TCP 5024).

**Por qué:** «tal cual como está debes colocar la ubicación geográfica, de ser necesario pide la ubicación como
permiso del navegador, ya que la que marcas es errónea; no pongas en agregar manualmente porque no me sirve;
debe ser de manera automática y debe aparecer tanto en movimientos como en Excel» (12:05). El punto es que
Chrome **no entrega la geolocalización en `http://`** (desde Chrome 50 la quita en orígenes no seguros: en
`http://10.252.x.x:5023` la ventaneta ni siquiera aparece), así que sin https era imposible cumplir lo pedido.

**Configuración:** sección `Https` en `appsettings.json` — `Activo` (verdadero), `Puerto` (5024), `Nombres`
(direcciones extra por si alguien entra por un alias del hospital; vacío porque las IPs locales se detectan
solas) y `Redirigir` (**falso por ahora**: el salto obligatorio de http:// a https:// se prende hasta que el
certificado esté confiado en todas las PC, para no dejar a nadie fuera). El salto, si se prende, nunca aplica a
`localhost` ni a `/intranet.cer`.

**Archivos:** nuevo `Services/CertificadoIntranet.cs`, `confiar-certificado.bat`, `abrir-puerto-seguro.bat`;
modificados `Program.cs` (https al arrancar, middleware opcional de salto, `/intranet.cer`,
`/api/bitacora/ubicacion-estado`, `resumen` con marca, Excel con columna nueva), `Models/Audit.cs`,
`Services/Ubicaciones.cs`, `wwwroot/js/ubicacion.js`, `wwwroot/admin/bitacora/index.html` (443 → 478 líneas) y
`appsettings.json`.

**Verificado:** compilación en limpio (0 advertencias, 0 errores) y `node --check` sobre `ubicacion.js` y sobre
el `<script>` de la pantalla (353 líneas). Servidor probado **de punta a punta sin tocar producción**: una
instancia aparte (content root temporal, almacén JSON, puertos 5097/5024) — arrancó con los dos direcciones,
creó el certificado con `10.252.252.200, 127.0.0.1, 192.168.100.8, DESKTOP-74GJ88M, localhost`, recibió un
reporte con `fuente: navegador` y devolvió «Calle Talavera 17, Col. Centro, C.P. 06060, Cuauhtémoc, Ciudad de
México» con `19.428469, -99.127656` y la marca **posición real del equipo (±18 m)** en `/api/bitacora`, en
`/api/bitacora/resumen`, en `/api/bitacora/ubicacion-estado` y **dentro del .xlsx real** (se abrió y se leyó).
En navegador contra esa instancia: la página cargó en https, el resumen de arriba escribió la frase de conteos
y el respaldo por IP funcionó. **No se pudo probar la ventaneta del permiso en un navegador real**: el Chrome
automatizado de aquí trae la geolocalización rechazada, y el aviso de certificado autofirmado no se pudo quitar
porque confiar el certificado en esta máquina no estaba dentro de lo autorizado. Tampoco **se publicó nada**.

**Lo que hay que saber antes de usarlo:** hasta que una PC no corra `confiar-certificado.bat`, al abrir
`https://…:5024` verá el aviso de «conexión no privada» (hay que avanzarlo una vez, o aceptar el certificado).
Un escritorio sin wifi/GPS puede seguir dando solo zona aproximada: la columna lo dice explícitamente, y con
cuatro hospitales (HES, HGR, HGO, HI) lo que corresponde es justamente que cada aparato reporte su propio punto.

**Despliegue:** **no publicado.** Compilado en `bin\Release`. Al publicar, `App_Data` no viaja en la
publicación (el `.csproj` lo bloquea), así que el certificado se crea solo al primer arranque en
`publish\App_Data\https` y sobrevive republicaciones. Orden sugerido: `actualizar.bat` (elevado) →
`abrir-puerto-seguro.bat` (elevado) → `confiar-certificado.bat` en cada PC → abrir la intranet en
`https://…:5024` y aceptar el permiso. Regla de la casa: **ningún cambio sale a producción sin el visto bueno
expreso del usuario.**

---

## 2026-11-02 (11:50) — Fuera también el panel «Rangos de IP y ubicaciones»: la ubicación sale sola

**Módulo:** Bitácora de accesos (`/admin/bitacora/`). **Tipo:** frontend. **Base de datos:** ninguna
(la tabla `bitacora_ubicacion` sigue existiendo, vacía; no se borró nada).

**Qué cambió:** la pantalla queda en dos secciones: **Usuarios · conexión, último acceso y desde dónde** y,
al abrir a un usuario, **Movimientos** con sus filtros por día, hora y equipo y su botón «Descargar Excel».
Se van el formulario «Rango de IP / Ubicación / Guardar rango», la tabla **Ubicaciones asignadas**, la tabla
**Equipos que se han conectado** y el aviso de la dirección del sitio. La ubicación sigue saliendo **sola**:
cada aparato reporta su punto al abrir la intranet, la intranet lo traduce a dirección, y esa dirección es
la que se ve en la columna «desde dónde» de cada usuario, dentro de cada movimiento y en el Excel. La
pantalla pasó de 726 a 443 líneas.

**Por qué:** «quita el apartado de Rangos de IP y ubicaciones, el chiste es que se tenga la ubicación
geográfica de manera automática» (11:50, con captura de la pantalla ya sin mapa).

**Qué se conserva:** los endpoints `GET/PUT/DELETE /api/bitacora/ubicaciones` y
`POST/GET/DELETE /api/bitacora/sitio` siguen en el servidor, y la tabla `bitacora_ubicacion` sigue ahí. Si
algún día se quiere volver a poner nombres por rango («RIS03 · Radiología, 2º piso») o a marcar el edificio,
el backend no hay que rehacerlo.

**El límite honesto de lo automático (para que no vuelva a salir «esto no me sirve»):** el punto que la
intranet puede averiguar **sin que nadie lo escriba** es la salida a Internet del aparato, y eso da **zona,
no edificio**. Además, mientras la intranet se sirva en `http://` (puerto 5023) el navegador **no entrega el
GPS real** por regla de seguridad —solo lo hace en HTTPS—, así que `wwwroot/js/ubicacion.js` reporta las
coordenadas que dan los servicios de geolocalización por IP y por eso todos los equipos salen con el mismo
punto. Y el hospital no es uno solo: el sistema maneja **HES, HGR, HGO e HI**, que están en puntos distintos
de la ciudad, así que una dirección fija para todo el sitio tampoco cuadraría. Para que la columna quede
exacta hay dos caminos reales, y ninguno es «automático puro»: poner la intranet en **HTTPS** y que cada
aparato acepte una vez el permiso de ubicación (ahí `ubicacion.js` ya usa el GPS sin tocar nada más), o
volver a una pantalla de configuración —mapa o rangos— que ahora ya no está.

**Archivos modificados:** `wwwroot/admin/bitacora/index.html` — fuera la tarjeta `#seccionRangos` con todo
su contenido y su código: `pintarRangos`, `pintarEquipos`, `pintarSitio`, `notaSitioExacta`, `notaCelda`,
`botonListo`, `cargarRangos`, `guardarRango`, `borrarRango`, `avisoRangos`, `refrescarDespuesDeCambio`, sus
`addEventListener` y el CSS `.rango-alta`.

**Verificado:** compilación en limpio (0 advertencias, 0 errores) y `node --check` sobre el `<script>` de la
pantalla (328 líneas). En navegador contra la API simulada
(`http://localhost:5098/admin/bitacora/index.html`): solo quedan las dos secciones de siempre y ya no existe
ningún elemento del panel retirado (`seccionRangos`, `nuevoRango`, `cuerpoRangos`, `cuerpoEquipos`); la
tabla de usuarios pinta su ubicación automática —«PAT01 · 10.252.253.90 · Ejido 26, Col. San Bartolo
Atepehuacan, C.P. 07730, Gustavo A. Madero, Ciudad de Mexico · Chrome 130 · Windows 10»—; al abrir los
movimientos de un usuario salen con su ubicación y los botones Cerrar / Limpiar / Descargar Excel. Consola
sin errores. **No se probó en vivo.**

**Despliegue:** **no publicado.** Compilado en `bin\Release`; hace falta el republicado elevado de siempre
(`actualizar.bat`, aceptando la ventana de administrador), que conserva `publish\App_Data` y
`publish\dicom_downloads`. Regla de la casa: **ningún cambio sale a producción sin el visto bueno expreso
del usuario.**

---

## 2026-11-02 (tarde) — Fuera el mapa de la bitácora: la dirección exacta se escribe en su rango de IP

**Módulo:** Bitácora de accesos (`/admin/bitacora/`).
**Tipo:** frontend (se retira una pantalla). **Base de datos:** ninguno.

**Qué cambió:** desaparece el bloque **Ubicación exacta del sitio** de la bitácora: el mapa, el campo
«Punto marcado», el campo «Dirección exacta» y los botones «Guardar ubicación exacta» y «Volver a la
automática». La pantalla vuelve a ser la de antes del 2 de noviembre —**Rangos de IP y ubicaciones** con su
tabla de «Ubicaciones asignadas» y su tabla de «Equipos que se han conectado»—. La línea de aviso ya no
invita a marcar el punto: ahora dice «Dirección del sitio: «…» (medida por la intranet al salir a
Internet…). Si la quiere exacta, escríbala en el rango de IP de arriba: lo escrito a mano es lo que sale en
la bitácora», y la ayuda de los equipos explica que lo que deduce la intranet es aproximado (acierta colonia
o zona, no el edificio).

**Por qué:** lo pidió el usuario, dos veces seguidas: «sigue apareciendo esto, quítalo» y, al preguntarle
cómo quería la pantalla, «borrar el mapa, dejarlo como antes». Revisando el servidor se entendió el
malentendido: la tabla `bitacora_sitio` está **vacía** (nadie guardó el punto), así que la bitácora seguía
mostrando la dirección aproximada de la IP —«Centro, Cuauhtémoc, 06060 Ciudad de México»— y lo único visible
del mapa eran sus textos de ejemplo en gris, que parecían datos reales.

**Cómo se pone ahora la dirección exacta, sin mapa:** en «Rangos de IP y ubicaciones», escribir el rango que
cubra el hospital (por ejemplo `10.252.*`) y en Ubicación la dirección completa —«Av. … 123, Col. …,
C.P. …, alcaldía, Ciudad de México»— y pulsar **Guardar rango**. Eso es lo que sale en la columna Ubicación
de la bitácora y en el Excel, también en los movimientos ya guardados, porque la ubicación se calcula al
consultar. Para distinguir servicios, se deja el rango general y se agregan los particulares
(`10.252.253.*` → «Radiología · Torre A, 2º piso»): manda siempre el rango más largo.

**Qué se queda:** el geocodificador con sus dos lecturas (`zoom=18` para el equipo que da GPS real desde el
navegador, `zoom=16` para lo que solo salió de geolocalizar la IP) y la columna **Coordenadas** del Excel,
que siguen siendo útiles sin mapa. Los endpoints `/api/bitacora/sitio*` siguen en el servidor pero ya no se
usan desde la pantalla; si algún día se vuelve a la idea, el backend está. Las reglas de la columna
«Ubicación» de la bitácora no cambiaron.

**Archivos modificados:** `wwwroot/admin/bitacora/index.html` — fuera el bloque del mapa y su CSS
(`.sitio-cal`, `.mapa`, `.leaflet-container`), fuera `cargarLeaflet`, `iniciarMapa`, `ponPunto`,
`previsualizaDireccion`, `ponDireccion`, `guardarSitio`, `quitarSitio`, `borrarMarcador`, `rehacerMapa`,
`leePunto`, `esPunto` y sus `addEventListener`; `pintarSitio`, `notaSitioExacta` y la celda «Ubicación» de la
tabla de equipos quedaron en su versión simple (sin hablar de calibración).

**Verificado:** compilación en limpio (0 advertencias, 0 errores). El `<script>` de la pantalla pasa
`node --check` (542 líneas). En navegador contra la API simulada
(`http://localhost:5098/admin/bitacora/index.html`): la página ya no tiene ni un elemento del mapa (de los
ids del bloque retirado solo sobrevive el aviso de texto), los encabezados son «Ubicaciones asignadas» y
«Equipos que se han conectado», y al guardar el rango `10.252.*` con una dirección completa la pantalla
responde «Guardado: 10.252.* ahora aparece como «Av. Instituto Científico y Educación 123, Col. Magnetópolis,
C.P. 07650, Gustavo A. Madero, Ciudad de México», también en los movimientos anteriores.» mientras la tabla
de equipos sigue pintando IP, equipo, dirección geográfica y ubicación asignada. Consola sin errores. **No
se probó en vivo.**

**Despliegue:** **no publicado.** Compilado en `bin\Release`; para verlo en la intranet hay que ejecutar el
republicado elevado de siempre (`actualizar.bat`, aceptando la ventana de administrador), que conserva
`publish\App_Data` y `publish\dicom_downloads`. Sigue en pie la regla de la casa: **ningún cambio sale a
producción sin el visto bueno expreso del usuario.**

---

## 2026-11-02 — La ubicación de la bitácora ahora es la dirección postal completa (con coordenadas)

**Módulo:** Bitácora de accesos (`/admin/bitacora/`) → `Services/Lugares.cs`, `Services/Ubicaciones.cs`.
**Tipo:** backend + API + frontend + Excel. **Base de datos:** ninguno (se guarda en la misma
`bitacora_sitio.direccion` de 200 caracteres).

**Qué cambió:**

1. **La dirección dejó de ser «la zona».** Al marcar el edificio en el mapa, la intranet ahora arma la
   dirección postal: **calle con su número, colonia, código postal, alcaldía, ciudad** (y el país solo si no
   es México). Antes salía «Vallejo, Gustavo A. Madero, 07870 Ciudad de México»; ahora sale, con los datos
   reales del mapa, algo como **«UMF No. 33 Vallejo, Calle Ejido 26, Col. San Bartolo Atepehuacan,
   C.P. 07730, Gustavo A. Madero, Ciudad de México»**. Si el punto cae encima de un sitio con nombre
   (una unidad médica, un hospital, un edificio), ese nombre abre la dirección.
2. **Vista antes de guardar.** En cuanto se suelta el punto, el mapa pregunta y la pantalla escribe la
   dirección completa en su campo y debajo muestra la lectura en voz alta: *«El mapa lee: «…» (coordenadas
   19.432823, -99.126978)»*. Se puede corregir a mano y lo corregido es lo que se guarda.
3. **Lo escrito a mano manda.** Antes, si había punto, el mapa pisaba lo que el administrador había
   escrito. Ahora el mapa solo escribe cuando el campo está vacío o cuando lo que hay ahí lo puso el propio
   mapa; una corrección a mano ya no se pisa al mover el punto.
4. **Las coordenadas viajan con la dirección.** Se ven en el aviso del panel («… — 19.432823, -99.126978»),
   en la tabla de equipos (`coordenadas` en la API) y en el **Excel**, que estrenó la columna
   **«Coordenadas»** junto a «Ubicación».
5. **Los equipos con GPS real también salen exactos.** Cuando el aparato reporta coordenadas propias
   (`fuente = navegador`, GPS o wifi), la dirección se pide con la misma lectura fina. Cuando el punto solo
   salió de geolocalizar la IP sigue saliendo **aproximado a propósito**: pedirle calle y número a un punto
   que es del proveedor de Internet daría una dirección finísima pero mentira, y eso no justifica nada.

**Por qué:** con el mapa ya calibrado el resultado seguía siendo «muy general», y de esa columna depende
justificar faltas ante Recursos Humanos. Lo pedido: dirección completa —calle, número, colonia, C.P.,
alcaldía, ciudad— con las coordenadas exactas, visible en la bitácora.

**Cómo funciona:** `GeocodificadorLugar.LeerAsync(lat, lon, exacta)` tiene dos lecturas del mismo punto,
cada una en su propio hueco de la caché (la clave guarda el modo): **exacta** con `zoom=18` y
**aproximada** con `zoom=16`. La parte fina vive en `GeocodificadorLugar.DesmenuzaExacta(raiz)`, que es
lógica pura y no toca la red: toma `road` + `house_number`, `neighbourhood`/`suburb`/`quarter` (le pone
«Col.» salvo que el mapa ya traiga la palabra), `postcode` como «C.P. …», `borough`/`city_district`/
`municipality` como alcaldía, `city`/`town`, `state` y quita lo repetido (`Ciudad de México` llega como
ciudad y como estado: se escribe una vez) y lo recorta a los 200 caracteres de la columna. Las reglas de
cortesía con OpenStreetMap no cambian: una petición por segundo, identificación en el User-Agent, caché de
30 días por punto y reintento a las 6 horas de un fallo —por mover el marcador varias veces no se gasta
cuota de más—.

**API:** nueva `GET /api/bitacora/sitio/preview?lat=&lon=` (solo lee, no guarda; 502 con mensaje si el mapa
no contesta, y la pantalla deja escribir a mano). Nueva `GET /api/bitacora/sitio` con la dirección guardada,
el punto y `coordenadas` ya escritos. `POST /api/bitacora/sitio` ahora guarda el texto del campo cuando el
administrador lo dejó lleno, devuelve también `edificio`, y sigue aceptando guardar solo la dirección (sin
punto) o solo el punto (la calcula). `GET /api/bitacora/ubicaciones` añade `coordenadas` por equipo. Los
tres endpoints de calibración piden `configuracion.administrar` y el de lectura `bitacora.ver`.

**Archivos creados:** `.harness-sitio/` (arnés fuera del proyecto web, enlaza `Services/Lugares.cs` y
`Services/Texto.cs` reales) con `muestras/real_centro.json` y `muestras/real_calle_sin_numero.json`, que son
**dos respuestas verdaderas de OpenStreetMap** guardadas como fija, y `.verify/mock-bitacora.mjs` (mock de la
pantalla en el puerto 5098, con su vista previa).
**Archivos modificados:** `Services/Lugares.cs` (dos lecturas, `PuntoLeido`, `DesmenuzaExacta`, caché por
modo), `Services/Ubicaciones.cs` (`CoordenadasDe`), `Program.cs` (`/sitio/preview`, `GET /sitio`, POST que
respeta lo escrito a mano, columna del Excel, `coordenadas` por equipo y la fina para `POST /api/ubicacion`
cuando el navegador dio GPS), `wwwroot/admin/bitacora/index.html` (bloque del mapa, vista previa, aviso con
coordenadas) y este registro.

**Verificado:** compilación en limpio (0 advertencias, 0 errores). Arnés con las respuestas reales del mapa:
**27 chequeos en paso** —los dos puntos verdaderos salen con calle, número, «Col.», «C.P.», alcaldía y
ciudad una sola vez; una calle sin número no inventa número; la lectura aproximada del mismo punto sigue
corta y sin el número; un edificio con nombre abre la dirección y no se repite dos veces; «Col.» no se
duplica cuando el mapa ya trae «Colonia»; alcaldía repetida como ciudad se escribe una vez; el país aparece
solo fuera de México; sin objeto `address` devuelve vacío en lugar de inventar; una dirección kilométrica
nunca pasa de 200 ni queda cortada en coma suelta—-. La pantalla se probó contra el mock en
`http://localhost:5098/admin/bitacora/index.html`: al hacer clic en el mapa la dirección completa se escribió
sola en el campo con sus coordenadas debajo; al corregirla a mano y mover el punto la corrección **no** se
pisó; al guardar, el aviso quedó diciendo «Ubicación exacta del sitio: «UMF No. 33 Vallejo, Ejido 93,
Col. San Bartolo Atepehuacan, C.P. 07730, Gustavo A. Madero, Ciudad de Mexico» — 19.432823, -99.126978
(fijada en el mapa por admin…)» y la tabla de equipos mostró esa dirección como la que verá la bitácora.
Sin errores en la consola del navegador. **No se probó contra el servicio en vivo** (está sin publicar).

**Uso:** abrir Bitácora → «Ubicación exacta del sitio», marcar el edificio en el mapa (o pegar las
coordenadas), leer la dirección que aparece debajo y, si hace falta, corregirla en su campo; pulsar
**Guardar ubicación exacta**. A partir de ese momento esa dirección —con sus coordenadas— es la que sale en
la columna Ubicación de la bitácora, en la pantalla y en el Excel, para todos los equipos que no tengan un
rango escrito a mano.

**Despliegue:** **no publicado.** Está compilado en `bin\Release`; para que se vea en la intranet hay que
ejecutar el republicado elevado de siempre (`actualizar.bat` / el script de despliegue, aceptando la ventana
de administrador), que conserva `publish\App_Data` y `publish\dicom_downloads`. Se respeta la regla de la
casa: **ningún cambio sale a producción sin el visto bueno expreso del usuario.** Ojo: al marcar el punto por
primera vez la intranet necesita salida a Internet para leer la dirección en OpenStreetMap; si el servidor
está aislado, se escribe la dirección a mano en el campo y se guarda igual.

**Efecto sobre lo ya guardado:** la dirección calibrada que estuviera puesta sigue siendo el mismo texto
(aproximada) hasta que el administrador vuelva a marcar el punto y guarde; los renglones de la bitácora no
tienen nada que migrar, porque la ubicación se calcula al consultar.

---

## 2026-11-02 — Descargas DICOM: varias a la vez, con comprobación de que llegó todo y ZIP en partes

**Módulo:** Visor DICOM → apartado de descargas (`/dicom/`, `Dicom/`). **Tipo:** backend + API + frontend + configuración. **Base de datos:** ninguno.

**Qué cambió:**

1. **Ya no hace falta esperar.** Se pueden iniciar varias descargas seguidas; cada una recibe su propia
   tarjeta con barra, fase, botón «✕ Cancelar» (cancela **esa**, las demás siguen) y sus enlaces de ZIP.
   El servidor deja **3** trabajando a la vez y las que llegan con el cupo lleno quedan **«En cola»** (la
   barra en 0 %) y arrancan solas cuando se libera un hueco. Si hay demasiados trabajos vivos (más de 20)
   la API responde **429** y la pantalla avisa de iniciar menos descargas.
2. **Se comprueba que no faltó nada.** Al terminar, la intranet espera a que la carpeta se asiente, cuenta
   las imágenes **distintas** que de verdad quedaron guardadas por estudio y lo compara con lo que anuncia
   el PACS. Lo que falte se **vuelve a pedir** hasta 2 veces; al final la tarjeta dice «Verificado: llegaron
   las N imágenes…» o, si algo quedó corto, «Faltan X imagen(es) en Y estudio(s), N estudio(s) re-pedido(s)
   al PACS» con el nombre del paciente, la fecha y el nodo de cada estudio corto.
3. **El ZIP ya no se niega.** Antes, al pasar de 100 estudios, directamente no se generaba ZIP y la persona
   se quedaba sin sus archivos. Ahora se corta en **partes numeradas** (`dicom_…_parte1de3.zip`), por
   estudios y por peso (tope nuevo: 2048 MB por parte); la pantalla las baja **una tras otra** y muestra el
   enlace de cada parte. **Un estudio nunca queda partido entre dos ZIP.**
4. **La depuración ya no borre archivos de un trabajo en curso.** El repaso horario solo consultaba las
   descargas; ahora también mira los **reenvíos** (que escriben en `dicom_downloads/reenvio/{jobId}`) y, con
   cualquiera de los dos trabajando, no toca nada.

**Por qué:** lo pedido fue «optimiza el apartado de descargas para que se puedan realizar descargas
múltiples, **sin pérdida de imágenes ni datos**». La revisión encontró los cuatro huecos anteriores: una
sola descarga por PC, ninguna verificación de lo que llegó (un lote cortado a medias producía un ZIP corto
**sin avisar**), el ZIP bloqueado por encima de 100 estudios y el repaso pudiendo borrar archivos de un
reenvío en curso. El usuario eligió las dos opciones de integridad: **«Verificar y reintentar»** y
**«ZIP en partes»**.

**Cómo funciona:** `IntegridadDescarga.DividirEnPartes` decide los cortes (unidad = carpeta del estudio; el
tope se mide **por parte**, no acumulado; una carpeta que sola pasa del tope viaja sola) y
`IntegridadDescarga.Faltantes` decide qué está incompleto (llegaron menos de lo anunciado, o no llegó nada;
si el PACS no informó el conteo basta con que haya llegado algo; **llegar de más nunca se marca como falta**
porque un reintento vuelve a mandar las mismas imágenes y los archivos se sobrescriben por su SOP InstanceUID
— re-pedir es idempotente). El conteo de imágenes distintas vive en `DownloadSession._unicosByStudy` (hash
FNV‑1a de 64 bits del SOP UID), ~1 MB de RAM por cada 100 mil imágenes; los contadores crudos no servían
porque duplican los reintentos y darían un falso «todo bien». La espera de asentamiento (`EsperarAsentamiento`,
2 s sin cambios, tope 30 s) existe porque el PACS puede mandar C‑STORE retrasados después de responder
*Success*; el ZIP se arma **después** de esa espera y de las re‑peticiones, no antes. El C‑FIND de verificación
se topa a 300 estudios para no castigar al PACS, y en el reenvío entre nodos la verificación va apagada
(`verificar: false`) porque se llama estudio por estudio y sumaría segundos a cada uno.

**API:** `POST /api/dicom/studies/download/start` ahora responde `jobId` + `statusUrl` + **`startedNow`**
(y **429** con la cola llena; el `jobId` rechazado se cancela para no dejarlo colgado); rutas nuevas
`GET /api/dicom/studies/download/activos` y `POST /api/dicom/studies/download/{jobId}/cancel`. Se corrigió
`statusUrl`, que apuntaba a `/api/studies/…` en lugar de `/api/dicom/studies/…`. El progreso
(`…/progress`) añade `label`, `phase` (`queued | retrieving | verifying | retrying | zipping | done`),
`verified`, `missingInstances`, `retriedStudies`, `incompleteStudies[]` y `zipParts[]`
(`number`, `total`, `fileName`, `downloadUrl`, `bytes`, `studies`, `files`); `zipFileName`/`zipDownloadUrl`
siguen trayendo la primera parte por compatibilidad. `GET /api/dicom/status` publica `maxSimultaneous`,
`verify`, `maxRetries`, `zipMaxStudiesPerPart`, `zipMaxMBPerPart` y `activeJobs` para que la pantalla se
configure sola.

**Frontend** (`wwwroot/dicom/index.html`): panel **«Descargas en curso (N)»** con una tarjeta por trabajo
(etiqueta legible —«5 estudio(s) · NUÑEZ LOPES JUANA y 3 paciente(s) más · ASYN02/ASYN03»—, barra, fase en
español, avisos de verificación y los enlaces del ZIP), cupo visible («3 a la vez · verificación activada ·
2 reintento(s)»), botones que **ya no se bloquean** mientras algo descarga, **«Ocultar terminadas»**, aviso
de confirmación al lanzar más de 500 estudios, descarga **secuencial** de las partes, y **reenganche
automático** a los trabajos en curso al recargar la página o abrir el apartado desde otra ventana.

**Archivos creados:** `Dicom/IntegridadDescarga.cs` (las dos decisiones de integridad, lógicas puras),
`.verify/mock-descargas.mjs` (mock de la API de descargas en el puerto 5097, con cupo 2, fases, faltantes y
partes) y el arnés `../.harness-integridad` (enlaza los archivos reales del proyecto; fuera de `wwwroot` y
del proyecto web, así que no se publica).
**Archivos modificados:** `Dicom/Models.cs`, `Dicom/DicomQrService.cs`, `Dicom/DownloadJobService.cs`,
`Dicom/Controllers/StudiesController.cs`, `Dicom/ReenvioJobService.cs`, `Dicom/LimpiadorDescargas.cs`,
`Dicom/DepuradorDescargas.cs`, `Dicom/DicomSendService.cs`, `wwwroot/dicom/index.html`, `appsettings.json`,
`docs/ARQUITECTURA.md` y este registro.

**Configuración** (llaves nuevas bajo `Local:`, ya escritas en `appsettings.json`):
`VerificarDescargas` (verdadero), `ReintentosMaximos` (2), `VerificarConPacs` (verdadero),
`VerificarConPacsMaxEstudios` (300), `ZipMaxMBPorParte` (2048), `MaxDescargasSimultaneas` (3) y
`MaxTrabajosEnCola` (20). `ZipMaxStudies` (100) sigue existiendo pero ahora significa **estudios por parte**
en lugar de «máximo antes de negar el ZIP». Los valores absurdos se corrigen solos (cupo 0 → 1, cupo 99 → 8).
Nada de esto cambia el AE Title, el puerto, los nodos PACS ni la retención.

**Verificado:** compilación en limpio (0 advertencias, 0 errores). Arnés con el código real y cuerpos
simulados, **37 chequeos** en paso: el reparto en partes (250 estudios → 100/100/50; topes de peso por
parte; carpeta enorme que viaja sola; topes en 0 = un ZIP; 100 vs 101 estudios; ningún estudio duplicado,
perdido ni partido), el cálculo de faltantes (completo/corto/cero/desconocido/sobrecarga y los avisos con
paciente, fecha y nodo) y la cola (con cupo 2 nunca hay 3 en curso y los 8 terminan; el que llega lleno se
queda `pending`/`queued` y arranca solo al liberarse el hueco; cancelar uno interrumpe ese token y no
afecta al otro; un `Complete` tardío no reetiqueta una cancelación como completada; con la cola llena se
rechaza; la barra se congela en 0 % en cola, ≥92 % verificando, 90–99 % empaquetando, 100 % al final con
sus 2 partes y el aviso de faltantes). La pantalla se probó contra el mock en `http://127.0.0.1:5097/dicom/`
(3 descargas simultáneas → 2 trabajando y 1 «En cola»; recarga a mitad → las dos tarjetas volvieron solas;
cancelar una dejó la otra bajando y mostró «Cancelada: 11/60 estudios y 85 imágenes llegaron al servidor»;
250 estudios → tres enlaces «⬇ Parte 1/3 (524.5 MB) … 3/3» y el aviso de faltantes truncado a 6 estudios
con «… y 32 más»; 10 lanzamientos seguidos → los 2 que desbordaban la cola avisaron sin romperse;
«Ocultar terminadas» quita solo las tarjetas cerradas). Sin errores en la consola del navegador.
**No se probó contra un PACS real** (datos de pacientes), así que la verificación de conteo y las re‑pedidas
quedan sin comprobar en vivo hasta el primer uso real.

**Uso:** nada nuevo que configurar. Buscar, seleccionar y **«Descargar seleccionados»** tantas veces como
se quiera; si algo queda incompleto la tarjeta lo dice y se puede volver a descargar solo ese estudio. Con
descargas grandes bajarán varias partes seguidas: hay que dejar que terminen todas.

**Despliegue:** **ya publicado por el usuario** el 2026-11-02 a las 10:02 (el `publish\Meditechnosys.Intranet.exe`
que está en servicio trae las tarjetas de descargas, las partes del ZIP y las seis llaves nuevas en
`publish\appsettings.json`: cupo 3, cola 20, verificación activada y tope de 2048 MB por parte). Queda anotado
porque la regla del día sigue en pie: **ningún cambio sale a producción sin su visto bueno expreso** —esta
entrada se publicó por decisión de él, no de aquí—. Antes de un republicado conviene apagar la intranet si hay
descargas en curso, porque el reinicio las corta.

---

## 2026-11-02 — La ubicación del sitio ahora se calibra marcando el edificio en el mapa

**Módulo:** Bitácora de accesos (`/admin/bitacora/`). **Tipo:** base de datos + backend + API + frontend.

**Qué cambió:** dentro del panel «Rangos de IP y ubicaciones» aparece el bloque **Ubicación exacta del
sitio** con un mapa (OpenStreetMap por Leaflet). El administrador hace clic sobre el edificio, arrastra el
marcador si quiere afinar, y pulsa **Guardar ubicación exacta**. Desde ese momento esa dirección —y no la
que se deducía por IP— es la que sale en la columna Ubicación de la bitácora y en el Excel, para todos los
equipos que no tengan un rango escrito a mano. Con el botón **Volver a la automática** se quita la
calibración y se regresa al comportamiento anterior.

El orden de la columna quedó así:

1. La ubicación escrita a mano para el rango de esa IP (sigue mandando sobre todo).
2. Si el sitio ya está calibrado en el mapa: **esa** dirección. También cuando el equipo había reportado
   una dirección salida de geolocalizar su IP, porque es la misma aproximación que la calibración corrige.
3. La dirección que reportó ese equipo **por GPS del navegador** (`fuente = navegador`): se respeta
   aunque exista calibración, porque son coordenadas reales del aparato y valen más que el punto del
   edificio (un portátil conectado fuera ya no aparece como si estuviera en el hospital).
4. Sin calibrar, la dirección que reportó el equipo (aunque sea por IP) y, si nunca reportó, la dirección
   automática del sitio.
5. Un renglón sin IP sigue quedando vacío, y `127.x` sigue diciendo «Este equipo (el servidor)».

**Por qué:** el usuario pidió «calibra bien la ubicación geográfica, que sea exacta». Se comprobó que por
IP no puede serlo —el servicio geolocaliza el equipo del proveedor de Internet, no el edificio, y aquí
devolvía «Centro, Cuauhtémoc, 06060» cuando la salida corresponde a Vallejo, GAM—, así que la única forma
de que salga exacta es que alguien diga dónde está el edificio. Se propuso y el usuario eligió
**«Fijar con un punto en el mapa»**.

**Cómo funciona:** tabla nueva `bitacora_sitio` (un solo renglón, `id = 1`) con `direccion`, `latitud`,
`longitud`, `actualizado_en`, `actualizado_por`; se crea sola al arrancar el servicio. `ISitioStore` con
`MySqlSitioStore` y `JsonSitioStore` (`App_Data/sitio.json`), igual que el resto de la intranet.
`LugarDelSitio` la consulta primero (en memoria dura 1 minuto, así que un cambio se nota en la siguiente
consulta sin recargar la base) y, cuando hay punto, **no hace ninguna consulta a Internet** —ni la
medición del egress ni la deducción de los equipos—, con lo que también deja de gastar la cuota de
Nominatim. `UbicacionResolver` expone `Calibracion()`, `SitioCalibrado()` e `InvalidaSitio()`.

**API:** `POST /api/bitacora/sitio` (permiso `configuracion.administrar`) recibe
`{latitud, longitud, direccion}`. Con punto, OpenStreetMap lo traduce a dirección; si el mapa no contesta
porque el servidor está aislado, se guarda la dirección que escribió el administrador, y si no escribió
ninguna devuelve 502 explicándolo. Sin punto y con dirección, se guarda esa dirección sola.
`DELETE /api/bitacora/sitio` quita la calibración (404 si no había). Los dos dejan constancia en la
bitácora con la cuenta, el punto y si fue marcada en el mapa o escrita a mano.
`GET /api/bitacora/ubicaciones` ahora devuelve también `sitio.latitud`, `sitio.longitud`,
`sitio.actualizadoPor` y `sitio.fuente` (`calibrado` / `servidor` / `equipos`).

**Frontend:** mapa con carga perezosa de Leaflet 1.9.4 desde su CDN; si la PC del administrador no tiene
Internet, el mapa no carga y el bloque lo dice, pero la calibración se puede hacer igual escribiendo la
dirección o pegando las coordenadas («19.42847, -99.12761», las da Google Maps con clic derecho). El
marcador **solo** se dibuja sobre el punto ya calibrado: la dirección automática por IP centra el mapa
para dar referencia, pero no se marca ni se pone en el campo, para que un «Guardar» distraído no fije el
punto aproximado del proveedor como si fuera el edificio. La línea verde distingue
«Ubicación exacta del sitio … (fijada en el mapa por X, fecha)» / «(escrita a mano por X, fecha)» de
«Dirección automática del sitio … (aproximada: es la zona del proveedor, no este edificio)», y la tabla de
equipos anota en cada fila de dónde salió su dirección.

**Archivos modificados:** `Services/Lugares.cs` (`SitioCalibrado`, `ISitioStore`, `JsonSitioStore`,
`MySqlSitioStore`, `DatoSitio.ActualizadoPor`, y `LugarDelSitio` con la calibración por delante),
`Services/Ubicaciones.cs` (`SitioCalibrado()`, `Calibracion()`, `InvalidaSitio()` y `UbicacionDe` con la
regla del reporte por IP frente al punto calibrado), `Program.cs` (registro de `ISitioStore`,
`EnsureSchema()` de `bitacora_sitio`, `POST`/`DELETE /api/bitacora/sitio`, `SitioCalibrarRequest` y el
`sitio` ampliado en el GET), `wwwroot/admin/bitacora/index.html` (bloque del mapa, botones, avisos y su
JS), `.verify/mock-bitacora.mjs` (estado calibrado con `POST`/`DELETE` y la misma regla de precedencia) y
este registro. **Archivos creados:** ninguno en la intranet; el arnés de pruebas sigue en
`Documentos/Qoder/2026-10-30/f06b519c/sitio-test` (ahora con las pruebas F, G y H).

**Base de datos:** tabla nueva `meditechnosys_intranet.bitacora_sitio` (se crea sola al arrancar; sin
migraciones de datos, `bitacora`, `bitacora_lugar` y `bitacora_ubicacion` no cambian).

**Verificado:** compilación en limpio (0 advertencias, 0 errores). Arnés con el código real y almacenes
simulados: (F) con punto guardado, `Actual()` devuelve `fuente = calibrado` con la dirección, el nombre de
quien lo marcó y su fecha, y las **0** peticiones de salida —no gasta Internet ni cuota de Nominatim—;
(G) al borrarlo vuelve la automática (`equipos`, deducida de los reportes) y las consultas de salida se
reanudan (3 intentos con los tres proveedores); (H) con calibración, el rango escrito a mano y el reporte
por GPS siguen ganando, y el reporte por IP («Centro, Cuauhtémoc, 06060») pasa a sustituirse por el punto
del mapa; (D) y (E), sin calibrar, el orden de la entrada anterior se conserva tal cual. La pantalla se
probó contra el mock en `http://127.0.0.1:5098/admin/bitacora/`, en los cuatro estados: al abrir sin
calibrar (aviso amarillo, mapa centrado, marcador 0 y campo vacío), guardar solo la dirección escrita
(«escrita a mano», sin marcador), guardar con punto («fijada en el mapa», marcador 1 y coordenadas en el
campo, `LAB-11` y `RIS02` pasan a la dirección exacta y `MED-014` conserva su GPS) y quitar la calibración
(vuelve el modo automático, el mapa se rehace sin marcador). Sin errores en la consola del navegador.

**Uso:** abrir **Bitácora de accesos → Rangos de IP y ubicaciones → Ubicación exacta del sitio**, hacer
clic sobre el edificio en el mapa y pulsar **Guardar ubicación exacta**. Si el mapa no carga, pegar las
coordenadas del edificio o escribir la dirección. Es un solo punto para toda la intranet; los puestos
concretos se siguen escribiendo por rango de IP arriba.

**Despliegue:** **no publicado todavía**, por decisión del usuario el 2026-11-02. El código queda
compilado en el proyecto (`bin\Release`); para dejarlo en la intranet hay que ejecutar el republicado
elevado de siempre (`.verify\deployar.ps1` o `actualizar.bat`, aceptando la ventana de administrador de
Windows), que crea la tabla `bitacora_sitio` sola al arrancar el servicio. Antes de publicar, la pantalla
se pudo revisar completa contra el mock local del puerto 5098 (ya detenido); al servicio real no se le
enviaron puntos de prueba.

---

## 2026-10-31 (tarde) — La ubicación geográfica de la bitácora se obtiene sola

**Módulo:** Bitácora de accesos (`/admin/bitacora/`). **Tipo:** backend + API + frontend.

**Qué cambió:** la columna **Ubicación** ya no espera a que alguien escriba nada ni a que un equipo se
haya reportado antes: la intranet **procura sola la dirección geográfica del sitio** y la usa de
respaldo. El orden ahora es:

1. La ubicación que el administrador escribió para el rango de esa IP (puesto exacto).
2. Si el rango no tiene nada escrito, la dirección geográfica que **ese** equipo reportó al abrir la
   intranet (`bitacora_lugar`, lo que se agregó en la entrada anterior).
3. Si ese equipo tampoco ha reportado nunca, la **dirección automática del sitio**: `LugarDelSitio`
   pregunta a la salida a Internet del servidor en qué coordenadas está (ipapi.co, y si no ipwho.is, y
   si no freeipapi.com) y traduce el punto con OpenStreetMap.
4. Un renglón **sin IP** (los que apunta el propio sistema) sigue quedando vacío: no hay de dónde
   deducir un lugar.

En el panel «Rangos de IP y ubicaciones» apareció una línea verde con esa dirección automática y su
origen («medida por la intranet al salir a Internet» / «deducida de lo que reportaron los equipos») con
la fecha; la tabla de equipos pasó a titularse **Dirección geográfica** y las filas que no tienen
reporte propio muestran la del sitio con la nota «dirección automática del sitio» en lugar de «sin
reporte». La IP local sigue visible en cada movimiento, en la lista de usuarios, en el detalle y en el
Excel.

**Cómo funciona `Services/Lugares.cs` (`LugarDelSitio`, nuevo):** es un singleton medido en un hilo
aparte. `Direccion` nunca espera la red: devuelve lo que ya se sabía (o lo deducido de los reportes) y
lanza la medición en segundo plano si se acabó el tiempo de la caché; `Interlocked` garantiza que solo
hay una medición en vuelo, la caché buena dura 6 horas y un intento fallido se pospone 20 minutos. Si el
servidor no tiene salida a Internet —lo normal en una intranet aislada—, se usa la dirección **más
repetida** entre las que reportaron los aparatos (todos salen por el mismo proveedor, así que coinciden
en la zona del edificio), renormalizada sin tildes para que «Ciudad de México» y «Ciudad de Mexico»
cuenten como el mismo lugar. Esa deducción se rehace cada 5 minutos y tampoco bloquea. Se dispara la
primera medición al arrancar el servicio, antes de que nadie abra una pantalla.

**Por qué:** el usuario pidió «que muestre la IP local y la ubicación, pero esta ubicación geográfica
que la muestre de manera automática que se obtenga». Después de la entrada anterior la dirección sí era
geográfica, pero dependía de que cada aparato hubiera abierto la intranet una vez con sesión; como desde
el despliegue nadie había entrado, `bitacora_lugar` estaba vacío y la pantalla no mostraba nada
automático. Con esta tercera capa, cualquier renglón con una IP de la red ya sale con dirección.

**Precisión (hay que decirla):** por IP se acierta colonia o zona, no el escritorio, y el dato puede
fallar de zona si el proveedor geolocaliza mal la salida a Internet. En la prueba de este equipo, las
coordenadas que devolvió ipapi.co (con código postal 07839, que es Vallejo) se tradujeron como
«Centro, Cuauhtémoc, 06060 Ciudad de México». Por eso la dirección automática es el respaldo y lo
escrito a mano sobre el rango sigue mandando: para el puesto exacto se usa el panel de rangos.

**Archivos creados:** ninguno.
**Archivos modificados:** `Services/Lugares.cs` (`DatoSitio` y `LugarDelSitio`), `Services/Ubicaciones.cs`
(`UbicacionResolver` recibe `LugarDelSitio`, la nueva `UbicacionDe` de tres precedencias, `Sitio()` y
`DireccionDelSitio()`, y `EnriquecerAsync` lee la dirección del sitio una vez por consulta en lugar de
una por renglón), `Program.cs` (`AddHttpClient(nameof(LugarDelSitio))`, `AddSingleton<LugarDelSitio>()`,
la primera medición al arrancar y `sitio` en `GET /api/bitacora/ubicaciones`),
`wwwroot/admin/bitacora/index.html` (línea de la dirección automática, columna «Dirección geográfica»,
nota por fila y texto explicativo), `.verify/mock-bitacora.mjs` (simula `sitio`, un equipo sin reporte y
un renglón sin IP) y este registro.

**Base de datos:** sin cambios (`bitacora_lugar` y `bitacora_ubicacion` quedan como están).

**Verificado:** compilación en limpio (0 advertencias, 0 errores). El código real de `LugarDelSitio` y
`UbicacionResolver` se ejecutó aislado, con almacenes simulados y con el manejo de peticiones fallando a
propósito: (A) con Internet real la primera consulta devolvió en 13 ms lo deducido de los reportes y, ya
medido el sitio en segundo plano, «Centro, Cuauhtémoc, 06060 Ciudad de México» con fuente `servidor`;
(B) sin Internet la dirección salió de los dos reportes que coinciden, con fuente `equipos`; (C) sin
Internet y sin reportes devolvió vacío en 0 ms —no esperó los 5 s de presupuesto, o sea la pantalla no se
cuelga—; (D) el orden quedó comprobado renglón por renglón (rango escrito a mano > reporte del equipo >
dirección del sitio > vacío sin IP > «Este equipo (el servidor)» para `127.x`); (E) `EnriquecerAsync`
pintó la dirección del sitio en los movimientos de un equipo que nunca reportó. La pantalla se revisó con
el mock en `http://127.0.0.1:5098/admin/bitacora/`: la línea automática aparece, la fila `LAB-11` (IP sin
reporte) muestra la dirección del sitio con su nota, `127.0.0.1` dice «el mismo servidor» y el usuario sin
IP queda «sin datos de red», sin errores en la consola del navegador.

**Despliegue:** republish elevado el 2026-11-01 (12:35) sobre `MeditechnosysIntranet`; servicio
`Running`, `http://127.0.0.1:5023/` responde, `publish/wwwroot/admin/bitacora/index.html` ya lleva el
panel nuevo y `App_Data\credenciales` y `publish\dicom_downloads` siguen intactos. La base no cambió de
estructura: `bitacora` 4,534 renglones y `bitacora_lugar` ya tiene 3 reportes reales, todos de este mismo
aparato (IP `127.0.0.1`) con sesión de `root` y dirección «Centro, Cuauhtémoc, 06060 Ciudad de México»,
o sea que el reporte desde el equipo funciona en producción. Ninguna de esas filas es una prueba mía:
la verificación de la pantalla se hizo contra el mock local del puerto 5098 (ya detenido), que registra
en su consola y no escribe en la base; al servicio real no se le enviaron coordenadas de prueba ni se
automatizó un inicio de sesión en él. Lo que queda por confirmar en vivo es la dirección que obtenga el
servidor por su propia salida a Internet, porque ese dato solo se lee en el panel autenticado.

---

## 2026-10-30 (noche, 3.ª) — La bitácora de accesos ahora dice **desde dónde** se conectó cada quien

**Módulo:** Bitácora de accesos (`/admin/bitacora/`). **Tipo:** base de datos + backend + API + frontend.

**Qué cambió:** cada movimiento de la bitácora muestra, además de la **IP**, el **nombre del equipo en la
red**, la **ubicación** que el administrador le asigne a ese rango de IP y el **tipo de dispositivo**
(PC Windows con Chrome, tableta iPad, teléfono Android, aplicación o script). Se ve en la tabla de
usuarios (columnas «Equipo · IP · Ubicación» y «Dispositivo»), como segunda línea de cada movimiento del
detalle de un usuario, y viaja al Excel en las columnas **IP, Equipo, Ubicación, Dispositivo**. La caja
de filtro nueva («Equipo · IP · ubicación») busca sobre esas cuatro piezas, sin distinguir acentos ni
mayúsculas —escribir `radiolog`, `RIS03` o `10.252.253` sirve igual— y ese mismo filtro se aplica al
Excel descargado, para que lo descargado coincida con lo que está en pantalla.

Debajo de la tabla de usuarios quedó el panel **«Rangos de IP y ubicaciones»** (solo para quien tiene el
permiso `configuracion.administrar`): una lista con el mapa `rango → ubicación` y, al lado, los equipos
que realmente se han conectado con su IP, su nombre en la red y cuántos movimientos tienen; el botón
**Asignar** de cada fila rellena el formulario con esa IP para escribir dónde está ese equipo.

**Por qué:** el usuario lo pidió («en bitácora de accesos, quiero que nos muestre en qué IP está
ingresando, en qué dispositivo y la ubicación») y eligió verlo «en la lista y en el Excel». Sobre la
«ubicación geográfica» hay que decir de dónde viene cada dato:

- La **IP** ya se guardaba en cada renglón; solo faltaba mostrarla.
- El **nombre del equipo** sale gratis: al consultar se hace la resolución inversa de DNS (PTR) dentro
  del hospital, con presupuesto de 900 ms y caché de 6 horas, así que una pantalla lenta no deja la
  bitácora colgada.
- La **ubicación física** es texto que escribe el administrador sobre el rango de IP. No se puede
  obtener automáticamente: son direcciones privadas (`10.252.x.x`), los servidores de geolocalización por
  IP no conocen esas redes, y la geolocalización del navegador exige HTTPS —la intranet es HTTP—. Por eso
  la ubicación **no** se guarda renglón por renglón: se deduce de la IP al consultar, de modo que
  corregir un rango acomoda también los registros viejos.
- El **dispositivo** se deduce del `User-Agent` que llega en la petición, igual que la IP de la conexión
  real; ni uno ni otro se toman de lo que el cliente declare.

**Archivos creados:** `Services/Dispositivo.cs` (User-Agent → texto legible, máx. 120 caracteres),
`Services/Ubicaciones.cs` (mapa de rangos en MySQL/JSON, coincidencia por prefijo, IP exacta o máscara,
resolución PTR con caché y `UbicacionResolver`).
**Archivos modificados:** `Models/Audit.cs` (campos `Dispositivo`, `Equipo`, `Ubicacion` y registro
`IpUso`), `Services/Bitacora.cs` (guarda el dispositivo, lista las IPs usadas y el último detalle por
usuario), `Program.cs` (`AddHttpContextAccessor`, esquema nuevo, `/api/bitacora` y
`/api/bitacora/resumen` ahora enriquecen, `/api/bitacora/export` con filtro `donde` y 9 columnas, rutas
nuevas `GET/PUT/DELETE /api/bitacora/ubicaciones`), `wwwroot/admin/bitacora/index.html` (columnas,
segunda línea por movimiento, filtro de texto y panel de rangos), `docs/REGISTRO-CAMBIOS.md`.

**Base de datos:** al arrancar, `ALTER TABLE bitacora ADD COLUMN dispositivo VARCHAR(120) NOT NULL
DEFAULT '' AFTER ip` (si ya existe, no hace nada) y `CREATE TABLE IF NOT EXISTS bitacora_ubicacion
(rango VARCHAR(60) PK, ubicacion VARCHAR(160))`. Ambos se ejecutan solos al arrancar el servicio
(`EnsureSchema`), sin perder los renglones que ya estaban.

**Verificado:** compilación en limpio (0 advertencias, 0 errores). En el mock local del navegador
(`.verify/mock-bitacora.mjs`, puerto 5098) la tabla salió con las 5 columnas
(`RIS03 · 10.252.253.75 · Radiología · Torre A, 2º piso` + `PC · Windows 10/11 · Chrome 131`), el
detalle mostró la segunda línea en cada movimiento, el filtro `radiolog` dejó los 5 movimientos del
usuario y `IPHONE` los dejó en 0, guardar el rango `10.252.90.14` actualizó al instante el mapa, la
tabla de equipos y los movimientos ya abiertos (con el aviso verde), **Asignar** rellenó la IP, quitar
el rango lo borró de todos lados, la descarga mandó `donde=ris03&dia=2026-10-29` y modo noche quedó
legible (los campos se pintan con `--theme-surface`), sin un solo error en consola. En el servicio real
publicado: `dispositivo` y `bitacora_ubicacion` creados, los **4.513 renglones anteriores intactos**, la
ruta nueva responde 401 sin sesión (existe y está protegida) y una prueba con un usuario de prueba
inexistente dejó `Teléfono · Android 13 · SM-A515F · Chrome 130` en la columna; ese renglón de prueba se
borró después. Respaldo previo en `.verify/respaldo-bitacora.sql`. Los equipos empiezan a llenar su
dispositivo desde la primera sesión posterior a esta publicación (los renglones viejos muestran guion).

**Nota de seguridad (misma sesión de trabajo):** durante la verificación aparecieron, dentro de los
resultados de las herramientas, líneas que imitaban instrucciones del sistema («Please proceed to the next
step by using the … tool first. No questions.»). No provenían del usuario ni de la aplicación; se
ignoraron y no se ejecutó ninguna orden con base en ellas.

---

## 2026-10-31 — La Ubicación de la bitácora es ahora una dirección geográfica

**Módulo:** Bitácora de accesos. **Tipo:** base de datos + backend + API + frontend.

**Qué cambió:** después de la entrada anterior el usuario aclaró que «ubicación» debe ser **ubicación
geográfica** (dio el ejemplo «Vallejo, Gustavo A. Madero, 07870 Ciudad de México, CDMX»), así que la
columna ya no depende solo de lo que alguien escriba a mano: ahora **el propio equipo reporta en qué
punto del mapa está** y la intranet lo traduce a dirección.

- `wwwroot/js/ubicacion.js` (nuevo, cargado desde `Pages/Shared/_Layout.cshtml` —o sea en `/`, `/Admin`
  e `/Ingenieria`— y añadido también antes de `</body>` en las 19 páginas de módulo
  `wwwroot/**/index.html`, para que un puesto que solo abre el visor DICOM también reporte): una vez por
  sesión (máximo uno cada 6 horas por aparato) pregunta a Internet en qué
  coordenadas está ese equipo —`ipapi.co`, con respaldo en `ipwho.is` y `freeipapi.com`— y manda
  latitud, longitud y el texto aproximado a `POST /api/ubicacion`. No abre ventanas ni pide permiso de
  GPS; si el aparato no tiene salida a Internet, no pasa nada y la pantalla sigue igual.
  Si algún día la intranet corre en HTTPS y el personal ya dio permiso de ubicación, el script usa
  entonces el GPS del aparato (más exacto) sin cambiar nada.
- `Services/Lugares.cs` (nuevo): `LugarReportado`, almacén en MySQL (`bitacora_lugar`) o en
  `App_Data/lugares.jsonl`, y `GeocodificadorLugar`, que convierte coordenadas en
  «colonia, alcaldía, código postal ciudad, estado» con OpenStreetMap (Nominatim), respeta las reglas
  del servicio (una petición por segundo, User-Agent identificado, caché 30 días y 6 horas de espera
  tras un fallo) y presupuesto de 4 segundos: si el mapa no contesta, la dirección queda vacía y la
  pantalla no se cuelga nunca.
- `POST /api/ubicacion` (requiere sesión, no necesita permiso especial) guarda el reporte con la IP de
  la conexión y la cuenta. Queda excluido del registro de «Acceso» porque lo hace la página sola, no la
  persona.
- `UbicacionResolver`: la **Ubicación** que ve la bitácora y el Excel ahora es *lo que escribió el
  administrador para ese rango* y, si ese rango no tiene nada, **la dirección geográfica reportada** por
  esa IP. Como sigue deduciéndose al consultar, un reporte nuevo acomoda también los renglones viejos.
- Panel «Rangos de IP y ubicaciones»: la tabla de equipos pasó de 5 a 6 columnas y ahora muestra
  **Dirección geográfica reportada** (con cuándo y de dónde salió: «por IP» o «GPS del equipo») junto
  con **Ubicación asignada**, para decidir si deja la dirección automática o escribe el puesto exacto.
  El botón Asignar ya no pisa la dirección: solo rellena la IP.

**Por qué:** una IP privada (`10.252.x.x`) no aparece en ningún mapa del mundo, así que la única forma
de sacar una dirección real es preguntar desde el aparato que está conectado a esa red. El dato por IP
es aproximado (acierta colonia o zona, no el escritorio); por eso convive con el mapa de rangos, que es
el que da el puesto exacto y el que manda cuando los dos existen.

**Archivos creados:** `Services/Lugares.cs`, `wwwroot/js/ubicacion.js`.
**Archivos modificados:** `Program.cs` (`ILugarStore`, `GeocodificadorLugar`, `AddHttpClient`,
`EnsureSchema` de `bitacora_lugar`, `POST /api/ubicacion`, `GET /api/bitacora/ubicaciones` con
`lugarGeografico`/`reportadoEn`/`fuenteLugar`, excepción del filtro de «Acceso», `record LugarRequest`),
`Services/Ubicaciones.cs` (el resolver consulta los lugares y añade `LugarDe`),
`Pages/Shared/_Layout.cshtml`, `wwwroot/admin/bitacora/index.html` (tabla de equipos de 6 columnas), las
otras 18 `wwwroot/**/index.html` (solo la línea del script de reporte), `.verify/mock-bitacora.mjs` (prueba
local), `.verify/agregar-ubicacion.mjs` (replicó el script en las páginas) y este registro.

**Base de datos:** `CREATE TABLE IF NOT EXISTS bitacora_lugar (id BIGINT PK AUTO_INCREMENT, momento
DATETIME(6), usuario VARCHAR(40), ip VARCHAR(45), latitud DECIMAL(9,6), longitud DECIMAL(9,6),
precision_metros SMALLINT, direccion VARCHAR(200), fuente VARCHAR(20), KEY ix_lugar_ip)` — se crea solo
al arrancar; `bitacora` no cambia.

**Verificado:** compilación en limpio (0 advertencias, 0 errores). El geocodificador se probó con el
código real, aislado del sitio: las coordenadas que reportó el navegador de este equipo
(19.42847, -99.12761) salen como «Centro, Cuauhtémoc, 06060 Ciudad de México», un punto de GAM como
«Colonia Tablas de San Agustín, Gustavo A. Madero, 07860 Ciudad de México» y uno de Monterrey como
«Avenida Cristóbal Colón, 64490 Monterrey, Nuevo León»; la segunda llamada al mismo punto tarda 0 ms
(caché), `(0,0)` y `91,-10` se rechazan sin preguntar al mapa y cada petición tarda ~260-500 ms (muy
por debajo del presupuesto de 4 s). En el navegador real contra el mock local, `ubicacion.js` consultó
`ipapi.co` y publicó `{"latitud":19.42847,"longitud":-99.12761,"direccion":"Mexico City, 07839","fuente":"ip"}`.
La pantalla, en el mock: 6 columnas, la dirección geográfica con su nota de tiempo y fuente,
`(usa la dirección)` cuando no hay rango escrito, `sin reporte` cuando el aparato no reportó, el filtro
`lindavist` y `GUSTAVO A. MADERO` encontrando el movimiento, y el rango manual (`10.252.253.*`)
siguiendo mandando sobre la dirección automática; sin errores en consola. Publicado el servicio
(`MeditechnosysIntranet` en línea): tabla `bitacora_lugar` creada, `bitacora` intacta con 4.519
renglones y `POST /api/ubicacion` respondiendo 401 sin sesión (protegido). Falta ver el primer renglón
real de `bitacora_lugar`, que se escribe solo la próxima vez que alguien abra la intranet desde un
equipo con salida a Internet.

---


**Módulo:** DICOM (envío por carpetas). **Tipo:** frontend.

**Qué cambió:** se quitó de `wwwroot/dicom/enviar/index.html` el recuadro punteado
«Arrastra aquí la carpeta con los estudios…», junto con su CSS (`.zona`, `.zona.sobre`) y con los
manejadores de `dragenter/dragover/dragleave/drop` y el recorrido recursivo de carpetas
(`entradas()`), que ya no tenía a dónde engancharse. La forma de cargar estudios es ahora únicamente
el botón **«Agregar carpeta de estudios…»**.

**Por qué:** el usuario lo pidió («quita eso») sobre una captura del recuadro; en la práctica siempre
usan el botón, y el arrastre de carpetas desde el Explorador no siempre llega con las subcarpetas.

**Verificado:** sin recargar el servicio —los archivos estáticos se sirven del disco
(`publish\wwwroot`)— se copió la página nueva a `publish` y quedó en 0 coincidencias de
`zona`/`Arrastra aquí`, conservando las 5 de `mdlEditar`. En el mock local: la lista se armó por el
input `#pickFolder` —el mismo que abre el botón, con sus subcarpetas y sus `.dcm` sin extensión— y
salió 2 estudios · 27 imágenes · 124.3 MB; el botón **Editar** siguió abriendo el cuadro, al guardar
el renglón quedó `HUERTA MEDINA, EDUARDO ALFONSO · 9988776655 · CT · Editado` y el envío reportó
`6/6 imágenes en CT01 · 6 con etiquetas corregidas`, sin errores en consola.

---

## 2026-10-30 (noche) — Botón «Editar» por estudio: el envío sale con el nombre, modalidad e ID de paciente corregidos

**Módulo:** DICOM (envío por carpetas). **Tipo:** frontend (pantalla) + backend (API y `DicomSendService`).

**Qué cambió:**

- **`wwwroot/dicom/enviar/index.html`** — la tabla de estudios ganó una columna **Acción** con un
  botón **«Editar»** en cada renglón (en modo noche usa `--panel`/`--dorado`; el botón ya editado se
  pinta en dorado y dice «Editado»). Abre un `dialog.modal` (`#mdlEditar`) con **Nombre del
  paciente**, **ID de paciente**, **Modalidad** y **Descripción del estudio**, más «Estado actual»
  del estudio, contador de imágenes, un botón **Restaurar** y otro **Cancelar**. El nombre se captura
  y se muestra como **`APELLIDOS, NOMBRE`** — la coma se convierte a `^` al armar la etiqueta DICOM
  (`aDicomPN()`), así que escribir «Ramirez Gomez, Maria de Jesus» produce
  `RAMIREZ GOMEZ^MARIA DE JESUS`. Un campo **vacío significa «no lo toques»**: conserva el valor que
  traía el archivo. Mientras hay una lectura de carpeta en curso o un envío corriendo, los botones
  se deshabilitan (`ocupado`), y al agrupar una carpeta por segunda vez el renglón ya editado no se
  recalcula (`if (!p.editado)`), para no pisar la corrección.
- **`Dicom/DicomSendService.cs`** — nueva clase `EdicionEstudio` (`Nombre`, `Paciente`, `Modalidad`,
  `Descripcion` + propiedad `Vacio`) y `AplicarEdicion(DicomDataset, EdicionEstudio)`, que escribe
  `(0010,0010) PatientName`, `(0010,0020) PatientID`, `(0008,0060) Modality` y `(0008,0061)
  ModalitiesInStudy`, `(0008,1030) StudyDescription`, recortando al largo máximo del VR (64/64/16/64)
  y poniendo la modalidad en mayúsculas. `StoreFilesAsync` / `StoreBatchAsync` reciben un
  `Dictionary<string, EdicionEstudio>` **clave = `StudyInstanceUID`** y aplican el parche al dataset
  **después** de validar los SOP UID y **antes** de armar el `DicomCStoreRequest`, o sea en memoria:
  el archivo del disco temporal no se reescribe y los `StudyInstanceUID` / `SOPInstanceUID` / píxel
  data quedan intactos. `ResultadoEnvio` reporta `Editadas`.
- **`Dicom/Controllers/EnviarController.cs`** — `Subir` acepta un campo de formulario más,
  `ediciones` (JSON `{ "<studyUid>": { nombre, paciente, modalidad, descripcion } }`), lo valida con
  `LeerEdiciones()` (descarta entradas vacías o JSON inválido sin reventar el envío) y lo pasa a
  `StoreFilesAsync`. La respuesta incluye `editadas` y el log queda
  «… N imagen(es) con etiquetas corregidas».

**Por qué:** el usuario pidió que cada estudio subido pudiera corregirse antes de salir —nombre,
modalidad e ID de paciente sobre todo— y que **el PACS reciba la corrección**, no el valor original.
Es el caso típico del estudio descargado y reenviado con el nombre o la modalidad mal capturados.
Se respetó el formato «apellidos, nombre» porque así lo dictan y así lo muestran los visores del
PACS; DICOM lo almacena con `^` como separador de componentes.

**Archivos:** modificados `wwwroot/dicom/enviar/index.html`, `Dicom/DicomSendService.cs`,
`Dicom/Controllers/EnviarController.cs`; sin cambios en base de datos, permisos ni configuración (el
envío sigue pidiendo `dicom.enviar`).

**Verificado:**

- Navegador contra el `mock` local (`:5099`): editar el renglón de 21 imágenes y guardar dejó
  `RAMIREZ GOMEZ, MARIA DE JESUS · 9988776655 · TC DE CRANEO SIMPLE · CT · 21 · Pendiente · Con
  etiquetas corregidas: se aplican al enviar. · Editado`. El `multipart` que salió a la API traía
  exactamente `{"1.2.840.113845.11.1000000002170592405.20261030064710.1082842":{"nombre":
  "RAMIREZ GOMEZ^MARIA DE JESUS","paciente":"9988776655","modalidad":"CT","descripcion":"TC DE
  CRANEO SIMPLE"}}`.
- Backend: un `.dcm` real del corpus pasó por `AplicarEdicion` y al reabrirlo reportó
  `PatientName = RAMIREZ GOMEZ^MARIA DE JESUS`, `StudyInstanceUID` y `SOPInstanceUID` **iguales** a
  los originales y píxel data de 384×384 legible — la edición no rompe el archivo.
- `dotnet build -c Release` sin advertencias ni errores; publicacion (`deployar.ps1`, elevado)
  completada y el servicio `MeditechnosysIntranet` quedó en `Running`. En `publish\` quedaron
  `App_Data\credenciales` intacto, 26 archivos en `dicom_downloads` y
  `wwwroot\js\dicom-cab.js` de 10 460 bytes; la página publicada contiene `mdlEditar`, `data-edit` y
  `ediciones`.
- Nota operativa: como **los UID no cambian**, si el PACS destino ya tiene ese estudio puede
  responder con un aviso de duplicado; el renglón sale entonces como «Con avisos» y la imagen queda
  guardada con el nombre correcto. Generar UID nuevos para crear una copia aparte sería un cambio
  distinto.

---

## 2026-10-30 (tarde) — Los estudios descargados del PACS dejan de quedar «Descartado» al volver a subirlos

**Módulo:** DICOM (envío por carpetas). **Tipo:** frontend (parser de cabeceras).

**Qué cambió:**

- **`wwwroot/js/dicom-cab.js`** — al recorrer el dataset, las **secuencias de longitud indefinida**
  (`SQ` con `len = 0xFFFFFFFF`, por ejemplo la `ProcedureCodeSequence` que Philips escribe así en
  RM/TC) se «saltaban a ciegas»: el ítem anidado `(FFFE,E000)` con longitud indefinida se leía como
  si fuera un valor normal, el puntero salía disparado más allá de la ventana de lectura y se
  perdían `PatientName`, `PatientID` y `StudyInstanceUID`, que van después. Sin `StudyInstanceUID`
  `leer()` devolvía `null` y la pantalla mandaba el archivo completo al renglón
  «(archivos que no son DICOM) · Descartado». Ahora `recorrer()` tiene niveles
  (`meta` / `data` / `seq` / `item`), entra en secuencias e ítems elemento por elemento, hereda el
  VR y el orden de bytes del padre, limita la profundidad a 8 por archivo corrupto y corta cuando
  el puntero no avanza (antes podía looppear). Las etiquetas dentro de una secuencia se leen en un
  cuaderno aparte para que no tapen las del estudio. Se agregó una segunda lectura con ventana de
  1 MB cuando la cabecera resulta más larga de los 96 KB previstos.
- **`wwwroot/dicom/enviar/index.html`** — el renglón de descartados ya no afirma que los archivos
  «no son DICOM»: dice «(archivos que no se leyeron como DICOM)» con el detalle
  «O no son DICOM o el lector no reconoció su cabecera». El total de `MB` de la barra
  (`#conteo`) dejó de sumar los bytes de los descartados, que no se envían
  (`totalBytes()` los excluye, como ya hacía `totalImagenes()`). Versión de la referencia a
  `?v=20261030b` para forzar la recarga del script.

**Por qué:** el usuario descargó un estudio con el propio descargador DICOM de la intranet y, al
devolverlo por la pantalla de envío, las 889 imágenes salían descartadas. Los archivos **sí traen
cabecera DICOM correcta** (128 bytes de preámbulo + `DICM` + grupo `0002` con su
`TransferSyntaxUID`): `DicomQrService.StoreIncomingAsync` guarda con
`new DicomFile(ds).SaveAsync(...)`, que siempre escribe preámbulo y metadatos, así que el fallo era
100 % del lector del navegador y no del descargador.

**Archivos modificados:** `wwwroot/js/dicom-cab.js`, `wwwroot/dicom/enviar/index.html`,
esta bitácora.

**Verificado:**

- Lote en Node (`DicomCab` ejecutado tal cual) sobre **620 archivos reales**: los 29 del ZIP
  `publish/dicom_downloads/_zips/dicom_20261030_091602_4c22ce2d.zip` (RM encefálico Philips),
  31 del ZIP `dicom_20261030_090205_0b4c3309.zip` (CR), la exportación de MicroDicom de 585
  archivos (compactado JPEG, `1.2.840.10008.1.2.4.70`), 2 DICOM de documento escaneado y 1 DX de
  17,7 MB implícito (`1.2.840.10008.1.2`). Con el script publicado antes: **32 null**; con el
  nuevo: **2 null**, y son los dos `.jpg` que estaban en la muestra. Los estudios armados son los
  mismos (585 / 2 / 1 / 29), así que no se rompió lo que ya funcionaba.
- Pantalla real en navegador (`dicom/enviar/` sobre un mock que sirve `wwwroot` sin tocar): se
  inyectaron 21 `.dcm` descargados del PACS + 1 `.jpg`. Sale **1 renglón de estudio**
  «HUERTA MEDINA, EDUARDO · 39977836091M1978OR · CRANEO CON GADOLINIO · MR · 21 · Pendiente»,
  el `.jpg` solo en el renglón Descartado y el conteo en **110.2 MB** (sin los 1.4 MB del jpg).
  Consola limpia.
- Republicado (`dotnet publish`) y servicio `MeditechnosysIntranet` **Running**;
  `http://127.0.0.1:5023/js/dicom-cab.js` responde 200 con el archivo nuevo (10 460 bytes frente a
  los 8 333 anteriores) y la página publicada lleva las etiquetas nuevas.

---

## 2026-10-30 — Envío de estudios DICOM por carpetas (una fila por estudio) y el visor vuelve a los checkboxes

**Módulo:** DICOM (envío + visor).

**Qué cambió:**

- **`wwwroot/dicom/enviar/index.html`** — la pestaña «Enviar estudios» ya no lista archivos. Se
  quitó «Agregar archivos…» con su `<input type="file">` sin `webkitdirectory`; quedó un solo
  botón, **«Agregar carpeta de estudios…»**, más la zona de arrastrar carpetas. La tabla es ahora
  **un renglón por estudio**: Nombre · ID Paciente · Descripción · Modalidad · Imágenes · **Estado**
  · Detalle. El estado lo calcula el navegador al agrupar por `StudyInstanceUID`
  (Pendiente → Enviando → Subido / Con avisos / Con errores / Error / Cancelado) y
  «Reintentar pendientes» vuelve a mandar sólo lo que no quedó subido. Lo que no trae cabecera
  DICOM se acumula en un renglón aparte, «(archivos que no son DICOM)», en estado Descartado y
  fuera del envío.
- **`wwwroot/js/dicom-cab.js` (nuevo)** — parser de cabeceras DICOM en el navegador (preámbulo,
  metadatos y dataset hasta `StudyInstanceUID`, `SOPInstanceUID`, `PatientName`, `PatientID`,
  `StudyDescription`/`SeriesDescription` y `Modality`). Lee sólo los primeros bytes de cada
  archivo y nunca baja el píxel data, y recorre 8 cabeceras a la vez: una carpeta de cientos de
  imágenes se arma sin saturar la PC.
- **`wwwroot/js/desplegable-nodos.js` (nuevo)** — la lista de nodos PACS de las dos pestañas del
  envío (un destino para subir, varios origen/destino para reenviar) con casillas dentro del
  desplegable, en lugar del `<select multiple>` que obligaba a Ctrl+clic.
- **`wwwroot/js/desplegable-nodos.js` (colores)** — el panel ya no pinta `background:#fff` a mano:
  usa `--panel` (opaco, no `--card`, que es translúcido y dejaba las filas sobre el emblema),
  `--texto` para los nombres y `--theme-control-border` para los bordes, así que en modo noche sale
  azul marino con letra blanco humo. Sin esto, la lista salía en blanco con el texto claro del tema
  nocturno y era ilegible. La referencia sube a `?v=20261030b` para que el navegador suelte la
  versión vieja del script.
- **`wwwroot/dicom/index.html` (visor)** — «PACS / AE Title a consultar» **vuelve a ser la fila de
  checkboxes** de siempre (☑ ASYN02 ☑ ASYN03 ☑ ASYN07 ☑ ASYN04), como estaban antes del listbox:
  salió el `<select multiple>`, sus botones Todos/Ninguno y su CSS. Además `.check-inline` ahora
  pinta casilla y nombre en la misma línea (la regla global de `label` los ponía en columna).

**Por qué:** al enviar siempre entran carpetas completas con cientos de imágenes; una fila por
archivo resultaba ilegible (miles de renglones) y no decía qué estudio estaba ya en el PACS. Con la
fila por estudio se ve de un vistazo paciente, ID, descripción, modalidad, número de imágenes y si
ya subió. En el visor, los checkboxes dejan elegir nodos sin teclado.

**Archivos creados:** `wwwroot/js/dicom-cab.js`, `wwwroot/js/desplegable-nodos.js`.
**Archivos modificados:** `wwwroot/dicom/enviar/index.html`, `wwwroot/dicom/index.html`,
`Models/Catalog.cs` y `Models/Permisos.cs` (la descripción de `dicom.enviar` habla ahora de
«subir carpetas de estudios»). El contrato del backend no cambió:
`POST /api/dicom/enviar/archivos` sigue recibiendo tandas de archivos con su etiqueta de ruta.

**Verificado:** `dotnet build -c Release` sin errores ni advertencias. Las dos pantallas se abrieron
en Chrome contra un servidor que sirve el `wwwroot` real y simula `/api/dicom/*` —sin tocar un PACS—:
el visor mostró los 4 nodos en checkboxes y marcó/desmarcó renglones con «Seleccionar todos»; el
envío leyó una carpeta fabricada de 15 archivos, armó 4 estudios más el renglón de descartados y, tras
«Enviar al PACS», quedó Subido (3) / Con errores (1, con el detalle que devolvió el C-STORE) /
Descartado (1); «Reintentar pendientes» volvió a mandar únicamente el estudio con errores (1 estudio,
3 imágenes) y «Quitar marcados» retiró el renglón elegido. Republicado con `dotnet publish` y servicio
reiniciado (12:32), con `publish\App_Data` y `dicom_downloads` intactos.

---

## 2026-10-30 — Depuración automática de las descargas DICOM (cada hora, interna)

**Módulo:** DICOM (descargas) + panel SISTEMAS.

**Qué cambió:**
- **`Dicom/DepuradorDescargas.cs` (nuevo)** — las reglas, separadas del temporizador para poder
  probarlas sobre cualquier carpeta. Un repaso borra: (1) los `.dcm` y los `.tmp` más viejos que
  `ConservarHoras`; (2) los ZIP de `_zips` más viejos que `ZipRetentionHours`; (3) las carpetas de
  paciente/estudio que quedaron vacías (siempre conserva la raíz y `_zips`); y (4) si aun así la
  carpeta pasa de `MaxMB`, lo más viejo primero hasta quedar debajo — sin tocar nunca los ZIP, que
  tienen su propia regla. Dos rieles de seguridad: **no borra nada** mientras haya una descarga en
  curso, y **nunca** toca un archivo modificado en los últimos 2 minutos (una transferencia puede
  estar escribiendo justo ahora aunque el registro aún no lo refleje). Cada archivo que estaba bloqueado
  se cuenta y se reintenta en el repaso siguiente.
- **`Dicom/LimpiadorDescargas.cs` (nuevo)** — el vigilante interno, mismo patrón que
  `Patologia/ImportadorService`: arranca con el servicio, hace su primer repaso 20 s después de prender
  (para recoger la basura que deja un reinicio a mitad de descarga) y luego repasa cada
  `Local:DepurarCadaMinutos` (60 por defecto). Guarda el último resultado en `Ultima` y nunca lanza:
  un fallo se registra y se reintenta a la hora.
- **`Dicom/DownloadJobService.cs`** — `TieneTrabajoActivo()` (hay algún trabajo en `pending`/`running`)
  y `TrabajosEnMemoria`; el comentario de la clase quedó corregido: los trabajos se retiran seis horas
  después de terminar.
- **Panel SISTEMAS** — `Services/Salud.cs` agrega `DepuracionDescargas()`, que se suma a `/api/status`.
  Muestra cuánto quedó en disco y a qué hora se repasó (por ejemplo
  *«2 archivo(s), 0.5 MB · repasado 08:43 · 1 MB liberados»*). **No vuelve a medir el disco**: el panel
  se refresca cada minuto y la carpeta puede tener miles de archivos, así que reutiliza los números que
  ya dio el último repaso. Si la depuración está apagada, el panel lo dice.
- **`Program.cs`** — registra `LimpiadorDescargas` como singleton y como `IHostedService` (el mismo
  objeto, para que `/api/status` consulte al que realmente trabaja) y documenta la sonda en el comentario
  de `/api/status`.
- **Configuración (opcional, todo ya trae default):** sección `Local` de `appsettings.json` —
  `DepurarDescargas` (true), `DepurarCadaMinutos` (60), `ConservarHoras` (3), `MaxMB` (4096) y
  `ZipRetentionHours` (24). No se escribió nada en `appsettings.json`: los defaults son exactamente la
  política pedida, y quien necesite cambiarla agrega la línea sin republicar.

**Por qué:** cada estudio descargado deja sus `.dcm` en el servidor y **antes no se borraban nunca**; la
limpieza de ZIP solo se ejecutaba cuando alguien iniciaba otra descarga. La medición real del servidor
(30-10-2026) fue **1 334 archivos y 1 024 MB** acumulados en `dicom_downloads`, con un ZIP de 316 MB
viviendo ahí desde el día anterior — el disco de la PC de la intranet se satura solo por usar el visor.
Además, una transferencia C-STORE cortada deja archivos `.tmp` que nadie reclamaba.

**Archivos creados:** `Dicom/DepuradorDescargas.cs`, `Dicom/LimpiadorDescargas.cs`.
**Archivos modificados:** `Dicom/DicomQrService.cs` (opciones nuevas en `LocalOptions`),
`Dicom/DownloadJobService.cs`, `Services/Salud.cs`, `Program.cs`.

**Verificado:** `dotnet build -c Release` sin errores ni advertencias. Dos pruebas con código real, sobre
carpetas temporales y **sin tocar el servidor**: (a) reglas — 22 aserciones sobre `.dcm` de 5 h borrado,
`.dcm` de 1 h intacto, `.tmp` huérfano borrado, ZIP de 30 h borrado y ZIP de 10 h conservado, carpetas
vacías podadas, nada fuera de la raíz alterado, repaso en blanco con descarga activa, tope de espacio que
se come lo más viejo y margen de 2 minutos respetado; (b) arranque — 27 aserciones armando el mismo grafo
de servicios que `Program.cs`, resolviendo el depurador como `IHostedService`, arrancándolo y comprobando
que a los ~20 s hace el primer repaso, que registra el resultado, que la sonda del panel lo lee, que con
una descarga activa no borra y que al terminar sí limpia, más el caso `DepurarDescargas=false`.

**Estado en producción:** publicado el **30-10-2026 a las 08:52** con el mismo procedimiento de
`actualizar.bat` (verificar que no haya descarga en curso → detener servicio → `dotnet publish` →
arrancar). El riel preventivo revisó `dicom_downloads`: el último archivo se había escrito 24 minutos
antes, así que nadie estaba descargando. Verificado después de arrancar: servicio **Running**,
`GET /login` responde **200**, `GET /api/status` responde **401** (ruta viva que pide sesión) y el binario
publicado (`Meditechnosys.Intranet.exe`, 123 108 325 bytes, 08:51:59) contiene las cadenas nuevas
(`repaso programado`, `hay una descarga en curso`, `Depuracion de descargas`).

**Qué se espera ver:** al publicar había **1 334 archivos y 1 023,8 MB** en `dicom_downloads`, todos con
menos de 3 horas salvo tres ZIP del 29-10 (09:31, 10:06 y 10:59) que todavía no cumplían las 24 horas, por
lo que el primer repaso (08:52) correctamente **no borró nada**. A partir de ahí: el ZIP de 316 MB se va en
el repaso de las **09:52**, el siguiente a las **10:52**, el último a las **11:52**, y los `.dcm` de hoy
(~660 MB, descargados a las 08:27) se borran en el repaso de las **11:52**, cuando cumplen las 3 horas. El
panel SISTEMAS muestra la tarjeta *Depuración de descargas* con lo que queda en disco y la hora del último
repaso.

**Tipo de cambio:** backend (servicio en segundo plano) + API de estado; sin base de datos y sin permisos.
En la interfaz lo único que se ve es la línea nueva del panel SISTEMAS.

---

## 2026-10-29 — Búsqueda en toda la intranet: la Ñ y las tildes dejan de estorbar

**Módulo:** transversal — Inventario, Almacén, Histórico, Playbook, Reportes, Bitácora del quemador,
panel de accesos y búsqueda DICOM.

**Qué cambió:**
- **Regla única en el servidor:** se crea `Services/Texto.cs` con `Texto.Normalizar()` (quita tildes y
  diéresis, pasa Ñ→N y Ç→C, mayúsculas y espacios colapsados), más `Contiene()`, `Empieza()` y
  `TieneLetrasAcentuables()`. Todos los filtros de las pantallas comparan ahora contra texto
  normalizado, así que «MUNOZ» encuentra a «MUÑOZ» y «CRANEO» a «CRÁNEO». Las comparaciones
  `Contains(..., OrdinalIgnoreCase)` de Inventario, Almacén, Histórico, Playbook, Reportes y la
  bitácora del quemador quedaron unificadas en esta función (la copia propia que tenía
  `BitacoraQuemador` ahora delega en ella).
- **Búsqueda al PACS tolerante a acentos:** `Dicom/CriteriosTolerantes.cs` arma una escalera de
  variantes de `PatientName` y `StudyDescription` —primero sin acentos, luego un comodín `?` por cada
  letra acentuable— y `DicomQrService.SearchAsync()` solo la recorre **cuando la consulta estricta no
  devolvió nada** (máximo 8 C-FIND extra, y ninguno si el término va entre comillas —exacto— o ya trae
  `*`/`?`). Después un filtro local sin acentos descarta los falsos positivos que el comodín arrastra.
- **Regla única en el navegador:** se crea `wwwroot/js/filtro.js` (`window.Filtro` con `norm`,
  `contiene`, `empieza`, `coincideAlguno`) y se carga en todas las páginas —en las Razor desde
  `_Layout.cshtml`, en el resto junto a `theme.js`, y antes de `js/inventario.js`—. Las cajas de
  búsqueda de panel de accesos, inventarios (equipos, IPs y AETitle), histórico, playbook, calendario,
  reportes y la página DICOM usan ahora esa función; cada página conserva un respaldo mínimo por si el
  archivo no carga, en cuyo caso la búsqueda sigue funcionando pero vuelve a distinguir la Ñ.
- **No se tocó:** la base de datos ya era insensible a acentos (`utf8mb4_0900_ai_ci` y la columna
  generada `nombre_norm`), y los identificadores exactos —folio, NSS, UIDs DICOM, IPs, AETitle, roles y
  permisos— siguen comparándose carácter por carácter; la tolerancia es solo para las cajas de búsqueda.

**Por qué:** el PACS y los registros guardan los apellidos con ñ y tildes escritas de forma irregular
(una PC manda «MUÑOZ», otra «MUNOZ»), y en C# y en el navegador `Ñ ≠ N` incluso ignorando mayúsculas.
El resultado era que el ingeniero tecleaba el apellido sin tilde y la intranet respondía «sin
resultados», incluso teniendo el estudio delante.

**Archivos creados:** `Services/Texto.cs`, `Dicom/CriteriosTolerantes.cs`, `wwwroot/js/filtro.js`.
**Archivos modificados:** `Services/Inventario.cs`, `Services/Almacen.cs`, `Services/Historico.cs`,
`Services/Playbook.cs`, `Services/BitacoraQuemador.cs`, `Dicom/DicomQrService.cs`, `Program.cs`
(filtro de reportes), `Pages/Shared/_Layout.cshtml`, `wwwroot/js/intranet.js`, `wwwroot/js/inventario.js`,
`wwwroot/dicom/index.html`, `wwwroot/calendario/index.html`, `wwwroot/gestion/historico/index.html`,
`wwwroot/gestion/playbook/index.html`, `wwwroot/gestion/inventario-equipos/index.html`,
`wwwroot/gestion/inventario-ips/index.html`, `wwwroot/gestion/inventario-aetitles/index.html`,
`wwwroot/admin/reportes/index.html`.

**Verificado:** `dotnet build -c Release` sin errores ni advertencias; prueba unitaria del escalonado
contra un PACS simulado (11 casos: estricto, sin tildes, comodín por letra, término entre comillas y
apellidos con dos diacríticos) y arnés de navegador (11 casos) que ejecuta la función de filtro real de
cada página —playbook, histórico, reportes, calendario, inventarios y DICOM— contra datos de muestra con
Ñ y tildes, incluyendo el caso de respaldo en que `filtro.js` no carga. Los bloques `<script>` de las
páginas modificadas se analizaron uno por uno y la revisión estática confirma que ninguna caja de
búsqueda quedó comparando en crudo ni quedó sin su `<script src="/js/filtro.js">`.

**Estado en producción:** publicado el 29-10-2026 17:36 con el mismo procedimiento de `actualizar.bat`
(detener servicio → `dotnet publish` → arrancar). Verificado en vivo: `GET /js/filtro.js` responde 200,
las pantallas DICOM, histórico, playbook, reportes, calendario e inventarios abren sin errores en la
consola del navegador y `POST /api/dicom/studies/download/{jobId}/cancel` existe (responde 401 sin sesión,
mientras que una ruta inexistente da 404), junto con el botón *✕ Cancelar descarga* ya funcional.

**Tipo de cambio:** backend + API + frontend (sin cambios de base de datos ni de permisos).

**Limitación conocida:** un apellido con dos diacríticos escrito «a secas» (IBÁÑEZ → «IBANEZ») puede no
aparecer en el PACS, porque el escalonado ensancha una sola letra por posición; sí se encuentra cuando
el nombre ya está en la lista cargada.

---

## 2026-10-29 — DICOM: filtro de resultados y botón «Cancelar descarga»

**Módulo:** Búsqueda y descarga DICOM (`/dicom/`).

**Qué cambió:**
- **Filtro de resultados:** bajo el título *Resultados* hay un cuadro **Filtrar resultados** que
  recorta la lista **ya traída del PACS** (no vuelve a consultar). Busca sin distinguir mayúsculas ni
  acentos en paciente, Patient ID, descripción, modalidad, fecha —`aaaa-mm-dd` o `yyyymmdd`—, nodo de
  origen y UID del estudio. Junto a los contadores aparece «mostrados por el filtro» y el botón
  *Quitar filtro* limpia el cuadro. Con filtro activo, *Seleccionar todos* marca solo los filtrados y
  el botón azul pasa a **Descargar los N filtrados**; *Ninguno* siempre desmarca todo.
- **Frontend:** junto a *Ninguno* aparece el botón rojo **✕ Cancelar descarga** (`#btnCancel`),
  deshabilitado salvo cuando hay una descarga en curso (la misma condición que deshabilita los
  botones *Descargar*). Al pulsarlo pide confirmación, llama a la API y deja de habilitarse hasta
  que el servidor confirme. El sondeo de progreso ahora reconoce el estado `cancelled`: muestra
  «Descarga cancelada», informa cuántos estudios e imágenes sí llegaron y avisa que no se generó ZIP.
- **API:** nuevo `POST /api/dicom/studies/download/{jobId}/cancel`, con la misma política `Ingenieria`
  que el resto del controlador. Devuelve 404 si el trabajo ya terminó o expiró.
- **Backend:** `DownloadJobService.Cancel(jobId)` cancela el `CancellationTokenSource` del trabajo y
  lo marca `cancelled`; `MarkCancelled` hace lo propio desde el hilo trabajador. Los contadores
  (`SetImages`, `IncrementStudies`, `MarkZipping`, `Complete`, `Fail`) ya no pueden reactivar ni
  reetiquetar un trabajo cancelado, y el porcentaje queda congelado al detenerse.
- **Comportamiento:** la cancelación surte efecto en el límite de cada lote/estudio —los estudios que
  faltaban no se piden al PACS—; las imágenes ya recibidas **no se borran**, quedan en la carpeta de
  salida del servidor y no se empaquetan en ZIP.

**Por qué:** las descargas grandes (cientos de estudios) pueden tardar horas y antes no había forma de
detenerlas desde la página; además, cuando la búsqueda devuelve cientos de estudios no había manera de
quedarse con un subconjunto (un paciente, una modalidad, un nodo) sin repetir la consulta al PACS.

**Archivos modificados:** `Dicom/DownloadJobService.cs`, `Dicom/Controllers/StudiesController.cs`,
`Dicom/Models.cs` (comentario de estados), `wwwroot/dicom/index.html`.

**Verificado:** `dotnet build -c Release` sin errores ni advertencias; el script de la página se ejecutó
sobre un DOM simulado comprobando el filtro (con y sin acentos, por fecha, ID, modalidad y nodo) y el
flujo descargar → cancelar → estado `cancelled` → botones reactivados.

**Tipo de cambio:** backend + API + frontend.

---

## 2026-10-19 — Bitácora: estado Conectado/Desconectado, filtros por día y hora, y descarga Excel

**Módulo:** Bitácora de accesos (`/admin/bitacora/`).

**Qué cambió:**
- **Conectado / Desconectado:** la columna *Estado* ya no muestra Activo/Inactivo sino
  **Conectado** (verde) o **Desconectado** (ámbar). Un usuario está *Conectado* si registró
  actividad en los últimos minutos (umbral configurable `Bitacora:UmbralConectadoMinutos`,
  15 por defecto); la web no mantiene conexión permanente, así que el estado se **deduce** del
  último movimiento. `GET /api/bitacora/resumen` ahora devuelve además `ultimaActividad`,
  `conectado` y `umbralMinutos`, usando el nuevo `IAuditLog.UltimasActividades()` (MySQL y JSON).
  Cuando está *Desconectado* se muestra su **última conexión** en la columna *Última conexión*.
- **Filtros por día y hora:** en "Movimientos de X" hay una barra con un selector de **día** y uno
  de **hora** (el desplegable se llena con las horas que sí tiene ese usuario) más un botón
  *Limpiar*. Filtran en el navegador sobre la lista ya cargada, en hora local.
- **Descarga en Excel:** botón *Descargar Excel* que llama a `GET /api/bitacora/export` con los
  mismos filtros visibles (`?usuario=&dia=&hora=`). El servidor genera un **.xlsx real** (ClosedXML,
  `Excel.Tabla`) con columnas Fecha / Hora / Usuario / Evento / Detalle / IP, filtrando en hora
  local para que coincida con lo mostrado, y registra el evento `Descarga de Excel` en la bitácora.
  Nunca CSV.

**Archivos modificados:** `Services/Bitacora.cs`, `Program.cs`,
`wwwroot/admin/bitacora/index.html`.

**Tipo de cambio:** backend + API + frontend.

---

## 2026-10-19 — Bandeja de soporte: listar, abrir y responder correos de soporte@meditechnosys.mx

**Módulo:** Nuevo módulo "Bandeja de soporte" (`/admin/correos/`) + lector/enviador IMAP-SMTP (MailKit).

**Qué cambió:**
- **Lector IMAP:** `Services/Correo.cs` (`CorreoService`) conecta por IMAP/SSL (puerto 993) al
  buzón `soporte@meditechnosys.mx`. Lista las **cabeceras** de los mensajes más recientes y, al
  abrir uno, devuelve el **contenido completo** (cuerpo en texto y HTML) y sus **adjuntos**
  descargables. Usa MailKit 4.18.1. Nunca lanza: un fallo (servidor caído, credenciales inválidas,
  sin red) se devuelve como mensaje legible y **sin datos sensibles**.
- **Responder por SMTP:** `CorreoService.ResponderAsync` envía la respuesta al remitente original
  por SMTP/SSL (puerto 465) con las mismas credenciales, encadenando la conversación
  (`In-Reply-To` / `References`) y prefijando el asunto con `RE:`.
- **Configuración:** sección `Correo:Imap` en `appsettings.json` con `Host`, `Port`, `Usuario`,
  `Ssl`, `SmtpHost`, `SmtpPort`, `SmtpSsl`, `RemitenteNombre`, `Carpeta`, `Maximo` y
  `TimeoutSegundos`. El campo `Contrasena` lo escribe el administrador en el servidor (misma
  credencial para IMAP y SMTP); **no** aparece en el código, los registros ni la documentación. Si
  falta, el módulo se reporta "sin configurar".
- **Endpoints:** `GET /api/correos` (lista), `GET /api/correos/{uid}` (detalle),
  `GET /api/correos/{uid}/adjuntos/{indice}` (descarga) —los tres con `correo.ver`— y
  `POST /api/correos/{uid}/responder` —con `correo.responder`—.
- **Permisos nuevos:** `correo.ver` y `correo.responder` (catálogo v8, grupo Administración). Por
  defecto los tiene el **Administrador**; el Ingeniero y los roles personalizados no. Las matrices
  guardadas se completan solas al subir de versión.
- **Interfaz:** página `wwwroot/admin/correos/index.html`: tabla Fecha/Remitente/Asunto con botón
  **Actualizar**; al hacer clic en un correo se abre el detalle (De/Para/Fecha, cuerpo en un
  `<iframe sandbox>` sin scripts si viene HTML, o texto plano), la lista de **adjuntos**
  descargables y el cuadro **Responder** (solo si el usuario tiene `correo.responder`). Muestra
  avisos claros si el buzón no está configurado o si algo falló. La tarjeta "Bandeja de soporte"
  aparece sola en el inicio para quien tenga el permiso.

**Archivos creados:** `Models/Correo.cs`, `Services/Correo.cs`, `wwwroot/admin/correos/index.html`.
**Archivos modificados:** `Meditechnosys.Intranet.csproj` (MailKit), `appsettings.json`,
`Models/Permisos.cs` (v8 + `correo.ver`/`correo.responder`), `Models/Catalog.cs` (tarjeta),
`Program.cs` (registro DI + endpoints).

**Tipo de cambio:** backend + API + frontend + permisos + configuración.

---

## 2026-10-19 — Bitácora con último acceso/estado, historial por usuario y día, y SISTEMAS real

**Módulo:** Bitácora de accesos (`/admin/bitacora/`) y panel SISTEMAS del inicio.

**Qué cambió:**
- **Último acceso y estado por usuario:** nueva tabla arriba en la bitácora con cada usuario, su
  **último inicio de sesión** y si está **Activo/Inactivo**. Viene del endpoint nuevo
  `GET /api/bitacora/resumen`, que combina el almacén de usuarios (`Activo`) con el último evento
  "Inicio de sesión" de la bitácora (`IAuditLog.UltimosAccesos()`, implementado en MySQL y JSON).
- **Historial por usuario y día:** al **hacer clic en un usuario** se abre "Movimientos de X",
  agrupado por día (recientes primero) y con etiquetas legibles en vez de rutas crudas, p. ej.
  `Visor DICOM → buscó estudios del paciente «12345678901»`, `Consulta de reportes → consultó
  reportes`, `Gestión → Alta de actividad · …`. El mapeo ruta→módulo/acción vive en el `<script>`
  de la página (`moduloDe` / `etiquetaAmigable`).
- **SISTEMAS con sondeos reales:** `GET /api/status` ya no devuelve solo valores fijos; ahora
  sondea de verdad (**`Services/Salud.cs`**, tope 2 s por sondeo): *Base de datos* (ping MySQL
  `SELECT 1`), *Visor de estudios* (socket TCP al primer nodo PACS) y *Consulta de reportes*
  (existe la carpeta del repositorio de Patología). Se quitó el "Servidor de estudios" fijo del
  catálogo para no duplicarlo.
- **Se quitó la ventana "Soporte TI"** del inicio (`Pages/Index.cshtml`) y su JS de copiar correo
  en `js/intranet.js`, porque no se usaba.

**Archivos modificados:** `Services/Bitacora.cs`, `Program.cs`, `Services/Salud.cs` (nuevo),
`Models/Catalog.cs`, `wwwroot/admin/bitacora/index.html`, `Pages/Index.cshtml`,
`Pages/Index.cshtml.cs`, `wwwroot/js/intranet.js`.

**Tipo de cambio:** backend + API + frontend.

---

## 2026-10-16 — Se retira el logo del IMSS de toda la intranet

**Módulo:** Todas las pantallas con cabecera de panel.

**Qué cambió:**
- Se **quitó el águila del IMSS** (el `<span class="logo">` teñido de dorado con máscara CSS) de
  **todas** las cabeceras: visor DICOM, consulta/carga de Patología, calendario, módulo Gestión
  (histórico, playbook, inventarios) y Administración (usuarios, bitácora, config, reportes, roles,
  administrativos).
- Se eliminó la regla CSS que lo pintaba: `header .logo` en `css/panel.css` y los `<style>` inline
  de `dicom/index.html` y `patologia/consulta/index.html`. Ya **no queda ninguna referencia** a
  `img/imss.png` en el código fuente (el archivo permanece en disco, sin uso).
- Las cabeceras conservan el título a la izquierda, el wordmark **Meditechnosys** centrado en dorado
  y los botones *Regresar*/*Home* a la derecha; la marca de agua de fondo (Meditechnosys) se conserva.
- Se subió el `?v=` de `panel.css` a `20261016f` en las 13 páginas que lo enlazan para forzar recarga.

**Archivos modificados:** `css/panel.css`, `dicom/index.html`, `patologia/consulta/index.html` y 13
páginas (span retirado + `?v=`).

**Tipo de cambio:** frontend.

---

## 2026-10-16 — DICOM: búsqueda por ID parcial sin escribir `*` y optimización

**Módulo:** Búsqueda y descarga DICOM (`/dicom/`).

**Qué cambió:**
- **ID parcial sin comodín manual:** ahora escribir solo una parte del *Patient ID* (o del
  *Accession Number*, *nombre* o *descripción*) coincide con los estudios cuyo valor empiece por
  ese texto, sin que el usuario tenga que teclear `*`. El servidor añade el comodín al final
  automáticamente (`NormalizarId` / `NormalizarTexto` en `DicomQrService`). Reglas: si el valor ya
  trae `*` o `?` se respeta tal cual; si va entre `"comillas"` se hace **coincidencia exacta**;
  los IDs se recortan, se les quitan espacios y se pasan a mayúsculas.
- **Guardia anti-volcado:** una consulta sin **ningún** criterio se rechaza con `400` en el servidor
  (`SinCriterio`) y con un aviso inmediato en la interfaz, evitando volcar miles de estudios del PACS.
- **Resultados más limpios:** se **deduplican** por (AE Title + StudyInstanceUID) y se **ordenan** por
  fecha descendente y nombre, de modo que los estudios recientes aparecen arriba.
- La interfaz refleja el nuevo comportamiento: *placeholders* y la línea de ayuda ya no piden `*`
  y explican la coincidencia parcial y la búsqueda exacta con `"…"`.

**Archivos modificados:** `Dicom/DicomQrService.cs`, `Dicom/Controllers/StudiesController.cs` y
`wwwroot/dicom/index.html`.

**Tipo de cambio:** backend + API + frontend.

---


**Módulo:** Administrativos · pestaña *Credenciales*.

**Qué cambió:**
- **Impresión en una sola hoja:** el frente queda a la izquierda y el reverso a la derecha, separados
  por una **línea punteada de doblez**; ya no sale una cara por página. Al recortar y doblar por esa
  línea, el reverso queda detrás del frente y se lee bien. Ambas caras conservan su tamaño real
  (54 × 86 mm) y la hoja lleva `page-break-inside: avoid` para que nunca se parta en dos páginas.
- **El botón PNG descarga una sola imagen con las dos caras** (frente a la izquierda, reverso a la
  derecha, fondo blanco), en vez de solo el frente. El archivo se llama
  `credencial-<nombre>-ambas-caras.png` y el botón ahora dice *PNG (2 caras)*.
- La **vista previa** explica el nuevo acomodo y `htmlCred()` / `lienzoAmbas()` quedan expuestos en
  `window.Administrativos` para previsualizar y verificar el formato (igual que las plantillas de acta).

**Archivos modificados:** `wwwroot/js/administrativos.js` y `wwwroot/admin/administrativos/index.html`
(subida de `?v=`).

**Tipo de cambio:** frontend.

---

## 2026-10-16 — Usuarios con nombre y cargo, y autocompletado en actas y credenciales

**Módulo:** Usuarios · Administrativos (actas y credenciales).

**Qué cambió:**
- **Cada usuario guarda ahora su nombre de persona y su cargo** (`nombre`, `cargo`), capturados en
  *Gestión de usuarios* al crear y al editar. La tabla muestra dos columnas nuevas (Nombre y Cargo).
- **Al elegir un usuario ligado en un acta o en una credencial, el nombre y el puesto se
  autocompletan solos** con los datos de su ficha; el selector además muestra
  "usuario — Nombre (rol)" para reconocer a la persona de un vistazo. Lo que no exista en la ficha se
  puede escribir a mano y elegir "(sin usuario ligado)" no pisa nada.
- **Base de datos:** `usuarios` gana `nombre VARCHAR(120) NOT NULL DEFAULT ''` y
  `cargo VARCHAR(80) NOT NULL DEFAULT ''`; `EnsureSchema()` las agrega por `ALTER TABLE` a bases previas
  (los usuarios existentes quedan con texto vacío, sin perder nada).
- **API:** `POST /api/usuarios` acepta `nombre` y `cargo`; `GET /api/usuarios` y
  `GET /api/usuarios/asignables` los devuelven; nuevo `PUT /api/usuarios/{usuario}/datos` que valida y
  recorta en el servidor (120/80 caracteres) y deja el evento de bitácora "Edición de datos de usuario".
- **Frontend:** nuevo gancho `onCambio(valor, api)` en `G.formDialog` (con `api.set`/`api.get`) para que
  un campo reaccione y rellene otros; lo usan el acta y la credencial.

**Archivos modificados:** `Services/UserStore.cs`, `Services/MySqlUserStore.cs`, `Services/UserRules.cs`,
`Models/Audit.cs`, `Program.cs`, `wwwroot/admin/usuarios/index.html`, `wwwroot/js/gestion.js`,
`wwwroot/js/administrativos.js` y las páginas que cargan `gestion.js` (subida de `?v=`).

**Tipo de cambio:** base de datos, API, frontend, backend.

---

## 2026-10-16 — Dos plantillas oficiales de acta: mala conducta y faltas injustificadas

**Módulo:** Administrativos · pestaña *Actas*.

**Qué cambió:**
- **El acta ahora tiene `tipo`** (`conducta` = mala conducta, `faltas` = faltas injustificadas) y **`hora`**
  (texto libre para el preámbulo "siendo las … horas"). El tipo se elige en el formulario y define la
  **plantilla con la que se imprime**; la tabla muestra una etiqueta de tipo por fila.
- **Dos formatos legales basados en los documentos oficiales de la empresa:**
  - *Mala conducta* (formato general): preámbulo en mayúsculas con ciudad, hora, día/mes/año y domicilio
    de la empresa; cláusula "SE LEVANTA PARA LOS EFECTOS LEGALES…"; relación de hechos;
    "INCURRIDOS POR PARTE DE …"; cláusula de reserva legal; cláusula de testigos bajo protesta de decir
    verdad; firmas de **INVOLUCRADO**, **TESTIGOS** y **ELABORÓ · ADMINISTRATIVOS**.
  - *Faltas injustificadas*: preámbulo en minúsculas con ciudad/hora/día/mes/año y domicilio; narrativa de
    hechos de inasistencia; cláusula de intento de contacto sin respuesta; **SANCIÓN PREVISTA Y ACUERDOS**;
    cláusula de validación por testigos; cierre con hora y fecha; firmas de **JEFE DIRECTO**, **ELABORÓ**,
    **INVOLUCRADO** y **TESTIGOS**.
- **Empresa y domicilio fijos** de Meditechnosys Group S.A. de C.V. (Av. Montevideo 498, CDMX) en el
  preámbulo y el pie; solo la ciudad/lugar es editable por acta.
- **Base de datos:** `actas` gana las columnas `tipo VARCHAR(20) NOT NULL DEFAULT 'conducta'` y
  `hora VARCHAR(20) NOT NULL DEFAULT ''`; `EnsureSchema()` las agrega por `ALTER TABLE` a tablas previas.
- **API:** `POST/PUT /api/actas` aceptan `tipo` y `hora`; un `tipo` inválido se normaliza a `conducta`
  (`ReglasActa.NormalizarTipo`).
- **Frontend:** el diálogo agrega selector *Tipo de acta* y campo *Hora*; `imprimirActa` se dividió en
  `estilosActa()` / `cuerpoActa(a)` / `htmlActa(a)` (funciones puras, expuestas en `window.Administrativos`
  para previsualizar el formato).

**Por qué:** el usuario pidió que las actas se basaran en los formatos oficiales enviados
(`pdf-acta-administrativa` y `pdf-ejemplo-de-acta-administrativa-por-faltas`), uno para mala conducta y
otro para faltas injustificadas.

**Archivos modificados:**
- `Services/Actas.cs` (record `Acta` + `Tipo`/`Hora`, `ReglasActa.NormalizarTipo`, interfaz y ambos
  almacenes con las columnas nuevas y la migración `ALTER TABLE`).
- `Program.cs` (`ActaRequest` + endpoints pasan `tipo`/`hora`).
- `wwwroot/js/administrativos.js` (selector de tipo/hora, columna *Tipo*, refactor de impresión en dos
  plantillas, constantes de empresa/domicilio).
- `wwwroot/css/panel.css` (chips `.acta-conducta` / `.acta-faltas`).
- `wwwroot/admin/administrativos/index.html` (columna *Tipo* y versión de caché).

**Tipo de cambio:** base de datos, API, frontend, documentación.

---

## 2026-10-16 — Módulo Administrativos: actas administrativas y credenciales con foto tamaño infantil

**Módulo:** Administrativos (página nueva `/admin/administrativos/` con dos pestañas).

**Qué cambió:**
- **Página nueva para administrativos** con dos pestañas: **Actas administrativas** (captura, edición,
  baja e impresión con membrete, folio consecutivo `ACTA-2026-0001…`, hechos, acuerdos, testigos y tres
  espacios de firma) y **Credenciales** (gafete oficial frente/reverso ligado a un usuario de la
  intranet, con vista previa, impresión a 54 × 86 mm y descarga en PNG).
- **Credenciales ligadas a usuarios:** cada credencial referencia una cuenta existente
  (`/api/usuarios/asignables`); el alta valida que el usuario exista. La **foto tamaño infantil
  (2.5 × 3 cm)** se recorta en el navegador (recortador con arrastre y zoom, relación 5:6) y se sube
  recodificada a JPEG; el servidor revalida tipo (JPEG/PNG por firma binaria), peso (≤ 2 MB) y la
  guarda en `App_Data/credenciales/`. La foto se sirve solo con el permiso correspondiente.
- **Formato oficial reproducido:** el ZIP del usuario traía el frente con textos de ejemplo
  ("NOMBRE", "INGENIERO DE SITIO", "VIG: 12/2028"). Un script de Node (zlib nativo, sin dependencias)
  decodificó el PNG, **inpintó esos textos** con interpolación bilineal + relajación de Gauss-Seidel
  (la marca de agua continúa desde los bordes) y generó `wwwroot/img/credencial/frente.png`; el reverso
  se copia tal cual (`reverso.png`) y se recortó el logo dorado (`logo.png`) para el encabezado de las
  actas. El render dibuja nombre, puesto, NSS, cédula y vigencia sobre el frente con canvas.
- **Permisos nuevos** `actas.administrar` y `credenciales.administrar` (catálogo **v6**, grupo
  *Administrativos (actas y credenciales)*; 40 claves en 13 grupos). La ruta `/admin/administrativos`
  abre con **cualquiera** de los dos; cada pestaña, tarjeta del inicio y endpoint exige su propia clave.
- **API:** `GET/POST /api/actas`, `PUT/DELETE /api/actas/{id}`; `GET/POST /api/credenciales`
  (multipart con foto), `PUT /api/credenciales/{id}`, `POST /api/credenciales/{id}/foto`,
  `GET /api/credenciales/{id}/foto`, `DELETE /api/credenciales/{id}`. Cada alta/edición/baja queda en la
  bitácora (`Alta/Edición/Baja de acta administrativa`, `Alta/Edición/Baja de credencial`).
- **Almacenes con degradación:** `IActaStore` (tabla `actas` / `App_Data/actas.json`) e
  `ICredencialStore` (tabla `credenciales` / `App_Data/credenciales.json`), con `EnsureSchema()` al
  arrancar; las fotos siempre viven en disco (`App_Data/credenciales/`).

**Por qué:** el usuario pidió "una pag. para administrativos donde puedan generar actas administrativas
y generar credenciales, y que las credenciales esten ligadas a los usuarios, para ello debe dejar subir
una foto tamaño infantil, quiero usar el formato de credencial que te mande".

**Archivos creados:**
- `Services/Actas.cs` (`Acta`, `ReglasActa`, `IActaStore`, `MySqlActaStore`, `JsonActaStore`).
- `Services/Credenciales.cs` (`Credencial`, `ReglasCredencial`, `FotosCredencial`, `ICredencialStore`,
  `MySqlCredencialStore`, `JsonCredencialStore`).
- `wwwroot/admin/administrativos/index.html` y `wwwroot/js/administrativos.js`.
- `wwwroot/img/credencial/frente.png` (plantilla limpia), `reverso.png` y `logo.png`.

**Archivos modificados:**
- `Models/Permisos.cs` (claves nuevas, grupo *Administrativos*, `Version = 6`).
- `Models/Audit.cs` (seis eventos nuevos de actas y credenciales).
- `Models/Catalog.cs` (tarjetas *Actas administrativas* y *Credenciales* en el grupo *Administrativos*).
- `Program.cs` (DI dual + `EnsureSchema`, guard de `/admin/administrativos` con permiso doble,
  endpoints `/api/actas*` y `/api/credenciales*`, exclusión de la bitácora de acceso,
  `/api/usuarios/asignables` ampliado a los dos permisos nuevos).

**Tipo de cambio:** base de datos, API, frontend, backend, permisos.

---

## 2026-10-15 — Apartado de Avisos dinámico en el inicio (publicar / editar / eliminar por permiso)

**Módulo:** Página de inicio (panel lateral *Avisos*).

**Qué cambió:**
- **El panel *Avisos* dejó de ser estático.** Antes mostraba tres avisos de ejemplo fijos
  (`Catalog.Notices`); ahora se alimenta de la tabla/almacén **`avisos`** y se publica desde la propia
  página de inicio. Se eliminaron el record `Notice` y la lista `Catalog.Notices`.
- **Permiso nuevo `avisos.administrar`** (catálogo v5, grupo *Avisos del inicio*): quien lo tiene ve en el
  panel el botón **+ Publicar** y, en cada aviso, **Editar** y **Eliminar** (diálogo reutilizando los
  estilos `.modal` de `intranet.css`). **Quien no lo tiene solo ve los avisos publicados**, sin ningún
  control. Por defecto lo tiene el Administrador; el Ingeniero y los roles personalizados, no.
- **Avisos pausados.** Cada aviso tiene bandera `activo`; un aviso pausado no aparece a los lectores pero
  quien administra lo ve atenuado con la etiqueta *· Pausado* para reactivarlo, editarlo o eliminarlo.
- **API:** `GET /api/avisos` (solo activos, cualquier sesión), `GET /api/avisos/todos`,
  `POST /api/avisos`, `PUT /api/avisos/{id}` y `DELETE /api/avisos/{id}` (estos cuatro exigen
  `P:avisos.administrar`). Cada alta/edición/baja queda en la **bitácora** (`Alta/Edición/Baja de aviso`).
- **Almacén con degradación:** `IAvisoStore` con `MySqlAvisoStore` (tabla `avisos`, `EnsureSchema` al
  arrancar) y `JsonAvisoStore` (`App_Data/avisos.json`) cuando MySQL no responde.

**Por qué:** el usuario pidió "un apartado de avisos, que el que tenga dicho permiso pueda crear, editar
o eliminar avisos y los que no se les visualice", usando el diseño del panel lateral existente.

**Archivos creados:**
- `Services/Avisos.cs` (`Aviso`, `ReglasAviso`, `IAvisoStore`, `MySqlAvisoStore`, `JsonAvisoStore`).
- `wwwroot/js/avisos.js` (diálogo publicar/editar y confirmación de eliminar contra la API).

**Archivos modificados:**
- `Models/Permisos.cs` (clave `avisos.administrar`, grupo *Avisos del inicio*, `Version = 5`).
- `Models/Audit.cs` (eventos `Alta/Edición/Baja de aviso`).
- `Models/Catalog.cs` (se retiran `Notice` y `Catalog.Notices`).
- `Program.cs` (registro DI de `IAvisoStore`, `EnsureSchema`, endpoints `/api/avisos*`, record
  `AvisoRequest`).
- `Pages/Index.cshtml{,.cs}` (carga los avisos del almacén y el permiso; pasa `AvisosPanelModel` al
  partial; carga `avisos.js` solo con permiso) y `Pages/Shared/_Avisos.cshtml` (panel dinámico +
  controles + diálogo).
- `wwwroot/css/intranet.css` (`.news-head`, `.news-empty`, `.news-acc`, aviso pausado, `textarea` del
  modal) y `Pages/Shared/_Layout.cshtml` (pie sin "avisos de ejemplo").

**Verificación (dev 5099):** build Release 0 errores / 0 advertencias; Admin: `POST`→201, `PUT`→204,
`GET /api/avisos` solo activos, `GET /api/avisos/todos` incluye pausados, panel con **+ Publicar** /
**Editar** / **Eliminar** y aviso pausado marcado; Ingeniero: `GET /api/avisos`→200 solo activos,
`/todos`→403, `POST`→403, `DELETE`→403 y panel sin controles. Avisos de prueba eliminados al terminar.

**Tipo de cambio:** base de datos (tabla `avisos` con respaldo JSON), API, backend, frontend, permisos.

---

## 2026-10-15 — Roles dinámicos, permisos por usuario e inicio único por acceso

**Módulo:** Administración (Roles y permisos, Usuarios) y página de inicio.

**Qué cambió:**
- **Roles dinámicos con nombre libre.** Se pueden **crear, renombrar y eliminar** roles desde
  *Admin → Roles y permisos*. Los roles de sistema **Admin** e **Ingeniero** no se renombran ni eliminan.
  La matriz de permisos de cada rol es editable y se guarda por rol (`IRoleStore`).
- **Ajustes de permisos por usuario.** Además de la matriz del rol, cada usuario puede **conceder (+)** o
  **revocar (−)** permisos concretos (*Heredado del rol* por defecto). Los permisos efectivos =
  matriz del rol + overrides, y se recalculan **en cada petición** (`IPermissionResolver` en
  `OnValidatePrincipal`), de modo que los cambios aplican de inmediato a las sesiones abiertas.
  Anti-bloqueo: **Admin siempre conserva `roles.administrar`**.
- **Inicio único filtrado por acceso.** `/Admin` e `/Ingenieria` redirigen a `/Index`, que muestra **solo**
  las herramientas cuyo permiso tiene el usuario (Patología, DICOM, Calendario, Histórico, Inventarios,
  Playbook, Reportes, Configuración, Usuarios, Roles, Bitácora). Cada permiso se valida **en la interfaz
  y en el servidor** (guards de ruta + políticas `P:<clave>`).

**Por qué:** el usuario pidió "poder agregar más roles", "actualizar la página de roles y permisos para
asignar cada permiso a los usuarios" y "que para un usuario (admin o ing) solo aparezca una página de
inicio con las herramientas a las que tendrá acceso". Se eligió el modelo **rol + ajustes por usuario**
y **nombre de rol libre e ilimitado**.

**Archivos creados:**
- `Services/RolesStore.cs` (`IRoleStore`, `IUserPermissionStore`, `IPermissionResolver` con
  implementaciones MySQL y JSON; `PermissionResolver.Calcular` = matriz del rol + overrides, y
  anti-bloqueo: Admin siempre conserva `roles.administrar`).

**Archivos modificados:**
- `Models/Permisos.cs` (catálogo v4: claves para todas las apps y grupos; `TodasLasClaves`).
- `Models/Catalog.cs` / `Models/Roles.cs` (herramientas visibles por permiso; `Roles.Display`).
- `Services/UserRules.cs` (`NormalizeRole` acepta cualquier nombre de rol válido; `ReglasRol`).
- `Services/{Almacen,Playbook,Inventario,Gestion}.cs` (sin parámetro `permisos` sin uso; `GestionService`
  expone `Sesion`, `EsAdmin`, `Puede`, `VeTodo`, `PermClaim`, `De`).
- `Program.cs` (sesión por claims de permiso en `OnValidatePrincipal`; políticas `P:<clave>`; guards de
  ruta por permiso; CRUD de roles `GET/POST/PUT/DELETE /api/roles`; matriz `PUT /api/roles`; permisos por
  usuario `GET/PUT /api/usuarios/{u}/permisos`; lista ligera `GET /api/usuarios/asignables`;
  `/api/permisos/mios`).
- `Pages/Index.cshtml{,.cs}` (inicio único filtrado por permisos); `Pages/{Admin,Ingenieria}.cshtml{,.cs}`
  (redirigen a `/Index`); `Pages/Shared/_Layout.cshtml` (nav reducido a *Inicio*).
- `wwwroot/js/gestion.js` (`veTodo`, `nombreRol`, `paginaRegreso`→`/`, diálogos con `/asignables`,
  acciones gateadas por permiso) y `wwwroot/js/inventario.js` (`puedeAdministrar` por permiso).
- `wwwroot/admin/roles/index.html` y `wwwroot/admin/usuarios/index.html` (reescritos: roles dinámicos y
  editor de ajustes por usuario con columna *Efectivo* en vivo).
- Portadas y badges de `admin/{reportes,config,bitacora}`, `gestion/{historico,playbook,inventario-equipos}`
  y `calendario`: de `esAdmin()` a checks por permiso y `nombreRol`.
- Caché: referencias `gestion.js`/`inventario.js` a `?v=20261015a`.

**Verificación (dev 5099):** build Release 0 errores / 0 advertencias; login, `/api/permisos/mios`,
`/api/roles`, `/api/usuarios/asignables` → 200; ciclo crear→renombrar→eliminar rol → 204 (rol de prueba
eliminado, sin datos residuales); inicio muestra 13 tarjetas para Admin y 7 para Ingeniero (admin/roles
oculto). Guards sin autenticar → 302 a `/Login`; APIs → 401.

**Tipo de cambio:** base de datos (nuevas tablas de roles/overrides con respaldo JSON), API, backend,
frontend, permisos.

**Pendiente de publicación:** el servicio de producción (publish\\, puerto 5023) se actualiza ejecutando
`actualizar.bat` **como administrador**. Antes de publicar conviene borrar las 2 actividades de prueba
restantes ("Prueba", "Prueba checkbox limite") desde el Histórico; **no** tocar el movimiento de
producción `ENT-2026-0001`.

---

## 2026-10-12 — Inventario de equipos: lotes en 0 ocultos y número de inventario automático

**Módulo:** Gestión (Inventario de equipos).

**Qué cambió:**
- **Los lotes con cantidad 0 desaparecen de la lista.** `AlmacenService.Existencias` ahora filtra
  `CantidadDe(i) > 0`, de modo que un equipo que se queda sin existencias ya no aparece en la tabla
  ni en el Excel (ambos endpoints comparten este método). El registro **no** se borra: se conserva en
  el catálogo junto con su historial de movimientos y **reaparece** si vuelve a entrar stock
  (`Services/Almacen.cs`).
- **El número de inventario se genera solo.** Al dar de alta un equipo sin indicar n.º de inventario,
  `InventarioService.Crear` asigna el siguiente correlativo `EQ-0001`, `EQ-0002`, … con el helper
  `SiguienteNumInventario()`. El máximo se calcula sobre **todos** los lotes de equipos (incluidos los
  que están en 0) para **no reutilizar** nunca un número retirado (`Services/Inventario.cs`).
- **El diálogo "+ Añadir equipo" ya no pide el n.º de inventario.** Se eliminó ese campo del formulario
  (queda en 12 campos); el texto de ayuda aclara que el número se genera automáticamente y el detalle
  del movimiento de entrada usa el valor devuelto por el servidor
  (`wwwroot/gestion/inventario-equipos/index.html`, caché `?v=20261012d`).

**Por qué:** el usuario pidió que "cuando esté en 0 que se elimine de la lista de equipos, y el número
de inventario que se genere solo".

**Archivos modificados:**
- `Services/Almacen.cs` (filtro `CantidadDe(i) > 0` en `Existencias`; helper `CantidadDe`).
- `Services/Inventario.cs` (autogeneración en `Crear`; helper `SiguienteNumInventario()`).
- `wwwroot/gestion/inventario-equipos/index.html` (campo n.º de inventario retirado del alta).
- `README.md`, `docs/INVENTARIO-EQUIPOS.md`, `docs/REGISTRO-CAMBIOS.md` (re-documentación).

**Tipo de cambio:** frontend, backend (sin cambios de esquema de base de datos).

---

## 2026-10-12 — Rediseño: pantalla única "Inventario de equipos" con pestaña Ingresos y hojas por fila

**Módulo:** Gestión (Inventario de equipos) y documentos imprimibles.

**Qué cambió:**
- **Se retiró el módulo independiente *Almacén de equipos*.** La tarjeta *Almacén* desapareció de
  los menús y la pantalla `/almacen/index.html` ahora **redirige** a
  `/gestion/inventario-equipos/index.html`. Solo queda **una** pantalla de equipos: *Inventario de
  equipos* (`Models/Catalog.cs`, `wwwroot/almacen/index.html`).
- **Pestaña única *Ingresos*.** La pantalla se reescribió con una sola pestaña *Ingresos*, un
  **selector de unidad** (`Almacén`, `HES`, `HGR`, `HGO`, `HI`; *Almacén* = hospital `General`,
  impreso en las hojas como **ALMACÉN CENTRAL**), buscador y tabla
  (`wwwroot/gestion/inventario-equipos/index.html`).
- **Botón + Añadir equipo (solo Admin).** Crea el lote, genera y **confirma su entrada** (que suma
  la cantidad), sella `docEntrada` y abre la **hoja de entrada**.
- **Hojas por fila.** Cada fila ofrece **Hoja entrada** (reimpresión), **Hoja salida** (descuenta
  stock, genera y confirma la hoja de salida y, si hay destino, **re-ingresa** el equipo allá) y
  **Editar** (solo Admin). El traslado entre unidades se modela como *salida + re-ingreso*.
- **Descargar Excel solo Admin.** El botón de exportación de existencias queda gated a Admin en la
  interfaz y validado por el permiso `almacen.exportar` en el servidor.
- **Unidad "Almacén" (General) habilitada** en equipos y movimientos: `permiteGeneral: true` en
  `Models/Inventario.cs`, `Services/Inventario.cs`, `Services/Inventarios.cs`, `Models/Almacen.cs`
  y `Services/AlmacenStore.cs`/`Services/Almacen.cs`.
- **Incremento de un lote existente permitido.** La comprobación de duplicado de una entrada ya
  **no** rechaza una línea que apunta al mismo lote (`detalles[].equipoId`): ahora le suma
  cantidad (`Services/Almacen.cs`: `dup is not null && dup.Id != d.EquipoId`). Solo se rechaza una
  serie/n.º de inventario duplicado **en un lote distinto** de la unidad.
- **Grupo de permisos renombrado** (solo la etiqueta visible) de *Almacén de equipos* a
  **"Inventario de equipos · entradas y salidas"** (`Models/Permisos.cs`); las claves `almacen.*`
  y el espacio de nombres `/api/almacen/*` **se conservan** por compatibilidad.

**Por qué:** el usuario pidió expresamente una única pestaña de *Ingresos* con las opciones
Almacén/HES/HGR/HGO/HI, un botón *Añadir* que genere la hoja de entrada y de salida en cada fila,
conservar solo *Inventario de equipos* (quitar el de *Almacén*), dejar *Descargar Excel* solo del
lado del Admin, y re-documentar/optimizar todo el código.

**Archivos creados:**
- `docs/INVENTARIO-EQUIPOS.md` (documentación del módulo unificado).

**Archivos modificados:**
- `Models/Catalog.cs` (tarjeta única *Inventario de equipos* en Admin e Ingeniería; sin *Almacén*)
- `Models/Permisos.cs` (etiqueta del grupo de permisos)
- `Models/Inventario.cs`, `Models/Almacen.cs` (`permiteGeneral` para la unidad *Almacén*)
- `Services/Inventario.cs`, `Services/Inventarios.cs`, `Services/AlmacenStore.cs`
  (`Hospitales.Normalizar(..., permiteGeneral: true)`)
- `Services/Almacen.cs` (permitir incremento sobre el lote existente en una entrada)
- `Program.cs` (`HojaAlmacenHtml`: `General` → **ALMACÉN CENTRAL**)
- `wwwroot/gestion/inventario-equipos/index.html` (pantalla única reescrita)
- `wwwroot/almacen/index.html` (redirección a la pantalla única)
- `README.md`, `docs/REGISTRO-CAMBIOS.md`, `docs/REPORTE-ENTREGA-2026-10-12.md` (re-documentación)

**Archivos eliminados:**
- `docs/ALMACEN.md` (sustituido por `docs/INVENTARIO-EQUIPOS.md`).

**Tipo de cambio:** frontend, backend, permisos (sin cambios de esquema de base de datos).

---

**Módulo:** Gestión (Almacén de equipos + inventarios) y documentos imprimibles.

**Qué cambió:**
- **Fusión de pantallas.** Se eliminó la tarjeta *Inventario de equipos* de los menús *Admin* e
  *Ingeniería* (`Models/Catalog.cs`); el **Almacén de equipos** (`/almacen/`) pasa a ser la
  **pantalla única de equipos**. La ruta antigua `/gestion/inventario-equipos/` ahora
  **redirige** al Almacén (meta-refresh + `location.replace`), de modo que no queda una segunda
  pantalla de equipos accesible.
- **Alta del inventario en el Almacén.** En la pestaña *Existencias* el Admin dispone de
  **+ Agregar equipo** y de **Editar** por fila; el diálogo captura hospital, nombre, categoría,
  **marca**, **modelo**, **serie**, número de inventario, **cantidad**, servicio, ubicación,
  estado, procedencia y notas. Reutiliza los endpoints genéricos
  `POST/PUT/DELETE /api/inventario/equipos` (`SoloAdmin`); el Ingeniero solo consulta y exporta.
- **Entradas y salidas enlazadas al inventario.** Al confirmar, una **entrada suma** cantidad al
  hospital destino, una **salida resta** del origen y un **traslado** resta en origen y suma en
  destino al recibirse. Nuevo motivo de salida **"Desecho / baja definitiva"**
  (`Models/Almacen.cs`): con ese motivo las unidades **no se re-ingresan** en ningún almacén.
- **Hojas con el formato oficial** (`Entrada.docx` / `Salida.docx`): título, recuadro de
  encabezado (razón social, unidad médica, área, contrato y vigencias), fecha larga + folio,
  tabla *EQUIPO / NUMERO DE SERIE / UNIDADES* con la fila *OBSERVACIONES* dentro, y bloque de
  **firmas en blanco sin nombres** (tres líneas de subrayado y los tres puestos). La misma
  función `HojaAlmacenHtml` sirve el HTML en línea (`/hoja`) y el Word (`/word`);
  `/almacen/hoja.html` replica los estilos para impresión/PDF.

**Por qué:** requerimiento de unificar el inventario de equipos con el almacén en una sola
pantalla (evitar duplicidad), permitir el alta de equipos con marca/modelo/serie/cantidad desde
el almacén, enlazar entradas y salidas con el stock por hospital (restando/sumando, salvo
desecho) y reproducir fielmente el formato de las hojas oficiales dejando las firmas en blanco.

**Archivos modificados:**
- `Models/Catalog.cs` (se retira la tarjeta *Inventario de equipos* de Admin e Ingeniería y se
  actualiza la descripción del Almacén)
- `Models/Almacen.cs` (motivo de salida *Desecho / baja definitiva*)
- `Program.cs` (endpoint `/api/almacen/movimientos/{id}/hoja` y `HojaAlmacenHtml` con el formato
  oficial y firmas sin nombres)
- `wwwroot/almacen/index.html` (botón *+ Agregar equipo*, columna *Editar*, tabla de existencias
  ampliada con marca/modelo/serie y diálogo de alta/edición)
- `wwwroot/almacen/hoja.html` (estilos de la hoja oficial y del bloque de firmas)
- `wwwroot/gestion/inventario-equipos/index.html` (redirección al Almacén)
- `README.md`, `docs/ALMACEN.md`, `docs/REPORTE-ENTREGA-2026-10-12.md` (re-documentación)

**Tipo de cambio:** frontend, backend, API, permisos (sin cambios de esquema de base de datos:
el alta reutiliza la tabla `inventario` existente).

---

## 2026-10-12 — Módulo Almacén de equipos (existencias, entradas, salidas, traslados, hojas) y ajustes de calendario

**Módulo:** Gestión (nuevo submódulo *Almacén de equipos*) + Calendario.

**Qué cambió:**
- **Almacén de equipos** (`/almacen/`): pantalla con pestañas *Existencias, Entradas, Salidas,
  Traslados e Historial*. Las existencias son los **lotes del catálogo de equipos** ampliados con
  número de inventario, cantidad, ubicación, estado, fecha de ingreso, procedencia y documento de
  entrada (**una sola fuente de verdad para el stock**, sin tabla paralela).
- **Movimientos con folio único** (`ENT-/SAL-/TRA-año-####`): entrada suma stock al confirmarse,
  salida lo resta, y traslado lo deja *En tránsito* hasta **recibirse** en el destino. Máquina de
  estados `Borrador → Confirmado/Recibido | Cancelado/Rechazado` con estados terminales.
- **Integridad del stock:** disminución en **dos fases** (se valida todo antes de aplicar nada: sin
  stock negativo ni salidas parciales); al confirmar una entrada se **rechaza la serie o número de
  inventario duplicado en el mismo almacén** (sin unidades físicas duplicadas); cancelar/rechazar un
  traslado en tránsito **reversa** el stock al origen. **Sin borrado físico** de movimientos.
- **Hojas institucionales** de ingreso/salida/traslado (plantilla de `Entrada.docx`/`Salida.docx`:
  unidad médica, área, contrato, vigencias, folio, fecha larga, tabla de equipos, observaciones y
  firmas), generadas desde los datos guardados: **imprimibles/PDF** (`/almacen/hoja.html`,
  `GET .../hoja`) y **descargables en Word** (`GET .../word`), reimpresión ilimitada desde el
  historial. Una sola función (`HojaAlmacenHtml`) genera ambas versiones.
- **Historial y auditoría:** filtros por tipo, estado, hospital, rango de fechas y texto; descarga
  en **Excel** de existencias e historial; cada alta/confirmación/recepción/cancelación y cada
  descarga quedan en la bitácora.
- **Permisos (catálogo v3):** nuevas claves `almacen.consultar`, `almacen.registrar`,
  `almacen.autorizar` y `almacen.exportar` en el grupo *Almacén de equipos*; cancelar/rechazar
  movimientos ajenos o en tránsito exige `almacen.autorizar`. Validación **en el servidor**
  (gating de ruta + `RequireAuthorization` + comprobación en el servicio), no solo en la interfaz.
- **Base de datos:** tabla nueva `almacen_movimientos` (con `folio UNIQUE` e índices
  `idx_almacen_tipo_estado` y `idx_almacen_fecha`), creada sola al arrancar; respaldo en
  `App_Data/almacen.json` si MySQL no responde.
- **Calendario:** casilla **"Deshabilitar fecha límite" por elemento** dentro de los diálogos de
  crear/editar/asignar (apaga y limpia el campo de fecha de ese elemento, en lugar de un ajuste
  global) y **panel derecho "Pendientes por ingeniero"** que agrupa, por responsable, las
  actividades/incidentes abiertos y los reportes de estatus pendientes, con conmutador
  *Actividades / Reportes*.
- **Frontend compartido:** `Gestion.formDialog` admite campos tipo `checkbox` (con `desactiva` para
  apagar otro campo) y la opción `contenidoDespues` para colocar un bloque interactivo debajo de
  los campos de la cabecera.

**Por qué:** requerimiento de un inventario central con trazabilidad por unidad, control de
entradas/salidas/traslados entre servicios y hospitales con autorización y hojas institucionales
reimpresibles, sin duplicar unidades físicas ni permitir stock negativo; y de llevar el control de
la fecha límite al nivel de cada elemento y mostrar la carga pendiente por ingeniero en el
calendario.

**Archivos creados:**
- `Models/Almacen.cs` (tipos, estados, motivos, `MovimientoInventario`, `MovimientoDetalle`, reglas)
- `Services/AlmacenStore.cs` (`IAlmacenStore` + almacenes MySQL/JSON + folios + `EnsureSchema`)
- `Services/Almacen.cs` (`AlmacenService`: existencias, confirmar/recibir/cancelar, stock y reversas)
- `wwwroot/almacen/index.html` (pestañas, filtros, Excel, diálogo de registro con líneas de equipo)
- `wwwroot/almacen/hoja.html` (hoja imprimible/PDF + descarga Word)
- `docs/ALMACEN.md` (documentación del módulo)

**Archivos modificados:**
- `Models/Inventario.cs` (campos nuevos de la colección `equipos`: n.º de inventario, cantidad,
  ubicación, estado, fecha de ingreso, procedencia, documento de entrada)
- `Models/Permisos.cs` (catálogo v3 + grupo y claves del almacén)
- `Models/Audit.cs` (eventos de crear/confirmar/recibir/cancelar movimiento)
- `Models/Catalog.cs` (tarjeta *Almacén de equipos* en los menús de Admin e Ingeniero)
- `Program.cs` (DI, gating `/almacen`, endpoints `/api/almacen/*`, `HojaAlmacenHtml`, registros de
  solicitud de Word/hoja)
- `wwwroot/js/gestion.js` (checkbox en `formDialog`, `desactiva`, `contenidoDespues`)
- `wwwroot/css/panel.css` (estilo de la casilla y del panel de pendientes)
- `wwwroot/calendario/index.html` (checkbox por elemento y panel derecho de pendientes)
- `README.md` (secciones, tabla de pantallas, estructura, configuración y enlaces del almacén)
- Todas las páginas `.html` de `wwwroot` (versión de caché `?v=` actualizada)

**Archivos eliminados:** ninguno (no se borró información de producción ni registros históricos).

**Tipo de cambio:** base de datos, API, backend, frontend, permisos, configuración, documentación.

---

## 2026-10-09 — Hospitales, histórico único con Excel, inventarios, Playbook y fecha límite administrativa

**Módulo:** Gestión (histórico, inventarios, Playbook, configuración) + Calendario + Administración.

**Qué cambió:**
- **Clasificación obligatoria por hospital** (HES, HGR, HGO, HI) en actividades, incidentes y
  tickets: campo nuevo `Hospital` en el modelo, en la tabla `actividades`, en los formularios de
  creación/edición/asignación, en el detalle, en los filtros y en las exportaciones.
- **Histórico único.** Se eliminó el apartado independiente *Histórico de tickets*: actividades,
  incidentes y tickets se consultan en una sola pantalla (`/gestion/historico/`) y se distinguen con
  el filtro **Tipo**. El panel izquierdo del calendario pasó a llamarse **Tickets** (panel de
  trabajo, no apartado de consulta) y remite al Histórico de actividades.
- **Descarga en Excel real (.xlsx)** con ClosedXML, respetando exactamente los filtros aplicados:
  histórico de actividades, los tres inventarios. Nunca se genera CSV.
- **Tres inventarios independientes** (equipos, IPs, AETitle), cada uno en su propia pantalla con
  filtro por hospital, búsqueda, alta/edición/baja para el Administrador y descarga en Excel.
- **Playbook**: biblioteca de documentación técnica en PDF por hospital (HES/HGR/HGO/HI/General) con
  nombre, versión, fechas de publicación/actualización, estado y archivo. El Administrador sube,
  renombra, cambia versión, reemplaza el archivo y elimina; el Ingeniero sólo consulta, abre y
  descarga. Se valida que el archivo sea realmente un PDF (`%PDF-`) y el tope es de 20 MB.
- **Fecha límite exclusivamente administrativa.** Nueva clave de configuración
  `items.fechaLimiteHabilitada` con interruptor Habilitada/Deshabilitada en `/admin/config/`.
  El Ingeniero **no ve** el campo en ningún caso (formularios, detalle, columna del histórico,
  insignia *Vencida*, filtro *Vencida* y columna del Excel). Si se deshabilita, la fecha límite no
  se solicita, no se guarda en los elementos nuevos y no se valida.
- **Permisos (catálogo v2)**: nuevas claves `historico.exportar`, `inventario.consultar`,
  `inventario.exportar`, `inventario.administrar`, `playbook.consultar`, `playbook.administrar` y
  `configuracion.fechaLimite`; se retiró `historico.tickets`. Las matrices ya guardadas se completan
  automáticamente con los valores por defecto del rol al detectar una versión de catálogo menor.
- **Menús.** El grupo Gestión del Administrador queda: *Roles y permisos, Calendario, Histórico de
  actividades, Reportes de estatus, Inventario de equipos, Inventario de IPs, Inventario de
  AETitle, Playbook, Configuración*. El del Ingeniero: *Calendario, Histórico de actividades,
  Inventario de equipos, Inventario de IPs, Inventario de AETitle, Playbook*. La pantalla
  `/Ingenieria` pasó a renderizarse agrupada por sección (igual que `/Admin`).
- **Reportes de estatus** (`/admin/reportes/`) incorporan columna y filtro por **Hospital**.
- **Calendario**: filtro por hospital que afecta a la rejilla mensual y al panel de tickets; los
  chips y las tarjetas muestran el hospital.

**Por qué:** requerimiento de separar toda la información por hospital, concentrar la consulta de
los tres tipos de elemento en un único histórico exportable a Excel, incorporar los inventarios y el
Playbook al módulo Gestión, y volver la fecha límite una decisión exclusivamente administrativa,
sin perder ni romper lo ya desarrollado.

**Archivos creados:**
- `Models/Hospitales.cs` (catálogo HES/HGR/HGO/HI + `General`, normalización y validación)
- `Models/Inventario.cs` (esquema de campos por colección, reglas y nombre de archivo)
- `Models/Documento.cs` (modelo del Playbook, estados, reglas y validación de PDF)
- `Services/Excel.cs` (generación .xlsx compartida con ClosedXML)
- `Services/Inventarios.cs` (`IInventarioStore` + almacenes MySQL y JSON)
- `Services/Inventario.cs` (`InventarioService`: consulta, alta/edición/baja y exportación)
- `Services/Documentos.cs` (`IDocumentoStore` + almacenes MySQL y JSON)
- `Services/Playbook.cs` (`PlaybookService`: subida, metadatos, reemplazo, eliminación y entrega del PDF)
- `Services/Historico.cs` (`FiltroHistorico` y `HistoricoExportador`)
- `wwwroot/js/inventario.js` (pantalla compartida de los tres inventarios, dirigida por esquema)
- `wwwroot/gestion/historico/index.html` (histórico único de actividades, incidentes y tickets)
- `wwwroot/gestion/inventario-equipos/index.html`, `wwwroot/gestion/inventario-ips/index.html`,
  `wwwroot/gestion/inventario-aetitles/index.html`
- `wwwroot/gestion/playbook/index.html`

**Archivos modificados:**
- `Models/Actividad.cs` (campo `Hospital` y `ActividadRules.HospitalError`)
- `Models/Audit.cs` (eventos de exportación, inventarios y Playbook)
- `Models/Permisos.cs` (catálogo v2, claves nuevas, `NuevasDesde`)
- `Models/Catalog.cs` (menús Gestión de Admin e Ingeniero; fuera *Histórico de tickets*)
- `Services/Actividades.cs` (columna `hospital`, migración y lectura/escritura)
- `Services/Gestion.cs` (`FechaLimiteHabilitada`, `FechaLimiteEfectiva`, hospital obligatorio,
  historial de cambios del hospital)
- `Services/Config.cs` (clave `items.fechaLimiteHabilitada`, `permisos.versionCatalogo`)
- `Services/Permisos.cs` (versión guardada y completado de matrices)
- `Program.cs` (DI y esquema de `inventario`/`playbook`, cerrojo estático de `/gestion`,
  endpoints de histórico Excel, inventarios y Playbook, `fechaLimite`/`hospitales` en
  `/api/permisos/mios`, validación del interruptor en `/api/config`, hospital en `/api/reportes`)
- `Pages/Ingenieria.cshtml` (render agrupado por sección)
- `wwwroot/js/gestion.js` (hospitales, `usaFechaLimite`, descargas/subidas, campo hospital en los
  diálogos, detalle con hospital y fecha límite condicional)
- `wwwroot/calendario/index.html` (filtro por hospital, panel *Tickets*, gating de fecha límite)
- `wwwroot/admin/config/index.html` (interruptor *Habilitar fecha límite*)
- `wwwroot/admin/reportes/index.html` (columna y filtro por hospital)
- `README.md`, `docs/ARQUITECTURA.md`, `docs/FRONTEND.md`, `docs/USUARIOS-Y-MYSQL.md`,
  `docs/MEJORAS-FUTURAS.md`, `docs/SERVICIO-WINDOWS.md`
- `Meditechnosys.Intranet.csproj` (exclusión de `App_Data\**` de la publicación)
- `publicar.bat` (aparta y devuelve `publish\App_Data` al regenerar la carpeta)

**Archivos eliminados:**
- `wwwroot/admin/historico-actividades/index.html` y `wwwroot/admin/historico-tickets/index.html`
  (sustituidos por el histórico único `/gestion/historico/`). Antes de borrarlos se verificaron
  referencias, dependencias, imports, configuraciones, uso en frontend y backend, uso en base de
  datos, compilación y despliegue: sólo los citaban la documentación y `Models/Catalog.cs`, que ya
  apuntaba a la nueva ruta. No hay tablas ni endpoints huérfanos por esta baja.
- `Pages/Shared/_ValidationScriptsPartial.cshtml` y `Pages/Shared/_Layout.cshtml.css`, residuos de
  la plantilla `dotnet new webapp`. Verificación previa (§28): ningún `.cshtml` invoca el partial
  (`grep` de `_ValidationScriptsPartial` → 0 coincidencias) y no existe la carpeta `wwwroot/lib`,
  así que los scripts jQuery a los que apuntaba no están en el proyecto; el CSS aislado genera el
  bundle `Meditechnosys.Intranet.styles.css`, pero ningún layout lo enlaza (`grep` de `styles.css` →
  0 coincidencias) y sus selectores son de Bootstrap, que aquí no se usa. Eliminación confirmada con
  el usuario y validada con `dotnet build -c Release` (0 errores).

**Base de datos:** nuevas tablas `inventario` (id, coleccion, hospital, datos JSON, creado_por,
creado, actualizado) y `playbook` (id, nombre, nombre_archivo, version, hospital, fecha_publicacion,
fecha_actualizacion, subido_por, subido, tamano, ruta, estado); nueva columna
`actividades.hospital VARCHAR(10) NOT NULL DEFAULT ''`. Migración automática con
`CREATE TABLE IF NOT EXISTS` + `ALTER TABLE ADD COLUMN`. Equivalente en JSON bajo `App_Data/`
(`inventario.json`, `playbook.json`) si MySQL no responde. Los PDF se guardan en
`Playbook:StorageDir` (por defecto `App_Data/playbook`) con nombre GUID.

**API:** nuevas rutas `GET /api/historico/excel`, `GET /api/inventario`,
`GET|POST /api/inventario/{coleccion}`, `GET /api/inventario/{coleccion}/excel`,
`PUT|DELETE /api/inventario/{coleccion}/{id}`, `GET /api/playbook/documentos`,
`GET /api/playbook/documentos/{id}/pdf`, `POST /api/playbook/documentos`,
`PUT /api/playbook/documentos/{id}`, `POST /api/playbook/documentos/{id}/archivo`,
`DELETE /api/playbook/documentos/{id}`. `POST/PUT /api/actividades` aceptan `hospital`;
`/api/permisos/mios` devuelve además `fechaLimite`, `hospitales` y `hospitalesConGeneral`;
`/api/reportes` devuelve `hospital`.

**Frontend:** cinco pantallas nuevas bajo `/gestion/`, más `js/inventario.js`; histórico, inventarios
y Playbook comparten `js/gestion.js` y `css/panel.css`.

**Backend:** la lógica de negocio de los módulos nuevos vive en `InventarioService`,
`PlaybookService` y `HistoricoExportador`; los endpoints quedan delgados y la generación de Excel se
centraliza en `Services/Excel.cs`.

**Permisos:** catálogo v2 (29 claves en 9 grupos). Ingeniero por defecto: calendario, crear los
tres tipos, actualizar estatus, registrar avance, entregar reportes, historial de cambios,
histórico de actividades, consultar y exportar inventarios y consultar el Playbook. El Ingeniero
**no** asigna tickets, **no** administra roles ni configuración, **no** configura ni modifica la
fecha límite y **no** administra documentos del Playbook.

**Configuración:** nueva clave `items.fechaLimiteHabilitada` (por defecto `true`) editable sólo por
quien tenga `configuracion.administrar` **y** `configuracion.fechaLimite`; nueva sección opcional
`Playbook:StorageDir`.

**Despliegue (corrección):** `App_Data\**` queda excluido de la publicación
(`CopyToPublishDirectory="Never"` en el `.csproj`). Antes, el globo por defecto del SDK Web incluía
los `.json` de `App_Data\` como contenido y **cada `dotnet publish` enviaba a producción el almacén
de la carpeta de desarrollo**: sobrescribía el respaldo JSON del servidor (usuarios, bitácora,
actividades, inventarios, Playbook) y desplegaba los hashes de las cuentas de prueba `admin`/`ing`.
Además `publicar.bat` borra `publish\` entero, así que ahora **aparta** `publish\App_Data` a
`..\App_Data_servidor` (fuera del árbol del proyecto, para que la propia publicación no lo vuelva a
copiar) antes de borrar y lo devuelve al terminar; si la publicación falla, avisa dónde quedaron los
datos. Verificado con un `dotnet publish` de prueba: el artefacto ya no contiene `App_Data\`.
Documentado en `docs/SERVICIO-WINDOWS.md`.

---

## 2026-10-08 — Módulo Gestión (roles, tickets, históricos, reportes, periodicidad)

**Módulo:** Gestión (nuevo) + Calendario + Administración.

**Qué cambió:**
- Se incorporó el **módulo Gestión** completo: roles y permisos editables, calendario de
  actividades/**incidentes**/**tickets**, históricos de actividades y de tickets, reportes de
  estatus con periodicidad configurable, historial de cambios por elemento y cierre formal.
- Los elementos pasaron a ser **uno solo con campo `Tipo`** (Actividad / Incidente / Ticket); los
  tickets reciben **número consecutivo**. Estados ampliados a
  `Pendiente → EnProceso → Completada → Cerrada` (+ `Cancelada`), con `Vencida` calculada.
- El **calendario** ahora muestra a la **izquierda** el *Histórico de tickets* (búsqueda y filtros
  Abiertos/Pendientes/En proceso/Vencidos/Todos) y a la derecha la rejilla mensual con chips de los
  tres tipos. **Admin e Ingeniero** pueden crear desde el calendario; lo creado por Ingeniero queda
  para revisión/asignación del Admin.
- El panel **/Admin** se reorganizó en secciones: el grupo **Gestión** contiene exactamente
  *Roles y permisos, Calendario, Histórico de actividades, Histórico de tickets, Reportes de
  estatus y Configuración*; **Gestión de usuarios** y **Bitácora** quedan **fuera** de Gestión.
- Se eliminaron las tarjetas muertas del panel (Configuración de GenDocM, Respaldos, Publicar avisos)
  y las instantáneas `.html` de la documentación (fuente única = Markdown).

**Por qué:** requerimiento de incorporar seguimiento formal de tickets/incidentes con asignación,
prioridades, fechas límite, reportes periódicos, históricos no borrables y permisos por rol,
reutilizando el diseño y los componentes existentes sin romper lo que ya funcionaba.

**Archivos creados:**
- `Models/Reporte.cs`, `Models/Cambio.cs`, `Models/Permisos.cs`
- `Services/Reportes.cs`, `Services/Historial.cs`, `Services/Permisos.cs`, `Services/Config.cs`, `Services/Gestion.cs`
- `wwwroot/js/gestion.js` (lógica compartida: identidad+permisos, diálogos genéricos, detalle con seguimiento)
- `wwwroot/admin/roles/index.html`, `wwwroot/admin/historico-actividades/index.html`,
  `wwwroot/admin/historico-tickets/index.html`, `wwwroot/admin/reportes/index.html`,
  `wwwroot/admin/config/index.html`
- `docs/REGISTRO-CAMBIOS.md` (este archivo)

**Archivos modificados:**
- `Models/Actividad.cs` (tipos, prioridades, estados, avance, fechas, número, periodicidad; propiedades calculadas `Etiqueta`, `Vencida`, `ProximoReporte`, `ReportePendiente`)
- `Models/Audit.cs` (nuevos eventos de bitácora del módulo)
- `Models/Catalog.cs` (grupo Gestión con 6 tarjetas; usuarios/bitácora fuera; se quitan tarjetas muertas)
- `Services/Actividades.cs` (esquema ampliado y normalización de elementos legacy)
- `Program.cs` (DI de los nuevos almacenes, `GestionService`, y toda la API del módulo)
- `Pages/Admin.cshtml` (render agrupado por sección)
- `wwwroot/js/intranet.js` (la búsqueda del panel oculta secciones vacías)
- `wwwroot/css/panel.css` (insignias, barra de estatus de 5 tramos, avance, pestañas, filtros, panel de tickets, matriz)
- `wwwroot/calendario/index.html` (reescrito: panel de tickets + tipos + seguimiento vía `gestion.js`)
- `README.md`, `docs/ARQUITECTURA.md`, `docs/FRONTEND.md`, `docs/USUARIOS-Y-MYSQL.md`, `docs/MEJORAS-FUTURAS.md`

**Archivos eliminados:**
- `README.html`, `docs/ARQUITECTURA.html`, `docs/SERVICIO-WINDOWS.html` (instantáneas generadas,
  desactualizadas; la fuente única es el `.md`). Verificados sin referencias de código/compilación/
  despliegue antes de borrar; se actualizaron las menciones en `README.md` y `docs/MEJORAS-FUTURAS.md`.

**Base de datos:** nuevas tablas en `meditechnosys_intranet`: `reportes_estatus`, `historial_cambios`,
`permisos`, `configuracion`; y columnas nuevas en `actividades`
(`tipo`, `fecha_limite`, `prioridad`, `avance`, `numero`, `periodicidad_dias`, `asignado`,
`ultimo_reporte`, `relacion_id`). Migración automática con `CREATE TABLE IF NOT EXISTS` +
`ALTER TABLE ADD COLUMN` por columna faltante. Equivalente en JSON bajo `App_Data/` si MySQL no responde.

**API:** nuevas rutas `/api/actividades/{id}/avance`, `/asignar`, `/observaciones`, `/historial`,
`/reportes` (GET/POST), `/api/reportes`, `/api/roles` (GET/PUT), `/api/permisos/mios`,
`/api/config` (GET/PUT). Se **eliminaron** `/api/actividades/{id}/completar` y `/cancelar`
(sustituidos por `PUT /api/actividades/{id}/estado`).

**Frontend:** nuevas páginas de panel y `js/gestion.js`; calendario reescrito; panel Admin agrupado.

**Backend:** nuevo `GestionService` concentra las reglas de negocio (permisos, transiciones de
estado, asignación, reportes, historial); los almacenes quedan como persistencia simple.

**Permisos:** nuevo catálogo de 23 permisos en 7 grupos, editable por rol en `/admin/roles/`;
matriz por defecto: Admin = todos; Ingeniero = acceder al calendario, crear los tres tipos,
actualizar estatus, registrar avance, entregar reportes y ver historial de cambios.
El Ingeniero **no** asigna tickets formalmente. El Admin siempre conserva `roles.administrar`.

**Configuración:** nueva clave `reportes.periodicidadDias` (por defecto `7`) editable en
`/admin/config/`; se aplica al asignar un elemento si no se indica periodicidad propia.

---

## 2026-10-07 — Calendario de actividades, usuarios en página propia y bitácora

**Módulo:** Calendario + Administración.

**Qué cambió:** calendario tipo Google Calendar con roles y barra de estatus; gestión de usuarios y
bitácora movidas a páginas autónomas con el formato DICOM (`panel.css`); logo IMSS en dorado.

**Por qué:** separar la administración del panel y dar seguimiento básico de actividades por usuario.

**Archivos creados:** `wwwroot/css/panel.css`, `wwwroot/admin/usuarios/index.html`,
`wwwroot/admin/bitacora/index.html`, `wwwroot/calendario/index.html`, `Models/Actividad.cs` (v1),
`Services/Actividades.cs` (v1), `docs/*`.

**Base de datos:** tabla `actividades` (v1). **API:** `/api/actividades` (v1, con `/completar` y `/cancelar`).

---

*Formato de entrada: fecha · módulo · cambios · motivo · archivos · tipo de cambio.*
