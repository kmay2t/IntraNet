namespace Meditechnosys.Intranet.Models;

/// Tipos de movimiento del almacén de equipos.
public static class TiposMovimiento
{
    public const string Entrada = "Entrada";
    public const string Salida = "Salida";
    public const string Traslado = "Traslado";

    public static readonly IReadOnlyList<string> Todos = new[] { Entrada, Salida, Traslado };

    public static bool EsValido(string? tipo) => tipo is not null && Todos.Contains(tipo);
}

/// Estados del ciclo de un movimiento. Las existencias solo cambian en las transiciones que
/// aplica el servicio (`AlmacenService`), nunca directamente desde la interfaz.
///  - Entrada/Salida: Borrador → Confirmado (aplica stock) | Cancelado | Rechazado.
///  - Traslado: Borrador → EnTransito (descuenta origen) → Recibido (suma destino) | Cancelado/Rechazado (reversa al origen).
public static class EstadosMovimiento
{
    public const string Borrador = "Borrador";
    public const string EnTransito = "EnTransito";
    public const string Confirmado = "Confirmado";
    public const string Recibido = "Recibido";
    public const string Cancelado = "Cancelado";
    public const string Rechazado = "Rechazado";

    public static readonly IReadOnlyList<string> Todos = new[] { Borrador, EnTransito, Confirmado, Recibido, Cancelado, Rechazado };

    public static bool EsValido(string? estado) => estado is not null && Todos.Contains(estado);

    /// Estado terminal: ya no admite transiciones ni mueve existencias.
    public static bool Terminal(string? estado) => estado is Confirmado or Recibido or Cancelado or Rechazado;
}

/// Motivos configurables por tipo de movimiento (se muestran como desplegable y se validan).
public static class MotivosMovimiento
{
    public static readonly IReadOnlyList<string> Entrada = new[]
    {
        "Adquisición o compra", "Recepción de equipo nuevo", "Devolución desde un servicio",
        "Transferencia desde otro almacén u hospital", "Donación o asignación institucional",
        "Regularización de inventario autorizada", "Otro"
    };

    public static readonly IReadOnlyList<string> Salida = new[]
    {
        "Asignación a un servicio del hospital", "Envío a otro almacén autorizado",
        "Envío a otro hospital", "Envío a proveedor o taller de mantenimiento",
        "Baja autorizada", "Desecho / baja definitiva", "Otro"
    };

    public static readonly IReadOnlyList<string> Traslado = new[]
    {
        "Traslado a servicio del mismo hospital", "Traslado entre hospitales",
        "Resguardo temporal", "Sustitución de equipo", "Otro"
    };

    public static IReadOnlyList<string> PorTipo(string tipo) => tipo switch
    {
        TiposMovimiento.Entrada => Entrada,
        TiposMovimiento.Salida => Salida,
        _ => Traslado,
    };

    public static bool EsValido(string tipo, string? motivo) =>
        PorTipo(tipo).Contains(motivo ?? "", StringComparer.OrdinalIgnoreCase);
}

/// Una línea del movimiento: el equipo afectado y la cantidad movida. Se guarda una fotografía
/// (serie, número de inventario, nombre) para que el documento y el historial no dependan de
/// ediciones posteriores del catálogo, además del `EquipoId` para aplicar el stock.
public record MovimientoDetalle(
    long EquipoId,
    string NumInventario,
    string Serie,
    string Nombre,
    string Marca,
    string Modelo,
    int Cantidad)
{
    /// Identificador legible para documentos y bitácora.
    public string Etiqueta =>
        (string.IsNullOrWhiteSpace(NumInventario) ? "" : $"INV {NumInventario} ") +
        (string.IsNullOrWhiteSpace(Serie) ? "" : $"SER {Serie} ") + Nombre;
}

