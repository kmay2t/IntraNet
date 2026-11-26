using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Patologia;

/// API de patología dentro de la intranet (puertos 8000/8100 del sistema Python, unificados).
/// Todo exige sesión y el permiso efectivo patologia.consultar; el borrado exige además rol Admin.
public static partial class PatoApi
{
    public static void MapPatoApi(this WebApplication app)
    {
        var g = app.MapGroup("/api/patologia").RequireAuthorization($"P:{CatalogoPermisos.PatologiaConsultar}");

        g.MapGet("/me", (HttpContext ctx) =>
            Results.Ok(new
            {
                usuario = ctx.User.Identity?.Name ?? "",
                rol = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? ""
            }));

        g.MapGet("/areas", () =>
            PatoDb.Areas.Select(a => new { codigo = a.Key, nombre = a.Key == "esp" ? "Patología Especialidades" : "Patología Ginecología" }));

        g.MapGet("/reportes", (HttpContext ctx, PatoDb db,
            string? area, string? q, string? nss, string? nombre, string? folio, string? modalidad, string? estudio,
            string? desde, string? hasta, int? pagina, int? por_pagina) =>
        {
            area = (area ?? "esp").Trim();
            var f = PrepFiltros(area, q, nss, nombre, folio, modalidad, estudio, desde, hasta, db, out var cond, out var ps);
            if (f is not null) return f;
            var pag = Math.Max(1, pagina ?? 1);
            var por = Math.Clamp(por_pagina ?? 25, 1, 100);
            using var cn = db.Conexion();
            cn.Open();
            long total;
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM reportes {cond}";
                AgregaParams(cmd, ps);
                total = Convert.ToInt64(cmd.ExecuteScalar());
            }
            List<Dictionary<string, object?>> filas;
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = $"SELECT * FROM reportes {cond} ORDER BY fecha DESC, id DESC LIMIT @lim OFFSET @off";
                AgregaParams(cmd, ps);
                cmd.Parameters.AddWithValue("@lim", por);
                cmd.Parameters.AddWithValue("@off", (pag - 1) * por);
                filas = LeerFilas(cmd);
            }
            db.Auditar("consulta", $"{area} resultados={total}", Ip(ctx), ctx.User.Identity?.Name ?? "");
            var estudios = EstudiosDisponibles(cn, area);
            return Results.Ok(new
            {
                area,
                total,
                pagina = pag,
                por_pagina = por,
                estudios,
                resultados = filas.Select(Fila),
            });
        });

        g.MapGet("/concentrado", (HttpContext ctx, PatoDb db,
            string? area, string? q, string? nss, string? nombre, string? folio, string? modalidad, string? estudio,
            string? desde, string? hasta) =>
        {
            var f = PrepFiltros(area, q, nss, nombre, folio, modalidad, estudio, desde, hasta, db, out var cond, out var ps);
            if (f is not null) return f;
            using var cn = db.Conexion();
            cn.Open();
            List<Dictionary<string, object?>> filas;
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = $"SELECT * FROM reportes {cond} ORDER BY fecha DESC, id DESC";
                AgregaParams(cmd, ps);
                filas = LeerFilas(cmd);
            }
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Reportes");
            var enc = new[] { "ID", "Área", "Folio", "Fecha", "Paciente", "NSS", "Agregado", "Tipo de estudio", "Estudio", "Archivo" };
            for (var i = 0; i < enc.Length; i++) ws.Cell(1, i + 1).Value = enc[i];
            var ren = 2;
            foreach (var r in filas)
            {
                ws.Cell(ren, 1).Value = Convert.ToInt64(r["id"]);
                ws.Cell(ren, 2).Value = area == "gineco" ? "Patología Ginecología" : "Patología Especialidades";
                ws.Cell(ren, 3).Value = Str(r["folio"]);
                ws.Cell(ren, 4).Value = r["fecha"] is DateOnly fd ? fd.ToString("yyyy-MM-dd") : "";
                ws.Cell(ren, 5).Value = Str(r["nombre"]);
                ws.Cell(ren, 6).Value = Str(r["nss"]);
                ws.Cell(ren, 7).Value = Str(r["agregado_medico"]);
                ws.Cell(ren, 8).Value = Str(r["modalidad"]);
                ws.Cell(ren, 9).Value = EstudioVisible(r["estudio"]);
                ws.Cell(ren, 10).Value = Str(r["archivo_original"]);
                ren++;
            }
            double[] anchos = { 8, 26, 18, 12, 40, 14, 12, 22, 42, 40 };
            for (var i = 0; i < anchos.Length; i++) ws.Column(i + 1).Width = anchos[i];
            ws.SheetView.FreezeRows(1);
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            db.Auditar("concentrado", $"{area} filas={filas.Count}", Ip(ctx), ctx.User.Identity?.Name ?? "");
            return Results.File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"concentrado_{area}.xlsx");
        });

        g.MapGet("/reportes/{rid:long}", (long rid, PatoDb db) =>
        {
            var r = PorId(db, rid);
            return r is null ? Results.NotFound(new { error = "Reporte no encontrado" }) : Results.Ok(Fila(r));
        });

        g.MapGet("/reportes/{rid:long}/pdf", (long rid, HttpContext ctx, PatoDb db) =>
        {
            var r = PorId(db, rid);
            if (r is null) return Results.NotFound(new { error = "Reporte no encontrado" });
            string? ruta = null;
            if (Str(r["ruta_pdf"]).Length > 0)
            {
                try { ruta = db.RutaAbsoluta(Str(r["ruta_pdf"])); } catch (InvalidOperationException) { }
            }
            if (ruta is null || !File.Exists(ruta))
                return Results.NotFound(new { error = "Archivo no disponible" });
            db.Auditar("ver_pdf", $"{r["area"]}:{r["folio"]} id={rid}", Ip(ctx), ctx.User.Identity?.Name ?? "");
            var crudo = (Str(r["folio"]).Length > 0 ? Str(r["folio"]) : $"reporte-{rid}").Trim();
            var ascii = ReNombreArchivo().Replace(crudo, "_");
            if (ascii.Length > 80) ascii = ascii[..80];
            ctx.Response.Headers["Content-Disposition"] =
                $"inline; filename=\"{ascii}.pdf\"; filename*=UTF-8''{Uri.EscapeDataString(crudo + ".pdf")}";
            ctx.Response.Headers["Cache-Control"] = "private, max-age=120";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(ruta, "application/pdf", enableRangeProcessing: true);
        });

        g.MapPost("/reportes", async (HttpContext ctx, PatoDb db, Repositorio repo) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var area = form["area"].ToString();
            var archivos = form.Files.GetFiles("archivos").ToList();
            if (area.Length == 0 || !PatoDb.Areas.ContainsKey(area))
                return Results.BadRequest(new { error = $"área inválida; usa una de: {string.Join(", ", PatoDb.Areas.Keys)}" });
            if (archivos.Count == 0)
                return Results.BadRequest(new { error = "Envía al menos un archivo" });
            var usuario = ctx.User.Identity?.Name ?? "";
            var salida = new List<object>();
            foreach (var up in archivos)
            {
                var nombre = string.IsNullOrWhiteSpace(up.FileName) ? "sin_nombre.pdf" : up.FileName.Trim();
                ResultadoGuardado item;
                try
                {
                    using var ms = new MemoryStream();
                    await up.CopyToAsync(ms);
                    item = repo.Guardar(area, ms.ToArray(), nombre, usuario);
                    if (item.Omitido)
                        item = item with { Ok = false, Error = item.Aviso ?? "Ya existía" };
                }
                catch
                {
                    item = new ResultadoGuardado(false, false, null, nombre, area, "No se pudo procesar el PDF", null);
                }
                salida.Add(new
                {
                    archivo = item.Archivo,
                    ok = item.Ok,
                    error = item.Error,
                    aviso = item.Aviso,
                    id = item.Id,
                    folio = item.Folio,
                    fecha = item.Fecha,
                    nombre = item.Nombre,
                    nss = item.Nss,
                    agregado_medico = item.Agregado,
                    modalidad = item.Modalidad,
                    estudio = item.Estudio,
                });
                if (item.Ok) db.Auditar("subir", $"{area}:{item.Folio}", Ip(ctx), usuario);
            }
            return Results.Ok(new { area, resultados = salida });
        }).DisableAntiforgery();

        g.MapPatch("/reportes/{rid:long}", (long rid, HttpContext ctx, PatoDb db, CamposEdit datos) =>
        {
            var sets = new List<string>();
            var vals = new List<(string, object)>();
            if (datos.Folio is not null) { sets.Add("folio=@folio"); vals.Add(("@folio", PatoDb.Trunc(datos.Folio.Trim(), 60))); }
            if (datos.Fecha is not null)
            {
                var f = datos.Fecha.Trim();
                if (f.Length > 0 && !DateOnly.TryParseExact(f, "yyyy-MM-dd", out _))
                    return Results.BadRequest(new { error = "Fecha inválida; usa el formato aaaa-mm-dd" });
                sets.Add("fecha=@fecha");
                vals.Add(("@fecha", f.Length > 0 ? (object)DateOnly.ParseExact(f, "yyyy-MM-dd") : DBNull.Value));
            }
            if (datos.Nombre is not null)
            {
                sets.Add("nombre=@nombre"); vals.Add(("@nombre", PatoDb.Trunc(datos.Nombre.Trim(), 200)));
                sets.Add("nombre_norm=@nnorm"); vals.Add(("@nnorm", PatoDb.Trunc(PatoDb.Normalizar(datos.Nombre), 200)));
            }
            if (datos.Nss is not null) { sets.Add("nss=@nss"); vals.Add(("@nss", PatoDb.Trunc(datos.Nss.Trim(), 20))); }
            if (datos.Agregado is not null) { sets.Add("agregado_medico=@agr"); vals.Add(("@agr", PatoDb.Trunc(datos.Agregado.Trim(), 20))); }
            if (datos.Modalidad is not null) { sets.Add("modalidad=@mod"); vals.Add(("@mod", PatoDb.Trunc(datos.Modalidad.Trim(), 60))); }
            if (datos.Estudio is not null)
            {
                if (datos.Estudio.Length > 255)
                    return Results.BadRequest(new { error = "El estudio no puede exceder 255 caracteres" });
                sets.Add("estudio=@estudio"); vals.Add(("@estudio", datos.Estudio.Trim()));
            }
            if (sets.Count == 0) return Results.BadRequest(new { error = "No hay nada que guardar" });

            using var cn = db.Conexion();
            cn.Open();
            using var tx = cn.BeginTransaction();
            try
            {
                using (var cmd = cn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = $"UPDATE reportes SET {string.Join(", ", sets)} WHERE id=@id";
                    foreach (var (n, v) in vals) cmd.Parameters.AddWithValue(n, v);
                    cmd.Parameters.AddWithValue("@id", rid);
                    if (cmd.ExecuteNonQuery() == 0) { tx.Rollback(); return Results.NotFound(new { error = "Reporte no encontrado" }); }
                }
                Dictionary<string, object?> fila;
                using (var cmd = cn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT * FROM reportes WHERE id=@id";
                    cmd.Parameters.AddWithValue("@id", rid);
                    fila = LeerFilas(cmd)[0];
                }
                tx.Commit();
                db.Auditar("editar", $"id={rid} folio={fila["folio"]}", Ip(ctx), ctx.User.Identity?.Name ?? "");
                return Results.Ok(Fila(fila));
            }
            catch (MySqlException e) when (e.Number == 1062)
            {
                tx.Rollback();
                return Results.BadRequest(new { error = "Ese folio ya existe en esta área" });
            }
        });

        g.MapDelete("/reportes/{rid:long}", (long rid, HttpContext ctx, PatoDb db) =>
        {
            Dictionary<string, object?>? r;
            using (var cn = db.Conexion())
            {
                cn.Open();
                using var tx = cn.BeginTransaction();
                using (var cmd = cn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT * FROM reportes WHERE id=@id";
                    cmd.Parameters.AddWithValue("@id", rid);
                    var filas = LeerFilas(cmd);
                    if (filas.Count == 0) { tx.Rollback(); return Results.NotFound(new { error = "Reporte no encontrado" }); }
                    r = filas[0];
                }
                using (var cmd = cn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM reportes WHERE id=@id";
                    cmd.Parameters.AddWithValue("@id", rid);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            try
            {
                if (Str(r["ruta_pdf"]).Length > 0) File.Delete(db.RutaAbsoluta(Str(r["ruta_pdf"])));
            }
            catch (InvalidOperationException) { }
            db.Auditar("borrar", $"{r["area"]}:{r["folio"]} id={rid}", Ip(ctx), ctx.User.Identity?.Name ?? "");
            return Results.Ok(new { ok = true });
        }).RequireAuthorization("SoloAdmin").RequireAuthorization(p => p.RequireRole(Roles.Admin));

        // Importación manual por Excel (sustituye a importar-excel.bat + manage.py).
        g.MapPost("/importar-excel", async (HttpContext ctx, PatoDb db, Importador imp) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var area = form["area"].ToString();
            if (area.Length == 0 || !PatoDb.Areas.ContainsKey(area))
                return Results.BadRequest(new { error = $"área inválida; usa una de: {string.Join(", ", PatoDb.Areas.Keys)}" });
            var file = form.Files.GetFile("excel");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "Envía un archivo .xlsx" });
            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "El archivo debe ser .xlsx" });

            ResultadoExcel res;
            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                ms.Position = 0;
                res = imp.ImportarExcel(ms, area);
            }
            catch (InvalidOperationException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (Exception e) { return Results.BadRequest(new { error = $"No se pudo leer el Excel: {e.Message}" }); }

            var usuario = ctx.User.Identity?.Name ?? "";
            db.Auditar("importar_excel",
                $"{area} nuevos={res.Nuevos} omitidos={res.Omitidos} errores={res.Errores}", Ip(ctx), usuario);
            return Results.Ok(new
            {
                area,
                nuevos = res.Nuevos,
                omitidos = res.Omitidos,
                errores = res.Errores,
                corregidos = res.Corregidos,
                filas = res.Filas.Select(f => new
                {
                    archivo = f.Archivo, area = f.Area, nuevo = f.Nuevo, id = f.Id,
                    folio = f.Folio, modalidad = f.Modalidad, estudio = f.Estudio, aviso = f.Aviso,
                    error = f.Error, corregido = f.Corregido,
                }),
            });
        }).DisableAntiforgery();
    }

    // ---------------------------------------------------------- helpers
    public record CamposEdit(string? Folio, string? Fecha, string? Nombre, string? Nss, string? Agregado, string? Modalidad, string? Estudio);

    [GeneratedRegex(@"[^A-Za-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex ReNombreArchivo();

    private static string Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "";

    private static string Str(object? v) => v?.ToString() ?? "";

    private static string EstudioVisible(object? v)
    {
        var texto = Str(v).Trim();
        return texto.Length > 0 ? texto : PatoEstudio.EstudioOtros;
    }

    private static string Like(string texto) =>
        (texto ?? "").Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string Alnum(string texto) => new((texto ?? "").Where(char.IsLetterOrDigit).ToArray());

    private static string Digitos(string texto) => new((texto ?? "").Where(char.IsDigit).ToArray());

    /// Construye el WHERE con los mismos criterios del sistema Python.
    private static IResult? PrepFiltros(string? area, string? q, string? nss, string? nombre, string? folio,
        string? modalidad, string? estudio, string? desde, string? hasta, PatoDb db, out string cond, out List<(string, object)> ps)
    {
        cond = ""; ps = new List<(string, object)>();
        area = (area ?? "esp").Trim();
        if (!PatoDb.Areas.ContainsKey(area))
            return Results.BadRequest(new { error = $"área inválida; usa una de: {string.Join(", ", PatoDb.Areas.Keys)}" });
        if ((estudio?.Length ?? 0) > 255)
            return Results.BadRequest(new { error = "El estudio no puede exceder 255 caracteres" });
        string? d = null, h = null;
        if (!string.IsNullOrEmpty(desde))
        {
            if (!DateOnly.TryParseExact(desde, "yyyy-MM-dd", out var fd)) return Results.BadRequest(new { error = "Fecha 'desde' inválida; usa el formato aaaa-mm-dd" });
            d = fd.ToString("yyyy-MM-dd");
        }
        if (!string.IsNullOrEmpty(hasta))
        {
            if (!DateOnly.TryParseExact(hasta, "yyyy-MM-dd", out var fh)) return Results.BadRequest(new { error = "Fecha 'hasta' inválida; usa el formato aaaa-mm-dd" });
            h = fh.ToString("yyyy-MM-dd");
        }
        if (d is not null && h is not null && string.CompareOrdinal(d, h) > 0)
            return Results.BadRequest(new { error = "La fecha 'desde' no puede ser posterior a 'hasta'" });

        var where = new List<string> { "area = @pArea" };
        ps.Add(("@pArea", area));
        var i = 0;
        string Nuevo() => $"@p{i++}";

        var texto = (q ?? "").Trim();
        if (texto.Length > 0)
        {
            var partes = new List<string>();
            var p1 = Nuevo(); partes.Add($"folio LIKE {p1} ESCAPE '\\\\'"); ps.Add((p1, $"%{Like(texto)}%"));
            var folioPlano = Alnum(texto);
            if (folioPlano.Length > 0)
            {
                var p2 = Nuevo(); partes.Add($"REPLACE(REPLACE(UPPER(folio), '-', ''), ' ', '') LIKE {p2} ESCAPE '\\\\'"); ps.Add((p2, $"%{Like(folioPlano.ToUpperInvariant())}%"));
            }
            var nssPlano = Digitos(texto);
            if (nssPlano.Length > 0)
            {
                var p3 = Nuevo(); partes.Add($"REPLACE(REPLACE(nss, '-', ''), ' ', '') LIKE {p3} ESCAPE '\\\\'"); ps.Add((p3, $"%{Like(nssPlano)}%"));
            }
            foreach (var termino in PatoDb.Normalizar(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (termino.Length < 2) continue;
                var p4 = Nuevo(); partes.Add($"nombre_norm LIKE {p4} ESCAPE '\\\\'"); ps.Add((p4, $"%{Like(termino)}%"));
            }
            var p5 = Nuevo(); partes.Add($"UPPER(agregado_medico) LIKE {p5} ESCAPE '\\\\'"); ps.Add((p5, $"%{Like(texto.ToUpperInvariant())}%"));
            where.Add("(" + string.Join(" OR ", partes) + ")");
        }

        var nssP = Digitos(nss ?? "");
        if (nssP.Length > 0)
        {
            var p = Nuevo(); where.Add($"REPLACE(REPLACE(nss, '-', ''), ' ', '') LIKE {p} ESCAPE '\\\\'"); ps.Add((p, $"%{Like(nssP)}%"));
        }
        foreach (var termino in PatoDb.Normalizar(nombre).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (termino.Length < 2) continue;
            var p = Nuevo(); where.Add($"nombre_norm LIKE {p} ESCAPE '\\\\'"); ps.Add((p, $"%{Like(termino)}%"));
        }
        if ((folio ?? "").Trim().Length > 0)
        {
            var f = folio!.Trim();
            var or = new List<string>();
            var p1 = Nuevo(); or.Add($"folio LIKE {p1} ESCAPE '\\\\'"); ps.Add((p1, $"%{Like(f)}%"));
            var fp = Alnum(f);
            if (fp.Length > 0)
            {
                var p2 = Nuevo(); or.Add($"REPLACE(REPLACE(UPPER(folio), '-', ''), ' ', '') LIKE {p2} ESCAPE '\\\\'"); ps.Add((p2, $"%{Like(fp.ToUpperInvariant())}%"));
            }
            where.Add("(" + string.Join(" OR ", or) + ")");
        }
        if ((modalidad ?? "").Trim().Length > 0)
        {
            var p = Nuevo(); where.Add($"UPPER(modalidad) = UPPER({p})"); ps.Add((p, modalidad!.Trim()));
        }
        if ((estudio ?? "").Trim().Length > 0)
        {
            var valor = estudio!.Trim();
            var p = Nuevo();
            where.Add($"UPPER(COALESCE(NULLIF(TRIM(estudio),''), 'Otros')) LIKE UPPER({p}) ESCAPE '\\\\'");
            ps.Add((p, $"%{Like(valor)}%"));
        }
        if (d is not null) { var p = Nuevo(); where.Add($"fecha >= {p}"); ps.Add((p, d)); }
        if (h is not null) { var p = Nuevo(); where.Add($"fecha <= {p}"); ps.Add((p, h)); }
        cond = "WHERE " + string.Join(" AND ", where);
        return null;
    }

    private static void AgregaParams(MySqlCommand cmd, List<(string Nombre, object Valor)> ps)
    {
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
    }

    private static List<string> EstudiosDisponibles(MySqlConnection cn, string area)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT COALESCE(NULLIF(TRIM(estudio),''), @otros) FROM reportes WHERE area=@area ORDER BY 1";
        cmd.Parameters.AddWithValue("@area", area);
        cmd.Parameters.AddWithValue("@otros", PatoEstudio.EstudioOtros);
        using var r = cmd.ExecuteReader();
        var valores = new List<string>();
        while (r.Read()) valores.Add(r.GetString(0));
        return valores;
    }

    private static async Task<List<string>> EstudiosDisponiblesAsync(MySqlConnection cn, string area, CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT COALESCE(NULLIF(TRIM(estudio),''), @otros) FROM reportes WHERE area=@area ORDER BY 1";
        cmd.Parameters.AddWithValue("@area", area);
        cmd.Parameters.AddWithValue("@otros", PatoEstudio.EstudioOtros);
        using var r = await cmd.ExecuteReaderAsync(ct);
        var valores = new List<string>();
        while (await r.ReadAsync(ct)) valores.Add(r.GetString(0));
        return valores;
    }

    private static List<Dictionary<string, object?>> LeerFilas(MySqlCommand cmd)
    {
        var filas = new List<Dictionary<string, object?>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var k = 0; k < r.FieldCount; k++)
            {
                var v = r.IsDBNull(k) ? null : r.GetValue(k);
                if (v is DateTime dt && IsDateColumn(r, k))
                    v = DateOnly.FromDateTime(dt);
                d[r.GetName(k)] = v;
            }
            filas.Add(d);
        }
        return filas;
    }

    private static bool IsDateColumn(MySqlDataReader r, int k) =>
        r.GetName(k) is "fecha";

    private static Dictionary<string, object?>? PorId(PatoDb db, long rid)
    {
        using var cn = db.Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM reportes WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", rid);
        return LeerFilas(cmd).FirstOrDefault();
    }

    private static object Fila(Dictionary<string, object?> f) => new
    {
        id = f["id"],
        area = f["area"],
        folio = Str(f["folio"]),
        nombre = Str(f["nombre"]),
        nss = Str(f["nss"]),
        agregado_medico = Str(f["agregado_medico"]),
        modalidad = Str(f["modalidad"]),
        estudio = EstudioVisible(f["estudio"]),
        archivo_original = Str(f["archivo_original"]),
        fecha = f["fecha"] is DateOnly fd ? fd.ToString("yyyy-MM-dd") : "",
        subido_en = f["subido_en"] is DateTime se ? se.ToString("yyyy-MM-dd HH:mm:ss") : "",
    };
}
