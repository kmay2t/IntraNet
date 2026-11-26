namespace Meditechnosys.Intranet.Models;

/// Definición de un permiso administrable desde "Roles y permisos".
/// <param name="Desde">
/// Versión del catálogo en la que apareció la clave (1 = catálogo original). Se escribe como
/// número literal, **nunca** como `CatalogoPermisos.Version`: si se usara `Version`, al subir el
/// catálogo cambiaría la fecha de introducción de claves antiguas y la matriz guardada volvería a
/// completarse con los valores por defecto, pisando lo que el administrador editó a mano.
/// </param>
/// <param name="SoloRaiz">
/// Con `true`, la clave NO entra en el conjunto por defecto de ningún rol: solo la cuenta root la
/// tiene (ver `RolesStore.Calcular`, que le entrega el catálogo completo). Cualquier otro
/// Administrador la recibe marcando la casilla en "Roles y permisos".
/// </param>
public record Permiso(string Clave, string Descripcion, int Desde = 1, bool SoloRaiz = false);

/// Un grupo de permisos para mostrarlos ordenados en la matriz.
public record GrupoPermisos(string Nombre, IReadOnlyList<Permiso> Permisos);

/// Catálogo completo de permisos del módulo Gestión y qué rol los tiene por defecto.
/// La matriz se puede editar en /admin/roles/; lo editado se guarda en el almacén de permisos
/// y estas claves son el punto de control que consultan la API y las páginas.
public static class CatalogoPermisos
{
    /// Versión del catálogo. Al guardar una matriz se anota esta versión; si después se agregan
    /// permisos nuevos, las matrices guardadas se completan con el valor por defecto del rol para
    /// que nadie pierda el acceso a las funciones nuevas tras una actualización.
    /// 11: la bandeja de correos pasa a ser visible por defecto para Admin e Ingeniero (las matrices
    /// guardadas antes de este cambio no traían las claves `correo.*` y se completan solas).
    /// 12: permiso `programador.administrar` (pantalla Programador). Se marca como SoloRaiz, así que
    /// ninguna matriz guardada lo recibe por defecto: lo concede el Administrador desde Roles.
    /// 13: permiso `items.reabrir` (reabrir una actividad ya completada o cerrada). Nace del lado
    /// administrativo —el Ingeniero no lo trae por defecto— porque desde este cambio la actividad que
    /// registra el Ingeniero se cierra en el acto y deshacer ese cierre es decisión del Administrador.
    /// 14: el ciclo de vida se recorta a **Pendiente, Completada y Cancelada**. Se retiran las claves
    /// `items.registrarAvance` (el avance ya no es un porcentaje: se escribe en los comentarios) y
    /// `tickets.cerrar` (ya no existe el cierre formal sobre lo completado), y nace `items.cancelar`:
    /// cancelar es decisión de quien administra, no del responsable.
    /// 15: `tickets.historialCambios` **empieza a valer de verdad**. Antes no lo consultaba nadie y el
    /// Ingeniero lo traía por defecto, así que la pestaña «Historial de cambios» se le mostraba a todos.
    /// Ahora rige la pestaña y el endpoint: sale del `IngenieroPorDefecto` y se marca `Desde: 15` para
    /// que las matrices ya guardadas del lado administrativo la reciban solas y el Ingeniero no.
    public const int Version = 15;

