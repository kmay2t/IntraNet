# Meditechnosys — Sistema Unificado

Una sola aplicación web que reúne **todo lo que antes estaba separado**:

1. **Intranet** — el inicio de sesión único y las pantallas de cada área.
2. **Repositorio de Patología** — guardar, buscar, editar y descargar reportes (PDF) de *Especialidades* y *Ginecología*.
3. **Visor DICOM** — buscar estudios de imagen en el PACS del hospital y descargarlos (C‑FIND / C‑MOVE),
   y **consulta de la lista de trabajo (MWL)**: ver los procedimientos programados por nombre, ID, número de acceso,
   fecha, hora, modalidad y procedimiento, y mandar la imagen de prueba técnica **al mismo nodo consultado**, que
   después responde qué guarda de ese estudio.
4. **Módulo Gestión** — calendario e histórico único de actividades/incidentes/tickets,
   asignación y seguimiento, reportes de estatus periódicos, matriz de roles y permisos,
   **inventarios por hospital** (IPs y AETitle), **Playbook** de documentación
   técnica en PDF e **Inventario de equipos** — la **pantalla única de equipos** (pestaña
   *Ingresos*): alta del inventario (marca, modelo, serie y cantidad), existencias por unidad
   (Almacén/HES/HGR/HGO/HI), entradas y salidas con folio, hoja imprimible/Word por fila y
   **exportación a Excel** (solo Admin).

Todo lo del módulo Gestión está **clasificado por hospital** (`HES`, `HGR`, `HGO`, `HI`, y
`General` en el Playbook) y se puede filtrar y exportar por hospital.

Todo funciona con **un único login** (el de la intranet). Nadie entra al repositorio ni al visor DICOM sin iniciar sesión, y solo los roles **Admin** e **Ingeniero** los ven.

---

## Guía rápida (lo más usado)

| Quiero… | Hago… |
|---|---|
| Probar el sistema en esta PC | Abrir **http://localhost:5023** e iniciar sesión (`admin` / `admin`). |
| Dejarlo fijo, que arranque solo con Windows | 1) Doble clic en **`publicar.bat`**  2) Clic derecho en **`instalar-servicio.bat`** → *Ejecutar como administrador*. |
| **Actualizar** después de cambiar el código | Clic derecho en **`actualizar.bat`** → *Ejecutar como administrador* (detiene el servicio, republica y lo reinicia solo). |
| Quitar el servicio | Clic derecho en **`desinstalar-servicio.bat`** → *Ejecutar como administrador*. |
| Entender cómo funciona por dentro | Leer **[`docs/ARQUITECTURA.md`](docs/ARQUITECTURA.md)**. |
| Entender usuarios, roles y MySQL | Leer **[`docs/USUARIOS-Y-MYSQL.md`](docs/USUARIOS-Y-MYSQL.md)**. |
| Entender el frontend (CSS/JS/modales/logo) | Leer **[`docs/FRONTEND.md`](docs/FRONTEND.md)**. |
| Entender el Inventario de equipos (existencias, entradas, salidas, hojas) | Leer **[`docs/INVENTARIO-EQUIPOS.md`](docs/INVENTARIO-EQUIPOS.md)**. |
| Ver qué se puede mejorar después | Leer **[`docs/MEJORAS-FUTURAS.md`](docs/MEJORAS-FUTURAS.md)**. |
| Entender el servicio de Windows | Leer **[`docs/SERVICIO-WINDOWS.md`](docs/SERVICIO-WINDOWS.md)**. |
| Ver el historial de cambios del proyecto | Leer **[`docs/REGISTRO-CAMBIOS.md`](docs/REGISTRO-CAMBIOS.md)**. |
| Consultar actividades, incidentes y tickets | **Reportes** (`/admin/bitacora/`) → pestaña **Histórico de actividades** → filtrar por *Tipo* y *Hospital* → **Descargar Excel**. |
| Dar de alta un equipo (marca, modelo, serie, cantidad) | **Inventario de equipos** (`/gestion/inventario-equipos/`) → elegir unidad → **+ Añadir equipo** → nombre, marca, modelo, serie y cantidad (el **número de inventario `EQ-####` se genera solo**); se genera su **hoja de entrada** (solo Admin). |
| Dar de alta IPs o AE Titles | La pantalla de cada inventario (`/gestion/inventario-ips/`, `-aetitles/`) → **Nuevo registro** → elegir hospital. |
| Registrar una salida de equipos (y su re-ingreso en otra unidad) | **Inventario de equipos** → en la fila del equipo → **Hoja salida** → cantidad, motivo y destino opcional → se descuenta, se genera la **hoja de salida** y, si hay destino, el equipo re-ingresa allá. |
| Imprimir o archivar la hoja de un movimiento | En la fila del equipo → **Hoja entrada** (reimpresión) o **Hoja salida**; la hoja se abre con **Imprimir / PDF**, **Descargar Word** y **Cerrar**. |
| Ver existencias y descargarlas en Excel | **Inventario de equipos** → elegir unidad y buscar → **Descargar Excel** (solo Admin). |
| Publicar un manual o procedimiento | **Playbook** (`/gestion/playbook/`) → **Subir documento** → nombre, versión y hospital (solo Admin). |
| Quitar la fecha límite de los registros | **⚙ Configuración** (arriba a la derecha, **solo en el inicio**) → *Parámetros del módulo Gestión* → desmarcar **Habilitar fecha límite** → **Guardar parámetros** (solo Admin). |
| Volver a abrir una actividad que ya quedó completada | Abrir su tarjeta (chip del calendario o fila del histórico) → **Reabrir** → escribir el motivo (opcional) → **Reabrir**. Vuelve a **Pendiente** con su evidencia intacta, para que se cierre otra vez. **Solo el Administrador** tiene ese botón; el Ingeniero no. |
| Ver los cambios que tuvo un elemento (asignaciones, evidencia, cancelaciones) | Abrir su tarjeta (chip del calendario o fila del histórico) → pestaña **Historial de cambios**. La ve quien tiene el permiso `tickets.historialCambios` (**Roles y permisos**, bloque *Tickets (administración)*): por defecto el **Administrador**; el **Ingeniero** no la ve y en su tarjeta queda solo **Reportes de estatus**. |
| Cambiar la lista de actividades, los aplicativos de una Capacitación, los servicios o las regiones médicas | **Programador** (tarjeta del grupo *Administración* del inicio, `/admin/programador/`) → editar la lista y **Guardar** → se nota al abrir **+ Nuevo** en el calendario. Los servicios se editan **por hospital**: primero se elige el hospital con el desplegable. En *Regiones médicas* la lista trae **una línea por región** y **al seleccionarla se despliegan debajo sus hospitales**; los códigos se escriben en ese panel: **+ Agregar región** y **+ Agregar hospital** abren cada uno su **ventana emergente**, donde se llenan varias filas y se aceptan todas juntas (con Cancelar no cambia nada). El nombre de la región se puede **elegir de la lista**, y al hacerlo salen los hospitales que ya tenía guardados. Solo la cuenta **root** la trae; a otro rol se le da con el permiso `programador.administrar`. |
| Que un usuario solo vea los hospitales de su región | **Usuarios** (`/admin/usuarios/`) → **Editar** ese usuario → bloque *Región*: elija la región en el desplegable (cada renglón dice cuántos hospitales tiene) y guarde; la columna **Región** de la tabla lo confirma. El primer renglón de esa lista es **«Todas las regiones»**, que es lo mismo que dejarlo sin límite. **root** sale bloqueado en «Todas las regiones». Mientras el Programador no guarde regiones, nadie está limitado. Afecta a listas, filtros, exportaciones a Excel y al dar de alta. Ver *Región por usuario*. |
| Cambiar el tema | **⚙ Configuración** arriba a la derecha, **en el inicio** (`/`): bloque **Tema**, **Oscuro** o **Claro**. Se guarda en **tu cuenta**, así que te sigue a cualquier equipo y a cualquier pantalla. |
| Cambiar el tamaño de la letra o cuánto aguanta tu sesión | **⚙ Configuración** (arriba a la derecha, **solo en el inicio**) → elegir y cerrar. La letra se guarda en el equipo que usas; el tiempo de sesión se guarda en tu cuenta. |

> **Credenciales de prueba** (solo cuando la base de datos está vacía): `admin` / `admin` (Administrador) e `ing` / `ing` (Ingeniero). Cámbialas en cuanto puedas desde la pantalla **Admin**.

---

## Qué necesita para funcionar

- **.NET 8** (el proyecto ya se puede publicar *autocontenido*, o sea, no hace falta instalar .NET en el servidor: ver `publicar.bat`).
- **MySQL** en `127.0.0.1:3306` con estas dos bases:
  - `meditechnosys_intranet` → tablas `usuarios`, `bitacora`, `actividades`, `reportes_estatus`,
    `historial_cambios`, `permisos`, `configuracion`, `inventario`, `playbook`,
    `almacen_movimientos`, `roles`, `usuario_permisos`, `avisos`, `actas` y `credenciales`. **Se crean solas**
    la primera vez (y se agregan las columnas nuevas al arrancar).
  - `RepoPato` → tablas `reportes` y `auditoria`. **Debe existir** (la crea el esquema de Patología; la app no la fabrica).
- **Red del hospital** reachable para el DICOM (nodos PACS `ASYN02/03/04/07` en el puerto 104) y el puerto **11113** libre para recibir imágenes.
- Una carpeta de almacenamiento para los PDF de Patología: por defecto `C:\Pato`
  (se cambia en `appsettings.json`). Los PDF del Playbook van en `Playbook:StorageDir`
  (por defecto `App_Data/playbook`).

---

## Estructura de carpetas (todo el código)

