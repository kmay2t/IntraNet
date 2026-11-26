using MySqlConnector;

namespace Meditechnosys.Intranet.Patologia;

/// Prepara el campo Estudio sin tocar los PDF ni los datos clínicos existentes. La columna se agrega
/// una sola vez, con respaldo previo de la tabla, y los registros antiguos se completan desde su PDF.
public static class PatoEstudio
{
    public const string EstudioOtros = "Otros";

    public static async Task InicializarAsync(PatoDb db, ILogger log, CancellationToken ct = default)
    {
        await AsegurarColumnaAsync(db, ct);
        var etiquetados = await EtiquetarVaciosAsync(db, ct);
        var (procesados, conDesignacion, sinDesignacion, errores) = await CompletarPendientesAsync(db, ct);
        if (etiquetados > 0)
            log.LogInformation("Campo Estudio: {Actualizados} registros sin designación se etiquetaron como Otros.", etiquetados);
        if (procesados > 0 || errores > 0)
            log.LogInformation("Campo Estudio preparado: {Procesados} reportes, {ConDesignacion} con designación, {SinDesignacion} como Otros, {Errores} pendientes.",
                procesados, conDesignacion, sinDesignacion, errores);
    }

    private static async Task AsegurarColumnaAsync(PatoDb db, CancellationToken ct)
    {
        using var cn = db.Conexion();
        await cn.OpenAsync(ct);
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema=DATABASE() AND table_name='reportes' AND column_name='estudio'
                """;
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0) return;
        }

        // MySQL confirma los ALTER aunque si algo falla después. Por eso se respalda la tabla
        // completa antes de agregar la columna; el nombre queda para una reversión manual segura.
        var respaldo = $"reportes_respaldo_estudio_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..48];
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = $"CREATE TABLE {respaldo} LIKE reportes";
            await cmd.ExecuteNonQueryAsync(ct);
        }
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = $"INSERT INTO {respaldo} SELECT * FROM reportes";
            await cmd.ExecuteNonQueryAsync(ct);
        }
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "ALTER TABLE reportes ADD COLUMN estudio VARCHAR(255) NULL AFTER modalidad";
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<int> EtiquetarVaciosAsync(PatoDb db, CancellationToken ct)
    {
        using var cn = db.Conexion();
        await cn.OpenAsync(ct);
        using var cmd = cn.CreateCommand();
        // NULL significa que el PDF todavía no se pudo leer; no se convierte. La cadena vacía sí
        // significa que el PDF se leyó correctamente pero no trae la designación de Estudio.
        cmd.CommandText = "UPDATE reportes SET estudio=@estudio WHERE estudio IS NOT NULL AND TRIM(estudio)=''";
        cmd.Parameters.AddWithValue("@estudio", EstudioOtros);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<(int Procesados, int ConDesignacion, int SinDesignacion, int Errores)> CompletarPendientesAsync(
        PatoDb db, CancellationToken ct)
    {
        var pendientes = new List<(long Id, string Ruta)>();
        using var cn = db.Conexion();
        await cn.OpenAsync(ct);
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, ruta_pdf FROM reportes WHERE estudio IS NULL AND ruta_pdf IS NOT NULL AND ruta_pdf<>'' ORDER BY id";
            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) pendientes.Add((r.GetInt64(0), r.GetString(1)));
        }

        var procesados = 0;
        var conDesignacion = 0;
        var sinDesignacion = 0;
        var errores = 0;
        foreach (var (id, rutaRelativa) in pendientes)
        {
            ct.ThrowIfCancellationRequested();
            var estudio = "";
            try
            {
                var ruta = db.RutaAbsoluta(rutaRelativa);
                if (!File.Exists(ruta) || new FileInfo(ruta).Length > Repositorio.MaxPdfBytes)
                {
                    // Un archivo ausente o demasiado grande no demuestra que el PDF carezca del
                    // recuadro. Se conserva NULL para reintentarlo cuando el archivo esté disponible.
                    errores++;
                    continue;
                }
                var pdf = await File.ReadAllBytesAsync(ruta, ct);
                var campos = Extractor.Extraer(pdf, Path.GetFileName(ruta));
                estudio = PatoDb.Trunc(campos.Estudio, 255);
                if (estudio.Length == 0) estudio = EstudioOtros;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Se deja NULL para poder reintentarlo en el siguiente arranque. El registro nunca
                // incluye nombres, NSS ni rutas de pacientes.
                errores++;
                continue;
            }

            using var update = cn.CreateCommand();
            update.CommandText = "UPDATE reportes SET estudio=@estudio WHERE id=@id AND estudio IS NULL";
            update.Parameters.AddWithValue("@estudio", estudio);
            update.Parameters.AddWithValue("@id", id);
            await update.ExecuteNonQueryAsync(ct);
            procesados++;
            if (estudio == EstudioOtros) sinDesignacion++;
            else conDesignacion++;
        }
        return (procesados, conDesignacion, sinDesignacion, errores);
    }
}
