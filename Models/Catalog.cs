namespace Meditechnosys.Intranet.Models;

/// Configuración de servicios deshabilitados. Cada valor false indica que el servicio
/// no debe aparecer en la interfaz ni estar accesible por URL directa.
public class ServiciosOptions
{
    public bool PatologiaConsultar { get; set; } = true;
    public bool DicomVer { get; set; } = true;
    public bool DicomEnviar { get; set; } = true;
    public bool DicomListaTrabajo { get; set; } = true;
}

/// Una herramienta (tarjeta) de la página de inicio. `Permiso` es la clave del catálogo que decide
/// si la tarjeta se muestra: si es `null` la ven todos los usuarios autenticados; si tiene valor,
/// solo aparece cuando los permisos efectivos del usuario la incluyen. La misma clave autoriza la
/// ruta en el servidor (ver los guards de `Program.cs`), así que interfaz y backend no se separan.
/// `Alternativos` permite reunir consultas en una tarjeta: Reportes aparece al poder consultar
/// Bitácora, Quemador, Histórico o Estatus. `Visibles` elige una pestaña autorizada como entrada.
public record AppLink(string Tag, string Name, string Description, string Url, string Keywords,
    string? Permiso = null, string[]? Alternativos = null);

public record SystemStatus(string Name, string State, string Label);

/// Catálogo único de herramientas de la intranet. Antes existían dos menús separados (Ingeniería y
/// Administración); ahora hay **una sola página de inicio** que muestra únicamente las tarjetas a
/// las que el usuario tiene acceso según sus permisos efectivos (rol + ajustes por usuario).
public static class Catalog
{
    public const string SupportEmail = "soporte@meditechnosys.mx";

    /// Todas las herramientas, agrupadas por `Tag` en la página de inicio. El orden de la lista
    /// define el orden de los grupos y de las tarjetas.
    public static readonly IReadOnlyList<AppLink> Herramientas = new[]
    {
        // ---- Sistemas médicos ----
        new AppLink("Sistemas médicos", "Consulta de reportes", "Busca, descarga y edita reportes de Patología (Especialidades y Ginecología).", "/patologia/consulta/index.html", "patología patologia reportes consulta buscar folio nss especialidades ginecología pdf excel editar subir", CatalogoPermisos.PatologiaConsultar),
        new AppLink("Sistemas médicos", "Visor de estudios DICOM", "Busca y descarga estudios DICOM desde el PACS (C-FIND / C-MOVE).", "/dicom/index.html", "visor imágenes estudios radiología dicom pacs descargar zip", CatalogoPermisos.DicomVer),
        new AppLink("Sistemas médicos", "Envío de estudios DICOM", "Sube carpetas de estudios DICOM a un nodo PACS con C-STORE, y —si tienes permiso— trae estudios de otro nodo y reenvíalos.", "/dicom/enviar/index.html", "enviar subir dicom pacs c-store ae title medimport reenviar traer otro servidor asyn", CatalogoPermisos.DicomEnviar),
        new AppLink("Sistemas médicos", "Consulta lista de trabajo", "Consulta los procedimientos programados de un nodo (MWL), filtra por nombre, ID, fecha, hora, modalidad y procedimiento, y manda la prueba de conexión o de imagen.", "/dicom/lista-trabajo", "lista trabajo programados agenda modalidad mwl worklist aetitle medi ping contraste círculo prueba filtro nombre id fecha hora procedimiento paciente", CatalogoPermisos.DicomVer),
        // ---- Gestión ----
        new AppLink("Gestión", "Calendario de actividades", "Crea actividades, incidentes y tickets y da seguimiento con comentarios, reportes y estatus.", "/calendario/index.html", "calendario actividades incidentes tickets agenda asignadas completar informe cancelar pendiente comentario reporte hospital", CatalogoPermisos.CalendarioAcceder),
        new AppLink("Gestión", "Inventario de equipos", "Pestaña única de Ingresos: selector Almacén/HES/HGR/HGO/HI, botón Añadir y hoja de entrada y de salida en cada fila.", "/gestion/inventario-equipos/index.html", "inventario equipos ingresos almacen hes hgr hgo hi añadir hoja entrada salida marca modelo serie cantidad excel", CatalogoPermisos.AlmacenConsultar),
        new AppLink("Gestión", "Inventario de IPs", "Direcciones IP por hospital, con búsqueda, alta, edición y descarga en Excel.", "/gestion/inventario-ips/index.html", "inventario ips direcciones ip máscara subred vlan hospital excel", CatalogoPermisos.InventarioConsultar),
        new AppLink("Gestión", "Inventario de AETitle", "AE Titles y nodos DICOM por hospital, con búsqueda, alta, edición y descarga en Excel.", "/gestion/inventario-aetitles/index.html", "inventario aetitle ae title dicom nodo pacs puerto modalidad hospital excel", CatalogoPermisos.InventarioConsultar),
        new AppLink("Gestión", "Playbook", "Biblioteca de documentación técnica: manuales, procedimientos y guías en PDF.", "/gestion/playbook/index.html", "playbook documentación manuales procedimientos guías pdf versión hospital", CatalogoPermisos.PlaybookConsultar),
        // ---- Administrativos ----
        new AppLink("Administrativos", "Actas administrativas", "Captura, edita e imprime actas administrativas con folio, hechos, acuerdos y firmas.", "/admin/administrativos/index.html#actas", "actas administrativas sanciones amonestaciones hechos acuerdos testigos firmas folio imprimir", CatalogoPermisos.ActasAdministrar),
        new AppLink("Administrativos", "Credenciales", "Genera credenciales ligadas a los usuarios con foto tamaño infantil, en el formato oficial de la empresa.", "/admin/administrativos/index.html#credenciales", "credenciales gafete identificación foto infantil nss cédula vigencia imprimir png", CatalogoPermisos.CredencialesAdministrar),
        // ---- Administración ----
        new AppLink("Administración", "Roles y permisos", "Define los roles, qué puede hacer cada uno y los ajustes por usuario.", "/admin/roles/index.html", "roles permisos matriz accesos administrador ingeniero usuarios", CatalogoPermisos.RolesAdministrar),
        new AppLink("Administración", "Programador", "Edita los catálogos del módulo Gestión: las actividades y sus subopciones (los aplicativos de una Capacitación), los servicios y las regiones médicas, con los hospitales de cada región escritos en su tarjeta.", "/admin/programador/index.html", "programador catalogos actividades subopciones aplicativos capacitacion servicios hospitales regiones medicas cmn la raza desglose listas fusiones exportacion revision migracion documentacion verificacion mantenimiento", CatalogoPermisos.ProgramadorAdministrar),
        new AppLink("Administración", "Gestión de usuarios", "Altas, bajas, desactivación, rol y permisos individuales de cada cuenta.", "/admin/usuarios/index.html", "usuarios altas bajas contraseñas cuentas desactivar editar eliminar rol permisos", CatalogoPermisos.UsuariosAdministrar),
        // La tarjeta «Configuración» ya no está: sus dos parámetros viven en el diálogo del botón
        // ⚙ de la cabecera (`js/preferencias.js`), sección «Parámetros del módulo Gestión», que solo
        // se muestra con `configuracion.administrar`. La ruta /admin/config/ y su guard quedan
        // intactos por si alguien llega por enlace directo.
        new AppLink("Administración", "Reportes", "Bitácora de usuarios, histórico de actividades y reportes de estatus, organizados por pestañas con sus filtros y descarga a Excel.", "/admin/bitacora/index.html", "reportes bitácora auditoría accesos registros seguridad movimientos quemador grabador usb cd dicom estudios descargas ip equipo sesión ventana usuario histórico actividades incidentes tickets ingeniero filtros hospital excel resumen vencidas estatus comentario canceladas", CatalogoPermisos.BitacoraVer, new[] { CatalogoPermisos.QuemadorVer, CatalogoPermisos.HistoricoActividades, CatalogoPermisos.ReportesConsultar }),
        new AppLink("Administración", "Bandeja de soporte", "Lee y responde con el correo de soporte y, si el administrador lo conectó, también con tu segundo correo: tú eliges cuál ves y con cuál envías.", "/admin/correos/index.html", "correo correos bandeja buzón soporte email mensajes inbox recibido segunda cuenta", CatalogoPermisos.CorreoVer),
    };

