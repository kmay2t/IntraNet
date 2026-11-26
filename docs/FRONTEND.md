# Frontend (wwwroot, CSS, JS y pantallas)

Guía del lado del navegador: qué archivos hay, cómo se organizan los estilos y scripts, cómo
funcionan las páginas autónomas de Administración (usuarios, bitácora y módulo Gestión) y el
Calendario de actividades, y cómo se organizan cabeceras y marca de agua (el logo IMSS fue retirado).

---

## 1. Mapa de `wwwroot/`

```
wwwroot/
├── css/
│   ├── intranet.css        ← Estilos de las páginas Razor (Login, Index, Ingenieria, Admin).
│   ├── login.css           ← Estilos exclusivos de la pantalla de login.
│   └── panel.css           ← Formato "DICOM" compartido por las páginas autónomas de panel
│                              (usuarios, bitácora, calendario y todo el módulo Gestión) y por la
│                              única Razor que lo usa: /dicom/lista-trabajo, vía _LayoutPanel.
├── js/
│   ├── intranet.js         ← Script GLOBAL (se carga en todas las páginas desde _Layout).
│   ├── gestion.js          ← Módulo compartido del módulo Gestión (window.Gestion): identidad y
│   │                          permisos, hospitales, modales, insignias, descargas y llamadas a la
│   │                          API. Lo cargan /calendario/ y todas las páginas de /admin/… y /gestion/…
│   ├── hoja-fecha.js       ← `window.HojaFecha`: hoja compartida de periodo (Día · Mes · Año · Rango) y de hora (Una hora · Rango)
│   │                          que se instala sobre los dos `<input type="date">` de las pantallas que
│   │                          consultan fechas. Ver §5 ter.
│   ├── reportes-tabs.js    ← Barra de pestañas de **Reportes** (Bitácora · Histórico de actividades ·
│   │                          Reportes de estatus). Ver §5 quater.
│   ├── theme.js            ← Tema de la cuenta (Oscuro/Claro) y tamaño de la letra. Se carga antes que el CSS para
│   │                          pintar sin parpadeo y expone `window.MeditechnosysApariencia`. Ver §5 quinquies.
│   ├── preferencias.js     ← Diálogo ⚙, que **solo existe en el inicio**: tema, letra, tiempo de sesión
│   │                          y los parámetros del módulo Gestión. Ver §5 quinquies.
│   └── inventario.js       ← Pantalla compartida de los tres inventarios (window.Inventario),
│                              dirigida por el esquema que publica GET /api/inventario.
├── img/
│   ├── imss.png            ← (sin uso) Logo IMSS retirado; queda el archivo por si acaso.
│   └── logo-meditechnosys.png ← Marca de agua de fondo de los subsistemas.
├── admin/
│   ├── usuarios/index.html ← Gestión de usuarios (página autónoma, solo Admin).
│   ├── bitacora/index.html ← Reportes · pestaña **Bitácora**: lista de usuarios y ventana con sus
│   │                          movimientos (página autónoma, `bitacora.ver` o `quemador.ver`).
│   ├── correos/index.html  ← Bandeja de correo: soporte@ + el segundo correo enlazado (Admin e Ingeniero).
│   ├── roles/index.html    ← Matriz de roles y permisos (solo Admin).
│   ├── reportes/index.html ← Reportes de estatus recibidos, con filtro por hospital (solo Admin).
│   └── config/index.html   ← Configuración: periodicidad de reportes e interruptor de fecha
│                              límite (solo Admin).
├── gestion/
│   ├── historico/index.html          ← Histórico ÚNICO de actividades, incidentes y tickets,
│   │                                    con filtros por hospital/tipo/ingeniero/estatus y Excel
│   │                                    (Admin + Ingeniero).
│   ├── inventario-equipos/index.html ← Inventario de equipos por hospital.
│   ├── inventario-ips/index.html     ← Inventario de direcciones IP por hospital.
│   ├── inventario-aetitles/index.html← Inventario de AE Title DICOM por hospital.
│   └── playbook/index.html           ← Biblioteca de documentación técnica en PDF.
├── calendario/index.html   ← Calendario + panel izquierdo de tickets (Admin + Ingeniero).
├── dicom/index.html        ← Visor DICOM (página autónoma con <style> propio).
├── patologia/consulta/     ← Consulta de reportes (autónoma).
├── patologia/subida/       ← Carga de PDF/Excel (autónoma).
└── favicon.ico
```

> **Organización:** antes cada subsistema tenía su propia copia de `imss.png` y `logo.png`
> (5 archivos repetidos). Ahora hay **una sola copia** en `img/` y todas las páginas la
> referencian con ruta absoluta (`/img/...`). El `imss.png` quedó **sin uso** tras retirar el
> logo del IMSS (2026-10-16); la marca de agua `logo-meditechnosys.png` sí se sigue usando.
>
> Los antiguos `js/usuarios.js` y `js/bitacora.js` **ya no existen**: la gestión de usuarios y la
> bitácora se mudaron a páginas autónomas que llevan su propio `<script>` embebido (igual que el
> visor DICOM). La tabla de usuarios dejó de vivir dentro de `Pages/Admin.cshtml`.
>
> La excepción es el módulo Gestión: tiene **nueve** pantallas que comparten modales, insignias y
> filtros, así que esa lógica vive en `js/gestion.js` (y la de los inventarios en
> `js/inventario.js`) en lugar de repetirse embebida en cada una.
>
> Las antiguas `admin/historico-actividades/` y `admin/historico-tickets/` **se eliminaron**: el
> histórico es ahora único y vive en `gestion/historico/`, accesible para Admin e Ingeniero.

---

## 2. Páginas Razor vs. páginas autónomas

Hay **dos familias** de pantallas y conviene tenerlas claras:

| Familia | Tecnología | Dónde | Estilos |
|---|---|---|---|
| Intranet "core" | Razor Pages (`Pages/*.cshtml`) + layout compartido | `/`, `/Ingenieria`, `/Admin`, `/Login` | `css/intranet.css` + `css/login.css` |
| Razor con formato de panel | Razor Pages + `Pages/Shared/_LayoutPanel.cshtml` | `Pages/ListaTrabajo.cshtml` → `/dicom/lista-trabajo` | `css/panel.css` + `css/theme.css` + `css/lista-trabajo.css` |
| Subsistemas y paneles | HTML + CSS + JS **autónomos** (un solo archivo cada uno) | `/dicom/`, `/patologia/consulta/`, `/patologia/subida/`, `/admin/usuarios/`, `/admin/bitacora/`, `/admin/correos/`, `/admin/roles/`, `/admin/reportes/`, `/admin/config/`, `/gestion/historico/`, `/gestion/inventario-equipos/`, `/gestion/inventario-ips/`, `/gestion/inventario-aetitles/`, `/gestion/playbook/`, `/calendario/` | `css/panel.css` compartido (paneles) o `<style>` embebido (dicom/patología) |

Las autónomas se sirven como archivos estáticos pero **protegidas**: `Program.cs` exige sesión y
rol para cada prefijo — Admin/Ingeniero en `/patologia/*`, `/dicom/*`, `/calendario/*` y
`/gestion/*`, **solo Admin** en `/admin/*` — y las sirve **sin caché** para que los
cambios de diseño se vean al recargar.

Las páginas de panel comparten el **formato del repositorio DICOM** mediante `css/panel.css`:
fondo blanco con marca de agua, cabecera negra fija con wordmark
"Meditechnosys" centrado, botones `Regresar`/`Home`, tarjetas `.card` y botones azules
`.btn-primary`. Su `<script>` va embebido al final del `<body>`; las del módulo Gestión cargan
además `js/gestion.js` para reutilizar modales e insignias. `/dicom/lista-trabajo` es la única
página Razor con este formato: en vez de `css/intranet.css` enlaza `panel.css` y luego `theme.css`
(el orden importa: así la paleta Día/Noche de `theme.css` manda sobre la de `panel.css`, igual que en las autónomas).

El layout compartido (`Pages/Shared/_Layout.cshtml`) aporta cabecera, pie, canvas de fondo y
carga `js/intranet.js`. Al final del `<body>` renderiza la sección opcional `Scripts`; la página
Admin ya **no la usa** (antes inyectaba `usuarios.js` y `bitacora.js`): ahora solo es un punto de
extensión para futuras páginas Razor.

`Pages/Shared/_LayoutPanel.cshtml` es su hermana con formato de panel (`body.theme-panel`,
`header.cab-panel` con título, `badge` de usuario y rol, `logo-centro` y `.botones-nav`) y la usa
solo `/dicom/lista-trabajo`. Sus mandos son Regresar, Home y Salir: la ventana ⚙ Configuración
**solo se pinta en el inicio**, así que aquí no hay botón, ni diálogo, ni `js/preferencias.js`.
El formato de panel sigue recibiendo el tema de la cuenta por la cookie `meditechnosys.tema`, que
`theme.js` pinta igual que en el resto de pantallas. Renderiza las secciones `Styles` y `Scripts`,
que es por donde `lista-trabajo.css` y `lista-trabajo.js` entran después de las dos hojas base.
El diálogo ⚙ está escrito una sola vez en `Pages/Shared/_PrefDialog.cshtml` y lo incluye
`_Layout.cshtml`, pero solo cuando la petición es el inicio (`/` o `/Index`).

`Pages/Login.cshtml` (`Layout = null`, estilos en `css/login.css` con los ajustes de tema en
`theme.css`) tiene un botón de ojo dentro del campo de contraseña (`#btnVerPwd`) que alterna
`type="password"` / `type="text"`. La preferencia se guarda en `localStorage` con la clave
`meditechnosys.login.verPassword` y se reaplica al cargar la página: es una decisión de cada
persona y de cada equipo, no del servidor. El `<input name="Password">` y el `POST` no cambian.

---

## 3. Variables de color (tema)

`css/intranet.css` define el tema oscuro de la intranet en `:root`:

| Variable | Valor | Uso |
|---|---|---|
| `--navy-900/800/700` | `#060d1f` / `#0a1630` / `#101f42` | Fondos y paneles. |
| `--navy-line` | `#1c2f5c` | Bordes y rejillas. |
| `--gold` | `#d4a838` | Acento principal (dorado). |
| `--gold-soft` | `#e8c76a` | Acento claro (hover, títulos). |
| `--ink` / `--ink-dim` | `#e9edf6` / `#96a3c0` | Texto y texto secundario. |
| `--ok` / `--warn` | `#4fc38a` / `#e8a13a` | Estados correcto / aviso. |

Los subsistemas (dicom, patologia) tienen su **propio** `:root` dentro de su `<style>`, con su
propio acento dorado (`--dorado: #c9a227`). Si quieres unificar el tono dorado en todo el sistema,
ese es el punto a tocar (ver `docs/MEJORAS-FUTURAS.md`).

---

## 4. El logo IMSS (retirado)

El **logo del IMSS ya no se muestra** en ninguna pantalla de la intranet (se retiró por solicitud
el 2026-10-16). Antes se teñía de dorado con la técnica de *máscara*: el archivo `img/imss.png`
(silueta negra con fondo transparente) se usaba como `mask` sobre un fondo `var(--dorado)`, y en el
marcado era un `<span class="logo" role="img" aria-label="IMSS"></span>`.

Hoy **no queda ninguna referencia** a `imss.png` ni al `<span class="logo">` del IMSS en
`wwwroot`: la regla `header .logo` se eliminó de `css/panel.css` y de los `<style>` inline de
`dicom/index.html` y `patologia/consulta/index.html`, y el `<span>` se quitó de todas las cabeceras.
Las cabeceras conservan el título a la izquierda, el wordmark centrado (`.logo-centro`) y los
botones a la derecha; al ser flexbox, quitar el logo no descuadra nada.

