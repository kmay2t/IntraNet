namespace Meditechnosys.Intranet.Models;

/// Reporte de estatus periódico que entrega el Ingeniero responsable de una actividad o ticket.
/// Se conserva siempre (histórico): nunca se edita ni se elimina.
/// <param name="ItemId">Actividad/ticket al que pertenece.</param>
/// <param name="FechaEstimada">Fecha estimada de conclusión declarada por el ingeniero.</param>
public record ReporteEstatus(
    long Id,
    long ItemId,
    DateTime Momento,
    string Autor,
    string Estado,
    int Avance,
    string DescripcionAvance,
    string Problemas,
    string Pendientes,
    string ProximasAcciones,
    DateTime? FechaEstimada,
    string Comentarios);

public static class ReporteRules
{
    /// Lo mínimo para que el reporte sirva al seguimiento.
    public static string? DescripcionError(string? descripcion) =>
        ActividadRules.TextoError(descripcion, "descripción del avance", minimo: 5, maximo: 2000);
}
