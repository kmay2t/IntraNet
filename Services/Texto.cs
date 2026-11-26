using System.Globalization;
using System.Text;

namespace Meditechnosys.Intranet.Services;

/// <summary>
/// Comparaciones de texto para las cajas de búsqueda de la intranet.
///
/// El problema que resuelve: en los apellidos y estudios médicos la ñ y las tildes se escriben
/// unas veces sí y otras no (el PACS guarda "MUÑOZ", el usuario teclea "MUNOZ"; la bitácora dice
/// "CRÁNEO", se busca "CRANEO"). Un <c>Contains</c> normal —incluso con <c>OrdinalIgnoreCase</c>—
/// trata Ñ y N como letras distintas y la búsqueda devuelve nada.
///
/// <see cref="Normalizar"/> quita los acentos y pasa a mayúsculas, así que comparar dos textos ya
/// normalizados hace que "NUNEZ" y "NUÑEZ" sean la misma cadena.
///
/// NO usar en claves de negocio (folio, NSS, números de inventario, UIDs DICOM, nombres de rol o
/// permiso): ahí el valor exacto sí importa.
/// </summary>
public static class Texto
{
    /// <summary>
    /// Texto sin acentos, en mayúsculas y con los espacios interiores reducidos a uno solo.
    /// "  Nuñez   Rodríguez " → "NUNEZ RODRIGUEZ".
    /// </summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";

        // FormKD separa cada letra con tilde en "letra + marca combinante"; al quitar las marcas
        // queda la letra seca (Ñ → N, Á → A). La Ñ NO se descompone (es letra propia de Unicode),
        // así que se sustituye a mano.
        var descompuesto = texto.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var ch in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch is 'Ñ' or 'ñ') { sb.Append('N'); continue; }
            if (ch is 'Ç' or 'ç') { sb.Append('C'); continue; }
            if (ch is 'ª' or 'º') continue;
            if (char.IsWhiteSpace(ch)) { sb.Append(' '); continue; }
            sb.Append(char.ToUpperInvariant(ch));
        }

        return Colapsar(sb.ToString());
    }

    /// <summary>«¿<paramref name="donde"/> contiene a <paramref name="que"/>?» sin distinguir acentos ni mayúsculas.</summary>
    public static bool Contiene(string? donde, string? que)
    {
        var q = Normalizar(que);
        if (q.Length == 0) return true;
        return Normalizar(donde).Contains(q, StringComparison.Ordinal);
    }

    /// <summary>«¿<paramref name="donde"/> empieza por <paramref name="que"/>?» sin distinguir acentos ni mayúsculas.</summary>
    public static bool Empieza(string? donde, string? que)
    {
        var q = Normalizar(que);
        if (q.Length == 0) return true;
        return Normalizar(donde).StartsWith(q, StringComparison.Ordinal);
    }

    /// <summary>
    /// ¿El texto tiene letras que en español suelen aparecer con tilde o con diéresis?
    /// Sirve para decidir si vale la pena ensanchar una consulta (p. ej. en el PACS).
    /// La U con diéresis (ü) sí se normaliza a U; la ñ ya la convierte <see cref="Normalizar"/>.
    /// </summary>
    public static bool TieneLetrasAcentuables(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return false;
        foreach (var ch in Normalizar(texto))
        {
            if (ch is 'A' or 'E' or 'I' or 'O' or 'U' or 'N' or 'C' or 'Z') return true;
        }
        return false;
    }

    private static string Colapsar(string v)
    {
        var s = v.Trim();
        if (!s.Contains("  ", StringComparison.Ordinal)) return s;
        var sb = new StringBuilder(s.Length);
        var espacioAnterior = false;
        foreach (var ch in s)
        {
            var esEspacio = ch == ' ';
            if (esEspacio && espacioAnterior) continue;
            sb.Append(ch);
            espacioAnterior = esEspacio;
        }
        return sb.ToString();
    }
}