> La marca de agua de fondo (`.fondo-logo`) sigue siendo `logo-meditechnosys.png` con
> `mix-blend-mode: multiply`; no se tiñe porque ya es clara y va de fondo. Es de Meditechnosys,
> no del IMSS, por lo que se conserva.

---

## 5. `js/intranet.js` (global)

Cinco bloques independientes; **cada uno consulta primero su elemento** y sale si no existe, para
que las páginas que no lo tienen no rompan el script:

1. **Reloj y saludo** → `#clock` (cabecera) y `#saludo` (hero de Ingeniería).
2. **Búsqueda de accesos** → `#q` filtra las tarjetas `.app` por texto y `data-k`; `#empty` avisa
   si no hay coincidencias, y los encabezados de grupo (`.grupo`) se ocultan cuando todas sus
   tarjetas quedaron fuera del filtro. Atajo de teclado `/` para enfocar. La comparación pasa por
   `window.Filtro` (`js/filtro.js`), así que «CRANEO» encuentra «CRÁNEO».
3. **Malla de circuitos** → dibuja el fondo en el canvas `#mesh` (determinista, semilla fija).
4. **Estado de sistemas** → rellena `#status` desde `GET /api/status` y lo refresca cada 60 s.
   Además del almacén de usuarios (`Usuarios (almacén)`), ese endpoint hace **sondeos reales**
   (`Services/Salud.cs`): ping a MySQL (*Base de datos*), socket TCP al primer nodo PACS
   (*Visor de estudios*) y existencia de la carpeta del repositorio (*Consulta de reportes*).
   El bloque "Copiar correo / Soporte TI" se retiró (2026-10-19) porque no se usaba.

---

## 5 bis. `js/filtro.js` (búsqueda sin acentos, global)

`wwwroot/js/filtro.js` define `window.Filtro` con cuatro funciones —`norm`, `contiene`, `empieza` y
`coincideAlguno`— y es **la única regla de búsqueda en el navegador**: «MUNOZ» encuentra a «MUÑOZ» y
«tomograf» a «TOMOGRAFÍA» en el panel, los tres inventarios, histórico, playbook, calendario, reportes
y el filtro de resultados DICOM. `norm` quita las marcas combinantes (NFD), cambia Ñ→N y Ç→C, pasa a
minúsculas y colapsa espacios.

Se carga antes que cualquier otro script de página: en las Razor desde `_Layout.cshtml` y en las
autónomas justo después de `js/theme.js`. Cada página define su alias con respaldo

```js
var Busca = window.Filtro || { norm: function (x) { return String(x == null ? '' : x).toLowerCase(); } };
```

de modo que si el archivo no llega a cargar la búsqueda sigue funcionando, solo vuelve a distinguir la
Ñ. En el servidor la regla equivalente es `Services/Texto.cs`; ambas deben moverse juntas si cambia el
comportamiento.

---

## 5 ter. `js/hoja-fecha.js` + `css/hoja-fecha.css` (las hojas de periodo y de hora compartidas)

Es la hoja que se construyó para la Bitácora (un solo cuadro que abre una rejilla con **Día · Mes · Año ·
Rango**) convertida en componente suelto, para que la usen **las seis pantallas que consultan por fechas**:
`/admin/reportes/`, `/gestion/historico/`, `/patologia/consulta/`, `/dicom/`, `/dicom/enviar/` y
`/dicom/lista-trabajo` (el periodo del C‑FIND de la lista de trabajo). Esta última lleva además la **caja de
hora** del mismo formato (`instalarHora`, más abajo).

El rótulo que escribe la librería es `<span class="hf-titulo">`, no un `<label>`, así que cada pantalla lo
arregla si quiere. En `/dicom/lista-trabajo` `lista-trabajo.css` lo pone en versalitas de 11 px
(`.lista-trabajo .hf-titulo`) para que «Periodo» y «Hora» hagan juego con las demás etiquetas del formato de
panel. El selector está acotado a la pantalla: la Bitácora, que usa la misma librería, conserva su aspecto.

**Cómo se instala.** Cada página sigue teniendo sus dos `<input type="date">` y su filtrado de siempre; solo
se añade el CSS en el `<head>` (después de `theme.css`), el script antes de su `<script>` y una línea:

```js
if (window.HojaFecha) {
  HojaFecha.instalar({ desde: 'fDesde', hasta: 'fHasta', titulo: 'Periodo', cualquier: 'Cualquier día' });
}
```

`instalar` es idempotente por par de campos y devuelve la instancia; `HojaFecha.sincronizar(campo)` fuerza el
repintado si alguna página reconstruye el DOM, y `HojaFecha.instancias` las lista.

**Quién es el dueño del valor.** El campo nativo **no se borra ni se sustituye**: se tapa
(`.hf-origen{display:none!important}` sobre su `<label>` o envoltorio) y sigue siendo donde vive el valor en
`yyyy-mm-dd`, con los **dos extremos incluidos** —exactamente el contrato que ya leían los filtros y los
`/api/...?desde=&hasta=`—. La hoja escribe con el `set` nativo de `HTMLInputElement.prototype.value` y
dispara **un solo `change`, en el campo inicial**: las cinco páginas leen los dos valores dentro del mismo
manejador, así que avisar también al final provocaría dos consultas idénticas por periodo (en patología,
`buscar(1)` dos veces).

**Cómo se entera de lo que escribe la página.** Los botones propios de cada pantalla («Hoy», «Últimos 7 días»,
«Limpiar», «Limpiar filtros», quitar la casilla de rango, `form.reset()`) escriben en el campo sin pasar por
la hoja. Para que el cuadro no se quede mentiroso se vigilan tres caminos: un interceptor sobre `.value` de
los dos campos (`Object.defineProperty`, con el `set` nativo por dentro), los eventos `change`/`input`, y el
evento `reset` del `<form>` (con `setTimeout 0`, porque el navegador limpia después de disparar el evento).
Si el navegador no permite el interceptor, la hoja sigue funcionando: solo queda sin repintar los casos que
escriben el valor en silencio.

**Qué dice el rótulo.** `modoDe(desde, hasta)` deduce el modo de lo que haya en los campos, para que un mes
completo se lea «noviembre de 2026» y no «01/11/2026 – 30/11/2026»; un año se lee «2026» y un día suelto
«04/11/2026». Con `×` se quita el filtro y el cuadro vuelve a «Cualquier día».

**Diferencias con la hoja de la Bitácora (deliberadas).** Aquí **no hay índice de días**: esas pantallas no
consultan un historial propio, así que todos los días están habilitados, las flechas ‹ › nunca se apagan y no
existe el pie «Todo» (que en la Bitácora significa «del primer día al último con movimientos»). El bloque de
años es fijo de doce en doce (`2016 – 2027`) porque no se sabe qué años existen. La Bitácora **conserva su
copia dentro de su página** (`#vCal`, `pintarCal()`, `fijarPeriodo()`), tal como se mandó; por eso el
componente se escribió con prefijo `hf-` en clases y variables CSS y no comparte una sola regla con ella.

**Colocación y cierre.** La hoja es `position:fixed` hija de `document.body` (ningún `overflow` de tarjeta o
fila de filtros la recorta, y en patología la fila es `flex-wrap`), se ancla 8 px debajo de su caja, se voltea
hacia arriba si no cabe, se recorta a 8 px de los bordes, y se persigue con `resize`/`scroll`. Cierra con
clic fuera (`pointerdown` en fase de captura), `Esc`, o al elegir el periodo; si hay dos instancias en la
misma página, abrir una cierra la otra.

**Cuidado con los nombres de clase.** Las páginas de la intranet dan estilo a etiquetas sueltas
(`.vacio { padding: 28px 12px }` en patología, para su aviso de «sin resultados»). El primer estado del cuadro
se llamaba `vacio` y heredó ese `padding`, con lo que el botón medía 89 px de alto. De ahí que **todas** las
clases de estado vayan prefijadas (`hf-vacio`, `hf-puesto`, `hf-sel`, `hf-hoy`, `hf-enrango`,
`hf-pendiente`, `hf-oculto`) y que el CSS empiece con un `reset` de `span` dentro de `.hf-campo`/`.hf-hoja`.
Lo mismo con el ancho: varias filas tienen `… > div { flex: 1 }`, que estiraba el cuadro a 930 px en DICOM,
así que `.hf-campo.hf-campo { flex: 0 0 auto }` (doble clase para ganar en especificidad) lo deja medir lo
que mide su rótulo, con un piso de 172 px.

**La hora: `HojaFecha.instalarHora` (la hoja de **Una hora · Rango**).** Es la misma hoja que la Bitácora usa
para el horario, compartida desde el cambio del 2026‑11‑06:
`HojaFecha.instalarHora({ desde: 'lt-f-hora-desde', hasta: 'lt-f-hora-hasta', titulo: 'Hora',
cualquier: 'Todas las horas', conteos: horasRecibidas })`. Monta una caja con icono de reloj y una hoja de 24
celdas en rejilla de cuatro columnas, con los modos **Una hora** y **Rango**, los atajos **Todo el día /
Mañana / Tarde**, el botón **Cualquier hora** y la `×` para quitar el filtro. Escribe `'HH:00'` en el campo
inicial y `'HH:59'` en el final —la hora entera incluida— y dispara **un solo `change`** en el inicial, igual
que la de fechas; los campos siguen siendo `<input type="text">` ocultos dueños del valor, así que el
filtrado por ventana de hora de cada pantalla no cambió. `conteos` es opcional: si la página devuelve
`{'09': 3, …}` cada celda explica en su título cuántas entradas caen en esa hora y el pie dice cuántas horas
tienen entradas. **No añade ninguna regla de CSS**: reutiliza `hf-caja`, `hf-modos`, `hf-dias.cuatro` y
`hf-pies`. Por ahora solo la usa `/dicom/lista-trabajo`; la Bitácora sigue con su copia dentro de su página.

---

## 5 quater. `js/reportes-tabs.js` (la barra de pestañas de Reportes)

La tarjeta **Reportes** del inicio reúne las tres consultas de histórico —**Bitácora**, **Histórico de
actividades** y **Reportes de estatus**—. Las tres páginas ponen la misma barra debajo de la cabecera:

```html
<nav class="reportes-tabs" data-reportes-activo="bitacora" aria-label="Apartados de Reportes">
  <a class="reportes-tab" href="/admin/bitacora/index.html" aria-current="page">Bitácora</a>
</nav>
```

**Por qué son enlaces y no paneles ocultos.** Cada apartado conserva **su ruta, su `<script>` y su guard del
servidor**; meter las tres páginas en un solo documento obligaría a mezclar identificadores (`#fDesde`,
`#error`, `#cuerpo`…), ventanas `<dialog>` y peticiones, y a reimplementar la autorización en el navegador.
Con enlaces: el botón atrás/adelante funciona, se puede abrir en otra pestaña, el `Esc` de la Bitácora sigue
saliendo de la ventana y no hay código propio de navegación que mantener. El HTML trae ya **su propia pestaña
activa** (marcada con `aria-current="page"`) para que la barra se lea aunque el JS no cargue; el script la
reconstruye con las pestañas que el usuario realmente puede abrir.

**Qué hace `reportes-tabs.js`.** Lee el `data-reportes-activo` de la página, pide `GET /api/permisos/mios`
(`credentials: 'same-origin'`, `cache: 'no-store'`) y pinta un enlace por apartado cuyo permiso tenga:
Bitácora con `bitacora.ver` **o** `quemador.ver`, Histórico con `historico.actividades`, Estatus con
`reportes.consultar`. Nada más: no llama a ninguna API de datos, no toca los filtros de la página y no guarda
nada en el navegador. La pestaña del apartado abierto se marca con `aria-current="page"` (no con `role="tab"`,
que sería mentira: no hay paneles dentro del mismo documento).