/// Un movimiento de almacén (entrada, salida o traslado) con su folio único y trazabilidad.
public record MovimientoInventario(
    long Id,
    string Folio,
    string Tipo,
    string Motivo,
    string HospitalOrigen,
    string UbicacionOrigen,
    string HospitalDestino,
    string UbicacionDestino,
    string Estado,
    DateTime Fecha,
    string ResponsableEntrega,
    string ResponsableRecibe,
    string UsuarioRegistra,
    string DocumentoReferencia,
    string Observaciones,
    DateTime Creado,
    DateTime? Modificado,
    DateTime? FechaAutorizacion,
    string UsuarioAutoriza,
    IReadOnlyList<MovimientoDetalle> Detalles)
{
    /// Hospital y ubicación de origen presentables (vacío cuando no aplica, p. ej. una compra).
    public string Origen => string.IsNullOrWhiteSpace(HospitalOrigen)
        ? (string.IsNullOrWhiteSpace(UbicacionOrigen) ? "" : UbicacionOrigen)
        : (string.IsNullOrWhiteSpace(UbicacionOrigen) ? HospitalOrigen : $"{HospitalOrigen} · {UbicacionOrigen}");

    public string Destino => string.IsNullOrWhiteSpace(HospitalDestino)
        ? (string.IsNullOrWhiteSpace(UbicacionDestino) ? "" : UbicacionDestino)
        : (string.IsNullOrWhiteSpace(UbicacionDestino) ? HospitalDestino : $"{HospitalDestino} · {UbicacionDestino}");
}

/// Reglas de validación compartidas por el almacén, el servicio y la API de movimientos.
public static class AlmacenRules
{
    public static string? TipoError(string? tipo) =>
        TiposMovimiento.EsValido(tipo) ? null : "El tipo debe ser Entrada, Salida o Traslado.";

    public static string? EstadoError(string? estado) =>
        EstadosMovimiento.EsValido(estado) ? null : "El estado del movimiento no es válido.";

    public static string? MotivoError(string tipo, string? motivo) =>
        MotivosMovimiento.EsValido(tipo, motivo) ? null : "Selecciona un motivo válido para el tipo de movimiento.";

    /// Valida la cabecera según el tipo: una entrada necesita destino; una salida, origen;
    /// un traslado, origen y destino distintos.
    public static string? CabeceraError(MovimientoInventario m)
    {
        if (TipoError(m.Tipo) is string eT) return eT;
        if (MotivoError(m.Tipo, m.Motivo) is string eM) return eM;
        if (m.Detalles is null || m.Detalles.Count == 0) return "Agrega al menos un equipo al movimiento.";
        foreach (var d in m.Detalles)
        {
            if (d.Cantidad <= 0) return "La cantidad de cada equipo debe ser mayor que cero.";
            if (string.IsNullOrWhiteSpace(d.Nombre)) return "Cada línea debe indicar el nombre del equipo.";
        }
        return m.Tipo switch
        {
            TiposMovimiento.Entrada when Hospitales.Error(m.HospitalDestino, permiteGeneral: true) is string e => $"Hospital de destino: {e}",
            TiposMovimiento.Entrada when string.IsNullOrWhiteSpace(m.UbicacionDestino) => "Indica el almacén o servicio receptor.",
            TiposMovimiento.Salida when Hospitales.Error(m.HospitalOrigen, permiteGeneral: true) is string e => $"Hospital de origen: {e}",
            TiposMovimiento.Salida when string.IsNullOrWhiteSpace(m.UbicacionOrigen) => "Indica el almacén o servicio de origen.",
            TiposMovimiento.Traslado when Hospitales.Error(m.HospitalOrigen, permiteGeneral: true) is string e => $"Hospital de origen: {e}",
            TiposMovimiento.Traslado when Hospitales.Error(m.HospitalDestino, permiteGeneral: true) is string e => $"Hospital de destino: {e}",
            TiposMovimiento.Traslado when string.IsNullOrWhiteSpace(m.UbicacionOrigen) || string.IsNullOrWhiteSpace(m.UbicacionDestino) => "Indica la ubicación de origen y la de destino.",
            _ => null,
        };
    }
}