```
Intranet_Unificada/
├── Program.cs                     ← Arranque: login, seguridad, rutas y registro de servicios.
├── Meditechnosys.Intranet.csproj  ← Definición del proyecto y paquetes (fo-dicom, MySql, ClosedXML…).
├── appsettings.json               ← Configuración real: bases de datos, nodos PACS, puertos, carpetas.
├── appsettings.Development.json   ← Usuarios semilla para desarrollo (admin/ing).
├── publicar.bat                   ← Genera la versión final autocontenida en publish\.
├── actualizar.bat                 ← Detiene el servicio, republica y reinicia (todo en uno).
├── instalar-servicio.bat          ← Instala el servicio de Windows (arranque automático).
├── desinstalar-servicio.bat       ← Elimina el servicio.
│
├── Models/                        ← Roles, hospitales, actividades/tickets, reportes, permisos,
│   │                                 inventarios, documentos y los "catálogos" de accesos.
│   ├── Actividad.cs               ← Elemento unificado (Actividad/Incidente/Ticket) + Hospital: los tres estados, prioridad y el recordatorio de 3 días.
│   ├── Hospitales.cs              ← HES / HGR / HGO / HI (+ General), validación y normalización.
│   ├── Reporte.cs                 ← Reporte de estatus entregado por el ingeniero.
│   ├── Cambio.cs                  ← Línea del historial de cambios (nunca se borra).
│   ├── Permisos.cs                ← Catálogo versionado de permisos y matriz por defecto de cada rol.
│   ├── Inventario.cs              ← Colecciones (equipos / ips / aetitles), sus campos y validaciones.
│   ├── Almacen.cs                 ← Movimientos del almacén (entrada/salida/traslado), estados, motivos y reglas.
│   ├── Documento.cs               ← Documento del Playbook (metadatos + versión + archivo PDF).
│   ├── Catalog.cs                 ← Tarjetas de las pantallas Ingeniería y Admin, agrupadas por sección.
│   ├── Audit.cs                   ← Eventos de la bitácora.
│   └── Roles.cs                   ← Roles y políticas de autorización.
├── Services/                      ← Usuarios, autenticación (MySQL o JSON), reglas, bitácora,
│   │                                 Gestión, inventarios, Playbook y exportación a Excel.
│   ├── UserStore.cs               ← Almacén JSON + hash PBKDF2 + interfaz IUserStore.
│   ├── MySqlUserStore.cs          ← Almacén MySQL (crea base y tabla solo).
│   ├── AuthStoreInfo.cs           ← Qué almacén quedó activo y por qué (avisa si MySQL cayó).
│   ├── IAuthenticator.cs          ← Validación de credenciales (interfaz + implementación).
│   ├── Bitacora.cs                ← Bitácora de accesos (MySQL o JSONL) + interfaz IAuditLog.
│   ├── Actividades.cs             ← Almacén de actividades/tickets (MySQL o JSON) + IActivityStore.
│   ├── Reportes.cs                ← Almacén de reportes de estatus (MySQL o JSON) + IReportStore.
│   ├── Historial.cs               ← Historial de cambios append-only (JSONL o MySQL) + IHistorialStore.
│   ├── Permisos.cs                ← Matriz de permisos por rol (MySQL o JSON) + IPermissionStore.
│   ├── Config.cs                  ← Configuración del módulo (MySQL o JSON) + IConfigStore.
│   ├── Catalogos.cs               ← Catálogos editables desde Programador (actividades, subopciones,
│   │                                 servicios por hospital y hospitales), guardados como JSON en configuración.
│   ├── Gestion.cs                 ← Reglas de negocio del módulo Gestión (GestionService).
│   ├── Inventarios.cs             ← Almacén de los tres inventarios (MySQL o JSON) + IInventarioStore.
│   ├── Inventario.cs              ← Reglas y permisos de los inventarios (InventarioService).
│   ├── AlmacenStore.cs            ← Almacén de movimientos del almacén (MySQL o JSON) + IAlmacenStore y folios.
│   ├── Almacen.cs                 ← Reglas del almacén: existencias, confirmar/recibir/cancelar y stock (AlmacenService).
│   ├── Documentos.cs              ← Almacén de metadatos del Playbook (MySQL o JSON) + IDocumentoStore.
│   ├── Playbook.cs                ← Reglas del Playbook: PDF, versiones, hospital (PlaybookService).
│   ├── Historico.cs               ← Consulta del histórico único y armado del Excel (HistoricoExportador).
│   ├── Sesion.cs                  ← `SesionPreferencias`: las seis ventanas de inactividad por usuario, su clave
│   │                                 en el almacén de configuración y las reglas de vencimiento/renovación.
│   ├── Excel.cs                   ← Escritura de .xlsx con ClosedXML (compartida por todos los módulos).
│   └── UserRules.cs               ← Reglas de nombre/rol/contraseña compartidas.
├── Pages/                         ← Pantallas: Login, Index, Ingenieria, Admin, Logout, Error y
│   │                                 ListaTrabajo (`/dicom/lista-trabajo`).
│   └── Shared/                    ← Las dos cabeceras: `_Layout.cshtml` (intranet, con `intranet.css`)
│                                     y `_LayoutPanel.cshtml` (formato de panel, con `panel.css`; solo la
│                                     usa la lista de trabajo). `_PrefDialog.cshtml` —el diálogo ⚙ escrito
│                                     una sola vez— solo lo pinta `_Layout.cshtml`, y solo en el inicio.
│
├── Patologia/                     ← Repositorio de reportes (100% C#, reemplaza al viejo Python).
│   ├── PatoDb.cs                  ← Conexión a MySQL, áreas, auditoría, rutas de archivos.
│   ├── Repositorio.cs             ← Guarda un PDF: valida, deduplica, escribe archivo + fila en BD.
│   ├── Extractor.cs               ← Lee el PDF y saca folio, fecha, nombre, NSS, agregado, modalidad.
│   ├── Importador.cs              ← Importa por carpetas "inbox" y por Excel.
│   └── PatoApi.cs                 ← API /api/patologia/* (buscar, subir, editar, borrar, Excel, PDF).
│
├── Dicom/                         ← Visor DICOM (fusionado desde el viejo "DicomQrApi").
│   ├── DicomQrService.cs          ← Motor fo-dicom: C-FIND (buscar) y C-MOVE/C-GET (descargar) + ZIP.
│   ├── DownloadJobService.cs      ← Seguimiento de descargas en segundo plano (barra de progreso).
│   ├── DepuradorDescargas.cs      ← Reglas de limpieza: antigüedad, ZIP, carpetas vacías y tope de espacio.
│   ├── LimpiadorDescargas.cs      ← Vigilante interno: repasa la carpeta de descargas cada hora.
│   ├── Models.cs                  ← Tipos: nodos PACS, estudio, petición/resultado de descarga.
│   ├── DicomPruebaStore.cs        ← Registro permanente (JSONL) de las pruebas DICOM enviadas y de lo que
│   │                                 respondió el nodo al preguntarle por ese estudio.
│   ├── WorklistService.cs         ← Lista de trabajo (MWL): C-FIND de programados, C-ECHO, prueba C-STORE y
│   │                                 C-FIND de Study Root para verificar qué guardó el destino.
│   ├── WorklistModels.cs          ← Tipos y validación en español de la consulta y de la prueba.
│   ├── ContrastTestImage.cs       ← Círculo de contraste sintético (Secondary Capture; serie e instancia nuevas,
│   │                                 estudio = el programado cuando la prueba va sobre una entrada de la lista).
│   │                                 Qué dato de la entrada escribe en los tags lo deciden las casillas de la pantalla.
│   └── Controllers/               ← API /api/dicom/* (StudiesController, FilesController y WorklistController).
│
├── wwwroot/                       ← Archivos estáticos (lo que ve el navegador).
│   ├── img/                       ← Logos compartidos (IMSS dorado y marca de agua).
│   ├── admin/                     ← Páginas exclusivas de Admin.
│   │   ├── usuarios/              ← Gestión de usuarios (formato DICOM).
│   │   ├── bitacora/              ← Reportes · pestaña Bitácora (lista de usuarios y su ventana de movimientos).
│   │   ├── roles/                 ← Matriz de roles y permisos del módulo Gestión.
│   │   ├── reportes/              ← Reportes · pestaña Reportes de estatus (avances entregados).
│   │   └── config/                ← Habilitar fecha límite y periodicidad de reportes.
│   ├── gestion/                   ← Páginas del módulo Gestión (Admin e Ingeniero, según permiso).
│   │   ├── historico/             ← Reportes · pestaña Histórico de actividades (los tres tipos + Excel).
│   │   ├── inventario-equipos/    ← Inventario de equipos: pantalla única (pestaña Ingresos).
│   │   ├── inventario-ips/        ← Inventario de IPs por hospital.
│   │   ├── inventario-aetitles/   ← Inventario de AETitle por hospital.
│   │   └── playbook/              ← Biblioteca de documentación técnica en PDF.
│   ├── almacen/                   ← index.html redirige a /gestion/inventario-equipos/;
│   │   │                             hoja.html muestra la hoja imprimible de entrada/salida.
│   ├── calendario/                ← Calendario tipo Google con panel de tickets a la izquierda.
│   ├── patologia/consulta/        ← Pantalla principal: buscar, ver PDF, editar (modal) y botón Subir.
│   ├── patologia/subida/          ← Solo carga de PDF y Excel (YA NO pide usuario ni edita y
│   │                                 YA NO tiene botón «Salir»: arriba quedan Regresar y Home).
│   ├── dicom/index.html           ← Pantalla del visor DICOM.
│   ├── js/gestion.js              ← Lógica compartida del módulo Gestión (diálogos, detalle, permisos).
│   ├── js/inventario.js           ← Lógica compartida de los tres inventarios (dirigida por esquema).
│   ├── js/intranet.js             ← Script global de las páginas Razor (reloj, búsqueda, estado).
│   ├── js/theme.js                ← Tema de la cuenta (Oscuro/Claro) y tamaño de la letra (`window.MeditechnosysApariencia`);
│   │                                 pinta `data-theme`/`data-fuente` antes del primer trazo.
│   ├── js/preferencias.js         ← El diálogo ⚙, que solo se carga en el inicio: letra, tiempo de sesión
│   │                                 (por cuenta) y parámetros del módulo Gestión (solo con `configuracion.administrar`).
│   ├── js/filtro.js               ← `window.Filtro`: búsqueda sin acentos ni mayúsculas, en todas las pantallas.
│   ├── js/hoja-fecha.js           ← `window.HojaFecha`: la hoja de periodo (Día · Mes · Año · Rango) que se
│   │                                 instala sobre los dos `<input type="date">` de las pantallas que consultan
│   │                                 fechas (reportes, histórico, consulta, DICOM y reenvío).
│   ├── js/reportes-tabs.js        ← Barra de pestañas común de **Reportes** (Bitácora · Histórico de
│   │                                 actividades · Reportes de estatus); solo muestra las que el permiso
│   │                                 efectivo autoriza y cada una conserva su ruta y su guard del servidor.
│   └── css/                       ← intranet.css y login.css (Razor) + panel.css (páginas de panel)
│                                     + hoja-fecha.css (la hoja de periodo; funciona con tema claro y oscuro).
│
└── App_Data/                      ← Respaldo en archivos cuando MySQL no responde, y los PDF del
                                      Playbook (`App_Data/playbook/`). Contiene users.json,
                                      actividades.json, reportes.json, historial.jsonl, permisos.json,
                                      configuracion.json, inventario.json, almacen.json, playbook.json,
                                      avisos.json, actas.json, credenciales.json y las fotos de las
                                      credenciales (`App_Data/credenciales/`), además de bitacora.jsonl y
                                      dicom-pruebas.jsonl (las pruebas DICOM enviadas).
```

---

## Cómo se usa (flujo normal)

1. Abres **http://localhost:5023** → te manda a **/Login**.
2. Inicias sesión. Según tu rol:
   - **Admin** → ve **Admin** (catálogo con los grupos *Gestión*, *Accesos* y *Seguridad*) e **Ingeniería**.
   - **Ingeniero** → ve **Ingeniería** (subsistemas + el grupo *Gestión*).
3. En **Ingeniería** los accesos están agrupados:
   - *Patología* → **Consulta de reportes**.
   - *Imagen médica* → **Visor de estudios DICOM** y **Consulta lista de trabajo** (programados del nodo,
     filtros por nombre, ID, número de acceso, fecha, hora, modalidad y procedimiento, prueba técnica de envío al
     mismo nodo y registro de lo que el nodo dice tener).
   - *Gestión* → **Calendario de actividades**,
     **Inventario de equipos** (pantalla única de equipos), **Inventario de IPs**,
     **Inventario de AETitle** y **Playbook**.
   - El **Histórico de actividades** y los **Reportes de estatus** ya no son tarjetas sueltas: viven
     dentro de la tarjeta **Reportes**, cada uno en su pestaña (junto con la **Bitácora**).
   (La pantalla de subida de Patología **no tiene acceso directo**: se entra desde Consulta con el
   botón **Subir**. El **Histórico de tickets ya no existe**: todo se consulta en el
   *Histórico de actividades* filtrando por `Tipo = Ticket`.)
4. En **Consulta de reportes**:
   - Eliges el **Área** (Especialidades / Ginecología) en el combo box de arriba.
   - Buscas por folio/NSS/nombre, ves el PDF con **Ver** y corriges datos con **Editar**
     (se abre una **ventana emergente** centrada).
   - El botón **Subir** (junto a *Descargar Excel*) te lleva a la pantalla de carga con el área ya elegida.
5. En **Subida**: arrastras PDFs o importas un Excel. El sistema **ya sabe quién eres** (tu sesión):
   no te pide usuario, solo el archivo y el área.
6. **Permisos de edición/borrado:**
   - **Ingeniero** → subir y editar. **No** puede eliminar.
   - **Admin** → subir, editar **y eliminar** (el botón rojo "Eliminar documento" solo lo ve Admin).

> Si después de un cambio no ves el diseño nuevo, pulsa **F5**: las páginas `.html` se sirven sin caché.

---

## Inicio: botón ⚙ Configuración (tema, letra y sesión)

Junto a **Salir**, **en la cabecera del inicio (`/`)**, está **⚙ Configuración**. La ventana abre
cuatro bloques; se puede abrir también con el enlace directo `http://localhost:5023/?configuracion=1`.
**El interruptor Día / Noche de la cabecera ya no está**: se quitó de las dos cabeceras y el tema se escoge
dentro del ⚙, con **Oscuro** y **Claro**.

| Bloque | Qué hace | Dónde se guarda |
|---|---|---|
| **Tema** | **Oscuro** (como abre por defecto) o **Claro**, para todas las pantallas de la intranet y para los paneles. | En **tu cuenta** (`GET`/`PUT /api/tema`), y se anota en la bitácora. El navegador además lleva una copia en una cookie (`meditechnosys.tema`) para pintar sin esperar a nadie. |
| **Tamaño de la letra** | Chica (88%), Normal, Grande (118%) agranda el texto de todas las pantallas. | En **este equipo** (`localStorage`). |
| **Tiempo de sesión** | 15 min, 30 min, 1 h, 2 h, 4 h u 8 h **sin hacer nada**. Al cumplirse, pide la contraseña otra vez. | En **tu cuenta** (`GET`/`PUT /api/sesion`), y se anota en la bitácora. |
| **Parámetros del módulo Gestión** | Fecha límite y periodicidad de reportes. **Solo aparece con `configuracion.administrar`.** | En la configuración común (`/api/config`). |

- La ventana se cierra con **Esc**, con la **X**, con un clic fuera de ella o volviendo a pulsar ⚙.
- El **tema es de tu cuenta, no del equipo**: entra desde cualquier pantalla del hospital y la ves como la
  dejaste. Quien nunca lo cambió lo ve **Oscuro**.
- El tiempo de sesión es una **ventana de inactividad**, no la vida total de la sesión: si trabajas
  a diario no se corta. Se renueva solo cuando pasa la mitad de la ventana elegida.