**Si `/api/permisos/mios` falla** (sin sesión, servidor caído, respuesta rara), la barra **no inventa enlaces**:
deja la pestaña actual —que ya pasó el guard del servidor— y muestra «No se pudieron cargar los otros
apartados» con un botón **Reintentar**. Nunca se muestran enlaces que no se pudieron autorizar.

**La entrada de la tarjeta no regala permisos.** `Catalog.Visibles` (`Models/Catalog.cs`) decide el destino con
el que se abre **Reportes**: si el usuario tiene `bitacora.ver`/`quemador.ver`, entra en la Bitácora; si no pero
tiene `historico.actividades`, entra en el Histórico; si no, en Reportes de estatus. Los guards de `Program.cs`
y las APIs siguen exactamente igual, así que un usuario con un solo permiso **nunca** ve ni carga los datos de
los otros apartados.

**CSS.** Todo va con prefijo `.reportes-tabs` / `.reportes-tab` dentro de `panel.css`, para no rozarse con las
pestañas `.tabs`/`.on` de los inventarios ni con los `<label>` de los filtros. En pantallas de menos de 600 px
las pestañas ocupan una fila cada una.

---

## 5 quinquies. `js/theme.js` + `js/preferencias.js` (el tema de la cuenta y la ventana ⚙ del inicio)

La tarjeta **Configuración** del catálogo se retiró. En su lugar, **solo la cabecera del inicio** pone
—junto a *Salir*— el botón **⚙ Configuración**, y el `<dialog id="pref-dialog">` se renderiza **en el servidor**
(con sus cuatro `<fieldset>` ya escritos)
para que la ventana se vea completa aunque el JS no cargue. El diálogo está escrito una sola vez en
`Pages/Shared/_PrefDialog.cshtml`, y `_Layout.cshtml` lo pinta únicamente cuando la ruta pedida es el inicio
(`/` o `/Index`): fuera de ahí tampoco se carga `js/preferencias.js`, y `_LayoutPanel.cshtml` —el formato de
panel de `/dicom/lista-trabajo`— ya no lo incluye.
**El interruptor Día/Noche de la cabecera (`#theme-toggle`) ya no existe**: se quitó de las dos cabeceras,
de `theme.js` (que lo creaba donde no hubiera) y de su CSS (`theme.css` / `intranet.css`). El tema se escoge
ahora en el bloque «Tema» del ⚙, con dos opciones —**Oscuro** y **Claro**—, y **Oscuro** es como abre por
defecto.

**Reparto de los cuatro bloques** — cada valor tiene un solo dueño, y eso decide dónde se guarda:

| Bloque | Dueño | Cómo se lee / se escribe |
|---|---|---|
| Tema (**Oscuro** / **Claro**, en el diálogo) | la cuenta | `GET`/`PUT /api/tema` → clave `tema.<usuario>` del almacén `IConfigStore`; su copia en la cookie `meditechnosys.tema` (no `HttpOnly`) es la que `theme.js` lee antes del pintado y pone en `data-theme` de `<html>`. |
| Tamaño de la letra | este equipo | `localStorage['meditechnosys.fuente']` → `data-fuente` en `<html>` (`chica` 88%, `normal`, `grande` 118%). |
| Tiempo de sesión | la cuenta | `GET`/`PUT /api/sesion` → clave `sesion.minutos.<usuario>` del almacén `IConfigStore`. |
| Parámetros del módulo | configuración común | `GET`/`PUT /api/config`, solo con `configuracion.administrar`. |

**`theme.js`** pinta y expone `window.MeditechnosysApariencia` (`tema()`, `cambiarTema(v)`, `fuente()`,
`cambiarFuente(v)`). Ya no arma ni busca interruptor alguno: lee la cookie `meditechnosys.tema` —si no
existe, o si trae un valor que ya no es opción, pinta **Oscuro**— y aplica `data-theme` antes del primer
pintado. Borra el viejo `localStorage['meditechnosys.theme']` para que un recuerdo de otra máquina no gane
sobre la cuenta. Escucha `pageshow` (bfcache) y `storage`, este último solo para la letra, y avisa a
`intranet.js` con el evento `meditechnosys:themechange` para que rehaga el fondo de líneas.

**La cookie es una copia, no el dato**: el valor manda en la cuenta (`tema.<usuario>`), y la copia existe
nada más que para que el navegador sepa qué pintar antes de llamar a ninguna API —los paneles autónomos de
`wwwroot` son HTML suelto y no pueden esperar una respuesta—. Por eso se escribe en el acceso
(`Pages/Login.cshtml.cs` y `/api/login`), se reescribe en `PUT /api/tema`, el middleware de `Program.cs`
(justo después de `UseAuthorization`) la pone al día con lo que dice la cuenta en cada petición autenticada, se
borra en la salida (`Pages/Logout.cshtml.cs` y `/api/logout`) y en cualquier petición sin sesión. Como
`UseStaticFiles` va antes que ese middleware, un panel autónomo recién abierto puede pintar con la copia
vieja un instante; la reconcilia y repinta en cuanto se abre el ⚙ **en el inicio**, que es el único sitio
donde hoy existe esa ventana.

**`preferencias.js`** solo sabe de diálogo: abrir/cerrar (⚙, la X, `Esc`, clic en el oscuro, y el enlace directo
`/?configuracion=1` o `#configuracion`, que limpia la URL con `history.replaceState`; fuera del inicio el enlace
no encuentra la ventana y simplemente no hace nada), pintar lo que ya está
elegido y mandar cada cambio a su dueño. **No inventa valores**: el tema llega del servidor
(`{tema, opciones[], porDefecto}`) y el tiempo de sesión también (`{minutos, opciones[]}`), así que las opciones
que se muestran son las que el backend acepta; si una API falla,
el bloque lo dice con un botón **Reintentar** y, en el caso del tiempo de sesión, **regresa el radio a lo que
realmente está guardado**. Los `fetch` van con `credentials: 'same-origin'` y `Accept: application/json`.

**Mientras se guarda un tema, ninguna lectura lo pisa** (`guardandoTema`): el `GET /api/tema` que confirma el
marcado al abrir el ⚙ puede contestar después de que la persona ya hizo clic, y su respuesta trae el valor
*anterior*. Si se pintara tal cual, desharía el clic —devolvería la pantalla al tema viejo justo cuando el `PUT`
iba camino al servidor—. Con un `PUT` en vuelo esa respuesta se descarta; lo que manda es la elección.

**El tamaño de la letra es `rem`, no píxeles.** `theme.css` pone `html:root[data-fuente=…] { font-size: 88% | 118% }`
y `intranet.css` cambió su `body { font-size: 15px }` por `.9375rem`, de modo que el texto de todas las páginas
escala con la preferencia. El diálogo y los botones se siguen midiendo en píxeles (`css/intranet.css`, prefijo
`.pref-`) para que agrandar la letra no infle la propia ventana de configuración.

**El tiempo de sesión se mide en el servidor, no en la caducidad de la cookie.** `Program.cs` arranca con 8 h y
`SlidingExpiration`; la preferencia se aplica en `OnValidatePrincipal` como **ventana de inactividad** sobre
`IssuedUtc`: al superar la mitad de la ventana se reescribe la marca de actividad (`ctx.ShouldRenew`) y la
caducidad absoluta que se escribe es `SesionPreferencias.VidaCookie` (8 h + 10 min) —**nunca la ventana elegida**—,
en las tres escrituras de la cookie: el acceso (`Pages/Login.cshtml.cs` y `POST /api/login`), el cambio de tiempo
(`PUT /api/sesion`) y el renuevo automático. Si la cookie caducara en el mismo instante en que se cumple la
ventana, el framework mataría el ticket *antes* de entrar a `OnValidatePrincipal`: la sesión se cerraría igual,
pero sin el marcador, y la persona llegaría al acceso sin saber por qué la salieron. Cumplida la ventana, la
sesión se rechaza y la redirección sale con `sesion=expirada`, que es lo que pinta el recuadro azul de
`Pages/Login.cshtml`. La lista de opciones llega hasta 480 minutos, y ninguna puede prometer más que
`VidaCookie`.

---

## 6. `/admin/usuarios/` (página autónoma, solo Admin)

`wwwroot/admin/usuarios/index.html` — la gestión de usuarios que antes vivía dentro de
`Pages/Admin.cshtml` ahora es una página aparte con formato DICOM. El botón *Gestión de usuarios*
del catálogo Admin navega directamente a ella. Puntos clave del `<script>` embebido:

- **Fuente de verdad:** `GET /api/usuarios`. Pinta la tabla al cargar y después de cada alta,
  edición o baja, para que nunca quede desincronizada.
- **Cabecera de estado:** la línea del almacén y el aviso amarillo "MySQL no disponible" se
  rellenan desde `GET /api/status`; la insignia superior, desde `GET /api/patologia/me`.
- **Modales nativos:** usa `<dialog>` (`showModal()` / `close()`), con estilos en `panel.css`
  (`dialog.modal`). Ventajas: fondo oscurecido (`::backdrop`), cierre con `Esc`, foco atrapado y
  cero dependencias.
- **Crear:** `#dlgCrear` → `POST /api/usuarios` con `{ usuario, password, rol }`. La contraseña pide
  **3 caracteres como mínimo** (`minlength="3"` en el formulario y `UserRules.PasswordMin` en el
  servidor; el mensaje de error se arma con esa constante, así que no hay cifras duplicadas).
- **Permisos:** `#dlgPermisos` (`dialog.modal.permisos`, ancho hasta 920 px) pinta con
  `pintarPermisos()` una **cuadrícula CSS**, no una tabla: `.perm-cab` marca las columnas
  *Permiso / Rol / Ajuste / Efectivo* y cada `.perm-fila` lleva `clave` + descripción en la columna
  elástica y los tres controles en columnas de ancho fijo. No hay `min-width` interior ni
  `overflow-x`, así que **nunca aparece scroll horizontal** ni nombres recortados; por debajo de
  760 px el encabezado se oculta y cada control muestra su propia etiqueta (`.perm-etq`). El
  `<select class="perm-select">` (heredado / conceder / revocar) recalcula el chip Efectivo con
  `efectivoDe()`, que replica al servidor: Admin siempre conserva `roles.administrar` (el `select`
  sale deshabilitado). Guardar envía `PUT /api/usuarios/{u}/permisos` con **solo** los ajustes
  distintos de «heredado».
- **Editar:** `#dlgEditar` → envía `PUT .../rol` **solo si el rol cambió**, `PUT .../password`
  **solo si se escribió una contraseña nueva** y `PUT .../activo` **solo si la casilla cambió**
  (campos independientes; el botón "Guardar cambios" aplica todo lo modificado).
- **Desactivar/Activar:** botón por fila que hace `PUT /api/usuarios/{u}/activo` sin abrir modal;
  el estado se refleja en la columna *Estado* (`Activo` / `Inactivo`).
- **Eliminar:** confirma con `confirm()` y hace `DELETE /api/usuarios/{u}`.
- **Errores:** la API devuelve `{ error }`; el mensaje se muestra **dentro del modal**
  (`#errCrear` / `#errEditar`) y el modal no se cierra hasta que la operación tiene éxito.
- Los clics de Editar/Desactivar/Eliminar se resuelven por **delegación** en el `<tbody>`, así
  funcionan también para las filas que se agregan al refrescar.

