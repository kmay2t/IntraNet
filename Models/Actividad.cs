namespace Meditechnosys.Intranet.Models;

/// Tipos de elemento del calendario de gestión. Los tres comparten almacén y flujo;
/// el tipo Ticket además recibe número consecutivo, asignación formal y reportes de estatus.
public static class TiposItem
{
    public const string Actividad = "Actividad";
    public const string Incidente = "Incidente";
    public const string Ticket = "Ticket";

    public static readonly IReadOnlyList<string> Todos = new[] { Actividad, Incidente, Ticket };

    public static bool EsValido(string? tipo) => tipo is not null && Todos.Contains(tipo);
}

/// Estados del ciclo de vida: desde el pedido «solo debe estar el estado pendiente, completado y
/// cancelado» los únicos estados vivos son **Pendiente → Completada**, con **Cancelada** como salida
/// administrativa (permiso `items.cancelar`, con justificación). "En proceso" y "Cerrada" se retiraron
/// de toda la intranet; las constantes quedan solo para reconocer registros antiguos, que se leen
/// ya normalizados (<see cref="Normalizar"/>). Una **Cancelada** ya no se mueve: solo queda el registro.
/// "Vencida" NO es un estado guardado: se calcula (fecha límite pasada sin terminar).
public static class EstadosActividad
{
    public const string Pendiente = "Pendiente";
    public const string EnProceso = "EnProceso";   // retirado: solo registros anteriores a 2026-11-20
    public const string Completada = "Completada";
    public const string Cancelada = "Cancelada";
    public const string Cerrada = "Cerrada";       // retirado: solo registros anteriores a 2026-11-20

    /// Los tres estados que existen hoy para crear, validar y pintar.
    public static readonly IReadOnlyList<string> Todos = new[] { Pendiente, Completada, Cancelada };

    public static bool EsValido(string? estado) => estado is not null && Todos.Contains(estado);

    /// Los registros guardados cuando aún existían cinco estados se leen con su equivalente de hoy:
    /// "En proceso" era un pendiente trabajado y "Cerrada" un completado revisado por el Administrador.
    public static string Normalizar(string? estado) => estado switch
    {
        EnProceso => Pendiente,
        Cerrada => Completada,
        null or "" => Pendiente,
        var otro => otro,
    };

    /// Abierto = aún admite cambios (solo Pendiente: lo completado y lo cancelado ya no se mueven).
    public static bool Abierto(string? estado) => Normalizar(estado) is Pendiente;
}

/// Prioridades de actividades, incidentes y tickets.
public static class Prioridades
{
    public const string Baja = "Baja";
    public const string Media = "Media";
    public const string Alta = "Alta";
    public const string Urgente = "Urgente";

    public static readonly IReadOnlyList<string> Todas = new[] { Baja, Media, Alta, Urgente };

    public static bool EsValida(string? prioridad) => prioridad is not null && Todas.Contains(prioridad);
}

