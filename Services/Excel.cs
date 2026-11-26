using ClosedXML.Excel;

namespace Meditechnosys.Intranet.Services;

/// Generación de archivos .xlsx reales (ClosedXML) para las descargas del módulo Gestión.
/// Todas las exportaciones pasan por aquí para compartir encabezado, anchos y congelado de fila.
public static class Excel
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// Crea un libro con una hoja: primera fila de encabezados y las filas de datos debajo.
    public static byte[] Tabla(string hoja, IReadOnlyList<string> encabezados, IEnumerable<IReadOnlyList<string>> filas)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(RecortarNombre(hoja));
        for (var c = 0; c < encabezados.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = encabezados[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }
        var renglon = 2;
        foreach (var f in filas)
        {
            for (var c = 0; c < f.Count && c < encabezados.Count; c++)
                ws.Cell(renglon, c + 1).Value = f[c] ?? "";
            renglon++;
        }
        ws.Range(1, 1, Math.Max(renglon - 1, 1), encabezados.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        for (var c = 1; c <= encabezados.Count; c++) ws.Column(c).Width = Ancho(ws, c, renglon - 1);
        ws.SheetView.FreezeRows(1);
        ws.Range(1, 1, 1, encabezados.Count).SetAutoFilter();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// Ancho de columna a partir del contenido (mínimo 10, máximo 60 caracteres).
    private static double Ancho(IXLWorksheet ws, int columna, int filas)
    {
        var max = 0;
        for (var r = 1; r <= Math.Min(filas, 200) + 1; r++)
        {
            var largo = ws.Cell(r, columna).GetString().Length;
            if (largo > max) max = largo;
        }
        return Math.Clamp(max + 2, 10, 60);
    }

    /// Excel limita el nombre de hoja a 31 caracteres y prohíbe algunos símbolos.
    private static string RecortarNombre(string hoja)
    {
        foreach (var c in new[] { ':', '\\', '/', '?', '*', '[', ']' }) hoja = hoja.Replace(c, ' ');
        hoja = hoja.Trim();
        return hoja.Length == 0 ? "Hoja1" : hoja.Length > 31 ? hoja[..31] : hoja;
    }

    public static string FmtFecha(DateTime? f) => f is null ? "" : f.Value.ToString("dd/MM/yyyy");

    public static string FmtFechaHora(DateTime? f) => f is null ? "" : f.Value.ToString("dd/MM/yyyy HH:mm");
}