---

## 6b. `/admin/bitacora/` (página autónoma — lista de usuarios y su ventana de movimientos)

`wwwroot/admin/bitacora/index.html` — **la única bitácora**. Desde el 2026-11-03 (tercer cambio de la
noche) la página **ya no trae la tabla general de movimientos**: es solo la tarjeta de usuarios, y los
renglones se ven **dentro de la ventana** que abre al hacer clic en una persona. Fuente de la ventana:
`GET /api/bitacora/unificada?usuario=&dia=&desde=&hasta=&limite=`, que devuelve `{ puedeAccesos, puedeQuemador,
limite, cortado, actividades[], movimientos[] }` con lo de la intranet (`bitacora`) y lo del programa de
grabación DICOM (`bitacora_quemador`) ya mezclado por hora y traducido por `Models/BitacoraUnificada.cs`.

- **Tarjeta de usuarios (`#seccionUsuarios`, única `<section>` de `main`):** quiénes están conectados,
  su última conexión y desde dónde (`GET /api/bitacora/resumen`). Cada renglón (`tr.fila-usuario`) llama
  a `abrirUsuario(usuario)`, marca la fila en dorado (`.activa`) y abre su ventana. Debajo queda el aviso
  de `GET /api/bitacora/ubicacion-estado` (por qué una ubicación sale «Sin confirmar»).
  **Quien solo tiene `quemador.ver`** recibe 403 en `/resumen`: `cargarResumen()` cae a su `catch` y arma
  la misma lista con `GET /api/bitacora/unificada?limite=2000`, agrupando por usuario y tomando el
  renglón más nuevo para el equipo/IP/ubicación; los rótulos cambian a **Registros** y **Último
  movimiento** (`thEstado` / `thUltimo`) para no prometer conexión de lo que no puede ver. Sin ninguno de
  los dos permisos la página manda a Acceso Denegado (302).
- **Ventana flotante (`dialog#ventanaUsuario`, clase `ventana-bitacora`):** `<dialog>` nativo abierto con
  `showModal()`. Pide por su cuenta `?usuario=X&limite=2000`, así que su historial es completo
  independientemente de lo que la tarjeta tenga a la vista; en cuanto se elige un periodo `cargarVentana()`
  lo vuelve a pedir **ya recortado** (`&desde=&hasta=`), de modo que el tope de 2.000 renglones cuenta
  dentro del periodo y no en toda la historia. Trae título (`Movimientos de X`), subtítulo,
  su barra de filtros (`vDiaBtn` con los campos ocultos `vDia`, `vDiaHasta` y `vModo`; `vHoraBtn` con
  `vHora` y `vHoraHasta`; `vActividad`, `vFuente`, `vTexto`), sus contadores
  (`#ventanaResumen`), la tabla de siete columnas con cabecera fija (`.ventana-tabla` rueda internamente,
  `max-height: 52vh`) y los botones **Limpiar filtros**, **Descargar su Excel** y **Cerrar**. Sus filtros
  y su lote (`_ventanaLote`) son independientes de la página. Se cierra con el botón, con `Esc` o pulsando
  el fondo atenuado; las tres rutas llaman a `cerrarVentana()`, que aborta la petición en vuelo
  (`_ventanaPeticion`) y desmarca la fila —sin eso la fila se quedaba marcada al cerrar—. Cada renglón lo
  pinta `filaDe(m)` → `celdaActividad` / `celdaIp` / `celdaLugar` / `celdaQue`, una sola copia para toda
  la página. En un navegador sin `showModal` la tarjeta avisa que se necesita uno actual: ya no hay hoja
  donde caer.
- **Rejilla de la ventana:** columnas **Fecha · Hora · Usuario · Actividad · IP · Equipo · Ubicación
  geográfica · Qué fue lo que hizo**. En *Actividad* el chip de resultado solo aparece cuando algo no
  salió redondo (`error`, `parcial`, `cancelado`), y debajo se lee de qué bitácora salió el renglón («de la
  intranet» / «del programa del quemador»). La IP lleva abajo el nombre del equipo y su `local …`; la
  ubicación lleva abajo de dónde salió (posición real del equipo, punto aprendido del rango, dirección
  escrita o «Sin confirmar») y su punto del mapa queda en el `title`. *Qué fue lo que hizo* escribe el
  módulo, la frase larga, el enlace `lote …` y el UID del estudio.
- **Filtros:** el **periodo** se corta dos veces —lo recorta el servidor con `desde`/`hasta` (hora local
  convertida a un rango UTC `[inclusive, exclusive)` por `RangoDePeriodo()`, que vive en los dos almacenes que
  consultan: `Services/Bitacora.cs` y `Services/BitacoraQuemador.cs`) y lo vuelve a comprobar
  `filtradasVentana()` sobre la lista ya traída, para que no se cuele un renglón de más—. **Hora**,
  actividad, bitácora de origen, texto y lote se aplican en el navegador, con 180 ms de espera en la caja de
  texto; el día se compara con `diaDe(m)` (`dd/mm/aaaa` → `yyyy-mm-dd`) y la hora con `horaDe(m)`, siempre
  con los dos extremos incluidos. El desplegable de actividad se llena con las etiquetas que **sí** existen
  en los renglones de esa persona (`rellenarVentana()`), para no ofrecer nunca una opción vacía.
- **Hoja de periodo (`#vCal`, elegida en vez del calendario del sistema):** el `<input type="date">` nativo
  lo pinta Windows —fuera de la paleta, sin saber qué días tienen registros y imposible de estilizar—, así
  que `#vDia` quedó oculto (`tabindex="-1"`, `aria-hidden`) y la caja visible es `button#vDiaBtn`. Arriba
  trae sus cuatro chips (`#vCalModos`: **Día · Mes · Año · Rango**) y `pintarCal()` redibuja la rejilla según
  el modo, siempre sobre `mapaDias()` —que ahora sale de `GET /api/bitacora/periodos` (`_indiceDias`:
  cuántos renglones dejó esa persona en **cada día de toda su historia**, no solo en los 2.000 que llegaron
  a pantalla)—:
  - **Día** (`pintarDias`): 7 columnas empezando en **lunes**, días con historia en dorado (`libre`, con el
    número en el `title`) y los demás `disabled`; punto dorado bajo el de hoy (`hoy`) y marco lleno en el
    elegido (`sel`). `hayEnMes()` apaga ‹ › cuando el mes vecino no tiene un solo renglón y `abrirCal()`
    arranca en el mes del día elegido o, si no hay, **en el mes más reciente con datos**. Pie: **Hoy**,
    **Ayer**, **Cualquier día**; nota «N día(s) con movimientos» / «Ningún movimiento en este mes».
  - **Mes** (`pintarMeses`): los doce meses del año a la vista, apagados los que no dejaron renglones; las
    flechas cambian de año y se apagan si ningún mes de ese año tiene historia; se oculta la fila de días de
    la semana. Pie: **Este mes**, **Mes pasado**, **Cualquier mes**.
  - **Año** (`pintarAnios`): un botón por año con movimientos, en orden, sin cabecera ni fila de días; si el
    índice viene vacío muestra un `—` apagado y «Ningún año con movimientos». Pie: **Este año**,
    **Cualquier año**.
  - **Rango** (`pulsarCal`): dos clics —el primero queda en `_calPendiente` con la nota «Elija el día final
    (después del …)» y el segundo escribe el periodo; si se pulsaron al revés, se acomoda solo—; las flechas
    **nunca** se apagan, porque los extremos pueden caer en meses sin historia. Los días del tramo salen con
    `enrango` y los dos extremos con `sel`. Pie: **7 días**, **30 días**, **Todo** (del primer día al último
    del índice) y **Cualquier día**.
  `fijarPeriodo(modo, desde, hasta)` escribe `#vModo`/`#vDia`/`#vDiaHasta`, cierra la hoja y llama a
  `sincronizarPeriodo()` (rótulo, clase `puesto`/`vacio` y `×` visible), `pintarVentana()` y
  `cargarVentana()`; la `×` (`vDiaQuitar`) y los atajos del pie pasan por el mismo camino.
- **Hoja de hora (`#vHoraCal`):** el mismo mecanismo, con dos chips (`#vHoraModos`: **Una hora · Rango**)
  sobre una rejilla de 24 celdas `HH:00` (`pintarHoras()`). `mapaHoras()` cuenta sobre los renglones ya
  cargados, así que las horas disponibles obedecen al periodo elegido: en **Una hora** quedan `disabled` las
  horas sin movimientos y en **Rango** están vivas las 24, para poder escribir cualquier tramo (`pulsarHora`
  hace los dos clics y los acomoda). Pie: **Todo el día** (00–23), **Mañana** (06:00–12:59), **Tarde**
  (13:00–19:59) y **Cualquier hora**; `fijarHora(desde, hasta)` escribe `#vHora`/`#vHoraHasta` y la `×`
  (`vHoraQuitar`) lo limpia.
