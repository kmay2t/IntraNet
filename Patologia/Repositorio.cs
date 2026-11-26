using System.Security.Cryptography;
using MySqlConnector;

namespace Meditechnosys.Intranet.Patologia;

public record ResultadoGuardado(
    bool Ok, bool Omitido, long? Id, string Archivo, string Area,
    string? Error, string? Aviso,
    string Folio = "", string Fecha = "", string Nombre = "", string Nss = "",
    string Agregado = "", string Modalidad = "", string Estudio = "");

/// Guardado de un PDF: extrae campos, asigna ID en MySQL y copia al almacenamiento fijo.
/// Port de app/repositorio.py.
public class Repositorio
{
    public const long MaxPdfBytes = 20 * 1024 * 1024;

    private readonly PatoDb _db;
    public Repositorio(PatoDb db) => _db = db;

    private static string FolioRespaldo(string sha, string nombre)
    {
        var f = Extractor.FolioDesdeNombreArchivo(nombre);
        if (f.Length > 0) return f;
        var base_ = Path.GetFileNameWithoutExtension(nombre).ToUpperInvariant().Replace(" ", "");
        if (base_.Length > 40) base_ = base_[..40];
        return base_.Length > 0 ? $"ARCH-{base_}" : $"ARCH-{sha[..12].ToUpperInvariant()}";
    }

    private static DateOnly? FechaONull(string valor) =>
        DateOnly.TryParseExact(valor, "yyyy-MM-dd", out var f) ? f : null;

    public ResultadoGuardado Guardar(string area, byte[] contenido, string nombre, string subidoPor)
    {
        _ = _db.CarpetaArea(area); // valida el área
        if (contenido.Length > MaxPdfBytes)
            return new ResultadoGuardado(false, false, null, nombre, area, "El archivo excede 20 MB", null);
        if (!contenido.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            return new ResultadoGuardado(false, false, null, nombre, area, "No es un PDF válido", null);

        CamposPatologia campos;
        try { campos = Extractor.Extraer(contenido, nombre); }
        catch (Exception e) { return new ResultadoGuardado(false, false, null, nombre, area, $"Error al leer el PDF: {e.Message}", null); }

        var sha = Convert.ToHexString(SHA256.HashData(contenido)).ToLowerInvariant();
        var folio = campos.Folio.Length > 0 ? campos.Folio : FolioRespaldo(sha, nombre);
        var fecha = campos.Fecha;
        var nombreP = campos.Nombre;
        var nss = campos.Nss;
        var agregado = campos.Agregado;
        var modalidad = campos.Modalidad;
        var estudio = string.IsNullOrWhiteSpace(campos.Estudio) ? PatoEstudio.EstudioOtros : campos.Estudio;

        string? destino = null;
        try
        {
            using var cn = _db.Conexion();
            cn.Open();
            using var tx = cn.BeginTransaction();

            using (var cmd = cn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT id, folio FROM reportes WHERE area = @a AND (folio = @f OR archivo_sha256 = @s) LIMIT 1";
                cmd.Parameters.AddWithValue("@a", area);
                cmd.Parameters.AddWithValue("@f", folio);
                cmd.Parameters.AddWithValue("@s", sha);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    var idEx = r.GetInt64(0);
                    var folioEx = r.GetString(1);
                    r.Close();
                    tx.Commit();
                    return new ResultadoGuardado(true, true, idEx, nombre, area, null,
                        $"Ya registrado (folio {folioEx})", folio, fecha, nombreP, nss, agregado, modalidad, estudio);
                }
            }

            long nuevoId;
            using (var cmd = cn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO reportes
                      (area, folio, fecha, nombre, nombre_norm, nss, agregado_medico, modalidad, estudio,
                       archivo_original, archivo_sha256, subido_por)
                    VALUES (@area,@folio,@fecha,@nombre,@nnorm,@nss,@agr,@mod,@estudio,@orig,@sha,@por)
                    """;
                cmd.Parameters.AddWithValue("@area", area);
                cmd.Parameters.AddWithValue("@folio", PatoDb.Trunc(folio, 60));
                cmd.Parameters.AddWithValue("@fecha", FechaONull(fecha) is { } f ? (object)f : DBNull.Value);
                cmd.Parameters.AddWithValue("@nombre", PatoDb.Trunc(nombreP, 200));
                cmd.Parameters.AddWithValue("@nnorm", PatoDb.Trunc(PatoDb.Normalizar(nombreP), 200));
                cmd.Parameters.AddWithValue("@nss", PatoDb.Trunc(nss, 20));
                cmd.Parameters.AddWithValue("@agr", PatoDb.Trunc(agregado, 20));
                cmd.Parameters.AddWithValue("@mod", PatoDb.Trunc(modalidad, 60));
                cmd.Parameters.AddWithValue("@estudio", PatoDb.Trunc(estudio, 255));
                cmd.Parameters.AddWithValue("@orig", PatoDb.Trunc(nombre, 255));
                cmd.Parameters.AddWithValue("@sha", sha);
                cmd.Parameters.AddWithValue("@por", PatoDb.Trunc(subidoPor, 60));
                try { cmd.ExecuteNonQuery(); }
                catch (MySqlException e) when (e.Number == 1062)
                {
                    // Carrera entre dos procesos: otro lo insertó justo antes.
                    tx.Rollback();
                    return new ResultadoGuardado(true, true, null, nombre, area, null,
                        $"Ya registrado (folio {folio})", folio, fecha, nombreP, nss, agregado, modalidad, estudio);
                }
                nuevoId = cmd.LastInsertedId;
            }

            var rutaRel = PatoDb.RutaRelativa(area, nuevoId);
            destino = _db.RutaAbsoluta(rutaRel);
            EscribirAtomico(destino, contenido); // si falla → rollback de la fila
            using (var cmd = cn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE reportes SET ruta_pdf = @r WHERE id = @id";
                cmd.Parameters.AddWithValue("@r", rutaRel);
                cmd.Parameters.AddWithValue("@id", nuevoId);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            _db.Auditar("guardar", $"{area}:{folio}", usuario: subidoPor);

            var faltan = new List<string>();
            if (folio.Length == 0) faltan.Add("folio");
            if (fecha.Length == 0) faltan.Add("fecha");
            if (nombreP.Length == 0) faltan.Add("nombre");
            if (nss.Length == 0) faltan.Add("nss");
            if (agregado.Length == 0) faltan.Add("agregado_medico");
            if (modalidad.Length == 0) faltan.Add("modalidad");
            var aviso = faltan.Count > 0 ? "Campos vacíos: " + string.Join(", ", faltan) : null;
            return new ResultadoGuardado(true, false, nuevoId, nombre, area, null, aviso,
                folio, fecha, nombreP, nss, agregado, modalidad, estudio);
        }
        catch (Exception e)
        {
            if (destino is not null) try { File.Delete(destino); } catch { }
            return new ResultadoGuardado(false, false, null, nombre, area, $"No se pudo guardar el reporte: {e.Message}", null);
        }
    }

    private static void EscribirAtomico(string destino, byte[] contenido)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        var tmp = Path.ChangeExtension(destino, ".tmp");
        File.WriteAllBytes(tmp, contenido);
        File.Move(tmp, destino, overwrite: true);
    }
}
