using Meditechnosys.Intranet.Services;

namespace Meditechnosys.Intranet.Dicom;

/// <summary>
/// Reescritura de los criterios de texto libre para que el PACS encuentre los nombres escritos con
/// otra ortografía de acentos.
///
/// El C-FIND del PACS compara carácter por carácter: "NUNEZ" no encuentra "NUÑEZ" y "MUÑOZ" no
/// encuentra "MUNOZ". Como no se puede normalizar lo que está almacenado allá, se hacen dos cosas:
///
/// 1. Quitar los acentos a lo que el usuario escribió (cubre el caso "escribí MUÑOZ, el PACS guarda
///    MUNOZ", muy frecuente en sistemas antiguos que solo aceptan ASCII).
/// 2. Sustituir UNA letra por `?` —el comodín DICOM de un solo carácter— en cada posición donde puede
///    haber un acento o una eñe (cubre "escribí MUNOZ, el PACS guarda MUÑOZ"). Se cambia una letra a
///    la vez, no todas juntas, para que el patrón siga siendo selectivo y el PACS no devuelva medio
///    hospital.
///
/// Después de cada intento los resultados se comprueban aquí en local con
/// <see cref="Texto.Normalizar"/>, así que un `?` que abrió de más no llena la tabla de ajenos.
/// </summary>
internal static class CriteriosTolerantes
{
    /// <summary>Máximo de comodines por búsqueda: cada uno es una consulta C-FIND adicional.</summary>
    internal const int MaximoVariantes = 8;

    /// <summary>
    /// Variantes de un criterio ya validado. No toca los campos que no son texto libre (ID, fecha,
    /// accession, modalidad) y respeta lo que el usuario pidió a propósito: entre comillas = exacto,
    /// o con `*`/`?` propios = ya sabe lo que quiere.
    /// </summary>
    public static IEnumerable<(SearchRequest Criterio, string Campo, string Termino)> Variantes(SearchRequest criteria)
    {
        var objetivos = new (string Campo, string? Valor)[]
        {
            ("nombre", criteria.PatientName),
            ("descripcion", criteria.StudyDescription)
        };

        foreach (var (campo, valor) in objetivos)
        {
            var termino = Texto.Normalizar((valor ?? "").Trim('"'));
            foreach (var patron in VariantesDe(valor))
            {
                var copia = new SearchRequest
                {
                    PatientId = criteria.PatientId,
                    PatientName = criteria.PatientName,
                    StudyDate = criteria.StudyDate,
                    AccessionNumber = criteria.AccessionNumber,
                    ModalitiesInStudy = criteria.ModalitiesInStudy,
                    StudyDescription = criteria.StudyDescription,
                    AeTitles = criteria.AeTitles
                };

                if (campo == "nombre") copia.PatientName = patron;
                else copia.StudyDescription = patron;

                yield return (copia, campo, termino);
            }
        }
    }

    /// <summary>Comodines candidatos para un valor de texto libre, en el orden en que conviene probarlos.</summary>
    public static List<string> VariantesDe(string? valor)
    {
        var salida = new List<string>();
        var v = (valor ?? "").Trim();
        if (v.Length == 0) return salida;
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') return salida;   // exacto pedido a propósito
        if (v.IndexOf('*') >= 0 || v.IndexOf('?') >= 0) return salida;     // el usuario ya puso comodines

        var plano = Texto.Normalizar(v);
        if (plano.Length == 0) return salida;

        // 1) El PACS guarda sin acentos y el usuario sí los escribió.
        if (plano != v) salida.Add(plano + "*");

        // 2) El PACS guarda con acento en ESA letra: `?` la cubre sin ensanchar el resto de la palabra.
        for (var i = 0; i < plano.Length && salida.Count < MaximoVariantes; i++)
        {
            if (!EsAcentuable(plano[i])) continue;
            var arr = plano.ToCharArray();
            arr[i] = '?';
            salida.Add(new string(arr) + "*");
        }
        return salida;
    }

    /// <summary>Letras del español que aparecen con tilde, diéresis o eñe.</summary>
    internal static bool EsAcentuable(char ch) => ch is 'A' or 'E' or 'I' or 'O' or 'U' or 'N';

    /// <summary>
    /// Estudia cuyo campo escrito coincide de verdad con el término buscado, sin distinguir acentos ni
    /// mayúsculas. Es el control de calidad del reintento: el `?` amplía, aquí se estrecha de nuevo.
    /// </summary>
    public static List<StudyInfo> Filtrar(List<StudyInfo> estudios, string campo, string terminoNormalizado)
    {
        if (terminoNormalizado.Length == 0) return estudios;
        return estudios.Where(e =>
        {
            var donde = campo == "nombre" ? e.PatientName : e.StudyDescription;
            return Texto.Normalizar(donde).Contains(terminoNormalizado, StringComparison.Ordinal);
        }).ToList();
    }
}
