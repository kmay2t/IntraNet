using MySqlConnector;

namespace Meditechnosys.Intranet.Patologia;

/// Consulta pública de Patología. Se mantiene separada de /api/patologia para no heredar
/// permisos de escritura: aquí solo existe una búsqueda de solo lectura y la entrega de PDF.
public static partial class PatoApi
{
    public static void MapPatoConsultaPublicaApi(this WebApplication app)
    {
        var g = app.MapGroup("/api/consulta-patologia").AllowAnonymous().RequireRateLimiting("consulta-publica");

        g.MapPost("/buscar", async (PatoConsultaFiltro filtro, HttpContext ctx, PatoDb db) =>
        {
            SinCachePublico(ctx);
            var error = ValidarFiltroPublico(filtro);
            if (error is not null) return error;

            var area = (filtro.Area ?? "esp").Trim();
            var preparados = PrepFiltros(area, filtro.Q, filtro.Nss, filtro.Nombre, filtro.Folio,
                filtro.Modalidad, filtro.Estudio, filtro.Desde, filtro.Hasta, db, out var cond, out var ps);
            if (preparados is not null) return preparados;

            var pagina = Math.Max(1, filtro.Pagina ?? 1);
            var porPagina = Math.Clamp(filtro.PorPagina ?? 25, 1, 100);
            try
            {
                using var cn = db.Conexion();
                await cn.OpenAsync(ctx.RequestAborted);

                long total;
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT COUNT(*) FROM reportes {cond}";
                    AgregaParams(cmd, ps);
                    total = Convert.ToInt64(await cmd.ExecuteScalarAsync(ctx.RequestAborted));
                }

                List<Dictionary<string, object?>> filas;
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT id, area, folio, fecha, nombre, nss, agregado_medico, modalidad, estudio FROM reportes {cond} ORDER BY fecha DESC, id DESC LIMIT @lim OFFSET @off";
                    AgregaParams(cmd, ps);
                    cmd.Parameters.AddWithValue("@lim", porPagina);
                    var offset = ((long)pagina - 1) * porPagina;
                    cmd.Parameters.AddWithValue("@off", offset);
                    using var reader = await cmd.ExecuteReaderAsync(ctx.RequestAborted);
                    filas = await LeerFilasAsync(reader, ctx.RequestAborted);
                }

                var estudios = await EstudiosDisponiblesAsync(cn, area, ctx.RequestAborted);
                return Results.Ok(new
                {
                    area,
                    total,
                    pagina,
                    porPagina = porPagina,
                    estudios,
                    resultados = filas.Select(FilaPublica),
                });
            }
            catch (MySqlException)
            {
                return Results.Json(new { error = "No fue posible consultar los reportes en este momento." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        g.MapGet("/reportes/{rid:long}/pdf", async (long rid, HttpContext ctx, PatoDb db) =>
        {
            SinCachePublico(ctx);
            Dictionary<string, object?>? reporte;
            try
            {
                using var cn = db.Conexion();
                await cn.OpenAsync(ctx.RequestAborted);
                using var cmd = cn.CreateCommand();
                cmd.CommandText = "SELECT id, area, folio, ruta_pdf FROM reportes WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", rid);
                using var reader = await cmd.ExecuteReaderAsync(ctx.RequestAborted);
                var filas = await LeerFilasAsync(reader, ctx.RequestAborted);
                reporte = filas.FirstOrDefault();
            }
            catch (MySqlException)
            {
                return Results.Json(new { error = "No fue posible abrir el reporte en este momento." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (reporte is null)
                return Results.NotFound(new { error = "Reporte no encontrado" });

            string? ruta = null;
            var rutaRelativa = Str(reporte["ruta_pdf"]);
            if (rutaRelativa.Length > 0)
            {
                try { ruta = db.RutaAbsoluta(rutaRelativa); }
                catch (InvalidOperationException) { }
            }

            if (ruta is null || !File.Exists(ruta))
                return Results.NotFound(new { error = "El archivo PDF no está disponible" });

            var crudo = (Str(reporte["folio"]).Length > 0 ? Str(reporte["folio"]) : $"reporte-{rid}").Trim();
            var ascii = ReNombreArchivo().Replace(crudo, "_");
            if (ascii.Length == 0) ascii = $"reporte-{rid}";
            if (ascii.Length > 80) ascii = ascii[..80];
            ctx.Response.Headers["Content-Disposition"] =
                $"inline; filename=\"{ascii}.pdf\"; filename*=UTF-8''{Uri.EscapeDataString(crudo + ".pdf")}";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";

            try
            {
                return Results.File(ruta, "application/pdf", enableRangeProcessing: true);
            }
            catch (IOException)
            {
                return Results.NotFound(new { error = "El archivo PDF no está disponible" });
            }
        });
    }

    public sealed record PatoConsultaFiltro(
        string? Area,
        string? Q,
        string? Nss,
        string? Nombre,
        string? Folio,
        string? Modalidad,
        string? Estudio,
        string? Desde,
        string? Hasta,
        int? Pagina,
        int? PorPagina);

    private static void SinCachePublico(HttpContext ctx)
    {
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.Headers["Pragma"] = "no-cache";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private static IResult? ValidarFiltroPublico(PatoConsultaFiltro f)
    {
        if ((f.Q?.Length ?? 0) > 120)
            return Results.BadRequest(new { error = "La búsqueda no puede exceder 120 caracteres" });
        if ((f.Nss?.Length ?? 0) > 20)
            return Results.BadRequest(new { error = "El NSS no puede exceder 20 caracteres" });
        if ((f.Nombre?.Length ?? 0) > 200)
            return Results.BadRequest(new { error = "El nombre no puede exceder 200 caracteres" });
        if ((f.Folio?.Length ?? 0) > 60)
            return Results.BadRequest(new { error = "El folio no puede exceder 60 caracteres" });
        if ((f.Modalidad?.Length ?? 0) > 60)
            return Results.BadRequest(new { error = "El tipo de estudio no puede exceder 60 caracteres" });
        if ((f.Estudio?.Length ?? 0) > 255)
            return Results.BadRequest(new { error = "El estudio no puede exceder 255 caracteres" });
        if ((f.Pagina ?? 1) < 1)
            return Results.BadRequest(new { error = "La página debe ser mayor o igual a 1" });
        if ((f.PorPagina ?? 25) < 1 || (f.PorPagina ?? 25) > 100)
            return Results.BadRequest(new { error = "La paginación debe estar entre 1 y 100" });
        return null;
    }

    private static async Task<List<Dictionary<string, object?>>> LeerFilasAsync(
        MySqlDataReader reader, CancellationToken cancellationToken)
    {
        var filas = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var k = 0; k < reader.FieldCount; k++)
            {
                var v = reader.IsDBNull(k) ? null : reader.GetValue(k);
                if (v is DateTime dt && IsDateColumn(reader, k))
                    v = DateOnly.FromDateTime(dt);
                d[reader.GetName(k)] = v;
            }
            filas.Add(d);
        }
        return filas;
    }

    // DTO mínimo: no expone rutas de almacenamiento, nombres de archivo ni datos de auditoría.
    private static object FilaPublica(Dictionary<string, object?> f) => new
    {
        id = f["id"],
        area = f["area"],
        folio = Str(f["folio"]),
        fecha = f["fecha"] is DateOnly fd ? fd.ToString("yyyy-MM-dd") : "",
        nombre = Str(f["nombre"]),
        nss = Str(f["nss"]),
        agregado_medico = Str(f["agregado_medico"]),
        modalidad = Str(f["modalidad"]),
        estudio = EstudioVisible(f["estudio"]),
    };
}