- **Las dos hojas conviven bien con la ventana:** cada una es `position: fixed` **hija del `<dialog>`** —sigue
  en la capa superior y no la recorta el `overflow-y: auto` de `.modal-form—; `posicionarHoja()` la ancla bajo
  su caja, la voltea hacia arriba si no cabe y la mantiene dentro de la ventana gráfica (`reposicionar()` la
  persigue cuando la tabla rueda), y `_hojaAbierta` hace que abrir una cierre a la otra. Con una hoja abierta,
  `cancel` hace `preventDefault()` y la cierra a ella (la ventana cae con la segunda `Esc`), y el primer clic
  en el fondo atenuado se come una vez gracias a `_hojaRecienCerrada` —anotada en `fueraDeHoja()` y consumida
  en el `click` del `<dialog>`— para no cerrar las dos capas con un solo punteo. `normalizaDia()`,
  `normalizaMes()` y `normalizaAnio()` aceptan también los formatos largos y viejos, así que los enlaces
  antiguos siguen funcionando.
- **La ventana no miente mientras carga:** `_cargando` pone «Cargando los movimientos de X…» en el hueco y un
  `(cargando…)` junto a los contadores; ya contestado, si no sale ningún renglón distingue los tres casos —
  «X no tiene movimientos registrados» (el índice de días viene vacío), «Ningún movimiento de X del
  14/08/2026 al 20/08/2026. Quitar el periodo (×) muestra toda su historia» (el hueco es del recorte) y
  «…coincide con los filtros seleccionados»—. Con el tope de 2.000 alcanzado el aviso dice «de este periodo»
  o «de su historia» según haya o no periodo puesto, y `cargarVentana(fuerza)` no repite la llamada si el
  rango ya está pedido (`_cargadoDesde`/`_cargadoHasta`) pero aborta la que estaba en vuelo
  (`_ventanaPeticion`) cuando llega un recorte nuevo.
- **Lote:** `lote DES-20261103-1` deja a la vista solo las filas que generó una misma acción del
  mostrador (una descarga o una grabación de varios estudios); el recordatorio sale en el resumen de la
  ventana con su `×` para quitarlo (`chipFijo`).
- **Excel:** el botón **Descargar su Excel** arma `urlExportVentana()` y pide
  `GET /api/bitacora/unificada/export?usuario=X&…` con los filtros de la ventana —el periodo como
  `desde`/`hasta` (los dos días incluidos, sea un día suelto, un mes, un año o un rango) y la hora como
  `hora`/`horaHasta`—; 20 columnas (las siete
  visibles más ubicación obtenida, coordenadas, equipo, dispositivo, fuente, resultado, paciente, ID,
  acceso, UID, medio, unidad y lote) y sin el tope de 2.000 renglones de la pantalla. El nombre del archivo
  trae lo elegido (`bitacora_mostrador01_desde2025-03-01_hasta2025-03-31_07h_a12h.xlsx`).
- **Enlaces:** `?usuario=X` abre la ventana sola cuando la lista ya está pintada
  (`cargarResumen().then(...)`), y `dia`, `mes=yyyy-mm`, `anio=aaaa`, `desde`/`hasta`, `hora`/`horaHasta`,
  `actividad`, `fuente`, `texto` y `lote` de la dirección
  se pasan a sus filtros (`abrirUsuario(usuario, filtros)`). La antigua pantalla del quemador redirige a
  `/admin/bitacora/index.html?fuente=quemador`, que hoy aterriza en la lista de usuarios.
- **`/admin/quemador/`** ya no es una pantalla: es un aviso que redirige a
  `/admin/bitacora/index.html?fuente=quemador`, pasando el `usuario`, `dia`, `lote` y `texto` que
  trajera la dirección antigua.

Antes la hoja se dividía en «usuarios» + «movimientos por usuario» con una tarjeta de detalle; ese
esquema desapareció al unificar las dos bitácoras.

---

## 6b2. `/admin/correos/` (Bandeja de correo, Admin + Ingeniero)

`wwwroot/admin/correos/index.html` — lista, **abre** y **responde** los correos por IMAP/SMTP (MailKit)
usando **dos cuentas a la vez**: el buzón de soporte (`soporte@meditechnosys.mx`, en `appsettings.json`) y
el segundo correo que el administrador enlazó al usuario. Página autónoma.

- **Lista:** `GET /api/correos?carpeta=&cuenta=` (permiso `correo.ver`) → `{ configurado, mensajes[], error }`;
  tabla Fecha / Remitente / Asunto con botón **Actualizar**. Cada fila es clicable.
- **Abrir:** al hacer clic se consulta `GET /api/correos/{uid}` y se pinta el detalle: De / Para /
  Fecha, el cuerpo en un `<iframe sandbox>` **sin scripts ni formularios** cuando el mensaje trae
  HTML (o texto plano en `<pre>` si no), y la lista de **adjuntos** como enlaces de descarga
  (`GET /api/correos/{uid}/adjuntos/{indice}`).
- **Responder:** si el usuario tiene el permiso `correo.responder` aparece el cuadro de respuesta;
  `POST /api/correos/{uid}/responder` envía por SMTP al remitente original encadenando la
  conversación (`RE:` + `In-Reply-To`/`References`). Sin ese permiso el cuadro no se muestra.
- **Sin configurar / error:** si falta la contraseña en `appsettings.json` (`Correo:Imap.Contrasena`)
  o el servidor no responde, se muestra un aviso ámbar explicativo (nunca datos sensibles).
- La tarjeta "Bandeja de soporte" aparece sola en el inicio para quien tenga el permiso.
- **Las dos cuentas, simultáneas:** `GET /api/correos/mio` devuelve `cuentas` (soporte primero, luego el
  buzón enlazado) con `disponible`, `enlazado` y `aviso` por cuenta, más `predeterminada`. La pantalla pinta
  como tarjeta **encima de las carpetas** el soporte **siempre** y el segundo correo **solo cuando `enlazado`
  es `true`**: sin enlace la bandeja queda con **una sola pestaña** (la tarjeta punteada «Sin enlazar · lo
  conecta el administrador» salió, porque era un botón que no abría nada). Un clic cambia cuál se ve y
  **Recibidos / Enviados / Borradores se leen de esa cuenta** (`cuenta=soporte|propio`; sin parámetro,
  soporte). El enlace del segundo correo **solo**
  lo hace el administrador en `/admin/usuarios/` al dar el alta o editar la cuenta; la bandeja es de lectura y
  no permite cambiarlo ni enlazar el propio. Si el enlace existe pero falla, la segunda tarjeta **sí** aparece
  (punteada, con `aviso` en el título y el motivo debajo) porque ahí hay algo que arreglar. Un enlace roto **no**
  apaga el soporte ni al revés: cada cuenta se abre por su lado.
- **Con cuál se envía:** la ventana de redactar trae el selector **Enviar con**, con solo las cuentas
  disponibles y arrancando en la bandeja que se está viendo (soporte por defecto; la elección de bandeja se
  recuerda en `sessionStorage`, así que al reabrir la pantalla se vuelve a soporte). Al enviar o guardar un
  borrador, la vista salta a la cuenta con la que salió, para que el mensaje aparezca en sus *Enviados* /
  *Borradores*. Los `uid` no cruzan de buzón: si se responde con la otra cuenta se sueltan `enRespuestaA`,
  `responderTodos` e `incluirOriginal` (la cita ya va escrita en el cuerpo).
- **Imagen en el cuerpo: se ve y se ajusta.** El editor pinta la captura con una *data URL* leída del base64
  que ya está en memoria (`urlVista`) porque `cid:` solo lo resuelve el cliente de correo; al enviar,
  `htmlParaEnviar()` clona el cuerpo y le regresa `src="cid:…<cid>"` a cada `img[data-cid]`, que es lo que
  esperan `ConstruirMensajeAsync` (parte `inline` con su `ContentId`) y `SanearHtml`. Con la imagen elegida
  aparece `#barraImagen` (nombre, medida en px y % del ancho, botones *Original/75/50/25/Ancho* y
  *Quitar imagen*) y `#asaImagen`, un cuadro de agarre que vive **dentro del `<dialog>`** —fuera de una
  ventana modal el navegador no deja pasar los clics— y escribe `width` en el atributo y en el estilo, con el
  alto automático. El tamaño se manda en el HTML; el adjunto base64 sigue siendo el archivo original.
- **Contraseña cifrada y su clave:** `usuario_buzones.password` guarda `g1.<nonce>.<cifrado>` con
  AES-256-GCM. La clave simétrica es un archivo de 32 bytes cuyo destino fijamos en
  `appsettings.json` → `Auth:BuzonKeyFile` (en el servidor, `App_Data/buzon-clave.bin` del proyecto).
  Hay que dejarla fijada: si cada compilación usa su propio `App_Data` (bin/Debug, bin/Release,
  publish) cada una cifra con una clave distinta y las contraseñas ya guardadas dejan de leerse al
  cambiar de versión. Cuando eso ocurre, `/admin/usuarios/` lo dice en claro (distintivo
  «contraseña por reescribir» y el aviso dentro de **Editar**): basta escribir la contraseña otra
  vez y guardar; el resto del enlace (correo, servidores, puertos) sigue sirviendo.

---

## 6c. `/calendario/` (Calendario + panel de tickets, Admin + Ingeniero)

`wwwroot/calendario/index.html` — calendario mensual **tipo Google Calendar** con formato DICOM
(`panel.css` + estilos propios de rejilla) y un **panel izquierdo de tickets**. Carga
`js/gestion.js` y consume `/api/actividades`.

- **Estructura:** `.cal-layout` es una rejilla de dos columnas —a la izquierda `aside.panel-tickets`
  (300 px) y a la derecha `.cal-scroll` con la rejilla del mes—. En pantallas ≤900 px las dos
  columnas se apilan.
- **Filtro por hospital:** el desplegable `#fHospital` de la barra de herramientas filtra a la vez la
  rejilla mensual y el panel de tickets (`Todos` / HES / HGR / HGO / HI). Los chips y las tarjetas
  muestran el hospital del elemento.
- **Panel izquierdo (Tickets):** es un **panel de trabajo**, no un apartado de consulta —la consulta
  completa de actividades, incidentes y tickets vive en `/gestion/historico/—. Tiene campo de
  búsqueda y tres botones de filtro `.panel-filtros`
  (`data-f` = `abiertos`, `vencidos`, `todos`) y una `.lista-tickets` con
  una `.ticket-card` por elemento (número, título, insignias de prioridad/estado, hospital y fecha
  límite cuando aplica), ordenadas con las **vencidas primero**. Al pulsar una tarjeta se abre el
  mismo detalle que en la rejilla; desde ahí el Admin puede asignar, editar o cancelar.
- **Ancho de la página:** `panel.css` topa `main` y `.card` en 1200 px para las pantallas de formulario; la
  hoja interna de `calendario/index.html` lo levanta **solo ahí** con `main { max-width: min(1780px, 96vw); }`
  y `main > .card { max-width: none; }`, porque los siete días, el panel de tickets y el de pendientes caben
  juntos. Con eso el `min-width` de la rejilla baja a **560 px** (80 px por día) y `.cal-scroll` **ya no saca
  barra horizontal** ni en 1366 px.
- **Rejilla mensual:** 7 columnas (Dom–Sáb) × 42 celdas `.dia`; **las siete columnas son `repeat(7, minmax(0, 1fr))`
  y `.dia`, `.chip` y `.chip .txt` llevan `min-width: 0`**, de modo que el ancho lo reparte la rejilla y **nunca lo
  manda el contenido**: un título largo se recorta con puntos suspensivos dentro de su chip en vez de ensanchar la
  celda y empujar toda la semana. Los días fuera del mes salen
  atenuados (`.fuera`), el día de hoy va enmarcado (`.hoy` con número en círculo). Cada elemento se
  pinta como un **chip de color según su estado** (`.chip.Pendiente` ámbar / `.Completada` verde
  / `.Cancelada` roja y tachada); los vencidos llevan un anillo `.vencida` **solo cuando la fecha
  límite está habilitada** y los tickets muestran su número `#NNN` como prefijo. Cuando al elemento
  **le toca informar** (`leTocaReporte`, 3 días o más sin comentarios) el chip gana una marca naranja
  `.marca` con un `!` al final del renglón y su `title` dice «favor de registrar avances en comentarios».
- **Panel derecho (Pendientes por ingeniero):** `.panel-usuarios` agrupa por responsable lo que está
  **abierto** (`Pendiente`) —para el Ingeniero eso es, en la práctica, su propia lista— y se pinta con
  **todas** las actividades del servidor —no con el filtro de hospital— porque lo que interesa es la carga
  real de cada quien. Sus dos pestañas `#filtrosUsuarios` separan **por tipo**: **«Actividades»** deja solo `tipo === 'Actividad'` y **«Incidentes»** solo
  `tipo === 'Incidente'`; **la pestaña «Reportes» se retiró** (los reportes de estatus que ya tocan se ven
  en la pestaña *Reportes de estatus* de la tarjeta del elemento y en `/admin/reportes/`). Los **tickets no
  entran aquí**: viven en el panel izquierdo. Cada renglón `.u-item` trae **solo** el título (con su
  prefijo `#NNN` si aplica) y debajo «Límite dd/mm/aaaa» o «Sin límite» más el hospital: **las insignias
  de tipo y de estado se quitaron por redundantes** —la pestaña ya dice si son actividades o incidentes, y
  en este panel todo lo que aparece está abierto, o sea `Pendiente`—. Lo único que sigue llevando chip es
  lo **vencido**: cuando `a.vencida` la `.u-meta` pinta `G.badge('est-Vencida', 'Vencida')` y el renglón
  gana la clase `.urgente` (borde rojo izquierdo); el orden pone los vencidos primero y después la fecha
  límite. Vacío dice «Sin actividades pendientes.» o «Sin incidentes pendientes.», y al pulsar un renglón se
  abre `Gestion.abrirDetalle`. **La cabecera `.u-cab` (nombre del responsable + contador) solo se pinta para
  quien tiene `tickets.asignar`** —Administrador/Coordinador, que ve la carga de varios—; para el Ingeniero el
  servidor ya filtra por su cuenta, así que el nombre sería siempre el suyo y la lista sale directa, sin
  cabecera. El texto de ayuda del panel habla de las dos pestañas **por tipo** (ya no dice «agrupado por
  responsable», que solo aplica al Admin).