- **8 horas es el máximo** que permite el servidor; elegir más no se puede prometer.
- Al cumplirse la ventana, la siguiente pantalla que abras te regresa al acceso con un recuadro azul:
  «Tu sesión terminó por inactividad». Entra con tu contraseña de siempre.
- La letra se agranda sobre el texto (unidades `rem`); los botones y la propia ventana mantienen su
  tamaño en píxeles para que la pantalla no se descoloque.
- Está **solo en el inicio**. Las demás pantallas —la intranet normal, el formato de panel de
  **Consulta lista de trabajo** (`/dicom/lista-trabajo`) y los paneles autónomos— ya no traen el botón ⚙:
  ahí no hay ventana de configuración que abrir. Para cambiar algo se vuelve al **Inicio** (el botón *Home*
  de cualquier pantalla lleva ahí) y se usa el ⚙ de esa cabecera.
- **El tema sigue viéndose en todas partes**: lo que se movió es el mando, no el efecto. La elección vive en
  tu cuenta y todas las pantallas la leen, abran donde abran.

---

## Bandeja de correo (soporte + segundo correo)

La tarjeta **Bandeja de soporte** (`/admin/correos/`, permiso `correo.ver`) maneja **dos cuentas a la
vez**, no una sola como antes:

| Cuenta | De dónde sale | Quién la ve |
|---|---|---|
| **Buzón de soporte** | `appsettings.json`, sección `Correo:Imap` / `Correo:Smtp` | siempre, todo el que tenga el permiso |
| **Segundo correo** | el buzón que el administrador enlaza al usuario en **Usuarios** (bloque *Buzón de correo*; tabla `usuario_buzones`) | su pestaña solo aparece si el administrador lo conectó |

- **Cuál se ve.** Arriba de las carpetas están las tarjetas de cuenta: *Buzón de soporte* **siempre** y
  *Segundo correo* **solo si ya está enlazado**, cada una con su dirección escrita; la activa se marca en
  azul. Un clic cambia de bandeja, y
  **Recibidos / Enviados / Borradores se leen de la cuenta activa** (los identificadores de los mensajes son
  propios de cada buzón). Se arranca siempre en soporte; la elección dura lo que dura la pestaña, porque se
  recuerda en `sessionStorage`.
- **Con cuál se envía.** **Redactar** abre la ventana con el selector **Enviar con**, que trae solo las
  cuentas disponibles y viene marcado en la de soporte. Si se cambia a la otra mientras se responde un
  mensaje, la ventana avisa que el original no viaja: su identificador pertenece a la otra bandeja, así que
  se sueltan la cita técnica, «Responder a todos» y «adjuntar el original» (el texto ya va escrito en el
  cuerpo).
- **Cuando no hay segundo correo.** La bandeja muestra **una sola pestaña**, la de soporte: la tarjeta del
  segundo correo **ya no aparece punteada con «Sin enlazar»** (antes ocupaba su sitio con un botón que no
  abría nada). En el editor, «Enviar con» solo ofrece el soporte. Si el enlace **sí existe pero no abre**
  (falta la contraseña o está incompleto), la segunda pestaña **sí** sale —punteada y con el motivo— porque
  ahí sí hay algo que arreglar.
- **La captura que pegas se ve y se ajusta.** Al insertar o pegar una imagen, el cuerpo la muestra (antes salía
  el icono roto: el editor pintaba la referencia `cid:`, que solo entiende el correo). Al hacer clic en ella
  aparece la barra **Imagen** con su nombre y su medida, los botones *Original · 75% · 50% · 25% · Ancho* y
  *Quitar imagen*, más un cuadrito azul en la esquina que se arrastra para dejarla del tamaño que se quiera
  (entre 60 px y el ancho del mensaje). El tamaño se manda escrito en el HTML (`width=`), pero **el archivo que
  viaja es el original completo**: ajustar no recorta ni re-muestrea la imagen.
- **Nada de contraseñas sueltas.** La del segundo correo se guarda cifrada (AES-256-GCM, con la clave en
  `App_Data/buzon-clave.bin` / `Auth:BuzonKeyFile`) y nunca sale hacia el navegador: ni en las listas, ni
  en los avisos, ni en la bitácora.
- **API.** Todas las lecturas y envíos aceptan `cuenta` (`soporte` | `propio`; sin parámetro se usa
  soporte): `GET /api/correos?carpeta=&cuenta=`, `GET`/`DELETE /api/correos/{uid}` con sus adjuntos e
  inline, `POST /api/correos/enviar`, `POST /api/correos/borrador` y `POST /api/correos/{uid}/responder`.
  `GET /api/correos/mio` ya no devuelve un solo buzón: devuelve `cuentas` (soporte y el segundo correo, con
  su estado, su aviso y su marca `enlazado`) y `predeterminada`. El enlace por usuario lo siguen gestionando `GET`/`PUT
  /api/usuarios/{u}/buzon` y `POST /api/correos/probar`.

---

## Gestión de usuarios (página propia)

