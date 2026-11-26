namespace Meditechnosys.Intranet.Models;

/// Una linea de la bitacora de accesos: quien hizo que, cuando y desde donde.
/// `Id` es 0 en el almacen JSON (no hay identidad numerica); en MySQL es autonumerico.
/// `Dispositivo` es el texto que se saca del User-Agent del navegador al registrar (se guarda).
/// `Equipo` (nombre en la red) y `Ubicacion` (a que area corresponde esa IP) NO se guardan en la
/// fila: se calculan al consultar, para que sirvan tambien con los miles de registros antiguos que
/// solo tienen la IP y para que corregir un rango surta efecto de inmediato.
public record AuditEntry(long Id, DateTime Momento, string Usuario, string Evento, string Detalle,
    string Ip, string Dispositivo = "")
{
    public string Equipo { get; init; } = "";
    public string Ubicacion { get; init; } = "";

    /// <summary>De dónde salió la ubicación que se muestra: «posición real del equipo», «punto aprendido
    /// del mismo rango de IP», «escrita para su rango de IP», «dirección del sitio» o «sin permiso del
    /// navegador …». Tampoco se guarda en la fila: se deduce junto con la ubicación.</summary>
    public string FuenteUbicacion { get; init; } = "";

    /// <summary>Margen de esa lectura en metros (cuando el punto lo dio un aparato de verdad, el propio
    /// equipo o el vecino de rango del que se aprendió). 0 = sin dato.</summary>
    public int PrecisionMetros { get; init; }
}

/// <summary>Una IP vista en la bitácora y cuántos movimientos tiene: sirve para que el
/// administrador vea qué equipos conviene asignarle una ubicación.</summary>
public record IpUso(string Ip, int Usos);

/// Nombres de evento usados en toda la aplicacion (se guardan tal cual en la bitacora).
public static class AuditEvents
{
    public const string Login = "Inicio de sesión";
    public const string LoginFallido = "Inicio de sesión fallido";
    public const string Logout = "Cierre de sesión";
    public const string CrearUsuario = "Alta de usuario";
    public const string CambiarRol = "Cambio de rol";
    public const string CambiarPassword = "Cambio de contraseña";
    public const string Activar = "Activación de usuario";
    public const string Desactivar = "Desactivación de usuario";
    public const string EliminarUsuario = "Baja de usuario";
    public const string CambiarDatos = "Edición de datos de usuario";
    /// Región que se le deja ver a un usuario desde la pantalla Usuarios.
    public const string AsignarRegion = "Asignación de región";
    /// Alta, cambio o desvinculación del buzon de correo enlazado a un usuario. Se anota el correo
    /// del buzon, jamas su contrasena.
    public const string EnlazarBuzon = "Enlace de buzon de correo";
    public const string CrearActividad = "Alta de actividad";
    public const string CrearIncidente = "Alta de incidente";
    public const string CrearTicket = "Alta de ticket";
    public const string EditarActividad = "Edición de actividad";
    public const string EliminarActividad = "Baja de actividad";
    public const string CompletarActividad = "Actividad completada";
    /// Adjuntar la foto o el archivo que respalda el trabajo al cerrar un elemento.
    public const string AdjuntarEvidencia = "Evidencia adjuntada";
    public const string CancelarActividad = "Actividad cancelada";
    public const string CambiarEstatus = "Cambio de estatus";
    /// Devolver una actividad Completada o Cerrada a Pendiente. Es exclusivo del Administrador desde
    /// que el alta del Ingeniero se cierra en el mismo momento, con su evidencia.
    public const string ReabrirActividad = "Actividad reabierta";
    public const string RegistrarAvance = "Registro de avance";
    public const string AsignarTicket = "Asignación de ticket";
    public const string CerrarTicket = "Cierre de ticket";
    public const string Observacion = "Observación";
    public const string EntregarReporte = "Reporte de estatus";
    public const string CambiarPermisos = "Cambio de permisos";
    public const string CambiarConfig = "Cambio de configuración";
    /// Tiempo de sesión que cada usuario elige para su propia cuenta (botón ⚙ de la cabecera del
    /// inicio). Se anota el cambio de minutos, no la cookie ni ningún secreto.
    public const string CambiarSesion = "Cambio de tiempo de sesión";
    /// Tema —Oscuro o Claro— que cada usuario elige para su propia cuenta en el bloque «Tema» del
    /// botón ⚙. Se anota el cambio de valor, como con el tiempo de sesión.
    public const string CambiarTema = "Cambio de tema";
    public const string ExportarExcel = "Descarga de Excel";
    public const string CrearItemInventario = "Alta en inventario";
    public const string EditarItemInventario = "Edición en inventario";
    public const string EliminarItemInventario = "Baja en inventario";
    public const string CrearMovimiento = "Alta de movimiento (almacén)";
    public const string ConfirmarMovimiento = "Confirmación de movimiento (almacén)";
    public const string RecibirMovimiento = "Recepción de traslado (almacén)";
    public const string CancelarMovimiento = "Cancelación/rechazo de movimiento (almacén)";
    public const string SubirDocumento = "Alta de documento (Playbook)";
    public const string EditarDocumento = "Edición de documento (Playbook)";
    public const string ReemplazarDocumento = "Archivo reemplazado (Playbook)";
    public const string EliminarDocumento = "Baja de documento (Playbook)";
    public const string CrearAviso = "Alta de aviso";
    public const string EditarAviso = "Edición de aviso";
    public const string EliminarAviso = "Baja de aviso";
    public const string CrearActa = "Alta de acta administrativa";
    public const string EditarActa = "Edición de acta administrativa";
    public const string EliminarActa = "Baja de acta administrativa";
    public const string CrearCredencial = "Alta de credencial";
    public const string EditarCredencial = "Edición de credencial";
    public const string EliminarCredencial = "Baja de credencial";
    public const string Acceso = "Acceso";
}