- **Navegación:** botones *Hoy*, `‹` y `›` cambian el mes; `#etiquetaMes` muestra "octubre 2026".
  La leyenda de colores acompaña a la barra de herramientas: Pendiente, Completada, Cancelada y
  «Favor de registrar avances en comentarios» en naranja (`--naranja`).
- **Crear:** el botón *+ Nuevo* y el `+` de cada día llaman a `Gestion.abrirCrear({ fecha })`, que
  abre el diálogo compartido con el orden de la cadena que se configura en Programador: **Tipo → Región →
  Hospital → Servicio → Actividad → Aplicativo → Tipo de aplicativo → título/descripción/prioridad/fechas**
  (**el Ingeniero no elige la fecha del alta: siempre es la de hoy**, ver «Alta solo del día de hoy» más abajo).
  (el campo Región solo existe si ya hay regiones guardadas; **y si la cuenta tiene una sola región
  asignada el campo sale fijo**: escrito con esa región, con una única opción y `disabled`, sin flecha —no se
  puede desplegar—, porque «Todas las regiones» lo único que lograría sería un Hospital vacío. Con dos o más
  regiones, o para quien ve todas (root, el Administrador, quien no tiene región), el desplegable vuelve con
  «Todas las regiones» delante y cambiarla filtra el Hospital; **Servicio** solo si el tipo es Actividad y el
  hospital elegido tiene servicios; **Aplicativo** solo si la actividad tiene subopciones en el catálogo; y
  **Tipo de aplicativo** solo si el aplicativo elegido tiene su propia lista en el bloque «Tipos por
  aplicativo». Los dos últimos son **opcionales** —su primera opción es «(Sin aplicativo)» y
  «(Sin tipo de aplicativo)»—). **Hospital es obligatorio**. El tramo final depende de `tickets.asignar`.
  **Con el permiso** (Administrador por defecto) se pintan **Prioridad**, **Fecha de
  inicio**, **Fecha límite** con su casilla **Deshabilitar fecha límite** e **Ingeniero responsable**: es la
  misma regla que gobierna el diálogo de asignación formal, y la fecha límite además
  requiere que esté habilitada en Configuración. Su alta viaja por **JSON** y el elemento nace
  `Pendiente`, como siempre. **Sin el permiso** (Ingeniero) esos cuatro campos no existen
  **y tampoco la Descripción**: con tipo **Actividad** la ventana termina en la lista Actividad y
  el botón dice **Crear**, pero **ese paso no guarda nada** —solo arma el título y abre la ventana de
  la evidencia (`G.dialogoCierre` con `alta: true`, titulada **«Registrar y completar actividad»**, con
  **Informe del trabajo realizado (opcional)** y **Evidencia (foto o archivo)** obligatoria). Es esa
  segunda ventana la que hace el alta completa en **un solo envío `multipart`** a `POST /api/actividades`
  (`tipo`, `hospital`, `titulo`, `descripcion`, `fecha`, `informe`, `archivos[]`) y el elemento nace ya
  **`Completada`**: nunca queda Pendiente. Si se cierra sin enviar, no quedó nada en el servidor. Con
  Tipo = **Incidente** no se abre esa segunda ventana: el diálogo termina en la Descripción y el alta
  viaja por JSON (`prioridad: 'Media'`, `fechaLimite: null`, `usuario: ''`, fecha del día desde el que se
  abierto —excepto para el Ingeniero, cuya fecha del alta es **siempre hoy**—), así que los
  incidentes del Ingeniero **sí** nacen `Pendiente`. `Services/Gestion.cs` refuerza
  lo mismo por el lado del servidor (prioridad Media y sin fecha límite para quien no asigna) y
  **rechaza con 400 el JSON de una Actividad** cuando quien la manda no tiene `tickets.asignar`: la única
  puerta para el Ingeniero es el multipart que nace completado. Los botones se ocultan
  si el rol no tiene ninguno de los permisos `items.crearActividad` / `crearIncidente` / `crearTicket`.
  **Alta solo del día de hoy (2026-11-23):** en `G.abrirCrear` se calcula `var fechaAlta =
  puedeAsignar ? (opts.fecha || G.hoy()) : G.hoy();` y esa `fechaAlta` viaja tanto al multipart del
  `alta: true` como al JSON: al Ingeniero el elemento **nunca** puede nacer con fecha de otro día, aunque
  el diálogo se haya abierto desde el «+» de otra celda (y ese «+» ya ni siquiera se pinta fuera de hoy:
  `puedeFechaOtroDia()` en `calendario/index.html`). El servidor repite la regla en `Services/Gestion.cs`
  —dentro de `Crear`, el embudo por el que también pasa `CrearYCerrada`, `if (!puedeAsignar &&
  fecha.Date != DateTime.Today)` devuelve `400` con «Solo puede registrar actividades del día de hoy.»—.
  Es **solo el alta**: completar con evidencia una actividad propia pendiente de cualquier fecha sigue
  permitido, porque recortar eso dejaría elementos pendientes sin salida.
- **Detalle:** al pulsar un chip o una tarjeta se abre `Gestion.abrirDetalle(item, { onReload })`
  (ver §6d) en el diálogo **ancho de la tarjeta** (`modal ancho detalle`, hasta 1180 px): barra de estatus de
  tres tramos, metadatos en 6 columnas (con **Último comentario** en lugar del avance), descripción,
  pestaña de *Reportes de estatus* y —solo con `tickets.historialCambios`— la de *Historial de cambios*,
  más las acciones permitidas según rol, estado y permisos.

---

## 6d. Módulo Gestión: `js/gestion.js`, `js/inventario.js` y las páginas de `/admin/…` y `/gestion/…`

El módulo Gestión tiene nueve pantallas que comparten **un solo módulo JS** (`js/gestion.js`,
expuesto como `window.Gestion`) en lugar de repetir el código embebido en cada una. Todas usan
`panel.css` para el formato y las insignias. Las tres de inventarios comparten además
`js/inventario.js` (`window.Inventario`).

**API pública de `Gestion`:**

