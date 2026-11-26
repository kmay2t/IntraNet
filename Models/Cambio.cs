namespace Meditechnosys.Intranet.Models;

/// Una línea del historial de cambios de una actividad/ticket (auditoría de seguimiento).
/// Solo se agrega: nunca se modifica ni se elimina ("no eliminar históricos").
/// <param name="Campo">Qué cambió: "Estado", "Ingeniero", "Prioridad", "Fecha límite", "Avance", "Observación"…</param>
/// <param name="Antes">Valor anterior ("" si no aplica, p. ej. en un comentario).</param>
/// <param name="Despues">Valor nuevo.</param>
public record Cambio(
    long Id,
    long ItemId,
    DateTime Momento,
    string Usuario,
    string Campo,
    string Antes,
    string Despues,
    string Comentario);