    public const string CalendarioAcceder = "calendario.acceder";
    public const string CrearActividad = "items.crearActividad";
    public const string CrearIncidente = "items.crearIncidente";
    public const string CrearTicket = "items.crearTicket";
    public const string ActualizarEstatus = "items.actualizarEstatus";
    public const string EditarItems = "items.editar";
    public const string EliminarItems = "items.eliminar";
    /// <summary>Pasar un elemento a **Cancelada** (con justificación obligatoria). Desde el pedido «el
    /// estado cancelado solo admin o quien tenga ese permiso la puede cambiar a dicho estado» ya no es
    /// una acción del responsable sino administrativa, y lo cancelado no se reabre: solo queda el registro.</summary>
    public const string CancelarItems = "items.cancelar";
    /// <summary>Devolver una actividad **Completada** a **Pendiente**. Existe porque desde el pedido
    /// «cuando el perfil de ingeniero asigna una actividad debe completarse en ese momento» el alta del
    /// Ingeniero ya nace completada: si el Administrador decide que todavía no estaba, es él quien la
    /// reabre (y solo él puede ponerle después la fecha límite).</summary>
    public const string ReabrirItems = "items.reabrir";
    public const string TicketsAsignar = "tickets.asignar";
    public const string TicketsCambiarIngeniero = "tickets.cambiarIngeniero";
    public const string TicketsPrioridad = "tickets.prioridad";
    public const string TicketsFechaLimite = "tickets.fechaLimite";
    public const string TicketsObservaciones = "tickets.observaciones";
    public const string TicketsHistorial = "tickets.historialCambios";
    public const string HistoricoActividades = "historico.actividades";
    public const string HistoricoExportar = "historico.exportar";
    public const string ReportesEntregar = "reportes.entregar";
    public const string ReportesConsultar = "reportes.consultar";
    public const string ReportesPeriodicidad = "reportes.configurarPeriodicidad";
    public const string SupervisionIngeniero = "supervision.porIngeniero";
    public const string InventarioConsultar = "inventario.consultar";
    public const string InventarioAdministrar = "inventario.administrar";
    public const string InventarioExportar = "inventario.exportar";
    public const string AlmacenConsultar = "almacen.consultar";
    public const string AlmacenRegistrar = "almacen.registrar";
    public const string AlmacenAutorizar = "almacen.autorizar";
    public const string AlmacenExportar = "almacen.exportar";
    public const string PlaybookConsultar = "playbook.consultar";
    public const string PlaybookAdministrar = "playbook.administrar";
    public const string RolesAdministrar = "roles.administrar";
    public const string ConfiguracionAdministrar = "configuracion.administrar";
    public const string ConfiguracionFechaLimite = "configuracion.fechaLimite";
    /// <summary>Editar los catálogos del módulo Gestión desde la pantalla Programador: la lista de
    /// actividades, sus subopciones (los aplicativos de una Capacitación), los servicios y los
    /// hospitales. Por defecto solo la cuenta root lo tiene.</summary>
    public const string ProgramadorAdministrar = "programador.administrar";
    // Permisos de acceso a las herramientas que antes no tenían clave propia. Sirven para que la
    // página de inicio única muestre (y el servidor autorice) solo lo que cada rol/usuario puede abrir.
    public const string PatologiaConsultar = "patologia.consultar";
    public const string DicomVer = "dicom.ver";
    /// <summary>Abrir «Envío de estudios DICOM»: subir carpetas de estudios DICOM a un nodo PACS (C-STORE).</summary>
    public const string DicomEnviar = "dicom.enviar";
    /// <summary>
    /// Además de lo anterior, puede «Traer de otro servidor y reenviar»: consultar un nodo PACS con el
    /// AE MEDI, bajar los estudios elegidos (C-MOVE) y volverlos a enviar por C-STORE a otro nodo.
    /// Es el único permiso que mueve imágenes *entre* servidores PACS, así que se otorga aparte.
    /// </summary>
    public const string DicomReenviar = "dicom.reenviar";
    public const string UsuariosAdministrar = "usuarios.administrar";
    public const string BitacoraVer = "bitacora.ver";
    /// Consulta la bitácora del quemador DICOM: qué usuario bajó qué estudios y los grabó
    /// en USB o CD, desde qué equipo y con qué dirección IP.
    public const string QuemadorVer = "quemador.ver";
    public const string CorreoVer = "correo.ver";
    public const string CorreoResponder = "correo.responder";
    public const string AvisosAdministrar = "avisos.administrar";
    public const string ActasAdministrar = "actas.administrar";
    public const string CredencialesAdministrar = "credenciales.administrar";