| Miembro | Para qué sirve |
|---|---|
| `init()` | Carga `/api/patologia/me` y `/api/permisos/mios`; rellena `YO`, `ROL`, `PERMISOS`, `FECHA_LIMITE`, `HOSPITALES` y `HOSPITALES_CON_GENERAL`. Devuelve una promesa. |
| `esAdmin()`, `tiene(clave)` | Consultas de rol y de permiso (para ocultar botones que el servidor de todos modos rechaza). |
| `usaFechaLimite()` | `true` solo si la sesión es Administrador **y** la fecha límite está habilitada en Configuración. Todas las pantallas la usan para mostrar u ocultar el campo, la columna, la insignia *Vencida* y el filtro correspondiente. |
| `api(ruta, opciones)` | `fetch` con `credentials`, JSON automático y `{ error }` convertido en excepción. |
| `descargar(ruta)` | Descarga un archivo con la cookie de sesión (`fetch` → blob → `<a download>`), toma el nombre del `Content-Disposition` y convierte un `{ error }` en excepción. Es lo que usan todos los botones **Descargar Excel**. |
| `subir(ruta, FormData)` | Envío `multipart/form-data` (subida y reemplazo de PDF del Playbook). |
| `opcionesHospital({ general, vacio })`, `campoHospital(valor, opts)` | Catálogo de hospitales para desplegables y campo de formulario obligatorio. |
| `paginaRegreso()` | `/Admin` o `/Ingenieria` según el rol: las pantallas de `/gestion/` son comunes a los dos. |
| `formDialog(cfg)` | Diálogo `<dialog class="modal">` genérico: `titulo`, `campos[]` (text/textarea/select/date/number/**file** con `hint`), `contenidoHtml` (marcado de solo lectura), `valores`, `textoSubmit`, `textoCancelar` (`null` lo oculta), `onSubmit(valores)` y `onClose`. Un campo `file` admite `multiple: true` y `miniaturas: <selector>`: lo elegido se pinta **antes** de enviar (foto de vista previa y documento con su extensión, nombre, peso y quién lo subió) y, si el campo es obligatorio y va vacío, el botón de enviar no dispara ningún request —avisa «Adjunta evidencia (foto o archivo) antes de continuar…»—. Un campo con `ocultoInicial: true` nace oculto y **deshabilitado**, y se muestra u oculta desde `onChanged`/`ajustarTipo` con `api.mostrar(nombre, visible)`; al estar deshabilitado no entra en los valores ni en la revisión de archivos obligatorios. Los errores del servidor se pintan dentro del modal con `G.error`. |
| `confirmar(mensaje, onSi)` | Confirmación simple. |
| `abrirDetalle(item, opts)` | El diálogo central del módulo —`dialog.modal.ancho.detalle`, **hasta 1180 px (94 % del ancho)** para aprovechar el sitio del calendario; los demás diálogos anchos siguen en 660 px—: insignias (tipo/estado/prioridad), **barra de estatus de tres tramos** (Pendiente · Completada · Cancelada), `.meta-grid` con **hospital**, fechas, ingeniero y **Último comentario** (la fecha límite solo cuando aplica), descripción, informe final o **justificación de la cancelación**, **bloque «Evidencia del trabajo»** (§6d bis), aviso de reporte pendiente, aviso naranja **«Favor de registrar avances en comentarios.»** cuando `item.leTocaReporte`, las acciones según permisos y las pestañas: **«Reportes de estatus»** siempre y **«Historial de cambios»** solo con `tickets.historialCambios` (`G.tiene`) —sin el permiso el botón no se pinta y no se pide `/historial`; el servidor devuelve `[]` igual—. Su divisor es `tickets.asignar`: con él (Administrador) quedan Completar, Entregar reporte, Asignar/Reasignar, Editar, Observación, **Cancelar** (que además exige `items.cancelar` y justificación), **Eliminar** (solo sobre `Pendiente`, con `items.eliminar`) y —solo sobre un elemento `Completada`— **Reabrir** (§6d bis); sin él (Ingeniero) la tarjeta es solo su reporte —**Completar** con evidencia obligatoria y **Agregar comentario**, que es texto puro y reinicia el recordatorio de 3 días— y desaparecen el aviso de reporte pendiente y **Reabrir** (que además exige `items.reabrir`). Sobre una **`Cancelada`** la tarjeta queda congelada (`congelado`): enseña la justificación, la fecha de cierre, su evidencia y su historial (esta pestaña, solo con el permiso), pero ningún botón de acción —solo el «Cerrar» que quita la ventana, que está siempre. Recarga con `opts.onReload`. |
| `abrirAsignar(item, despues)` | Modal de asignación formal (solo Admin): ingeniero, prioridad, fecha límite (si está habilitada) y periodicidad de reportes. |
| `abrirEditar(item, despues)` | Modal de edición de datos del elemento (incluye hospital). Solo sobre elementos `Pendiente`. |
| `abrirCrear(opts)` | Modal de alta con el tipo y el hospital obligatorio, precargando la fecha del día elegido. Con `tickets.asignar` termina en los campos de asignación y envía JSON (elemento `Pendiente`); sin él y con Tipo = Actividad **no trae Descripción y no envía nada**: abre `G.dialogoCierre({ alta: true, … })`, que hace el alta multipart completa y deja el elemento `Completada`. |
| `dialogoCierre(item, despues)` | Ventana de la evidencia (informe opcional + archivo obligatorio). Con `item.id` completa un elemento que ya existe; con `item.alta` **da de alta y completa en un solo envío** (§6d bis). |
| `badgeEstado/badgePrioridad/badgeTipo/avanceHtml`, `fmtFecha/fmtFechaHora/fmtFechaLarga`, `claveDia/hoy`, `esc`, `error/sinError`, `ESTADOS/ORDEN_ESTADOS/PRIORIDADES/TIPOS` | Utilidades de presentación y formato compartidas por las nueve pantallas. `ESTADOS`/`ORDEN_ESTADOS` ya solo tienen **Pendiente, Completada y Cancelada**. `avanceHtml` sigue existiendo para las columnas de los **reportes de estatus** (`/admin/reportes/` y el Histórico), que sí conservan su porcentaje; la tarjeta del elemento ya no lo muestra. |

**Páginas solo Admin (`/admin/…`):**

- **`/admin/roles/`** — matriz de permisos: `GET /api/roles` devuelve `{ grupos, matriz }` y se
  pinta en `table.matriz` con una fila de grupo (`tr.fila-grupo`) por cada categoría y una casilla
  por rol (`td.col-check`). La casilla `roles.administrar` de Admin va deshabilitada (nadie puede
  quitarse el acceso a la propia pantalla). Guardar hace `PUT /api/roles { rol, permisos[] }`.
  La gestión de **usuarios** no vive aquí: enlaza a `/admin/usuarios/`.
- **`/admin/reportes/`** — reportes de estatus recibidos (`GET /api/reportes`), filtrables por
  autor, **hospital**, rango de fechas y texto; la tabla muestra Momento, Ingeniero, Elemento,
  Hospital, Estatus, Avance y Descripción. Al pulsar una fila se abre el reporte en solo lectura
  (`formDialog` con `contenidoHtml`) y un botón que salta al seguimiento del elemento.
- **`/admin/config/`** — configuración del módulo: la **periodicidad global de reportes**
  (`reportes.periodicidadDias`) y el interruptor **Habilitar fecha límite**
  (`items.fechaLimiteHabilitada`). Se deshabilita sin `configuracion.administrar`, y el interruptor
  exige además `configuracion.fechaLimite`. El texto de la pantalla deja claro que la fecha límite
  es exclusivamente administrativa y que el Ingeniero no la ve en ningún caso.

**Páginas comunes a Admin e Ingeniero (`/gestion/…`):**

- **`/gestion/historico/`** — el **único** punto de consulta de actividades, incidentes y tickets
  (ver [`ARQUITECTURA.md`](ARQUITECTURA.md) §3c). Filtros combinables, resumen por ingeniero, tabla
  con la columna *Límite* condicional y botón **Descargar Excel** que solo aparece con
  `historico.exportar`. `aplicarVisibilidadLimite()` oculta todo lo relativo a la fecha límite
  (`[data-limite]`, `[data-requiere-limite]`, la opción *Vencida* y la línea de vencidas del
  resumen) cuando `Gestion.usaFechaLimite()` es falso. El botón *Regresar* apunta a `/Admin` o
  `/Ingenieria` según el rol.
- **`/gestion/inventario-equipos/`, `/gestion/inventario-ips/` y
  `/gestion/inventario-aetitles/`** — tres pantallas **separadas** (no se mezclan) que comparten
  `js/inventario.js`; cada una solo declara `window.INVENTARIO = 'equipos' | 'ips' | 'aetitles'`.
  El script lee el esquema con `GET /api/inventario`, construye las columnas y el formulario de
  alta/edición (tipos `texto`, `area`, `numero` y `lista`), y ofrece filtro por hospital, búsqueda
  libre, **Descargar Excel** (`inventario.exportar`) y, solo para el Administrador con
  `inventario.administrar`, **Nuevo registro** y los botones Editar/Eliminar de cada fila. Un clic en
  la fila abre el detalle en solo lectura.
- **`/gestion/playbook/`** — biblioteca de PDF. Filtros por texto, hospital (incluye **General**) y
  estado; tabla con Nombre, Hospital, Versión, Publicación, Actualización, Archivo PDF (con tamaño),
  Estado y Acciones. **Ambos roles** tienen *Abrir* (en pestaña nueva) y *Descargar*. Solo el
  Administrador ve *Subir documento*, *Editar* (nombre, versión, hospital, fecha de publicación,
  estado), *Archivo* (reemplazar el PDF por una versión nueva) y *Eliminar*, cuyo diálogo usa
  exactamente el texto exigido: «¿Está seguro de que desea eliminar este documento?» / «Esta acción
  eliminará el documento del Playbook.» / *Cancelar* / *Eliminar*. El campo de archivo acepta
  únicamente `application/pdf,.pdf` y el servidor vuelve a validar la firma `%PDF-`.

---

## 6d bis. La evidencia del trabajo en la tarjeta de detalle

La petición que originó este bloque fue «cuando el ingeniero la complete debe poner la evidencia ya sea foto o
archivo, unicamente», y «de lado de ingeniero debe quedar como completado ya que solo es el reporte de la
actividad que hizo, y solo puede agregar comentarios». Todo vive en `Gestion.abrirDetalle` y en un campo más de
`Gestion.formDialog`; no hay página nueva.

La siguiente vuelta —«cuando el perfil de ingeniero asigna una actividad debe completarse en ese momento y pedir
la evidencia, solo el administrador debe poder reabrir la actividad y poner fecha limite»— se apoyó en el mismo
motor: la ventana de cierre es el mismo `formDialog` de dos campos (hoy `G.dialogoCierre`), y la ventana de
reapertura es un `formDialog` más. Cambió el divisor: **Reabrir** no usa `tickets.asignar`, usa el permiso nuevo
**`items.reabrir`** (`CatalogoPermisos.ReabrirItems`, catálogo en versión 13).

La tercera vuelta —«en el perfil del ing, quita la descripcion y despues de crear que arroje el apartado de
Informe del trabajo realizado (opcional)+evidencia (obligatorio)»— convirtió ese cierre en **el segundo paso
del alta**: el *+ Nuevo* del Ingeniero pierde la **Descripción** y también el informe y la evidencia, y al
pulsar **Crear** se abre sola la ventana `G.dialogoCierre`, la misma que usa el botón *Completar* de la
tarjeta, ahora con el informe **opcional**. En esa vuelta el primer paso todavía enviaba un JSON y dejaba el
elemento `Pendiente` unos segundos.

La cuarta vuelta —«solo debe estar el estado pendiente, completado y cancelado … las canceladas ya no se pueden
reabrir y solo queda el registro … directamente al crear una actividad debe estar en completo, no puede quedar
pendiente», más «el registrar avance queda en comentarios y se debe recordar cada 3 días»— recortó el módulo a
**tres estados** y volvió el alta del Ingeniero **atómica**:

- **`G.ESTADOS` y `G.ORDEN_ESTADOS` ya solo tienen tres claves** (`Pendiente`, `Completada`, `Cancelada`), así
  que la barra `.estatus` de la tarjeta pinta tres tramos y los chips/insignias de las nueve pantallas no
  conocen «En proceso» ni «Cerrada». Los registros viejos los traduce el servidor al leerlos
  (`ActivityStoreBase.Normalizar`: `EnProceso` → `Pendiente`, `Cerrada` → `Completada`), de modo que la
  interfaz nunca ve un cuarto estado.
- **El alta del Ingeniero ya no guarda nada en el primer paso.** `abrirCrear` conserva `var cierraAlCrear =
  !puedeAsignar;` y con `cierraAlCrear && esActividad` el `onSubmit` **no hace fetch**: arma un objeto
  `{ alta: true, tipo, hospital, titulo, descripcion, fecha }`, devuelve `Promise.resolve(null)` y abre
  `G.dialogoCierre(alta, opts.onClose)` en un `setTimeout(…, 0)` para que la primera ventana termine de
  cerrarse. Si el usuario cierra la segunda sin enviar, **no quedó nada en el servidor**.
- **`G.dialogoCierre` sabe hacer el alta completa.** Con `item.alta === true` cambia el título a
  **«Registrar y completar {tipo}»**, el botón verde a **«Crear y completar»** y la intro avisa que ese envío
  da de alta y deja completada la actividad; el `FormData` añade `tipo`, `hospital`, `titulo`, `descripcion` y
  `fecha` junto al `informe` y los `archivos`, y se manda con `G.subir` a **`POST /api/actividades`** (201,
  elemento ya `Completada`). Con `item.id` el destino sigue siendo `POST /api/actividades/{id}/completar`.
- **Cancelar es de quien tiene `items.cancelar`** (permiso nuevo del catálogo en versión 14; el rol Admin lo
  trae por defecto). El botón **Cancelar** —rojo, solo con el elemento abierto— abre un `formDialog` con una
  **Justificación** obligatoria (tope 500) y hace `PUT /api/actividades/{id}/estado` con
  `{ estado: 'Cancelada', justificacion }`, y al terminar refresca la tarjeta.
- **Una `Cancelada` se congela.** `var congelado = item.estado === 'Cancelada';` y `var abierto = item.estado
  === 'Pendiente';` gobiernan los botones: con `congelado` no se pinta Completar, Comentario, Entregar reporte,
  Asignar/Reasignar, Editar, Observación, Cancelar, Reabrir ni Eliminar —solo «Cerrar», que quita la ventana—.
  A cambio la tarjeta muestra la **Justificación de la cancelación** y la **Fecha de cierre**, y sus pestañas
  siguen vivas —*Reportes de estatus* siempre y *Historial de cambios* solo con el permiso—: «solo queda el
  registro».
- **Reabrir se limita a `Completada`.** La condición del botón pasó de `item.estado === 'Completada' ||
  'Cerrada'` a `item.estado === 'Completada'`; sobre una cancelada el servidor también responde 400.
- **El avance se jubiló y el seguimiento es un comentario.** Desaparecieron el botón **Registrar avance**, la
  meta «Avance» con su barra y el endpoint que consumían; en su lugar la `.meta-grid` enseña **Último
  comentario** (fecha y hora, `—` si no hay ninguno). El botón **Agregar comentario** del Ingeniero y la
  **Observación** del Administrador escriben en `POST /api/actividades/{id}/observaciones` y **reinician el
  recordatorio de 3 días**.
- **El recordatorio de los 3 días vive en la tarjeta y en el calendario.** Cuando el servidor marca
  `item.leTocaReporte` (3 días o más desde `ultimoComentario`, o desde la fecha de inicio si todavía no hay
  ninguno) y la sesión es la dueña del elemento abierto, la tarjeta pinta un `.aviso-avance` **naranja** con
  el texto exacto «Favor de registrar avances en comentarios.». En el calendario el chip gana la marca `.marca`
  con un `!` (también naranja) y la leyenda cierra con «Favor de registrar avances en comentarios».
  El naranja es suyo: el aviso ámbar `.aviso-reporte` queda solo para el reporte de estatus solicitado, y
  los tonos por tema salen de `--aviso-avance-bg` / `--aviso-avance-text` en `theme.css`.
- **Los reportes de estatus conservan su porcentaje.** El botón **Entregar reporte** (solo quien puede asignar,
  elemento abierto y `reportes.entregar`) sigue pidiendo *Porcentaje de avance* y *Descripción del avance*, y
  las tablas de `/admin/reportes/` y del Histórico siguen mostrando esa columna: lo que se retiró fue el avance
  **del elemento**, no el del reporte. El reporte ya no mueve el estatus (no existe «En proceso»): el elemento
  sigue `Pendiente` hasta que se completa, y sobre una `Cancelada` el servidor lo rechaza.

- **El divisor sigue siendo `tickets.asignar`** (`G.tiene('tickets.asignar')`), el mismo permiso de la
  asignación formal y del tramo final de *+ Nuevo*.
  Con el permiso (Administrador) la tarjeta conserva Completar, Entregar reporte, Asignar/Reasignar, Editar,
  Observación, Cancelar (`items.cancelar`) y Eliminar (`items.eliminar`, solo `Pendiente`). Sin él (Ingeniero)
  la tarjeta es únicamente su reporte: **Completar** y **Agregar comentario**, más el botón «Cerrar» que
  simplemente quita la ventana y que está en todas las tarjetas de ambos roles. El aviso de reporte pendiente
  (`.aviso-reporte`) tampoco se pinta sin el permiso. **Reabrir** es la pieza que se sale del divisor: la
  condiciona `items.reabrir`, y **ningún** perfil sin ese permiso la ve —el `IngenieroPorDefecto` tampoco lo
  trae. Ya no existen los botones **Iniciar**, **Registrar avance** ni «Cerrar formalmente».
- **`G.dialogoCierre(item, despues)`, el helper compartido.** Un `formDialog` **ancho** con dos campos:
  `informe` —etiqueta *Informe del trabajo realizado (opcional)*, `textarea`, `maxlength: 2000`— y `archivos`
  —*Evidencia (foto o archivo)*, `tipo: 'file'`, `multiple: true`, `required: true`, `accept` con la lista de
  extensiones y miniaturas—. El botón verde dice **«Crear y completar»** cuando `item.alta` es `true` y
  **«Marcar como completada»** cuando el elemento ya existe. La bandera `listo` + `fin()` hacen que `despues`
  (el refresco de la lista) se dispare **una sola vez**, aunque `onClose` llegue también tras el envío
  correcto. Lo usan dos caminos: el alta del Ingeniero y `dialogoCompletar` dentro de `abrirDetalle`
  (`function dialogoCompletar() { G.dialogoCierre(item, refresco); }`).
- **Reabrir (solo con `items.reabrir`).** Dentro de `abrirDetalle`, el bloque de botones del Administrador
  tiene su propia condición: si `item.estado === 'Completada'` y la sesión tiene `items.reabrir`, se pinta el
  botón **Reabrir**. Su `formDialog` tiene un solo campo —`motivo` (textarea, `maxlength: 1000`,
  opcional)— y llama a `POST /api/actividades/{id}/reabrir` con `{ motivo }` en JSON, vía `G.api`. La intro
  avisa que vuelve a Pendiente con su evidencia intacta, para que el elemento se pueda cerrar otra vez sumando
  archivos nuevos.
- **Completar pide evidencia obligatoria; el informe no.** `dialogoCierre` es un `formDialog` con dos campos:
  `informe` (textarea, **opcional**, `maxlength: 2000`) y `archivos` (`tipo: 'file'`, `multiple: true`,
  `required: true`, `accept: 'image/*,.pdf,.doc,.docx,…'` y miniaturas). La caja de miniaturas se llena con
  `URL.createObjectURL` al elegir los archivos —fotos vista previa, documentos con su extensión en grande— y
  se liberan en `close`. Si se envía sin archivos, el botón **no dispara ningún request**: pinta
  «Adjunta evidencia (foto o archivo) antes de continuar. Hasta 5 archivos de 20 MB. Se listan abajo para que
  los veas antes de enviar.».
- **Un solo envío multipart.** `onSubmit` arma un `FormData` con el informe (vacío si no se escribió) y cada
  archivo en el campo `archivos`, y lo manda con `G.subir` (no `G.api`, que fuerza JSON): a
  `POST /api/actividades/{id}/completar` cuando el elemento ya existe, o a `POST /api/actividades` —añadiendo
  `tipo`, `hospital`, `titulo`, `descripcion` y `fecha`— cuando es un alta (`item.alta`). El servidor adjunta y
  cambia el estado en la misma petición; si la evidencia no pasa, devuelve su error y **no queda nada a medias**
  (ni elemento nuevo sin evidencia, ni cambio de estado).
- **El comentario es solo texto.** `dialogoComentario` tiene una única textarea obligatoria (tope 1000) y llama a
  `POST /api/actividades/{id}/observaciones`; el servidor la registra como «Comentario» cuando escribe el
  dueño del elemento y como «Observación» cuando tiene `tickets.observaciones`. Se puede comentar sobre un
  elemento abierto **y sobre uno ya `Completada`**, pero **no sobre una `Cancelada`** (el servidor responde 400).
  Cada comentario nuevo reinicia la cuenta del recordatorio de 3 días, y tras guardarlo la tarjeta se refresca
  sola (`.then(refresco)`).
- **Bloque «Evidencia del trabajo».** Al abrir la tarjeta se consultan `GET /api/actividades/{id}/evidencias` y
  se pintan los recuadros `.mini`: las imágenes dentro de un `<a target="_blank">` que apunta a
  `GET /api/evidencias/{id}` (se abren a tamaño completo) y los documentos como insignia con su extensión más el
  nombre enlazado, con `.mini-meta` (quién lo subió · fecha · peso). Sin archivos muestra
  «Sin evidencia todavía: se adjunta (foto o archivo) al marcar como completada.». Si la consulta falla, el
  bloque desaparece en vez de quedarse vacío.
- **Lo que rechaza el navegador es poco:** el filtro de extensión, tamaño y firma lo vuelve a aplicar el
  servidor (`Models/Evidencia.cs`), así que un cuerpo forjado a mano no cuela un ejecutable disfrazado.

---

## 7. Estilos de los paneles (`panel.css`) y legacy (`intranet.css`)

`css/panel.css` es la base compartida del formato DICOM para las páginas de panel:

- Cabecera: `header` negro fijo, `.logo-centro` (wordmark),
  `#statusBadge`, `.botones-nav` / `.boton-nav` (Regresar, Home). En pantallas ≤900px el wordmark
  se oculta y la cabecera deja de ser fija.
- **Cabecera Razor de panel** (`header.cab-panel`, la de `_LayoutPanel.cshtml`): lleva la salida
  (el interruptor Día/Noche se retiró: el tema se escoge en el ⚙, que ahora solo existe en el inicio).
  El `<form method="post">` de *Salir* no pinta caja propia
  —`header .botones-nav form { display: contents; }`— para que su botón quede alineado con los demás,
  y el usuario con su rol viaja en `header .badge`, junto al título. Al quedarse sin el botón ⚙, el grupo
  derecho pesa lo mismo que en los demás paneles, así que el wordmark centrado se oculta aquí también a
  **1180px** (la regla propia de 1320px de `.cab-panel .logo-centro` en `theme.css` se eliminó).
- **Ojo con `<header>` dentro de `<main>`:** `panel.css` pinta *toda* etiqueta `header` como la barra
  negra fija. Un encabezado de sección debe ser `div` (en la lista de trabajo es `div.lt-heading`).
- Contenido: `main`, `.card`, `.grid-form`, `label/input/select/textarea`, `.empty`,
  `.form-error`, `.table-wrap`/`table`/`th`/`td`, `.row-actions`, `.hint`.
- Botones: `.btn-primary` (azul), `.btn-secondary`, `.btn-success`, `.btn-danger`, `.btn-mini`.
- Modales: `dialog.modal` con `::backdrop` oscurecido, `.modal-form` con scroll propio,
  `dialog.modal.ancho` (660 px) para los diálogos de seguimiento y `dialog.modal.ancho.detalle`
  (`min(1180px, 94vw)`) solo para la tarjeta del elemento, que es la que aprovecha el ancho del calendario.
- **Insignias del módulo Gestión:** `.badge-chip` con las variantes de estado (`.est-Pendiente`,
  `.est-Completada`, `.est-Cancelada`, `.est-Vencida`), de
  prioridad (`.prio-Baja|Media|Alta|Urgente`) y de tipo (`.tipo-Actividad|Incidente|Ticket`),
  agrupables en `.badges`. Ya no existe `.est-EnProceso`; `.est-Cerrada` quedó como gris neutro porque
  el Playbook la usa para sus documentos *Vigentes*, no Gestión.
- **Seguimiento:** `.estatus` (barra de **tres** tramos con `div.on[data-e="Pendiente|Completada|Cancelada"]`),
  `.meta-grid`, `.caja`, `.tabs` + `.lista-entradas` (reportes e historial)
  y `.aviso-reporte` (ámbar: reporte de estatus pedido) y `.aviso-avance` (naranja: recordatorio de los
  3 días sin comentarios). La clase `.avance`
  (barra de porcentaje) sigue en la hoja porque las tablas de reportes aún la usan con `avanceHtml`.
- **Evidencia (§6d bis):** `.evidencia` (el recuadro «Evidencia del trabajo» de la tarjeta y la caja de
  vista previa de `formDialog`), `.miniaturas` (rejilla de recuadros), `.mini` (un archivo), `.mini img`
  (la foto recortada a 132 px), `.mini-icono` (la insignia con la extensión: PDF, DOCX…), `.mini-nombre`
  (enlace al archivo) y `.mini-meta` (quién lo subió, fecha y peso).
- **Listados:** `.filtros` (rejilla de campos de filtro). Los estilos propios de cada listado
  (tarjetas del resumen por ingeniero, columnas numéricas) viven en el `<style>` de su página.
- **Calendario:** `.cal-layout`, `.panel-tickets`, `.panel-filtros`, `.lista-tickets` y
  `.ticket-card` (con `.vencido`).
- **Matriz de roles:** `table.matriz` con `.col-rol`, `.col-check`, `.desc` y `tr.fila-grupo`.

En `intranet.css` **quedan** los bloques legacy de la antigua tabla de usuarios dentro de Admin
(`.tablehead`, `.store-line`, `.aviso`, `.modal`, `.btn-gold`…); ya no los usa ninguna pantalla
pero se conservan por si se quisieran reutilizar en páginas Razor (candidatos a limpieza, ver
`docs/MEJORAS-FUTURAS.md`).

---

## 7b. Las pantallas `/Admin` e `/Ingenieria` (catálogos de accesos)

Ninguna de las dos dibuja ya una lista plana: ambas agrupan las tarjetas por `Tag` en secciones
`<section class="grupo" data-tag="…">` con su `<h2>`.

- **`Pages/Admin.cshtml`** usa `Models/Catalog.cs` → `AdminCatalog.Apps`. Los grupos son
  **Gestión** (roles y permisos, calendario, histórico de actividades, reportes de estatus,
  inventario de equipos, inventario de IPs, inventario de AETitle, Playbook y configuración),
  **Accesos** (gestión de usuarios) y **Seguridad** (bitácora).
- **`Pages/Ingenieria.cshtml`** usa `Catalog.Apps`. Los grupos son **Patología** (consulta de
  reportes), **Imagen médica** (visor DICOM) y **Gestión** (calendario, histórico de actividades,
  los tres inventarios y Playbook).

Los estilos de `.grupo`, `.apps` y `.app` están en `intranet.css`, y la búsqueda del hero (`#q`, en
`js/intranet.js`) oculta también los grupos que quedan sin tarjetas visibles. **No existe en ningún
menú un acceso llamado "Histórico de tickets"**: se eliminó junto con su página.

---

## 8. Convenciones y buenas prácticas aplicadas

- **Sin dependencias frontend:** todo es HTML/CSS/JS vanilla; no hay build ni paquetes que compilar.
- **`asp-append-version="true"`** en css/js: agrega un hash al URL para romper la caché del
  navegador cuando el archivo cambia.
- **Accesibilidad:** `role`/`aria-label` en el logo, `aria-labelledby` en los modales,
  `role="alert"` en los mensajes de error, `:focus-visible` con contorno dorado.
- **Rutas absolutas** (`/img/...`, `/api/...`) en los estáticos para que funcionen desde cualquier
  subcarpeta (`/dicom/`, `/patologia/...`).
