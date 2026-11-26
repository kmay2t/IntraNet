using Meditechnosys.Intranet.Models;

namespace Meditechnosys.Intranet.Services;

/// Filtros del Histórico de actividades (actividades, incidentes y tickets en una sola pantalla).
/// Los mismos criterios usan la consulta en pantalla y la descarga a Excel, de modo que el archivo
/// exportado contiene exactamente lo que se está viendo.
/// <param name="CampoFecha">Fecha de referencia del rango: `fecha`, `asignado`, `fechaLimite` o `finalizacion`.</param>
/// <param name="Estado">Estado guardado, o el valor derivado `Vencida`.</param>
/// <param name="CreadoPor">Usuario que creó el registro.</param>
public record FiltroHistorico(
    string? Texto = null,
    string? Hospital = null,
    string? Ingeniero = null,
    string? Tipo = null,
    string? Estado = null,
    string? Prioridad = null,
    string? CampoFecha = "fecha",
    DateTime? Desde = null,
    DateTime? Hasta = null,
    string? CreadoPor = null);

/// Filtrado y exportación del Histórico. Vive aquí (y no en la página) para que el Excel respete
/// exactamente los mismos criterios que la tabla y para que la regla quede del lado del servidor.
public class HistoricoExportador(IHistorialStore historial)
{
    public static IReadOnlyList<Actividad> Aplicar(IEnumerable<Actividad> lista, FiltroHistorico f)
    {
        var texto = Texto.Normalizar(f.Texto);
        var hospital = Hospitales.Normalizar(f.Hospital);
        var ingeniero = (f.Ingeniero ?? "").Trim();
        var creadoPor = (f.CreadoPor ?? "").Trim();
        var tipo = TiposItem.EsValido(f.Tipo) ? f.Tipo! : "";
        var prioridad = Prioridades.EsValida(f.Prioridad) ? f.Prioridad! : "";
        var estado = (f.Estado ?? "").Trim();
        var soloVencidas = string.Equals(estado, "Vencida", StringComparison.OrdinalIgnoreCase);
        if (!soloVencidas && !EstadosActividad.EsValido(estado)) estado = "";
        var campo = f.CampoFecha is "asignado" or "fechaLimite" or "finalizacion" ? f.CampoFecha : "fecha";

        return lista
            .Where(a => texto.Length == 0
                || Texto.Normalizar(a.Etiqueta).Contains(texto, StringComparison.Ordinal)
                || Texto.Normalizar(a.Descripcion).Contains(texto, StringComparison.Ordinal)
                || Texto.Normalizar(a.Usuario).Contains(texto, StringComparison.Ordinal)
                || Texto.Normalizar(a.CreadoPor).Contains(texto, StringComparison.Ordinal)
                || Texto.Normalizar(a.Hospital).Contains(texto, StringComparison.Ordinal))
            .Where(a => hospital.Length == 0 || string.Equals(a.Hospital, hospital, StringComparison.Ordinal))
            .Where(a => ingeniero.Length == 0 || string.Equals(a.Usuario, ingeniero, StringComparison.OrdinalIgnoreCase))
            .Where(a => creadoPor.Length == 0 || string.Equals(a.CreadoPor, creadoPor, StringComparison.OrdinalIgnoreCase))
            .Where(a => tipo.Length == 0 || a.Tipo == tipo)
            .Where(a => prioridad.Length == 0 || a.Prioridad == prioridad)
            .Where(a => soloVencidas ? a.Vencida : estado.Length == 0 || a.Estado == estado)
            .Where(a => EnRango(a, campo, f.Desde, f.Hasta))
            .OrderBy(a => a.Fecha).ThenBy(a => a.Id)
            .ToList();
    }

    private static bool EnRango(Actividad a, string campo, DateTime? desde, DateTime? hasta)
    {
        if (desde is null && hasta is null) return true;
        var v = ValorFecha(a, campo);
        if (v is null) return false;
        var d = v.Value.Date;
        if (desde is not null && d < desde.Value.Date) return false;
        if (hasta is not null && d > hasta.Value.Date) return false;
        return true;
    }