/// Un elemento del calendario de gestión (actividad, incidente o ticket), ligado a un usuario.
/// <param name="Hospital">Hospital al que pertenece el registro (HES, HGR, HGO o HI).</param>
/// <param name="Usuario">Ingeniero responsable ("" = sin asignar, solo tickets recién creados).</param>
/// <param name="Fecha">Día de inicio (medianoche local) en el que aparece en el calendario.</param>
/// <param name="FechaLimite">Fecha límite de entrega (null = sin límite o con la opción deshabilitada).</param>
/// <param name="Numero">Número consecutivo del ticket (0 si no es ticket).</param>
/// <param name="PeriodicidadDias">Cada cuántos días se debe entregar reporte de estatus (0 = sin configurar).</param>
/// <param name="Asignado">Momento de la asignación formal del Admin.</param>
/// <param name="UltimoReporte">Momento del último reporte de estatus entregado.</param>
/// <param name="RelacionId">Elemento relacionado (p. ej. ticket creado a partir de un incidente).</param>
/// <param name="Informe">Reporte que escribe el responsable al completarla.</param>
/// <param name="Justificacion">Motivo obligatorio cuando se cancela.</param>
/// <remarks>`UltimoComentario` no se guarda: lo rellena el servicio de gestión al leer, con la fecha
/// del último comentario/observación del historial, para poder calcular el recordatorio de 3 días.</remarks>
public record Actividad(
    long Id,
    string Tipo,
    string Hospital,
    string Titulo,
    string Descripcion,
    string Usuario,
    DateTime Fecha,
    DateTime? FechaLimite,
    string Estado,
    string Prioridad,
    int Avance,
    string Informe,
    string Justificacion,
    string CreadoPor,
    DateTime Creado,
    DateTime? Cerrado,
    long Numero,
    int PeriodicidadDias,
    DateTime? Asignado,
    DateTime? UltimoReporte,
    long? RelacionId)
{
    /// Etiqueta corta para la UI: "#007 Título" en tickets, el título en el resto.
    public string Etiqueta => Tipo == TiposItem.Ticket && Numero > 0 ? $"#{Numero:000} · {Titulo}" : Titulo;

    /// Vencida: tiene fecha límite pasada y sigue abierta.
    public bool Vencida => FechaLimite.HasValue
        && FechaLimite.Value.Date < DateTime.Today
        && EstadosActividad.Abierto(Estado);

    /// Cuándo toca el siguiente reporte de estatus (null si no aplica).
    public DateTime? ProximoReporte
    {
        get
        {
            if (PeriodicidadDias <= 0 || !EstadosActividad.Abierto(Estado)) return null;
            var desde = UltimoReporte ?? Asignado ?? Creado;
            return desde.Date.AddDays(PeriodicidadDias);
        }
    }

    /// El sistema "solicita" el reporte cuando la fecha próxima ya pasó.
    public bool ReportePendiente => ProximoReporte is DateTime p && p.Date <= DateTime.Today;

    /// Cada cuántos días sin comentarios se pide el seguimiento en la tarjeta y en el calendario.
    public const int DiasParaRecordar = 3;

    /// Momento del último comentario u observación del historial (null si nunca le han escrito).
    /// **No se guarda**: lo rellena el servicio de gestión al leer, para no duplicar el historial.
    public DateTime? UltimoComentario { get; init; }

    /// Recordatorio de seguimiento: mientras el elemento siga abierto y hayan pasado 3 días o más
    /// desde el último comentario (o desde la fecha de inicio, si todavía no tiene ninguno), la
    /// tarjeta y el calendario avisan que toca escribir cómo va. Cada comentario nuevo reinicia la cuenta.
    public bool LeTocaReporte => EstadosActividad.Abierto(Estado)
        && (UltimoComentario ?? Fecha).Date.AddDays(DiasParaRecordar) <= DateTime.Today;
}

/// Reglas de validacion compartidas por los almacenes, el servicio de gestión y la API.
public static class ActividadRules
{
    public static string? TituloError(string? titulo) =>
        string.IsNullOrWhiteSpace(titulo) ? "El título es obligatorio."
        : titulo.Trim().Length < 3 ? "El título debe tener al menos 3 caracteres."
        : titulo.Trim().Length > 120 ? "El título no puede pasar de 120 caracteres."
        : null;

    public static string? UsuarioError(string? usuario) =>
        string.IsNullOrWhiteSpace(usuario) ? "Debe asignarse a un usuario." : null;

    public static string? TipoError(string? tipo) =>
        TiposItem.EsValido(tipo) ? null : "El tipo debe ser Actividad, Incidente o Ticket.";

    public static string? HospitalError(string? hospital) => Hospitales.Error(hospital);

    public static string? PrioridadError(string? prioridad) =>
        Prioridades.EsValida(prioridad) ? null : "La prioridad debe ser Baja, Media, Alta o Urgente.";

    public static string? AvanceError(int avance) =>
        avance is < 0 or > 100 ? "El avance debe estar entre 0 y 100." : null;

    public static string? PeriodicidadError(int dias) =>
        dias is < 0 or > 365 ? "La periodicidad debe ser de 0 a 365 días (0 = sin reportes)." : null;

    /// El informe es obligatorio al completar; la justificacion, al cancelar. El nombre del campo se
    /// entrecomilla para que el aviso sirva igual con palabras femeninas y masculinas
    /// («justificación», «comentario», «descripción del avance»…).
    public static string? TextoError(string? texto, string campo, int minimo = 5, int maximo = 2000) =>
        string.IsNullOrWhiteSpace(texto) ? $"Falta el campo «{campo}»."
        : texto.Trim().Length < minimo ? $"El campo «{campo}» debe tener al menos {minimo} caracteres."
        : texto.Trim().Length > maximo ? $"El campo «{campo}» no puede pasar de {maximo} caracteres."
        : null;
}