La antigua pantalla **/Admin** ya **no existe como tal**: ahora **redirige a la página de inicio `/`**,
que es **única** y muestra solo las herramientas a las que el usuario tiene acceso (ver *Roles y
permisos*). Las tarjetas de **usuarios** y **bitácora** viven en sus propias páginas, cada una con el
formato del repositorio DICOM (cabecera negra, marca de agua, botones *Regresar*/*Home*):

- **/admin/usuarios/** — tabla de usuarios con **+ Crear usuario** (ventana emergente con la lista de
  roles **dinámica**, leída de `GET /api/roles`). Cada fila muestra **Nombre**, **Cargo** y **Región**
  además de usuario, rol, alta y estado, y tiene **Editar** (rol, nombre, cargo, **región que ve**,
  estado y/o contraseña),
  **Permisos** (editor de ajustes por usuario: conceder/revocar sobre los heredados del rol, con columna
  *Efectivo* en vivo), **Desactivar/Activar** y **Eliminar**.
- **/admin/bitacora/** — lista de usuarios; al hacer clic se abre su ventana con todas sus actividades.

Detalles de usuarios:

- Los cambios viajan por la API `/api/usuarios` (protegida con el permiso efectivo
  `usuarios.administrar`, política `P:usuarios.administrar`) y se guardan en el almacén configurado:
  **MySQL** (`meditechnosys_intranet.usuarios`) o, si se configura `json`, en `App_Data/users.json`.
- El **rol** de un usuario debe existir en el almacén de roles; al cambiarlo se **invalida la caché** de
  permisos para que la sesión refleje el nuevo rol al momento.
- Un usuario **desactivado** no puede iniciar sesión y, si ya tenía sesión abierta, se le cierra
  sola en la siguiente petición. Nunca se puede desactivar al último administrador activo.
- **Si MySQL no responde al arrancar**, la intranet **no se cae**: usa el archivo JSON de
  respaldo, lo avisa con un recuadro amarillo en la propia página de usuarios y lo reporta en
  `/api/status`. Cuando MySQL vuelva, el siguiente arranque lo retoma y migra lo creado en JSON.
- Reglas: usuario de 3–40 caracteres (`letras, números, . - _`), contraseña mínima de 8, rol de hasta
  20 caracteres (`ReglasRol`), y nunca se puede dejar el sistema sin al menos un administrador.
- Cada usuario guarda su **nombre de persona** (máx. 120) y su **cargo** (máx. 80). Con ellos, al elegir
  un usuario ligado en un **acta** o una **credencial** se **autocompletan el nombre y el puesto**, y el
  selector muestra "usuario — Nombre (rol)". El cambio de estos datos deja el evento de bitácora
  *Edición de datos de usuario*.


### Reportes (pestañas: Bitácora · Histórico de actividades · Reportes de estatus)

La tarjeta **Reportes** de la página de inicio abre la **Bitácora** y trae arriba las pestañas de las tres
consultas: **Bitácora**, **Histórico de actividades** y **Reportes de estatus**. No es una página nueva ni un
empotrado: cada pestaña es su ruta de siempre (`/admin/bitacora/`, `/gestion/historico/`, `/admin/reportes/`),
con sus propios filtros, su Excel y su guard en el servidor, así que los marcadores y los enlaces viejos
siguen funcionando y **nadie ve una pestaña que no tenga autorizada** (las lista `js/reportes-tabs.js` con
`/api/permisos/mios`). Quien solo tiene permiso de una de las dos consultas entra directo a esa pestaña: la
tarjeta no regala accesos.

La página **/admin/bitacora/** es **la lista de usuarios**: quién está conectado, su última conexión y
desde dónde. **Al hacer clic en un usuario se abre una ventana flotante con todas sus actividades**,
mezcladas por hora —lo que pasó en la intranet y lo que pasó en el programa de grabación DICOM
(`DicomQrDesktop`)—. Sus columnas son **Fecha · Hora · Usuario · Actividad · IP · Equipo · Ubicación
geográfica · Qué fue lo que hizo**. Ya no hay una tabla general de movimientos en la hoja: la única
forma de entrar a los renglones es el clic en una persona.

Cada renglón original se traduce a esa forma común al consultar (`Models/BitacoraUnificada.cs`); las
dos tablas siguen guardándose aparte y con su historial intacto. En la columna *Actividad* aparecen,
entre otras: *Inicio de sesión*, **Intento de sesión fallido** (cuando alguien escribió mal la
contraseña, en la web o en el mostrador), *Cierre de sesión*, *Abrió una página*, *Búsqueda de
estudios*, *Descarga de estudios*, *Quemado en USB*, *Quemado en CD/DVD*, *Descarga de Excel* y los
eventos con nombre propio (alta/edición/baja de usuarios, actividades, inventarios, Playbook,
permisos y configuración). *Qué fue lo que hizo* escribe la frase completa —«Buscó estudios del
paciente «PEREZ JUAN CARLOS»», «Quemó … en USB E: (PQI USB 32GB) · 296 imagen(es)»— y debajo dice de
qué bitácora salió el renglón. La ubicación geográfica se deduce de la IP **al consultar** (nunca se
guarda por renglón), así que corregir un rango acomoda también los registros viejos.

**La ventana** pide por su cuenta los **2.000 movimientos más recientes de esa persona** —o los 2.000 más
recientes **del periodo** que se elija, si lo hay—, así que sale completa siempre. Trae su propia barra de
filtros —periodo, hora, actividad, de qué bitácora salió y texto libre—, sus contadores, el filtro de lote
pulsando el enlace «Lote …» de un renglón, y los botones **Limpiar filtros**, **Descargar su Excel** (lo de
ese usuario con ese filtro puesto; 20 columnas, incluidas paciente, ID, acceso, UID, medio, unidad y lote,
y sin el tope de 2.000) y **Cerrar**. Se cierra con el botón, con `Esc`, o pulsando fuera del recuadro, y al
cerrarse la fila deja de estar marcada. Cuando el recorte viene vacío la ventana lo dice con precisión —«no
tiene movimientos registrados» si de plano no tiene historia, o «ningún movimiento del 14/08/2026 al
20/08/2026» si el hueco es del periodo—, y mientras el servidor contesta el recorte nuevo avisa
«(cargando…)» en lugar de afirmar que no hay nada.

**La hoja de Periodo no usa el calendario del sistema** (lo pinta Windows, no lleva nuestra paleta y no sabe
qué días tienen registros). Es una hoja propia, del color del tema, con cuatro modos —**Día**, **Mes**, **Año**
y **Rango**— que se eligen arriba mismo:

- **Día**: rejilla de 7 columnas desde el **lunes**, con **en dorado solo los días en los que esa persona dejó
  movimientos** —el número de renglones se lee al pasar el cursor— y los demás apagados y sin clic; las flechas
  solo avanzan hacia meses que sí tienen historia. Abajo: **Hoy**, **Ayer**, **Cualquier día**.
- **Mes**: los doce meses del año a la vista, habilitados solo los que dejaron renglones; las flechas cambian
  de año y se apagan cuando ningún mes de ese año tiene historia. Abajo: **Este mes**, **Mes pasado**,
  **Cualquier mes**.
- **Año**: un botón por cada año con movimientos («Años con movimientos: 2025, 2026»). Abajo: **Este año**,
  **Cualquier año**.
- **Rango**: dos clics en cualquier día —el orden no importa, se acomoda solo— y las flechas **nunca** se
  apagan, porque los extremos pueden caer en meses vacíos. Abajo: **7 días**, **30 días**, **Todo** (de su
  primer movimiento al último) y **Cualquier día**.

**La hoja de Hora** sigue el mismo idioma con dos modos: **Una hora** deja elegir solo las horas que tuvieron
movimientos dentro del periodo cargado y **Rango** permite dos clics sobre las 24. Abajo trae **Todo el día**
(00:00–23:59), **Mañana** (06:00–12:59), **Tarde** (13:00–19:59) y **Cualquier hora**.

Lo elegido se guarda en los campos ocultos (`#vDia`/`#vDiaHasta`/`#vModo` y `#vHora`/`#vHoraHasta`) y viaja al
servidor como `desde`/`hasta` en `yyyy-mm-dd`, con los dos extremos incluidos, así que el recorte de la tabla,
el Excel y los enlaces comparten un solo contrato; el enlace también acepta el formato viejo `dd/mm/aaaa`. Al
cambiar de periodo la ventana **vuelve a pedir al servidor** los renglones de ese recorte (por eso un año con
2.100 movimientos se ve completo, en vez de los 2.000 más recientes de toda la historia). Con la hoja abierta,
la primera `Esc` o el primer clic fuera la cierran a ella; la ventana se cierra con la segunda.

La página abre con `bitacora.ver` **o** `quemador.ver`, y dentro de ella cada quien recibe solo la
parte que le corresponde: quien tiene únicamente `quemador.ver` no puede consultar `/resumen`, así que
su lista se arma con los renglones del mostrador y los rótulos cambian a **Registros** y **Último
movimiento** (no promete conexión de lo que no ve). **El usuario y la IP los fija el servidor**, no el
programa del mostrador, así que no se pueden falsear desde una máquina.

Por enlace también se entra directo a una ventana: `?usuario=X` la abre sola, y puede traer los filtros que se
aplican dentro de ella —`dia=yyyy-mm-dd`, `mes=yyyy-mm`, `anio=aaaa`, `desde=` y `hasta=` para un rango,
`hora=` y `horaHasta=` para una hora o un tramo, además de `actividad`, `fuente`, `texto` y `lote`—.

**/admin/quemador/** ya no es una pantalla: es un aviso que redirige a la Bitácora con `?fuente=quemador`
puesto, para no romper marcadores ni enlaces antiguos.

| API | Para qué |
|---|---|
| `POST /api/quemador/eventos` | El quemador reporta su actividad (requiere sesión) |
| `GET /api/quemador/eventos` · `/valores` · `/export` | La consulta y el Excel propios del mostrador (siguen existiendo) |
| `GET /api/bitacora/unificada` | Los renglones ya mezclados y traducidos, con `puedeAccesos` / `puedeQuemador`; con `usuario=&limite=2000` es la ventana flotante de esa persona, y `desde=&hasta=` la recorta por periodo |
| `GET /api/bitacora/unificada/export` | Excel de la ventana (`usuario=` más sus filtros, incluidos `desde`/`hasta` y `hora`/`horaHasta`) |
| `GET /api/bitacora/periodos` | Cuántos renglones dejó esa persona en **cada día de toda su historia** —lo que pinta en dorado la hoja de periodo y limita meses, años y horas a lo que de plano existe— |
| `GET /api/bitacora/resumen` | Quién está conectado y desde dónde (la lista de usuarios) |

Detalle completo (esquema, API, seguridad y flujo de los modales):
**[`docs/USUARIOS-Y-MYSQL.md`](docs/USUARIOS-Y-MYSQL.md)**.

Los eventos viven en las tablas MySQL `bitacora` y `bitacora_quemador` (se crean solas al arrancar)
y, si `Auth:Store` es `json`, en `App_Data/bitacora.jsonl` y `App_Data/bitacora_quemador.jsonl`.
Almacenes: `Services/Bitacora.cs` y `Services/BitacoraQuemador.cs`.

---

## Módulo Gestión

Los paneles **/Admin** e **/Ingenieria** agrupan las herramientas en secciones. La sección
**Gestión** reúne estas pantallas (la *Gestión de usuarios* y la *Bitácora* viven en secciones
aparte, fuera de Gestión; el *Histórico de actividades* y los *Reportes de estatus* son ahora
pestañas de la tarjeta **Reportes**). **No existe "Histórico de tickets"**: el histórico es único.

| Pantalla | Ruta | Admin | Ingeniero | Para qué |
|---|---|:---:|:---:|---|
| Roles y permisos | `/admin/roles/` | ✔ | — | Matriz editable de qué puede hacer cada rol. |
| Programador | `/admin/programador/` | solo root | — | Edita los catálogos de Gestión: actividades, subopciones (los aplicativos de una Capacitación), los servicios de cada hospital y las regiones médicas (una línea por región; al seleccionarla salen sus hospitales). Ver *Programador (catálogos de Gestión)*. |
| Calendario de actividades | `/calendario/` | ✔ | ✔ | Crear y dar seguimiento a **actividades e incidentes** (los tickets se registran como incidencias; los ya creados se siguen viendo en el panel de Tickets). |
| Histórico de actividades | `/gestion/historico/` | ✔ | ✔ | Pestaña de **Reportes**. **Consulta única** de los tres tipos con filtros y exportación a Excel. |
| Reportes de estatus | `/admin/reportes/` | ✔ | — | Pestaña de **Reportes**. Histórico de los reportes de avance entregados. |
| Inventario de IPs | `/gestion/inventario-ips/` | ✔ | ✔ | Direcciones IP por hospital (solo Admin da de alta/edita). |
| Inventario de AETitle | `/gestion/inventario-aetitles/` | ✔ | ✔ | AE Titles DICOM por hospital (solo Admin da de alta/edita). |
| Playbook | `/gestion/playbook/` | ✔ | ✔ | Documentación técnica en PDF (solo Admin la administra). |
| Inventario de equipos | `/gestion/inventario-equipos/` | ✔ | ✔ | **Pantalla única de equipos** con pestaña *Ingresos*: selector de unidad (Almacén/HES/HGR/HGO/HI), **+ Añadir equipo** y **Descargar Excel** (solo Admin), y en cada fila **Hoja entrada**, **Hoja salida** (descuenta y, con destino, re-ingresa) y **Editar**. Sustituye al antiguo *Almacén de equipos*; `/almacen/` redirige aquí. |
| Configuración | `/admin/config/` | ✔ | — | **Ya no tiene tarjeta en el catálogo**: la fecha límite y la periodicidad se ajustan en el botón **⚙ Configuración** del inicio. La pantalla sigue abierta para quien llegue por enlace directo. |

### Clasificación por hospital

**Todo** lo del módulo se guarda etiquetado con un hospital: `HES`, `HGR`, `HGO` o `HI` (el
Playbook admite además `General`). El hospital es **obligatorio** al crear un registro y aparece
como combo box *Seleccione hospital* tanto en el alta como en la consulta; los históricos y los
tres inventarios tienen **filtro por hospital** y el Excel respeta el hospital elegido.
Esos cuatro códigos vienen en el código (`Models/Hospitales.cs`) y **no se pueden quitar**; si hace
falta otro, se escribe en la tarjeta de su región dentro de **Programador** (bloque *Regiones médicas*)
y queda disponible en todos los módulos sin tocar la base de datos.

### Elementos: actividades, incidentes y tickets

Todo se modela con **un único elemento** (`Actividad`) que lleva los campos `Tipo`
(**Actividad**, **Incidente** o **Ticket**) y `Hospital`. Los tickets reciben un **número
consecutivo** (`#001`, `#002`…). Esto evita duplicar tablas y pantallas: el *Histórico de
actividades* muestra los tres tipos con filtro por **Tipo**, **Hospital**, ingeniero, estatus,
prioridad y rango de fechas, y el panel izquierdo del calendario muestra solo los **tickets**
como panel de trabajo rápido.

- **Estados: solo tres.** `Pendiente` → `Completada`, y desde `Pendiente` también `Cancelada` (con
  justificación). «En proceso» y «Cerrada» se retiraron de toda la intranet: los registros viejos que
  todavía los traen se traducen al leerlos (`EnProceso` → `Pendiente`, `Cerrada` → `Completada`), así que
  ninguna pantalla los muestra. **Cancelar** es decisión de quien administra (permiso **`items.cancelar`**,
  que el rol Admin trae por defecto): exige **justificación** y **congela** el elemento —ya no se edita,
  no se reasigna, no se completa, no admite comentarios ni evidencia ni reportes y **no se reabre**; solo
  queda el registro con su historial. La única vuelta atrás es del **Administrador** (permiso
  `items.reabrir`): puede **Reabrir** una actividad `Completada` y la regresa a `Pendiente` con
  **su evidencia intacta**. **Vencida** no es un estado guardado: se calcula cuando la fecha
  límite ya pasó y el elemento sigue abierto.
- **Prioridad:** Baja / Media / Alta / Urgente. **Avance:** el porcentaje del elemento ya no se usa —el
  seguimiento se escribe en los **comentarios** y la tarjeta muestra cuándo fue el último—; los **reportes
  de estatus** sí conservan su porcentaje.
- **Recordatorio cada 3 días:** cuando un elemento abierto lleva **3 días o más sin comentarios** (desde el
  último comentario, o desde la fecha de inicio si todavía no tiene ninguno) la intranet lo recuerda: la
  tarjeta pinta el aviso naranja **«Favor de registrar avances en comentarios.»** y el chip del calendario
  gana una marca también naranja con un `!`. Escribir un comentario u observación **reinicia la cuenta**.
- **Quién crea:** **Admin e Ingeniero** crean **actividades e incidentes** desde el calendario; en el
  diálogo **+ Nuevo** el tipo ya solo ofrece esas dos opciones (si hace falta un ticket, se registra
  como incidencia). Las **actividades** se comportan distinto según quién las registre: el **Ingeniero**
  las anota **ya terminadas y en un solo envío** —su ventana de alta trae solo Tipo, Hospital, Servicio y
  Actividad (sin Descripción) y el botón **Crear** no guarda nada: abre la ventana
  **«Registrar y completar actividad»**, con **Informe del trabajo realizado (opcional)** y **Evidencia**
  (foto o archivo) **obligatoria**, y es esa ventana la que da de alta el elemento ya en `Completada`. Si la
  cierra sin enviar, no quedó nada guardado—, mientras que el **Admin** las crea en `Pendiente` para
  luego asignarlas. Los **incidentes** no cambian: cualquiera los registra en `Pendiente` para que el
  **Admin los revise y asigne**.
  Los tickets existentes siguen visibles en el panel **Tickets** del calendario y en el Histórico, y ahí
  el Admin los asigna, edita o cancela. El Ingeniero **no** asigna formalmente tickets a otros ingenieros.
- **Título de las actividades:** con Tipo = `Actividad` el campo **Título** se sustituye por la lista
  **Actividad**: Fusiones, Exportación de estudios DICOM, Revisión de migración, Capacitación,
  Documentación, Verificación de servicio, Mantenimiento preventivo y Mantenimiento correctivo. La
  opción elegida es el título que se guarda y el detalle va en **Descripción**; con Tipo = `Incidente`
  vuelve el campo de texto libre. Las opciones ya **no** están fijadas en el código: son las que se
  editan en **Programador** (ver abajo); `G.TIPOS_ACTIVIDAD` en `/js/gestion.js` queda solo como
  respaldo por si el servidor aún no manda el catálogo.
- **Subopciones, tipos y servicio:** el diálogo **+ Nuevo** sigue el orden de la cadena que se arma en
  **Programador**: Tipo → Región → Hospital → **Servicio** → Actividad → **Aplicativo** → **Tipo de
  aplicativo**. Si la actividad elegida tiene subopciones configuradas (es el caso de **Capacitación**,
  cuya segunda lista se llama **Aplicativo**: *Aplicativos Fuji* o *Aplicativos propios*), esa lista aparece
  justo debajo de la actividad. Es **opcional**: su primera opción es «(Sin aplicativo)» y se puede guardar
  sin elegirlo. **Y cada aplicativo puede tener su propia lista de tipos** (el bloque *Tipos por
  aplicativo*): si el aplicativo elegido tiene lista, aparece el campo **Tipo de aplicativo**, también
  **opcional** con su «(Sin tipo de aplicativo)»; si no tiene lista, ese campo no aparece.
  **Servicio** también es opcional y **depende del hospital**: al elegir el hospital, la lista se llena solo
  con los servicios que ese hospital tiene configurados; si el hospital no tiene ninguno (o todavía no se
  eligió hospital) el campo no aparece.
  Todo lo elegido se compone en un solo título:
  `Capacitación — Aplicativos Fuji — Estación de trabajo — Anatomía Patológica` (máximo 120 caracteres, que
  es lo que admite la columna `titulo`; sin aplicativo —o sin tipo— el título se arma sin esa pieza:
  `Capacitación — Anatomía Patológica`. No se agregaron campos nuevos a la base de datos).
- **El final del formulario depende de quién pueda asignar:** **Prioridad**, **Fecha de inicio**,
  **Fecha límite** con su casilla **Deshabilitar fecha límite** e **Ingeniero responsable** se muestran
  únicamente a la cuenta con el permiso **Asignar tickets a ingenieros** (`tickets.asignar`) —el
  Administrador por defecto, o el rol al que se lo den en **Roles y permisos**—, y con ellos la ventana
  conserva también su **Descripción** y se envía con **Crear** (el elemento nace `Pendiente`). Quien **no**
  tiene ese permiso ve una ventana corta —Tipo, Hospital, Servicio y Actividad, **sin Descripción**— y al
  pulsar **Crear** **no se envía nada**: se abre sola la ventana **«Registrar y completar actividad»**, con
  **Informe del trabajo realizado (opcional)** y **Evidencia (foto o archivo) obligatoria** (hasta 5
  archivos de 20 MB con sus miniaturas) y el botón verde **Crear y completar**. Esa segunda ventana es la
  que da de alta la actividad y la deja `Completada` en un solo envío; si se cierra sin enviar, no se guardó
  nada. Con **Incidente** no se abre esa segunda ventana: el incidente conserva el alta abierta de siempre. El
  **Ingeniero** nunca ve **fecha límite** ni **ingeniero responsable**: en su alta la **fecha de inicio
  es el día del calendario desde el que abrió la ventana** (hoy si entró con **+ Nuevo**), la
  **prioridad queda en Media** y **no hay fecha límite**; el Administrador los fija al asignar o al
  editar. El servidor refuerza la misma regla: si alguien sin ese permiso manda prioridad o fecha
  límite, se guardan en Media y sin fecha límite.

### Programador (catálogos de Gestión)

`/admin/programador/` — tarjeta **Programador** del grupo *Administración* del inicio. Edita las
listas que usan los formularios del módulo Gestión:

| Catálogo | Clave en configuración | Qué controla |
|---|---|---|
| Actividades | `gestion.actividades` | La lista que aparece con Tipo = Actividad en **+ Nuevo**. |
| Subopciones por actividad | `gestion.subopciones` | La segunda lista de alguna actividad (etiqueta + opciones; p. ej. *Aplicativo* → Fuji / propios). |
| Tipos por aplicativo | `gestion.subopcionesTipos` | El **tercer escalón** de esa cadena: la lista de tipos de cada aplicativo (p. ej. *Aplicativos Fuji* → Estación de trabajo / Servidor). Vacío por defecto: sin tipos guardados el campo **Tipo de aplicativo** no aparece. |
| Regiones médicas | `gestion.regiones` | Los nombres que aparecen en el desplegable **Región** de **+ Nuevo**. Vacío por defecto: sin regiones ese campo no existe. |
| Desglose por región | `gestion.hospitalesPorRegion` | Los hospitales de cada región, **escritos** uno por fila. **Un hospital solo puede estar en una región**; lo que no se escribe queda «Sin región». |
| Servicios por hospital | `gestion.serviciosPorHospital` | El desplegable **Servicio** de **+ Nuevo**, uno por hospital: se elige el hospital arriba y se edita su lista. |
| Servicios (general) | `gestion.servicios` | Respaldo y resultado de mezclar todo: se actualiza solo con la suma de lo que se guarda por hospital. |
| Hospitales | `gestion.hospitales` | Los códigos extra además de HES/HGR/HGO/HI. **Ya no tiene bloque propio en la pantalla:** se alimenta solo, al guardar una región, con los códigos nuevos que se escribieron en sus tarjetas. |

- **El tercer escalón de la cadena —*Tipos por aplicativo*— se edita igual que los servicios por hospital:**
  se elige el aplicativo en su desplegable y se escribe su lista de tipos con «+ Agregar tipo». El
  desplegable **solo ofrece los aplicativos que ya están guardados en *Subopciones por actividad*** (el
  servidor rechazaría una lista de tipos sobre un aplicativo que nadie registró), y al guardar se envía el
  mapa completo, no solo lo que se ve, para no perder lo escrito en el aplicativo de enfrente. Un aplicativo
  **sin tipos** es lo que hace que el campo **Tipo de aplicativo** desaparezca de **+ Nuevo** al elegirlo;
  tampoco se puede dejar todo vacío (la pantalla lo avisa antes de llamar al servidor). Renombrar o borrar
  un aplicativo en el bloque de arriba —o borrar su actividad— **borra sus tipos solos**, en cascada, para
  que no quede una lista que ninguna pantalla ya puede mostrar.

- **Mientras no se guarde nada en «Servicios por hospital»**, el catálogo viene vacío y todos los
  hospitales muestran la lista general de siempre, así que lo que ya estaba funcionando no cambia.
  En cuanto se guarda, cada hospital tiene la suya: un hospital **sin servicios** es lo que hace que
  el campo **Servicio** desaparezca en **+ Nuevo** al elegirlo (no se puede dejar todas las listas
  vacías; la pantalla lo avisa).

- **Las regiones solo agrupan, no cambian nada.** El registro **no** guarda la región: se deduce del
  hospital, así que en **+ Nuevo** el campo **Región** (que aparece únicamente si hay regiones
  guardadas) filtra el desplegable **Hospital** y el alta termina siendo la misma fila de siempre.
  Mover un hospital de región reordena también su histórico sin tocar un solo elemento. **La lista muestra
  una sola línea por región** —el nombre y cuántos hospitales lleva— y **al seleccionarla se despliega
  debajo su lista de hospitales**; las demás quedan plegadas, así que la pantalla se lee de una mirada (la
  línea activa se marca con el borde dorado, y «Ver hospitales»/«Ocultar» la abre o la cierra). **Los dos
  botones abren su ventana emergente**: «+ Agregar región» pide el nombre y las
  filas, y «+ Agregar hospital» de la región desplegada pide solo códigos. En la ventana se pueden llenar
  **varias filas antes de aceptar** —con «+ Agregar otro hospital», hasta 40, el tope del servidor—, y
  lo escrito es un borrador: no toca la lista hasta pulsar el botón azul, y con **Cancelar** se queda
  todo como estaba. La lista, por sí sola, nunca saca filas vacías: teclear en la última no hace
  aparecer otra.
  El campo nombre —en la ventana y en la lista— ofrece en su lista desplegable las regiones ya
  guardadas, y al elegir una **aparecen sus hospitales** (en la ventana, antes de aceptar; en la lista,
  en el panel de abajo). Como un hospital pertenece a una sola región, la ventana frena ahí mismo
  el código repetido nombrando la región dueña, y al guardar la pantalla lo detecta otra vez antes de
  llamar al servidor, que lo rechaza igual. Un
  código que no esté en la lista de hospitales del sistema se agrega ahí mismo al guardar (hasta 10
  caracteres, solo letras y números, en mayúsculas) y la pantalla lo dice; ese catálogo ya no tiene
  bloque propio — se gestiona escribiendo en el panel de la región. Los hospitales sin escribir quedan «Sin
  región» y siguen saliendo cuando se elige *Todas las regiones*. Un solo botón —**Guardar región y
  hospitales**— deja las dos cosas guardadas.
  Borrar o renombrar una región recorta su desglose en el servidor —y si un código llegara a salir de la
  lista de hospitales del sistema, sale también del panel—, igual que pasa con las subopciones de
  una actividad borrada.

- **El desplegable Región de **+ Nuevo** solo ofrece las regiones que de verdad traen hospitales**
  (`G.opcionesRegion` en `/js/gestion.js`): una región sin desglose, o la de otro usuario, dejaría el campo
  **Hospital** vacío, así que no se muestra.

- **Lo guardado sobrevive a apagar o reiniciar el servidor.** No vive en el navegador sino en el almacén
  de configuración (ver siguiente punto): al arrancar el servicio, `AplicarHospitales()` (en `Program.cs`,
  antes de atender la primera petición) vuelve a registrar los códigos escritos en las tarjetas, y las
  lecturas del catálogo van directo al almacén —sin caché—, así que basta volver a abrir **+ Nuevo** o la
  pantalla para ver las regiones tal como quedaron.

- Se guardan como JSON en el almacén de configuración (`configuracion` en MySQL, con la columna
  `valor` ampliada a `TEXT`, o `App_Data/configuracion.json` de respaldo). **No hay tablas nuevas**
  y los registros ya creados no cambian: lo elegido quedó escrito dentro de su título.
- **HES, HGR, HGO y HI no se pueden quitar** (hay historial que los usa); los códigos que se agreguen
  valen hasta 10 caracteres, solo letras y números, y en mayúsculas, porque esa es la anchura de la
  columna `hospital`. Se aceptan también en los inventarios, Playbook y filtros.
- Se necesita el permiso **`programador.administrar`**. Está marcado como `SoloRaiz` en el catálogo
  (`Models/Permisos.cs`), así que **solo la cuenta root lo trae por defecto**; cualquier otro rol —
  incluido Administrador— tiene que recibirlo en **Roles y permisos**. Al ser una clave nueva, las
  matrices guardadas se completan solas con `CatalogoPermisos.NuevasDesde` sin pisar lo ya editado.
- Los cambios se notan al volver a abrir **+ Nuevo** (el catálogo viaja con `/api/permisos/mios`).

### Región por usuario (quién ve qué hospitales)

Se asigna en **Usuarios** (`/admin/usuarios/`, con el permiso `usuarios.administrar`): la tabla tiene la
columna **Región** y el diálogo **Editar** un bloque con la lista de regiones —cada una dice cuántos
hospitales tiene, y el primer renglón es **«Todas las regiones»**, que deja a la persona sin límite—, más
una nota que dice en vivo cuántos hospitales va a ver esa persona y cuáles. Guarda con
`PUT /api/usuarios/{usuario}/region` y queda en la bitácora (acción `AsignarRegion`);
`GET /api/usuarios/regiones` alimenta la lista.

**Quién ve qué** (las mismas reglas en `/api/permisos/mios`, en los listados, en las exportaciones a Excel y
en el alta, que rechaza un hospital de otra región):

- **root** ve todas las regiones siempre; su lista sale bloqueada en «Todas las regiones» y el servidor no
  admite asignarle una.
- Un usuario con **«Todas las regiones»** elegido (o que nunca tuvo renglón) ve todas.
- Un usuario con región asignada ve **solo los hospitales de esa región**: en filtros y desplegables, en las
  listas, en los Excel y al dar de alta. En Inventario esa lista es además «Almacén» (`General`) más sus
  hospitales, y en Playbook pasa igual.
- **Si esa región es la única que tiene asignada, en «+ Nuevo» del calendario no elige región:** el campo
  sale escrito con la suya, sin flecha y sin poder desplegarse (no hay nada más que elegir, y «Todas las
  regiones» solo dejaría el Hospital vacío). Con **dos o más regiones** —o para quien ve todas: root, el
  Administrador, quien no tiene región— el desplegable vuelve con «Todas las regiones» arriba y cambiarla
  filtra el Hospital. Es solo la pantalla: la Región **no se guarda** en el elemento.
- **El Ingeniero solo puede registrar actividades del día de hoy**: para quien no tiene
  `tickets.asignar`, el «+» del calendario solo se pinta en la celda de hoy, la fecha del alta en la
  ventana es siempre la de hoy (no hay campo editable), y el servidor rechaza con `400 «Solo puede
  registrar actividades del día de hoy.»` cualquier fecha distinta. El Administrador y cualquier rol con
  `tickets.asignar` siguen fechando cualquier día. La restricción **es solo para el alta**: completar con
  evidencia una actividad propia pendiente de cualquier fecha sigue permitida.
- **Mientras el Programador no haya guardado ninguna región, nadie está limitado**, y si el almacén se lee
  ilegible se interpreta como «sin regiones»: el cambio no puede dejar a nadie sin sus datos.
- Una región **cuyo desglose quedó vacío no limita** —por eso la pantalla lo avisa, en la tabla («sin
  hospitales») y en la nota del diálogo—. Al **asignarla** el servidor la rechaza hasta que el Programador le
  escriba hospitales; la nota del diálogo lo dice antes de guardar, no después.
- `General` (la unidad de Almacén y del Playbook) **nunca se limita**, y los códigos de hospital en blanco o
  fuera del catálogo siguen siendo visibles para no esconder datos históricos.
- Lo que ve cada usuario se guarda en la clave `gestion.regionesPorUsuario`
  (`{ usuario: { region, todas } }`) del mismo almacén de configuración, así que **sobrevive a apagar o
  reiniciar el servidor** y no necesita tablas nuevas. Al borrar un usuario se borra también su renglón.

### Fecha límite (solo administrativa)

En el botón **⚙ Configuración** del inicio (bloque *Parámetros del módulo Gestión*, solo con
`configuracion.administrar`) el Admin encuentra el interruptor **"Habilitar fecha límite"**
(`items.fechaLimiteHabilitada`, por defecto habilitada):

- **Habilitada** → la fecha límite se pide al crear/asignar, se muestra en el histórico y en el
  calendario, y se calcula el estado *Vencida* (filtro *Vencidos* y anillo rojo en el chip).
- **Deshabilitada** → la fecha límite **no se usa ni se solicita**: desaparece de los formularios,
  de la tabla del histórico, del calendario y de las insignias, y nadie puede marcar "vencido".

El **Ingeniero nunca ve el campo de fecha límite**, ni habilitada ni deshabilitada, y no puede
modificarla: la configuración y el uso de la fecha límite son **exclusivamente administrativos**. El campo
tampoco se ofrece en **+ Nuevo** a quien no tenga `tickets.asignar`, así que
lo mismo aplica a **Prioridad**, **Fecha de inicio** e **Ingeniero responsable**: esos cuatro campos del
alta los ve solo quien hace la asignación formal. Y como el alta del Ingeniero ahora **nace
`Completada`**, tampoco hay forma de ponerle fecha límite después desde su lado: la única vía es que el
Administrador la fije al asignar o editar, o que **reabra** la actividad y la vuelva a programar.

### Flujo de un ticket (registro → cierre)

1. **Registro** (Admin o Ingeniero) → se elige **hospital** y el sistema asigna número de ticket automático.
2. **Revisión y asignación** (solo Admin): ingeniero responsable, prioridad, fecha límite (si está
   habilitada) y **periodicidad de reporte** (cada cuántos días debe informar).
3. **Seguimiento:** el ingeniero escribe **comentarios** en la tarjeta —que es como ahora se informa el
   avance— y, cuando quien asigna lo pide, entrega **reportes de estatus**
   (avance %, descripción, problemas, pendientes, próximas acciones, fecha estimada, comentarios). Si pasan
   **3 días o más sin comentarios**, la tarjeta y el calendario lo recuerdan en naranja: «Favor de registrar
   avances en comentarios».
4. **Supervisión:** el Admin consulta reportes, historial de cambios y agrega observaciones.
5. **Fin:** el responsable la deja en **`Completada`** con su evidencia, o quien tiene `items.cancelar` la
   pasa a **`Cancelada`** con su justificación y queda solo como registro. Ya no existe el «cierre formal»
   sobre lo completado.

### La tarjeta de detalle: qué ve cada perfil

La tarjeta que se abre al pulsar un chip o una ticket del calendario se parte con el permiso
**Asignar tickets a ingenieros** (`tickets.asignar`), el mismo de la asignación formal:

- **Sin ese permiso (Ingeniero)** la tarjeta es únicamente el reporte de lo que hizo: **Completar** y
  **Agregar comentario**. No ve *Cancelar*, *Entregar reporte*, *Reasignar*, *Editar*, *Eliminar*, *Reabrir*
  ni el aviso de reporte pendiente. Los botones *Iniciar*, *Registrar avance* y «Cerrar formalmente» ya no
  existen para nadie.
- **Las pestañas de la tarjeta** son dos cosas distintas: **«Reportes de estatus»** siempre está (es el
  seguimiento que entregó el responsable) y **«Historial de cambios»** solo la ve quien tiene el permiso
  **`tickets.historialCambios`** (versión 15 del catálogo: **Admin** por defecto, **Ingeniero ya no**). Sin
  el permiso la pestaña ni se pinta, no se pide al servidor, y `GET /api/actividades/{id}/historial`
  responde vacío aunque se sea el dueño.
- **Ancho de la tarjeta:** la ventana del elemento es la única que llega a **~1180 px** (o el 94 % de la
  pantalla si es menor), porque el calendario deja ese sitio libre; ahí los metadatos se reparten en seis
  columnas y los botones caben en una fila. Los demás diálogos (alta con evidencia, edición) siguen en
  660 px.
- **Con el permiso (Administrador)** conserva todas esas acciones y la asignación. **Cancelar** además pide
  su propio permiso, **`items.cancelar`** (versión 14 del catálogo, Admin por defecto), y exige
  **justificación**.
- **Una `Cancelada` se congela:** la tarjeta enseña la justificación, la fecha de cierre, la evidencia y el
  historial, pero ningún botón de acción —solo «Cerrar», que quita la ventana—. No se edita, no se reasigna,
  no se completa, no admite comentarios ni reportes y **no se reabre**.
- **Reabrir es solo del Administrador** (`items.reabrir`): en una actividad **`Completada`** aparece el botón
  **Reabrir**, que pide un **motivo** opcional (máximo 1000
  caracteres) y devuelve el elemento a **`Pendiente`** con la **evidencia intacta** y
  sin fecha de cierre; el motivo queda en el historial y en la bitácora como «Actividad reabierta».
  Desde ahí el ingeniero la trabaja y la vuelve a cerrar con **más** evidencia encima, no en lugar de la
  anterior. Reabrir algo que aún está abierto **o que está cancelado** responde **400**, y un Ingeniero que
  lo intente responde **403**.
- **Completar pide evidencia obligatoria:** **al menos una foto o archivo** (hasta 5 archivos de 20 MB;
  jpg, png, gif, bmp, webp, tif, pdf, doc, docx, xls, xlsx, ppt, pptx, txt, csv, log, zip, rar o 7z). El
  **informe es opcional** —se puede marcar como completada dejándolo en blanco— y lo elegido se ve en
  miniatura **antes** de enviar; si no se adjuntó nada, el botón no manda nada. El servidor rechaza
  igual el `Completada` sin evidencia, por cualquier vía.
- **El comentario es solo texto** —la evidencia se adjunta únicamente al completar— y puede escribirse
  sobre un elemento abierto o sobre uno ya `Completada`, pero **no sobre una `Cancelada`**. Lo del
  responsable queda registrado como «Comentario» y lo de quien tiene `tickets.observaciones` como
  «Observación»; cualquiera de los dos **reinicia el recordatorio de los 3 días**.
- La tarjeta muestra el bloque **«Evidencia del trabajo»** para los dos perfiles: las fotos se ven
  dentro y se abren en pestaña nueva; los documentos se listan con su extensión y se descargan.

### Exportación a Excel

El **Histórico de actividades** y **cada uno de los tres inventarios** tienen su botón
**Descargar Excel**, que exporta **exactamente lo que se está viendo con los filtros aplicados**
(los mismos criterios se vuelven a aplicar del lado del servidor) en un archivo `.xlsx` real
— nunca CSV. Columnas del histórico: **ID, Elemento, Tipo, Hospital, Fecha, Registrado por,
Ingeniero responsable, Descripción, Prioridad, Estatus, Fecha de creación, Fecha límite** (solo si
está habilitada), **Fecha de finalización, % avance, Último reporte, Próximo reporte y
Observaciones**. El nombre del archivo refleja los filtros, por ejemplo
`Historico_actividades_HES_Incidente_20261001_20261031.xlsx`, `Inventario_Equipos_HES.xlsx` o
`Inventario_AETitle_HGO.xlsx`.

La exportación del histórico requiere el permiso `historico.exportar` (por defecto **solo Admin**);
los inventarios los pueden exportar ambos roles. Toda descarga queda en la bitácora.

### Inventarios (IPs y AETitle)

Son **dos pantallas independientes** — nunca mezcladas en una sola — con la misma mecánica:
consulta, **filtro por hospital**, búsqueda por texto, visualización ordenada, alta/edición/baja
(solo Admin) y **Descargar Excel**. Los campos son extensibles: cada inventario declara su esquema
en `Models/Inventario.cs` (`InventarioCatalogo`) y la pantalla, la validación y el Excel se generan
a partir de él (`js/inventario.js`), así que **añadir un campo o un inventario nuevo no toca el
almacén ni la API**. El hospital siempre es obligatorio (`*` = campo obligatorio):

- **IPs:** Equipo o servicio\*, Dirección IP\* (IPv4 válida), Máscara de subred, Puerta de enlace,
  Asignación (Fija / Dinámica / Reservada), VLAN y Notas.
- **AETitle:** Descripción\*, AE Title\*, Host o IP\*, Puerto (1–65535), Modalidad (CR, CT, DX, MG,
  MR, PT, RF, US, WSD, Otro), Rol DICOM (Nodo PACS / Modalidad / Workstation / Receptor / Otro)
  y Notas.

> El **inventario de equipos** es la **pantalla única de equipos** del sistema
> (`/gestion/inventario-equipos/`). El antiguo módulo independiente *Almacén de equipos*
> **desapareció** de los menús y la ruta `/almacen/` **redirige** a esta pantalla.

### Inventario de equipos (pantalla única con pestaña Ingresos)

Pantalla **`/gestion/inventario-equipos/`** con una **única pestaña: *Ingresos***. Arriba, un
**selector de unidad** (`Almacén`, `HES`, `HGR`, `HGO`, `HI`; *Almacén* = almacén central, se
guarda como hospital `General` y en las hojas se imprime **ALMACÉN CENTRAL**), un buscador y los
botones **+ Añadir equipo** y **Descargar Excel** (ambos **solo Admin**).

- **Añadir equipo (Admin):** captura unidad, nombre, categoría, marca, modelo, serie, cantidad,
  servicio, ubicación, estado, procedencia y notas. El **número de inventario se genera solo**
  (`EQ-0001`, `EQ-0002`, …), ya no se teclea. Al aceptar se **crea el lote**, se genera y **confirma
  su entrada** (que suma la cantidad), se sella su `docEntrada` y se abre la **hoja de entrada**.
- **Por cada fila** hay: **Hoja entrada** (reimprime la hoja de la entrada del lote), **Hoja
  salida** (descuenta stock, genera y confirma la **hoja de salida** y, si se indica destino,
  **re-ingresa** el equipo en esa otra unidad) y **Editar** (solo Admin).
- **Lotes en 0 ocultos:** en cuanto un equipo se queda sin existencias (cantidad 0) **desaparece de la
  tabla y del Excel**. El registro y su historial se conservan; si vuelve a entrar stock, reaparece.
- **Existencias = lotes** del catálogo de equipos ampliados con número de inventario, cantidad,
  ubicación y estado: **una sola fuente de verdad para el stock**.
- Los **movimientos** (entradas y salidas) son documentos con **folio único**
  (`ENT-/SAL-/TRA-año-####`) que modifican el stock al confirmarse. Un **traslado entre unidades**
  se modela como *salida + re-ingreso* (dos hojas). Si la salida es por **desecho / baja
  definitiva** las unidades **no** se re-ingresan en ningún sitio.

Reglas clave:

- **Incrementar un lote existente sí se permite:** una entrada cuya línea apunta a un lote ya
  existente (`equipoId`) le **suma cantidad**; solo se rechaza una serie o número de inventario
  duplicado **en un lote distinto** de la misma unidad. La misma serie puede existir en unidades
  distintas (es otro activo físico).
- **Sin stock negativo:** toda disminución se valida completa antes de aplicar nada.
- **Sin borrado físico de movimientos:** un movimiento equivocado se **cancela o rechaza** (queda
  el rastro en el historial y la bitácora), nunca se elimina.
- **Hojas institucionales:** cada hoja reproduce el **formato oficial** (título, recuadro con
  razón social, unidad médica, área, número de contrato y vigencias, fecha larga y folio, tabla
  *EQUIPO / NUMERO DE SERIE / UNIDADES* con la fila de *OBSERVACIONES* y tres líneas de firma con
  su puesto). Las firmas quedan **en blanco, sin nombres**, listas para firmarse a mano; se pueden
  reimprimir o descargar en **Word** cuantas veces se quiera.
- **Permisos:** consultar, registrar y exportar los tienen Admin e Ingeniero; **cancelar o
  rechazar movimientos ajenos o en tránsito** exige `almacen.autorizar` (por defecto solo Admin).
  El backend conserva el espacio de nombres `/api/almacen/*` y las claves `almacen.*` por
  compatibilidad.

Detalle completo (pantalla, flujos, estados, API, esquema de base de datos y hojas):
**[`docs/INVENTARIO-EQUIPOS.md`](docs/INVENTARIO-EQUIPOS.md)**.

### Playbook (documentación técnica en PDF)

Biblioteca de manuales, procedimientos y guías. Cada documento guarda: **nombre**, **nombre del
archivo**, **versión**, **fecha de publicación**, **fecha de actualización**, **hospital**
(incluye `General`), **usuario que lo cargó**, **fecha de carga**, **tamaño**, **archivo PDF** y
**estado** (`Vigente` o `Retirado`).

- **Admin** (permiso `playbook.administrar`): subir PDF, ver/abrir, cambiar **nombre**, cambiar
  **versión**, sustituir el archivo por una versión nueva, **eliminar** y consultar toda la
  información. Cambiar la versión o reemplazar el archivo **sella sola** la fecha de actualización.
- **Ingeniero** (permiso `playbook.consultar`): **solo** ver, consultar nombre/versión/fecha de
  actualización y descargar/abrir el PDF. **No puede** subir, eliminar, cambiar nombres, cambiar
  versiones ni modificar la documentación: los botones no aparecen y el servidor devuelve `403`.
- Validaciones del servidor: el archivo **debe ser un PDF real** (se revisa la firma `%PDF-`, no
  basta la extensión), máximo **20 MB**, nombre de 3–150 caracteres, versión obligatoria (máx. 20),
  hospital obligatorio (`HES/HGR/HGO/HI/General`), estado `Vigente`/`Retirado` y **nunca se
  modifica el contenido del PDF**.
- Eliminar pide exactamente: *"¿Está seguro de que desea eliminar este documento? / Esta acción
  eliminará el documento del Playbook. / [Cancelar] [Eliminar]"*, y borra también el archivo de disco.

### Reportes de estatus y periodicidad

Cada elemento asignado tiene una **periodicidad de reporte** (días). La pantalla de detalle avisa
cuando toca entregar (`Reporte de estatus solicitado`). El valor por defecto global se configura en
el botón **⚙ Configuración** del inicio → *Parámetros del módulo Gestión* → *Periodicidad de reporte (días)*,
y se aplica al asignar si no se indica otro.
Todos los reportes se conservan como histórico consultable en **/admin/reportes/** (que incluye la
columna y el filtro **Hospital**) y en la pestaña *Reportes* del detalle.

### Historial de cambios

Cada movimiento (alta, asignación, cambio de hospital/ingeniero/prioridad/fecha límite/periodicidad,
cambio de estatus, comentarios y observaciones, reportes, cancelación y reapertura) queda en un **historial append-only** por
elemento (pestaña *Historial de cambios* del detalle). **Nunca se borra información histórica**; por eso solo
se pueden *eliminar* elementos `Pendiente` sin reportes — lo demás se cancela o se cierra.

**Ver ese registro es un permiso, no una consecuencia de ser el responsable:** lo gobierna
`tickets.historialCambios` (bloque *Tickets (administración)* de **Roles y permisos**). El **Admin** lo trae
por defecto; el **Ingeniero no**, así que su tarjeta solo ofrece *Reportes de estatus*. El servidor lo
aplica también: sin el permiso el endpoint del historial devuelve lista vacía.

### Roles y permisos

Los roles son **dinámicos y de nombre libre**: en **/admin/roles/** (solo con el permiso
`roles.administrar`, que el rol Administrador siempre conserva para no quedar bloqueado) se pueden
**crear, renombrar y eliminar** roles, además de editar la **matriz de permisos** de cada uno. Los roles
de sistema **Admin** e **Ingeniero** no se renombran ni eliminan. Son **46 permisos en 13 grupos**
(**versión 15** del catálogo);
cuando se agregan permisos nuevos, el catálogo sube de versión y se otorgan automáticamente a los roles
que corresponda para que nadie pierda acceso. **Solo se completan los que el rol tiene por defecto**, así
que una clave nueva que se le quiere dar a **un rol propio guardado antes del cambio** se marca a mano
desde la matriz; la versión 15 es justamente cuando `tickets.historialCambios` (Ver la pestaña
«Historial de cambios») empezó a consultarse y salió de los defectos del Ingeniero.

Además de la matriz del rol, en **/admin/usuarios/** cada cuenta tiene un botón **Permisos** para hacer
**ajustes por usuario**: **conceder (+)** o **revocar (−)** permisos concretos sobre los heredados del
rol (por defecto quedan *heredados*). Los **permisos efectivos = matriz del rol + ajustes**, y se
recalculan **en cada petición**, así que los cambios aplican de inmediato a las sesiones ya abiertas.

El servidor los hace cumplir con guards de ruta y políticas `P:<clave>` (`GET /api/permisos/mios`), y las
pantallas ocultan lo que el usuario no puede hacer. La **página de inicio (`/`) es única** para todos:
muestra **solo las tarjetas de las herramientas a las que el usuario tiene acceso** (por ejemplo, un
Ingeniero ve 7 herramientas y un Administrador las 15); `/Admin` e `/Ingenieria` redirigen a `/`.

- **Admin (por defecto):** todas las herramientas, incluidos roles, usuarios, bitácora, configuración,
  histórico, reportes e inventarios.
- **Ingeniero (por defecto):** calendario, sus actividades, crear actividades/incidentes/tickets,
  actualizar el seguimiento, consultar el Inventario de equipos y los inventarios de IPs/AETitle
  (y descargarlos), entrar al Playbook y entregar reportes. **No** asigna tickets, ni administra roles,
  usuarios, configuración ni documentos del Playbook.
- Estos son solo los valores por defecto: cualquier rol (nuevo o existente) y cualquier usuario pueden
  ajustarse permiso a permiso desde la interfaz.

### Avisos del inicio

La página de inicio tiene un panel lateral **Avisos** que ven **todos** los usuarios con sesión. Quien
tiene el permiso **`avisos.administrar`** (por defecto el Administrador) ve además el botón
**+ Publicar** y, en cada aviso, **Editar** y **Eliminar**; el resto **solo lee** los avisos publicados,
sin ningún control. Cada aviso tiene **título**, **etiqueta** (el texto corto en mayúsculas, p. ej.
"Esta semana"), **texto** opcional y la casilla **Publicado**: si se desmarca, el aviso queda **pausado**
(no lo ven los lectores, pero quien administra lo ve atenuado para reactivarlo, editarlo o eliminarlo).
Todo se guarda en MySQL (tabla `avisos`) o, si la base no responde, en `App_Data/avisos.json`, y cada
alta/edición/baja queda en la bitácora.

### Administrativos (actas y credenciales)

La página **/admin/administrativos/** reúne dos herramientas del área administrativa; abre con
**cualquiera** de los dos permisos del grupo y cada pestaña exige el suyo:

- **Actas administrativas** (`actas.administrar`): captura de actas con **folio consecutivo**
  (`ACTA-2026-0001…`), **tipo** (*Mala conducta* o *Faltas injustificadas*), persona (nombre, puesto y, si
  tiene, su **usuario de intranet ligado**), ciudad/lugar, fecha y **hora** de los hechos, relación de
  hechos, acuerdos/sanción y testigos. El botón **Imprimir** genera el documento con el membrete de la
  empresa, el folio y la **redacción legal del formato oficial según el tipo**: el de *mala conducta*
  (preámbulo en mayúsculas, cláusulas de reserva legal y de testigos, firmas de involucrado, testigos y
  quien elaboró) o el de *faltas injustificadas* (narrativa de inasistencias, sanción prevista y firmas de
  jefe directo, elaboró, involucrado y testigos). La empresa y el domicilio del preámbulo son fijos de
  Meditechnosys; desde ahí se guarda en PDF o se imprime.
- **Credenciales** (`credenciales.administrar`): gafetes **ligados a un usuario de la intranet** con el
  **formato oficial** de la empresa (frente y reverso). Al crearla se sube la **foto tamaño infantil
  (2.5 × 3 cm)**: un recortador en el navegador encuadra la imagen (arrastre + zoom, relación 5:6) y el
  servidor revalida que sea JPEG/PNG real de ≤ 2 MB. Se capturan nombre impreso, puesto, NSS, cédula y
  vigencia (MM/AAAA). Cada credencial ofrece **Vista previa**, **Imprimir** (frente y reverso en **una
  sola hoja**, lado a lado con línea de doblez, a 54 × 86 mm por cara) y **PNG (2 caras)** (descarga una
  sola imagen con ambas caras). Las fotos viven en `App_Data/credenciales/` y solo se
  sirven a quien tiene el permiso.

Los datos viven en MySQL (tablas `actas` y `credenciales`) o, si la base no responde, en
`App_Data/actas.json` y `App_Data/credenciales.json`; cada alta/edición/baja queda en la bitácora.

### Calendario con panel de tickets

La página **/calendario/** muestra a la **izquierda** el panel de **Tickets** (búsqueda por
número/título y filtros *Abiertos / Vencidos / Todos*; al seleccionar uno
se abre su detalle y, si eres Admin, puedes asignarlo o editarlo), en el **centro** la rejilla
mensual con un chip por elemento (color según estado, marco rojo si está vencido y una marca naranja `!`
cuando lleva 3 días o más sin comentarios). **La página del calendario ocupa ahora el ancho real de la
pantalla** (hasta 1780 px, o el 96 % si la pantalla es menor), así que **los siete días —Dom a Sáb— se ven
sin barra horizontal** también en un monitor de 1366. **Las siete columnas reparten el ancho a partes iguales
y el título del chip se recorta con puntos suspensivos: un título largo ya no ensancha su celda ni empuja la
semana.** La tarjeta que se abre al pulsar el chip es **ancha** (hasta ~1180 px) para aprovechar ese sitio. A la
**derecha** está el panel **Pendientes por ingeniero**: agrupa por responsable lo que sigue abierto y sus dos
pestañas separan **por tipo** —**Actividades** e **Incidentes**—; la antigua pestaña «Reportes» **se retiró**
(los reportes de estatus que ya tocan se ven dentro de la tarjeta de cada elemento y en la pantalla de *Reportes*).
Cada renglón trae solo el título y su «Límite …» con el hospital: **las etiquetas ACTIVIDAD/INCIDENTE y
PENDIENTE se quitaron por redundantes** (la pestaña ya dice el tipo y aquí todo está pendiente); lo único que
conserva su etiqueta roja es lo **vencido**. Y **el nombre del responsable arriba de cada bloque solo lo ve
el Administrador** (quien puede asignar): para el Ingeniero todo lo de esa lista es suyo, así que su pantalla
muestra los renglones sin esa cabecera. Los tickets **no** aparecen ahí: viven en el panel de la izquierda. Arriba hay un
filtro por **Hospital** que afecta a la rejilla y al panel. El botón **+ Nuevo** y el **+** de cada
día están disponibles para **Admin e Ingeniero** según sus permisos, y el alta pide el hospital.
El alta del Ingeniero es distinta: si registra una **actividad**, su ventana de alta no trae
Descripción y, al guardar, se abre enseguida la ventana de cierre con el informe (opcional) y la
evidencia (obligatoria); al aceptarla el elemento queda en `Completada` (ver «La tarjeta de detalle»).
La consulta completa de los tres tipos vive en el *Histórico de actividades*, no aquí.

Todo se guarda en MySQL (`meditechnosys_intranet`: tablas `actividades`, `reportes_estatus`,
`historial_cambios`, `permisos`, `configuracion`, `inventario`, `playbook`, `evidencias`,
`almacen_movimientos`, `roles`, `usuario_permisos`, `avisos`, `actas` y `credenciales`) o, si la base no
responde, en archivos bajo `App_Data/` (`actividades.json`, `reportes.json`, `historial.jsonl`,
`permisos.json`, `configuracion.json`, `inventario.json`, `almacen.json`, `playbook.json`, `evidencias.json`,
`roles.json`, `usuario_permisos.json`, `avisos.json`, `actas.json`, `credenciales.json`). Los PDF del Playbook
viven en disco (`App_Data/playbook/` por defecto), las fotos de las credenciales en
`App_Data/credenciales/` y la **evidencia** adjuntada al completar (`App_Data/evidencias/` por defecto). La API es `/api/actividades`,
`/api/historico/excel`, `/api/reportes`, `/api/roles`, `/api/permisos/mios`, `/api/config`,
`/api/inventario/{coleccion}`, `/api/almacen/*`, `/api/playbook/*`, `/api/avisos*`, `/api/actas*`
y `/api/credenciales*`, más `/api/actividades/{id}/completar` (el segundo paso del Ingeniero: informe
opcional y evidencia en un solo envío `multipart`; el `POST /api/actividades` también acepta su forma
`multipart` heredada, con informe y archivos juntos, para los navegadores que aún tengan la página vieja
en caché), `/api/actividades/{id}/reabrir` (solo Admin),
`/api/actividades/{id}/evidencias` y `/api/evidencias/{id}`, y cada operación queda también en la bitácora
de accesos.

Detalle interno (esquema, endpoints, reglas y permisos):
**[`docs/ARQUITECTURA.md`](docs/ARQUITECTURA.md)**.

---

## Configuración (`appsettings.json`)

| Sección | Para qué sirve |
|---|---|
| `ConnectionStrings:Usuarios` | Base `meditechnosys_intranet` (login, bitácora y módulo Gestión). |
| `ConnectionStrings:Patologia` | Base `RepoPato` (reportes y auditoría). |
| `Patologia:StorageDir` / `InboxDir` | Dónde se guardan los PDF y la carpeta de entrada automática. |
| `PacsNodes` | Lista de servidores PACS (nombre, IP, puerto 104, AE Title). |
| `Local` | AE local (`MEDI`), puerto de recepción DICOM (`11113`), modo (`move`), carpeta de descargas, límites de ZIP. |
| `Playbook:StorageDir` | Carpeta de los PDF del Playbook. Si se omite: `App_Data/playbook`. |
| `Gestion:EvidenciasDir` | Carpeta de la evidencia (foto o archivo) que se adjunta al completar un elemento del calendario. Si se omite: `App_Data/evidencias`. |
| `Dicom:PruebasFile` | Archivo del registro de pruebas DICOM enviadas. Si se omite: `App_Data/dicom-pruebas.jsonl`. |
| `Almacen:Contrato` / `Almacen:VigenciaInicio` / `Almacen:VigenciaFin` | Número de contrato y vigencias que se imprimen en las hojas de ingreso/salida/traslado (por defecto `D260012SI`, `01/JULIO/2026` y `31/DICIEMBRE/2026`). |
| `Auth:Store` | `mysql` (recomendado) o `json`. |
| `Auth:UsersFile` / `AuditFile` / `ActivityFile` / `ReportFile` / `HistorialFile` / `PermissionFile` / `ConfigFile` / `InventarioFile` / `AlmacenFile` / `PlaybookFile` / `RoleFile` / `UserPermissionFile` / `AvisoFile` / `ActaFile` / `CredencialFile` | Rutas opcionales de los archivos de respaldo en `App_Data/` (si se omiten, se usa el nombre por defecto de cada almacén). |

> Las contraseñas de base de datos están en `appsettings.json`. Si este equipo o la carpeta se comparten, cuida ese archivo.

---

## Compilar y publicar (para quien dé mantenimiento)

```bash
# Compilar (verifica que no haya errores)
dotnet build -c Release

# Correr en desarrollo (usa localhost:5023 y los usuarios admin/ing)
dotnet run

# Generar la versión final autocontenida en publish\  (o simplemente ejecutar publicar.bat)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

El servicio de Windows se instala desde la carpeta `publish\` con `instalar-servicio.bat` (ver **[`docs/SERVICIO-WINDOWS.md`](docs/SERVICIO-WINDOWS.md)**).

> **`App_Data\` no se publica.** Es el almacén de respaldo *en tiempo de ejecución*: el `.csproj` lo
> excluye (`CopyToPublishDirectory="Never"`) para que `dotnet publish` / `actualizar.bat` nunca
> sobrescriban los datos del servidor con los de la carpeta de desarrollo ni envíen a producción las
> cuentas de prueba. Como `publicar.bat` borra `publish\` completo, ese script **aparta**
> `publish\App_Data` a `..\App_Data_servidor` (fuera del proyecto) y lo devuelve al terminar. En un
> servidor nuevo la carpeta se crea sola al primer arranque.

---

## Solución de problemas rápida

- **No abre / error 500 al entrar a Patología** → revisa que MySQL esté arriba y que exista la base `RepoPato` con sus tablas.
- **Aparece aviso "MySQL de usuarios no disponible"** → la base de login no respondió al arrancar; la app siguió operando con el archivo JSON. Levanta MySQL y reinicia el servicio para volver a MySQL (y migrar lo creado en JSON).
- **El login no acepta a nadie** → la base `meditechnosys_intranet` se crea sola, pero si quedó vacía usa las credenciales semilla o crea el primer admin por variables de entorno (ver `docs/ARQUITECTURA.md`).
- **No veo un cambio de diseño reciente** → pulsa **F5** en el navegador (las `.html` ya se sirven sin caché). Si cambiaste código C#, ejecuta **`actualizar.bat`** como administrador.
- **DICOM no descarga** → confirma red hacia los nodos PACS y que el puerto `11113` no esté ocupado por otra app.
- **Un PDF del Playbook no se sube** → el archivo debe ser un **PDF real** (el servidor revisa la firma `%PDF-`) y pesar menos de 20 MB; además hay que indicar nombre, versión y hospital.
- **No veo el botón "Descargar Excel" del histórico** → la exportación del histórico necesita el permiso `historico.exportar` (por defecto solo Admin). Los inventarios sí los puede exportar el Ingeniero.
- **No encuentro el botón ⚙ Configuración** → está **solo en el inicio**: vuelve a `/` —con *Home* en las
  pantallas de panel, con el texto `meditechnosys` de la cabecera en las demás— y lo verás arriba a la
  derecha, junto a *Salir*. Desde cualquier otra pantalla ya no aparece.
- **No veo la fecha límite** → puede estar deshabilitada en **⚙ Configuración** (bloque *Parámetros del módulo Gestión*), o puedes estar entrando como **Ingeniero** (ese rol nunca la ve).
- **En «+ Nuevo» no veo Prioridad, fechas ni Ingeniero responsable** → es el comportamiento esperado para el perfil **Ingeniero**: esos campos solo se muestran a quien tiene el permiso **Asignar tickets a ingenieros** (el Administrador). Para que otro rol los vea, actíveselo en **Roles y permisos**. Si lo que se registró es un **Incidente**, el alta igual funciona: queda en **Pendiente**, en **Media**, con la fecha del día elegido y sin fecha límite, en espera de que el Administrador la asigne.
- **En «+ Nuevo» no veo el campo Descripción y, al pulsar Crear, se abre otra ventana que pide «Informe del trabajo realizado (opcional)» y «Evidencia (foto o archivo) (obligatorio)»** → es lo esperado para el **Ingeniero**: su alta es en **dos pasos**. El informe puede quedar en blanco; la evidencia no (hasta 5 archivos de 20 MB). Al aceptar, la actividad queda en **Completada**. Si lo que quieres es dejar una actividad **pendiente** para que el Administrador la asigne, regístrala como **Incidente** o pídele a él que la dé de alta.
- **En la tarjeta de un ticket solo veo «Completar» y «Agregar comentario»** → es el comportamiento esperado del perfil **Ingeniero**: la tarjeta se parte con el permiso **Asignar tickets a ingenieros**, así que Cancelar, Entregar reporte, Reasignar, Editar, Observación y **Reabrir** las ve únicamente quien puede asignar (los botones *Iniciar* y *Registrar avance* ya no existen: el avance se escribe en los comentarios). Para completar hay que **adjuntar la evidencia** (foto o archivo, hasta 5 de 20 MB): sin archivo el botón no envía nada y el servidor rechaza el «Completada». Si la evidencia la elegiste pero no aparece, revisa que la extensión esté en la lista admitida y que el archivo sea realmente lo que dice ser.
- **No veo el botón «Cancelar»** → lo muestra solo el permiso **`items.cancelar`** («Cancelar actividades, incidentes y tickets»), que el rol **Admin** trae por defecto y el Ingeniero no; se le puede activar a otro rol en **Roles y permisos**. Es un permiso de la **versión 14** del catálogo: las matrices guardadas en versiones anteriores lo reciben solas al arrancar el servidor, así que si faltó, reinicia el servicio. Además solo aparece con el elemento en `Pendiente`, y la cancelación **exige justificación**.
- **No veo el botón «Reabrir» en una actividad ya completada** → lo muestra solo el permiso **`items.reabrir`** («Reabrir actividades ya completadas»), que el rol **Admin** trae por defecto y el Ingeniero no; se le puede activar a otro rol en **Roles y permisos**. Es un permiso de la **versión 13** del catálogo: las matrices guardadas en versiones anteriores lo reciben solas al arrancar el servidor, así que si faltó, reinicia el servicio. Y solo aparece sobre elementos `Completada`: reabrir algo abierto **o cancelado** responde que no.
- **Reabrí una actividad y sigue mostrando la evidencia anterior** → es intencional: la reapertura **no borra archivos**, devuelve el elemento a **Pendiente** y deja lo adjuntado intacto para que el ingeniero le **sume** más evidencia al volver a completarla. El motivo de la reapertura queda en el historial y en la bitácora como «Actividad reabierta».
- **Cancelé una actividad y ya no puedo hacer nada con ella** → también es intencional: lo cancelado **no se reabre**, no se edita, no se reasigna y no admite comentarios, evidencia ni reportes. Solo queda el registro (su justificación, su historial y su evidencia) para consulta.
- **En la tarjeta no veo la pestaña «Historial de cambios»** → es el comportamiento esperado: desde la **versión 15** del catálogo esa pestaña la gobierna el permiso **`tickets.historialCambios`** («Ver la pestaña «Historial de cambios» de cada elemento»), que el rol **Admin** trae por defecto y el **Ingeniero ya no**. Sin él no aparece la pestaña, no se consulta el servidor y `GET /api/actividades/{id}/historial` responde vacío aunque se sea el responsable. El Ingeniero se queda con **«Reportes de estatus»**, que es su seguimiento. Para otro rol: **Roles y permisos** (bloque *Tickets (administración)*) → activar la casilla y guardar. Ojo con lo que se actualiza solo: al arrancar el servicio la **matriz del Admin** recibe la clave automáticamente, pero **un rol propio guardado antes del cambio no** —hay que marcarla a mano—, y el **Ingeniero no la recibe** porque ya no está entre sus defectos. Si sigue sin verse, recarga con **F5** (el `gestion.js` puede estar en caché) y reinicia el servicio (la matriz se completa al leerla).
- **El botón ⚙ Configuración no muestra *Parámetros del módulo Gestión*** → esa sección solo aparece con el permiso `configuracion.administrar` (el rol Admin la trae; el Ingeniero no).
- **Cambié el tema y en otra PC no se ve igual** → el **tema** viaja contigo: se guarda en tu cuenta (**⚙ Configuración → Tema**, en el inicio), así que en otro equipo basta con entrar. Solo tarda un momento en la primera pantalla abierta después de cambiarlo. El **tamaño de la letra** sí se guarda **en el equipo** (`localStorage`); esa es la diferencia.
- **Se me cierra la sesión muy pronto** → **⚙ Configuración** (en el inicio) → *Tiempo de sesión* → elegir 2, 4 u 8 horas.
- **No veo la tarjeta Programador en el inicio** → el permiso `programador.administrar` es `SoloRaiz`:
  solo **root** lo trae por defecto. Para otro rol, activarlo en **Roles y permisos** (bloque
  *Administración*).
- **En Capacitación no aparece la lista de aplicativos** → o el catálogo no tiene subopciones guardadas
  para esa actividad (se definen en **Programador** → *Subopciones por actividad*), o el navegador aún
  trae el `gestion.js` anterior: con **F5** se carga la versión nueva.
- **No sé todavía en qué aplicativo fue la capacitación** → déjalo en **«(Sin aplicativo)»** y guarda: el
  campo es opcional y el título queda solo con la actividad y su servicio.
- **No aparece el campo «Tipo de aplicativo»** → es el escalón que sigue: se configura en **Programador** →
  *Tipos por aplicativo*, eligiendo el aplicativo y escribiendo sus tipos. Si el aplicativo no tiene ninguna
  tipo guardado, el campo **no** aparece en **+ Nuevo** (igual que pasa con Región y con Servicio). Y si ya
  lo configuraste, recarga con **F5**: el catálogo se lee al abrir la página.
- **Tampoco sé el tipo cuando capturo** → déjalo en **«(Sin tipo de aplicativo)»**: el título se arma sin esa
  pieza, como con el aplicativo.
- **Guardé un catálogo y en el calendario sigue igual** → el catálogo se lee al abrir la página; cierra
  y vuelve a abrir **+ Nuevo** (o recarga con F5). Los elementos ya registrados conservan su título.
- **En el calendario veo solo hasta el jueves y abajo sale una barra horizontal** → el ancho de la página ya
  se levantó (los siete días caben hasta en 1366 px), así que eso es el `calendario/index.html` **en caché**:
  recarga con **Ctrl+F5**. Si en cambio ves los siete días pero muy apretados, la ventana del navegador está
  por debajo de ~1100 px y la pantalla pasa a una sola columna.
- **Un usuario no ve un hospital que sí existe** → mira su renglón en **Usuarios** → columna **Región**:
  si tiene una asignada, solo ve los hospitales de esa región (y así lo dice la nota del diálogo Editar).
  Para que vea todos, elíjele **«Todas las regiones»** en la lista y guarda. Si la región sale con el
  aviso «sin hospitales» es que el Programador aún no le escribió ninguno, y en ese caso **no limita**.
- **Asigné una región y el desplegable de Usuarios no la trae** → primero hay que guardarla en
  **Programador** → *Regiones médicas*; el servidor rechaza una región que no esté en esa lista, y también
  una que todavía no tenga hospitales (hasta que no tiene ninguno, no limitaría a nadie).
- **El campo Servicio no aparece, o no cambia al elegir el hospital** → con **F5** se carga el
  `gestion.js` nuevo (la versión se controla con `?v=20261117b` en las pantallas). Si ya está nuevo, el
  hospital elegido no tiene servicios guardados en **Programador** → *Servicios por hospital*: se
  selecciona el hospital en su desplegable, se agrega la lista y se pulsa **Guardar servicios**. Con el
  catálogo sin configurar, todos los hospitales muestran la lista general; mientras no se elija
  hospital tampoco se muestra el campo.
- **Puerto 5023 ocupado** → cambia la URL al instalar el servicio (variable `URL` dentro de `instalar-servicio.bat`).

---

*Documentación generada para el sistema unificado. Para el detalle interno del código, ver la carpeta `docs/`.*