    private static DateTime? ValorFecha(Actividad a, string campo) => campo switch
    {
        "asignado" => a.Asignado,
        "fechaLimite" => a.FechaLimite,
        "finalizacion" => a.Cerrado,
        _ => a.Fecha,
    };

    /// Libro .xlsx con lo filtrado. `fechaLimiteActiva` controla si se incluye la columna de fecha
    /// límite: con el interruptor apagado la columna no aparece (no se usa ni se solicita).
    public (string Nombre, byte[] Archivo) Exportar(IReadOnlyList<Actividad> lista, bool fechaLimiteActiva, FiltroHistorico f)
    {
        var encabezados = new List<string>
        {
            "ID", "Elemento", "Tipo", "Hospital", "Fecha", "Registrado por", "Ingeniero responsable",
            "Descripción", "Prioridad", "Estatus", "Fecha de creación",
        };
        var indiceLimite = -1;
        if (fechaLimiteActiva) { indiceLimite = encabezados.Count; encabezados.Add("Fecha límite"); }
        encabezados.AddRange(new[]
        {
            "Fecha de finalización", "% avance", "Último reporte", "Próximo reporte", "Observaciones",
        });

        var filas = lista.Select(a =>
        {
            var fila = new List<string>
            {
                a.Id.ToString(), a.Etiqueta, a.Tipo, a.Hospital.Length > 0 ? a.Hospital : "(sin hospital)",
                a.Fecha.ToString("dd/MM/yyyy"), a.CreadoPor, a.Usuario.Length > 0 ? a.Usuario : "(sin asignar)",
                a.Descripcion, a.Prioridad, Estatus(a), a.Creado.ToString("dd/MM/yyyy HH:mm"),
            };
            if (indiceLimite >= 0) fila.Add(Excel.FmtFecha(a.FechaLimite));
            fila.Add(Excel.FmtFecha(a.Cerrado));
            fila.Add($"{a.Avance}%");
            fila.Add(Excel.FmtFechaHora(a.UltimoReporte));
            fila.Add(Excel.FmtFecha(a.ProximoReporte));
            fila.Add(Observaciones(a));
            return (IReadOnlyList<string>)fila;
        }).ToList();

        return (NombreArchivo(f), Excel.Tabla("Histórico de actividades", encabezados, filas));
    }

    /// Estatus legible, incluyendo el derivado "Vencida". Los elementos ya llegan con su estado
    /// normalizado (solo Pendiente, Completada o Cancelada), así que basta con escribirlo.
    public static string Estatus(Actividad a) => a.Vencida ? "Vencida" : a.Estado;

    /// Observaciones del Admin + informe de cierre del responsable, en orden cronológico.
    private string Observaciones(Actividad a)
    {
        var partes = new List<string>();
        foreach (var c in historial.PorItem(a.Id))
        {
            if (string.Equals(c.Campo, "Observación", StringComparison.Ordinal) && c.Comentario.Length > 0)
                partes.Add($"{c.Momento:dd/MM/yyyy HH:mm} {c.Usuario}: {c.Comentario}");
        }
        if (a.Informe.Length > 0) partes.Add($"Informe: {a.Informe}");
        if (a.Justificacion.Length > 0) partes.Add($"Justificación: {a.Justificacion}");
        var texto = string.Join(" | ", partes);
        return texto.Length > 2000 ? texto[..2000] : texto;
    }

    private static string NombreArchivo(FiltroHistorico f)
    {
        var partes = new List<string> { "Historico_actividades" };
        var h = Hospitales.Normalizar(f.Hospital);
        if (h.Length > 0) partes.Add(h);
        if (TiposItem.EsValido(f.Tipo)) partes.Add(f.Tipo!);
        if (f.Desde is not null) partes.Add(f.Desde.Value.ToString("yyyyMMdd"));
        if (f.Hasta is not null) partes.Add(f.Hasta.Value.ToString("yyyyMMdd"));
        return string.Join("_", partes) + ".xlsx";
    }
}
