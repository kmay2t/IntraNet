namespace Meditechnosys.Intranet.Dicom;

/// <summary>
/// Las dos decisiones de integridad de una descarga, separadas del código DICOM para que se puedan
/// comprobar solas (ver <c>.verify</c> y el registro de cambios):
///
/// 1. <see cref="DividirEnPartes"/>: en cuántas partes se reparte el ZIP de una descarga grande, y
/// 2. <see cref="Faltantes"/>: qué estudios NO llegaron completos, comparando lo que anuncia el PACS
///    contra lo que de verdad se guardó.
///
/// Ninguna de las dos toca red ni disco: reciben datos y devuelven listas.
/// </summary>
internal static class IntegridadDescarga
{
    /// <summary>Una carpeta de estudio —la unidad que se empaqueta— con cuántos archivos y cuánto pesan.</summary>
    internal readonly record struct CarpetaDeZip(string Clave, int Archivos, long Bytes);

    /// <summary>
    /// Reparte las carpetas en las partes del ZIP. Un estudio nunca queda partido entre dos ZIP: el
    /// corte solo se hace entre carpetas, y se decide con lo que lleva <b>la parte en curso</b> (no
    /// con el acumulado), así cada archivo comprimido cumple el tope por separado.
    ///
    /// <paramref name="maxEstudios"/> o <paramref name="maxBytes"/> en 0 significa "sin tope por ese
    /// lado". Una carpeta que sola pasa del tope viaja sola: antes que un ZIP enorme que no baje
    /// nadie, perder ese estudio.
    /// </summary>
    public static List<List<CarpetaDeZip>> DividirEnPartes(
        IReadOnlyList<CarpetaDeZip> carpetas, int maxEstudios, long maxBytes)
    {
        var topeEstudios = maxEstudios > 0 ? maxEstudios : int.MaxValue;
        var topeBytes = maxBytes > 0 ? maxBytes : long.MaxValue;

        var partes = new List<List<CarpetaDeZip>>();
        var actual = new List<CarpetaDeZip>();
        long bytesActual = 0;

        foreach (var c in carpetas)
        {
            if (actual.Count > 0 &&
                (actual.Count + 1 > topeEstudios || bytesActual + c.Bytes > topeBytes))
            {
                partes.Add(actual);
                actual = new List<CarpetaDeZip>();
                bytesActual = 0;
            }
            actual.Add(c);
            bytesActual += c.Bytes;
        }
        if (actual.Count > 0) partes.Add(actual);
        return partes;
    }

    /// <summary>
    /// Estudios que quedaron incompletos. Se comparan las imágenes **distintas** guardadas
    /// (<paramref name="recibidas"/>) contra las que el PACS anuncia (<paramref name="esperado"/>):
    ///   · si llegaron menos de las esperadas → falta gente;
    ///   · si no llegó ninguna → el estudio ni se materializó (lote cortado, AE no autorizado…);
    ///   · si el PACS no informó el conteo, solo se puede exigir que haya llegado algo;
    ///   · si llega de más (un reintento repite imágenes) NO se avisa: el PACS manda el estudio
    ///     completo otra vez y los archivos se sobrescriben con el mismo nombre.
    /// </summary>
    public static List<IncompleteStudy> Faltantes(
        IReadOnlyList<DownloadItem> items,
        Func<string, int> esperado,
        Func<string, int> recibidas)
    {
        var salida = new List<IncompleteStudy>();
        foreach (var item in items)
        {
            var uid = item.StudyInstanceUid;
            var meta = esperado(uid);
            var recibido = recibidas(uid);
            if (recibido > 0 && (meta <= 0 || recibido >= meta)) continue;

            salida.Add(new IncompleteStudy
            {
                StudyInstanceUid = uid,
                Expected = meta,
                Received = recibido,
                PatientName = item.PatientName ?? "",
                StudyDate = item.StudyDate ?? "",
                SourceName = item.SourceName ?? "",
            });
        }
        return salida;
    }
}
