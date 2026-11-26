using Meditechnosys.Intranet.Models;

namespace Meditechnosys.Intranet.Services;

/// Reglas de negocio del Playbook: biblioteca de documentación técnica en PDF clasificada por
/// hospital. El Administrador sube, renombra, cambia la versión, reemplaza el archivo y elimina;
/// el Ingeniero únicamente consulta y descarga/abre los PDF (nunca los modifica).
/// Los PDF se guardan en disco con un nombre interno (GUID) para evitar colisiones y rutas
/// relativas maliciosas; el nombre original que subió el Admin queda en los metadatos.
public class PlaybookService(
    IDocumentoStore store,
    IAuditLog bitacora,
    IConfiguration config,
    IWebHostEnvironment env,
    ILogger<PlaybookService> logger,
    GestionCatalogos catalogos)
{
    /// Carpeta donde viven los PDF. Se puede mudar con `Playbook:StorageDir`; por defecto queda
    /// dentro de App_Data para que la instalación no dependa de rutas absolutas.
    public string Carpeta { get; } = string.IsNullOrWhiteSpace(config["Playbook:StorageDir"])
        ? Path.Combine(env.ContentRootPath, "App_Data", "playbook")
        : config["Playbook:StorageDir"]!;

    public bool Tiene(GestionService.Sesion s, string clave) => s.Puede(clave);

    public void AsegurarCarpeta()
    {
        try { Directory.CreateDirectory(Carpeta); }
        catch (Exception e) { logger.LogWarning("No se pudo crear la carpeta del Playbook ({Carpeta}): {Error}", Carpeta, e.Message); }
    }

    /// Lista filtrada por hospital ("" = todos; admite "General"), estado y texto libre.
    public IReadOnlyList<Documento> Filtrar(GestionService.Sesion s, string? hospital, string? texto, string? estado)
    {
        if (!Tiene(s, CatalogoPermisos.PlaybookConsultar)) return Array.Empty<Documento>();
        var h = Hospitales.Normalizar(hospital, permiteGeneral: true);
        var t = Texto.Normalizar(texto);
        var e = EstadosDocumento.EsValido(estado) ? estado! : "";
        return catalogos.FiltraPorRegion(s.Usuario, store.List(), d => d.Hospital)
            .Where(d => h.Length == 0 || string.Equals(d.Hospital, h, StringComparison.Ordinal))
            .Where(d => e.Length == 0 || string.Equals(d.Estado, e, StringComparison.Ordinal))
            .Where(d => t.Length == 0
                || Texto.Normalizar(d.Nombre).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(d.NombreArchivo).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(d.Version).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(d.SubidoPor).Contains(t, StringComparison.Ordinal))
            .ToList();
    }

    public Documento? Find(long id) => store.Find(id);

    public (int Status, string? Error, Documento? Item) Subir(
        GestionService.Sesion s, string nombre, string tipo, string version, string hospital, DateTime? fechaPublicacion,
        string estado, string nombreArchivo, byte[]? contenido, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.PlaybookAdministrar))
            return (403, "Solo el Administrador puede subir documentos al Playbook.", null);
        if (DocumentoRules.NombreError(nombre) is string eN) return (400, eN, null);
        if (DocumentoRules.TipoError(tipo) is string eT) return (400, eT, null);
        if (DocumentoRules.VersionError(version) is string eV) return (400, eV, null);
        if (Hospitales.Error(hospital, permiteGeneral: true) is string eH) return (400, eH, null);
        // «General» sigue siendo válido para todos: es la documentación que no es de un hospital.
        if (catalogos.ErrorRegionDe(s.Usuario, hospital) is string eR) return (400, eR, null);
        if (DocumentoRules.EstadoError(estado) is string eE) return (400, eE, null);
        if (DocumentoRules.ArchivoError(contenido) is string eA) return (400, eA, null);

        var ruta = NuevoNombre();
        if (!Escribir(ruta, contenido!)) return (500, "No se pudo guardar el PDF en disco.", null);

        var ahora = DateTime.Now;
        var borrador = new Documento(0, nombre, SoloNombre(nombreArchivo), version.Trim(),
            Hospitales.Normalizar(hospital, permiteGeneral: true), fechaPublicacion?.Date, null,
            s.Usuario, ahora, contenido!.LongLength, ruta, estado, tipo.Trim());
        var (status, error, guardado) = store.Create(borrador);
        if (status != 201 || guardado is null)
        {
            Borrar(ruta);
            return (status, error, null);
        }
        bitacora.Register(s.Usuario, AuditEvents.SubirDocumento, $"{guardado.Nombre} · {guardado.Hospital} · {guardado.Version} · {guardado.NombreArchivo}", ip);
        return (201, null, guardado);
    }

    /// Edita los datos del documento (nombre, versión, hospital, fecha de publicación, estado).
    /// Cambiar la versión sella la fecha de actualización; el PDF no se toca.
    public (int Status, string? Error, Documento? Item) Editar(
        GestionService.Sesion s, long id, string nombre, string tipo, string version, string hospital,
        DateTime? fechaPublicacion, string estado, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.PlaybookAdministrar))
            return (403, "Solo el Administrador puede editar los documentos del Playbook.", null);
        var actual = store.Find(id);
        if (actual is null) return (404, "El documento no existe.", null);
        if (DocumentoRules.NombreError(nombre) is string eN) return (400, eN, null);
        if (DocumentoRules.TipoError(tipo) is string eT) return (400, eT, null);
        if (DocumentoRules.VersionError(version) is string eV) return (400, eV, null);
        if (Hospitales.Error(hospital, permiteGeneral: true) is string eH) return (400, eH, null);
        if (DocumentoRules.EstadoError(estado) is string eE) return (400, eE, null);
        // Ni el documento guardado ni el hospital al que se quiere mover pueden estar fuera de su región.
        if (catalogos.ErrorRegionDe(s.Usuario, actual.Hospital) is string eActual) return (400, eActual, null);
        if (catalogos.ErrorRegionDe(s.Usuario, hospital) is string eR) return (400, eR, null);

        var cambioVersion = !string.Equals(actual.Version, version.Trim(), StringComparison.Ordinal);
        var nueva = actual with
        {
            Nombre = nombre.Trim(),
            Tipo = tipo.Trim(),
            Version = version.Trim(),
            Hospital = Hospitales.Normalizar(hospital, permiteGeneral: true),
            FechaPublicacion = fechaPublicacion?.Date,
            Estado = estado,
            FechaActualizacion = cambioVersion ? DateTime.Now : actual.FechaActualizacion,
        };
        var (status, error, guardado) = store.Guardar(nueva);
        if (status != 200 || guardado is null) return (status, error, null);
        var cambios = new List<string>();
        if (actual.Nombre != guardado.Nombre) cambios.Add($"nombre → {guardado.Nombre}");
        if (actual.Tipo != guardado.Tipo) cambios.Add($"tipo → {guardado.Tipo}");
        if (cambioVersion) cambios.Add($"versión {actual.Version} → {guardado.Version}");
        if (actual.Hospital != guardado.Hospital) cambios.Add($"hospital → {guardado.Hospital}");
        if (actual.Estado != guardado.Estado) cambios.Add($"estado → {guardado.Estado}");
        if (actual.FechaPublicacion?.Date != guardado.FechaPublicacion?.Date) cambios.Add($"publicación → {guardado.FechaPublicacion:dd/MM/yyyy}");
        bitacora.Register(s.Usuario, AuditEvents.EditarDocumento,
            $"{guardado.Nombre}{(cambios.Count > 0 ? " · " + string.Join(", ", cambios) : "")}", ip);
        return (200, null, guardado);
    }

    /// Sustituye el PDF por una versión nueva del archivo: conserva los metadatos y sella la
    /// fecha de actualización. El archivo anterior se borra de disco.
    public (int Status, string? Error, Documento? Item) Reemplazar(
        GestionService.Sesion s, long id, string nombreArchivo, byte[]? contenido, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.PlaybookAdministrar))
            return (403, "Solo el Administrador puede reemplazar el archivo de un documento.", null);
        var actual = store.Find(id);
        if (actual is null) return (404, "El documento no existe.", null);
        if (catalogos.ErrorRegionDe(s.Usuario, actual.Hospital) is string eR) return (400, eR, null);
        if (DocumentoRules.ArchivoError(contenido) is string eA) return (400, eA, null);

        var anterior = actual.Ruta;
        var ruta = NuevoNombre();
        if (!Escribir(ruta, contenido!)) return (500, "No se pudo guardar el PDF en disco.", null);
        var (status, error, guardado) = store.Guardar(actual with
        {
            NombreArchivo = SoloNombre(nombreArchivo),
            Ruta = ruta,
            Tamano = contenido!.LongLength,
            FechaActualizacion = DateTime.Now,
        });
        if (status != 200 || guardado is null)
        {
            Borrar(ruta);
            return (status, error, null);
        }
        if (anterior != ruta) Borrar(anterior);
        bitacora.Register(s.Usuario, AuditEvents.ReemplazarDocumento, $"{guardado.Nombre} · {guardado.Version} · {guardado.NombreArchivo}", ip);
        return (200, null, guardado);
    }

    public (int Status, string? Error) Eliminar(GestionService.Sesion s, long id, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.PlaybookAdministrar))
            return (403, "Solo el Administrador puede eliminar documentos del Playbook.");
        var actual = store.Find(id);
        if (actual is null) return (404, "El documento no existe.");
        if (catalogos.ErrorRegionDe(s.Usuario, actual.Hospital) is string eR) return (400, eR);
        var (status, error) = store.Delete(id);
        if (status == 204)
        {
            Borrar(actual.Ruta);
            bitacora.Register(s.Usuario, AuditEvents.EliminarDocumento, $"{actual.Nombre} · {actual.Hospital} · {actual.Version}", ip);
        }
        return (status, error);
    }

    /// Ruta absoluta del PDF (null si el archivo ya no está en disco).
    public string? RutaPdf(Documento d)
    {
        var ruta = RutaAbsoluta(d.Ruta);
        return ruta is not null && File.Exists(ruta) ? ruta : null;
    }

    /// Resuelve una ruta relativa dentro de la carpeta del Playbook, sin permitir salir de ella.
    public string? RutaAbsoluta(string relativa)
    {
        if (string.IsNullOrWhiteSpace(relativa)) return null;
        try
        {
            var raiz = Path.GetFullPath(Carpeta);
            var completa = Path.GetFullPath(Path.Combine(raiz, relativa));
            return completa.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? completa : null;
        }
        catch { return null; }
    }

    private static string NuevoNombre() => $"{Guid.NewGuid():N}.pdf";

    private bool Escribir(string ruta, byte[] contenido)
    {
        try
        {
            AsegurarCarpeta();
            File.WriteAllBytes(Path.Combine(Carpeta, ruta), contenido);
            return true;
        }
        catch (Exception e) { logger.LogError("No se pudo escribir el PDF {Ruta}: {Error}", ruta, e.Message); return false; }
    }

    private void Borrar(string ruta)
    {
        try
        {
            var absoluta = RutaAbsoluta(ruta);
            if (absoluta is not null && File.Exists(absoluta)) File.Delete(absoluta);
        }
        catch (Exception e) { logger.LogWarning("No se pudo borrar el PDF {Ruta}: {Error}", ruta, e.Message); }
    }

    /// Quita cualquier carpeta del nombre que envía el navegador (solo interesa el archivo).
    private static string SoloNombre(string? nombreArchivo)
    {
        var n = (nombreArchivo ?? "").Trim();
        if (n.Length == 0) return "documento.pdf";
        n = n.Replace('\\', '/');
        var i = n.LastIndexOf('/');
        if (i >= 0) n = n[(i + 1)..];
        return n.Length > 200 ? n[..200] : n;
    }
}
