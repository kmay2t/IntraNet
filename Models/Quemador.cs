namespace Meditechnosys.Intranet.Models;

/// Una linea de la bitácora del quemador DICOM (DicomQrDesktop).
///
/// A diferencia de la bitácora de accesos —que es una fila por petición—, esta es
/// **una fila por estudio tocado**: una descarga de tres estudios genera tres filas
/// compartiendo el mismo `Lote`. Así se puede responder «¿qué estudio se llevó esta
/// persona?» filtrando por paciente, número de acceso o UID sin parsear texto libre.
///
/// `Id` vale 0 en el almacén JSON (no hay identidad numérica); en MySQL es autonumérico.
/// `Momento` siempre se guarda en UTC.
/// `Usuario` y `Ip` los fija el servidor (sesión y peer TCP), nunca lo que reporta el cliente.
public record QuemadorEvento(
    long Id,
    DateTime Momento,
    string Usuario,
    string Equipo,
    string Ip,
    string IpLocal,
    string Evento,
    string Resultado,
    string Lote,
    string Paciente,
    string PacienteId,
    string Acceso,
    string EstudioUid,
    string Modalidad,
    string FechaEstudio,
    int Imagines,
    string Medio,
    string Unidad,
    string Mensaje);

/// Lo que envía el programa del mostrador. El cliente no puede fijar `Usuario` ni `Ip`:
/// el endpoint los sobrescribe con la identidad de la sesión y la dirección real del peer,
/// para que la bitácora no se pueda falsear desde una workstation.
public record QuemadorEventoEntrada(
    string? Evento,
    string? Resultado,
    string? Lote,
    string? Equipo,
    string? IpLocal,
    DateTime? Momento,
    string? Paciente,
    string? PacienteId,
    string? Acceso,
    string? EstudioUid,
    string? Modalidad,
    string? FechaEstudio,
    int? Imagines,
    string? Medio,
    string? Unidad,
    string? Mensaje);

/// Criterios de consulta de la pantalla de administración. `Texto` busca por paciente,
/// identificación, número de acceso, UID del estudio, unidad o mensaje. `Lote` deja ver de golpe
/// todas las filas que generó una misma acción del mostrador (una descarga o una grabación).
/// `Desde`/`Hasta` son días locales y ambos entran en el recorte, para poder filtrar por mes, por
/// año o por un rango de fechas; `Dia` sigue siendo el atajo de un solo día.
public record QuemadorFiltro(
    string? Usuario = null,
    string? Equipo = null,
    string? Evento = null,
    string? Resultado = null,
    string? Texto = null,
    DateTime? Dia = null,
    int Limite = 500,
    string? Lote = null,
    DateTime? Desde = null,
    DateTime? Hasta = null);

/// Nombres de evento que reporta el quemador. Se guardan tal cual y se muestran en la pantalla.
public static class QuemadorEventos
{
    public const string InicioSesion = "Inicio de sesión";
    public const string IntentoFallido = "Intento de acceso fallido";
    public const string CierreSesion = "Cierre de sesión";
    public const string Busqueda = "Búsqueda";
    public const string Descarga = "Descarga";
    public const string GrabacionUsb = "Grabación en USB";
    public const string GrabacionCd = "Grabación en CD/DVD";

    /// Los que admiten una fila por estudio (llevan paciente/UID rellenos).
    public static readonly IReadOnlyList<string> ConEstudio = new[] { Busqueda, Descarga, GrabacionUsb, GrabacionCd };

    /// Todos, para validar lo que llega del cliente y para poblar el filtro de la pantalla.
    public static readonly IReadOnlyList<string> Todos = new[]
    {
        InicioSesion, IntentoFallido, CierreSesion, Busqueda, Descarga, GrabacionUsb, GrabacionCd
    };

    public static bool EsConocido(string? e) => e is not null && Todos.Contains(e, StringComparer.Ordinal);
}

/// Cómo terminó la acción registrada.
public static class QuemadorResultados
{
    public const string Exito = "éxito";
    public const string Parcial = "parcial";
    public const string Error = "error";
    public const string Cancelado = "cancelado";

    public static readonly IReadOnlyList<string> Todos = new[] { Exito, Parcial, Error, Cancelado };

    public static string Normalizar(string? r) =>
        Todos.FirstOrDefault(t => string.Equals(t, r, StringComparison.OrdinalIgnoreCase)) ?? Exito;
}