    /// Herramientas visibles para un conjunto de permisos efectivos (las de `Permiso == null`
    /// siempre se muestran). La página de inicio lo usa para pintar solo lo que el usuario puede abrir.
    public static IReadOnlyList<AppLink> Visibles(IReadOnlySet<string> permisos, ServiciosOptions? servicios = null) =>
        Herramientas.Where(a => PuedeAbrir(a, permisos) && ServicioHabilitado(a, servicios)).Select(a =>
        {
            // Reportes reúne tres consultas, pero NO concede permisos nuevos. Si la persona no
            // puede leer la Bitácora, entra por su primera pestaña autorizada. Los guards de cada
            // ruta/API se conservan y los enlaces antiguos siguen abriendo el apartado correcto.
            if (a.Url == "/admin/bitacora/index.html" &&
                !permisos.Contains(CatalogoPermisos.BitacoraVer) &&
                !permisos.Contains(CatalogoPermisos.QuemadorVer))
                return a with { Url = permisos.Contains(CatalogoPermisos.HistoricoActividades)
                    ? "/gestion/historico/index.html" : "/admin/reportes/index.html" };
            return a;
        }).ToList();

    /// <summary>
    /// Si la tarjeta se puede abrir: basta con tener su permiso o cualquiera de los alternativos, que
    /// es el caso de Reportes: basta con poder consultar cualquiera de sus apartados.
    /// </summary>
    public static bool PuedeAbrir(AppLink a, IReadOnlySet<string> permisos) =>
        a.Permiso is null || permisos.Contains(a.Permiso) ||
        (a.Alternativos ?? Array.Empty<string>()).Any(permisos.Contains);

    /// <summary>
    /// Verifica si el servicio de una tarjeta está habilitado según la configuración.
    /// Si no se proporciona configuración, se asume que todos están habilitados (comportamiento original).
    /// </summary>
    private static bool ServicioHabilitado(AppLink a, ServiciosOptions? servicios)
    {
        if (servicios is null) return true;

        return a.Permiso switch
        {
            CatalogoPermisos.PatologiaConsultar => servicios.PatologiaConsultar,
            CatalogoPermisos.DicomVer when a.Url.StartsWith("/dicom/lista-trabajo") => servicios.DicomListaTrabajo,
            CatalogoPermisos.DicomVer => servicios.DicomVer,
            CatalogoPermisos.DicomEnviar => servicios.DicomEnviar,
            _ => true
        };
    }

    public static IReadOnlyList<SystemStatus> Status() => new[]
    {
        new SystemStatus("GenDocM", "ok", "Operativo"),
        new SystemStatus("Correo", "ok", "Operativo"),
        new SystemStatus("VPN", "warn", "En revisión"),
    };
}