    public static readonly IReadOnlyList<GrupoPermisos> Grupos = new[]
    {
        new GrupoPermisos("Sistemas médicos", new[]
        {
            new Permiso(PatologiaConsultar, "Abrir la Consulta de reportes de Patología", Desde: 8),
            new Permiso(DicomVer, "Abrir el Visor de estudios DICOM", Desde: 8),
            new Permiso(DicomEnviar, "Abrir el Envío de estudios DICOM (subir carpetas de estudios .dcm a un nodo PACS)", Desde: 10),
            new Permiso(DicomReenviar, "Traer estudios de otro nodo PACS y reenviarlos (C-MOVE + C-STORE entre servidores)", Desde: 10),
        }),
        new GrupoPermisos("Acceso", new[]
        {
            new Permiso(CalendarioAcceder, "Acceder al Calendario de actividades"),
        }),
        new GrupoPermisos("Creación (desde el calendario)", new[]
        {
            new Permiso(CrearActividad, "Crear actividades"),
            new Permiso(CrearIncidente, "Crear incidentes"),
            new Permiso(CrearTicket, "Crear tickets"),
        }),
        new GrupoPermisos("Seguimiento", new[]
        {
            new Permiso(ActualizarEstatus, "Actualizar el estatus de sus actividades y tickets"),
            new Permiso(ReportesEntregar, "Entregar reportes de estatus"),
        }),
        new GrupoPermisos("Tickets (administración)", new[]
        {
            new Permiso(TicketsAsignar, "Asignar tickets a ingenieros"),
            new Permiso(TicketsCambiarIngeniero, "Cambiar el ingeniero responsable"),
            new Permiso(TicketsPrioridad, "Establecer prioridades"),
            new Permiso(TicketsFechaLimite, "Establecer fechas límite"),
            new Permiso(TicketsObservaciones, "Agregar observaciones"),
            new Permiso(CancelarItems, "Cancelar actividades, incidentes y tickets (lo cancelado ya no se mueve)", Desde: 14),
            new Permiso(TicketsHistorial, "Ver la pestaña «Historial de cambios» de cada elemento", Desde: 15),
            new Permiso(EditarItems, "Editar actividades, incidentes y tickets"),
            new Permiso(EliminarItems, "Eliminar elementos pendientes"),
            new Permiso(ReabrirItems, "Reabrir actividades ya completadas (las devuelve a Pendiente)", Desde: 13),
        }),
        new GrupoPermisos("Históricos y supervisión", new[]
        {
            new Permiso(HistoricoActividades, "Consultar el Histórico de actividades (actividades, incidentes y tickets)"),
            new Permiso(HistoricoExportar, "Descargar el Histórico de actividades en Excel", Desde: 8),
            new Permiso(SupervisionIngeniero, "Supervisar actividades asignadas por ingeniero"),
        }),
        new GrupoPermisos("Reportes de estatus", new[]
        {
            new Permiso(ReportesConsultar, "Consultar los reportes de estatus"),
            new Permiso(ReportesPeriodicidad, "Configurar la periodicidad de los reportes"),
        }),
        new GrupoPermisos("Inventarios", new[]
        {
            new Permiso(InventarioConsultar, "Consultar los inventarios de equipos, IPs y AETitle", Desde: 8),
            new Permiso(InventarioExportar, "Descargar los inventarios en Excel", Desde: 8),
            new Permiso(InventarioAdministrar, "Dar de alta, editar y eliminar registros de inventario", Desde: 8),
        }),
        // Estas permisos rigen las entradas/salidas que viven dentro de la pantalla única
        // "Inventario de equipos" (ya no existe un módulo de Almacén independiente).
        new GrupoPermisos("Inventario de equipos · entradas y salidas", new[]
        {
            new Permiso(AlmacenConsultar, "Consultar existencias, movimientos e historial de equipos", Desde: 8),
            new Permiso(AlmacenRegistrar, "Registrar entradas y salidas de equipos", Desde: 8),
            new Permiso(AlmacenExportar, "Descargar existencias e historial de equipos en Excel", Desde: 8),
            new Permiso(AlmacenAutorizar, "Autorizar, recibir y cancelar movimientos de equipos", Desde: 8),
        }),
        new GrupoPermisos("Playbook", new[]
        {
            new Permiso(PlaybookConsultar, "Consultar y abrir los documentos del Playbook", Desde: 8),
            new Permiso(PlaybookAdministrar, "Subir, editar y eliminar documentos del Playbook", Desde: 8),
        }),
        new GrupoPermisos("Administrativos (actas y credenciales)", new[]
        {
            new Permiso(ActasAdministrar, "Crear, editar, eliminar e imprimir actas administrativas", Desde: 8),
            new Permiso(CredencialesAdministrar, "Generar credenciales ligadas a usuarios con foto tamaño infantil", Desde: 8),
        }),
        new GrupoPermisos("Avisos del inicio", new[]
        {
            new Permiso(AvisosAdministrar, "Publicar, editar y eliminar los avisos del inicio (los demás solo los ven)", Desde: 8),
        }),
        new GrupoPermisos("Administración", new[]
        {
            new Permiso(RolesAdministrar, "Administrar roles y permisos"),
            new Permiso(UsuariosAdministrar, "Administrar las cuentas de usuario (altas, bajas, rol y permisos)", Desde: 8),
            new Permiso(BitacoraVer, "Consultar la bitácora de accesos", Desde: 8),
            new Permiso(QuemadorVer, "Consultar la bitácora del quemador DICOM: qué se descargó y grabó, en qué equipo y con qué IP", Desde: 9),
            new Permiso(CorreoVer, "Ver la bandeja de correos (recibidos, enviados y borradores) del buzón enlazado o del de soporte", Desde: 11),
            new Permiso(CorreoResponder, "Abrir correos, descargar adjuntos, responder, reenviar y guardar borradores", Desde: 11),
            new Permiso(ConfiguracionAdministrar, "Acceder a Configuración"),
            new Permiso(ConfiguracionFechaLimite, "Habilitar o deshabilitar la fecha límite", Desde: 8),
            new Permiso(ProgramadorAdministrar, "Editar los catálogos de Gestión desde Programador (actividades, aplicativos, servicios y hospitales)", Desde: 12, SoloRaiz: true),
        }),
    };

    /// Todo el catálogo plano (para validar claves al guardar la matriz).
    public static readonly IReadOnlyList<string> TodasLasClaves =
        Grupos.SelectMany(g => g.Permisos).Select(p => p.Clave).ToList();

    /// Claves que ningún rol recibe por defecto: solo la cuenta root las tiene (le entra el catálogo
    /// completo). Se conceden a otros roles o usuarios desde "Roles y permisos".
    public static readonly IReadOnlySet<string> SoloRaiz = new HashSet<string>(
        Grupos.SelectMany(g => g.Permisos).Where(p => p.SoloRaiz).Select(p => p.Clave), StringComparer.Ordinal);

    /// Permisos por defecto del Ingeniero. El Administrador tiene todo por defecto.
    /// Nota: el Ingeniero NO puede realizar la asignación formal de tickets a otros ingenieros,
    /// NO administra roles ni configuración, NO ve la fecha límite, NO administra el Playbook,
    /// NO cancela (`items.cancelar`), NO reabre (`items.reabrir`) y **NO ve el historial de cambios**
    /// (`tickets.historialCambios`): su seguimiento se escribe en los comentarios del elemento y la
    /// tarjeta le deja solo la pestaña de reportes de estatus. Si alguien necesita el historial,
    /// se le concede la clave desde «Roles y permisos».
    /// La bandeja de correos sí la tiene: ver y responder (con borradores y enviados) es parte del
    /// trabajo diario de soporte; el buzón que ve cada quien es el suyo si enlazó uno, si no, soporte.
    private static readonly HashSet<string> IngenieroPorDefecto = new(StringComparer.Ordinal)
    {
        PatologiaConsultar, DicomVer,
        CalendarioAcceder, CrearActividad, CrearIncidente, CrearTicket,
        ActualizarEstatus, ReportesEntregar,
        InventarioConsultar, InventarioAdministrar, InventarioExportar, PlaybookConsultar,
        AlmacenConsultar, AlmacenRegistrar, AlmacenExportar,
        CorreoVer, CorreoResponder,
    };

    public static bool EsClaveValida(string? clave) => clave is not null && TodasLasClaves.Contains(clave);

    /// Permisos activos por defecto para un rol (cuando no hay matriz guardada). El Administrador
    /// tiene todo **salvo las claves marcadas `SoloRaiz`**, que quedan reservadas a la cuenta root
    /// hasta que el Administrador las conceda; el Ingeniero, el subconjunto de seguimiento;
    /// **un rol personalizado arranca sin permisos** y el Administrador se los asigna a mano desde
    /// "Roles y permisos".
    public static IReadOnlySet<string> PorDefecto(string rol) =>
        string.Equals(rol, Roles.Admin, StringComparison.OrdinalIgnoreCase)
            ? new HashSet<string>(TodasLasClaves.Except(SoloRaiz), StringComparer.Ordinal)
        : string.Equals(rol, Roles.Ingeniero, StringComparison.OrdinalIgnoreCase)
            ? new HashSet<string>(IngenieroPorDefecto, StringComparer.Ordinal)
        : new HashSet<string>(StringComparer.Ordinal);

    /// Claves agregadas al catálogo después de `version` que ese rol tendría por defecto.
    /// Sirve para completar una matriz guardada antes de la actualización sin pisar lo editado.
    public static IEnumerable<string> NuevasDesde(int version, string rol)
    {
        var porDefecto = PorDefecto(rol);
        return Grupos.SelectMany(g => g.Permisos)
            .Where(p => p.Desde > version && porDefecto.Contains(p.Clave))
            .Select(p => p.Clave);
    }
}
