using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Meditechnosys.Intranet.Patologia;

/// Palabra con coordenadas estilo pdfplumber (top/bottom medidos desde arriba).
public record PlWord(string Text, double X0, double X1, double Top, double Bottom)
{
    public double Cx => (X0 + X1) / 2;
}

public record CamposPatologia(string Folio, string Fecha, string Nombre, string Nss, string Agregado, string Modalidad, string Estudio);

/// Extracción de campos de reportes de Patología en PDF (IMSS).
/// Port directo de app/extractor.py: mismos layouts, mismas reglas.
public static partial class Extractor
{
    private static readonly Dictionary<string, int> Meses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enero"] = 1, ["febrero"] = 2, ["marzo"] = 3, ["abril"] = 4, ["mayo"] = 5, ["junio"] = 6,
        ["julio"] = 7, ["agosto"] = 8, ["septiembre"] = 9, ["setiembre"] = 9, ["octubre"] = 10,
        ["noviembre"] = 11, ["diciembre"] = 12,
    };

    public static readonly string[] Modalidades =
    {
        "Biopsia", "Transoperatorio", "Quirúrgico", "Revisión de laminillas", "Citología", "Otro",
    };

    private static readonly HashSet<string> Particulas =
        new(StringComparer.Ordinal) { "DE", "DEL", "LA", "LAS", "LOS", "Y", "E", "DA", "DO", "DAS", "DOS", "JESUS", "JESÚS" };

    private static readonly HashSet<string> StopNombre = new(StringComparer.Ordinal)
    {
        "NOMBRE", "NSS", "AGREGADO", "MEDICO", "MÉDICO", "GENERO", "GÉNERO",
        "EDAD", "FEMENINO", "MASCULINO", "DATOS", "DEL", "PACIENTE", "ESTUDIO",
        "MODALIDAD", "FECHA", "FOLIO", "PATOLOGIA", "PATOLOGÍA", "IMSS",
        "DIRECCION", "DIRECCIÓN", "PRESTACIONES", "MEDICAS", "MÉDICAS",
        "HOSPITAL", "ESPECIALIDADES", "AÑOS", "MESES", "DIA", "DÍA", "ANOS",
        "BIOPSIA", "CITOLOGIA", "CITOLOGÍA", "QUIRURGICO", "QUIRÚRGICO",
        "TRANSOPERATORIO", "AUTOPSIA", "HISTOPATOLOGICO", "HISTOPATOLÓGICO",
        "DESCRIPCION", "DESCRIPCIÓN", "DIAGNOSTICO", "DIAGNÓSTICO",
        "MICROSCOPICA", "MICROSCÓPICA", "MACROSCOPICA", "MACROSCÓPICA",
        "SOLICITANTE", "DR", "DRA", "CORRESPONDA", "QUIEN", "TEJIDOS",
        "BLANDOS", "PIERNA", "AÑO", "ANO", "REVISION", "REVISIÓN", "LAMINILLAS",
        "SISTEMA", "URINARIO", "VEJIGA", "HEMATOLOGIA", "HEMATOLOGÍA", "MEDULA",
        "MÉDULA", "OSEA", "ÓSEA", "OFTALMOLOGIA", "OFTALMOLOGÍA", "CONJUNTIVA",
        "GASTRICA", "GÁSTRICA", "PIEL", "LIQUIDO", "LÍQUIDO", "PLEURAL",
        "BAAF", "TIROIDES", "ESOFAGO", "ESÓFAGO", "ESTOMAGO", "ESTÓMAGO",
        "COMPLETO", "SEGURIDAD", "SOCIAL", "NUMERO", "NÚMERO", "SEXO", "NAC",
        "GINECO", "OBSTETRICIA", "RESULTADOS", "EPITELIO", "VAGINAL",
        "ANATOMIA", "ANATOMÍA", "PATOLOGICA", "PATOLÓGICA", "UMAE", "RAZA",
    };

    private const RegexOptions Rx = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"(?:Folio)\s*[:.]?\s*((?:[A-Z]{1,3}\s*-?\s*)?\d{2,6}\s*-\s*\d{2})", Rx)]
    private static partial Regex ReFolio();
    [GeneratedRegex(@"(?<![A-Z0-9])((?:[A-Z]{1,3}\s*-?\s*)?\d{2,6}\s*-\s*\d{2})(?![A-Z0-9])", Rx)]
    private static partial Regex ReFolioSuelto();
    [GeneratedRegex(@"(?:lunes|martes|mi[eé]rcoles|jueves|viernes|s[aá]bado|domingo)?\s*,?\s*(\d{1,2})\s+de\s+([A-Za-záéíóúÁÉÍÓÚñÑ]+)\s+(?:del?\s+)?(\d{4})", Rx)]
    private static partial Regex ReFechaLarga();
    [GeneratedRegex(@"\b(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{2,4})\b", RegexOptions.CultureInvariant)]
    private static partial Regex ReFechaCorta();
    [GeneratedRegex(@"\b(\d{10,11})\b", RegexOptions.CultureInvariant)]
    private static partial Regex ReNss();
    [GeneratedRegex(@"\b([0-9][A-Z]\d{4}\s?[A-Z]{1,4})\b", Rx)]
    private static partial Regex ReAgregado();
    [GeneratedRegex(@"\b(BI[OÓ]PSIA|CITOLOG[IÍ]A|QUIR[UÚ]RGICO|TRANSOPERATORIO|REVISI[OÓ]N\s+DE\s+LAMINILLAS)\b", Rx)]
    private static partial Regex ReModExplicita();
    [GeneratedRegex(@"GINECO|OBSTETRICIA|GINECOLOG[IÍ]A", Rx)]
    private static partial Regex ReGineco();
    [GeneratedRegex(@"DESCRIPCI[OÓ]N\s+MACRO", Rx)]
    private static partial Regex ReCorteMacro();
    [GeneratedRegex(@"[^A-ZÁÉÍÓÚÑÜ0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex ReNormToken();
    [GeneratedRegex(@"\.pdf$", Rx)]
    private static partial Regex ReExtPdf();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex ReEspacios();
    [GeneratedRegex(@"(?<![A-Za-záéíóúñÑ])(?:[A-Za-záéíóúñÑ]\s){2,}[A-Za-záéíóúñÑ](?![A-Za-záéíóúñÑ])", RegexOptions.CultureInvariant)]
    private static partial Regex ReLetrasEspaciadas();
    [GeneratedRegex(@"(?<!\d)(?:\d\s){2,}\d(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex ReDigitosEspaciados();
    [GeneratedRegex(@"(?<=\d)\s+(?=\d)", RegexOptions.CultureInvariant)]
    private static partial Regex ReEspacioEntreDigitos();
    [GeneratedRegex(@"^([A-Z]{1,3})-?(\d{2,6}-\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex ReFolioPref();
    [GeneratedRegex(@"^(\d{2,6}-\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex ReFolioNum();
    [GeneratedRegex(@"(\d{1,2})de([a-z]+?)(?:del|de)?(\d{4})", RegexOptions.CultureInvariant)]
    private static partial Regex ReFechaSinEspacios();
    [GeneratedRegex(@"((?:[A-Za-z]{1,3}-)?\d{2,6}-\d{2})", RegexOptions.CultureInvariant)]
    private static partial Regex ReFolioPermisivo();
    [GeneratedRegex(@"revisi[oó]n\s+de\s+laminill", RegexOptions.CultureInvariant)]
    private static partial Regex ReModLaminillas();
    [GeneratedRegex(@"transoper", RegexOptions.CultureInvariant)]
    private static partial Regex ReModTrans();
    [GeneratedRegex(@"intraoper", RegexOptions.CultureInvariant)]
    private static partial Regex ReModIntra();
    [GeneratedRegex(@"quir[uú]rgico\b", RegexOptions.CultureInvariant)]
    private static partial Regex ReModQuir();
    [GeneratedRegex(@"\bpieza\b", RegexOptions.CultureInvariant)]
    private static partial Regex ReModPieza();
    [GeneratedRegex(@"citolog", RegexOptions.CultureInvariant)]
    private static partial Regex ReModCito();
    [GeneratedRegex(@"biops", RegexOptions.CultureInvariant)]
    private static partial Regex ReModBiops();
    [GeneratedRegex(@"Modalidad\s*[:.]?\s*([A-Za-záéíóúÁÉÍÓÚñÑüÜ][A-Za-záéíóúÁÉÍÓÚñÑüÜ\s]{1,40})", Rx)]
    private static partial Regex ReModEtiqueta();
    [GeneratedRegex(@"DATOS\s+DEL\s+ESTUDIO", Rx)]
    private static partial Regex ReDatosEstudio();
    [GeneratedRegex(@"Estudio\s*Modalidad\s*Fecha", Rx)]
    private static partial Regex ReEtiquetasEstudio();
    [GeneratedRegex(@"(?:BI[OÓ]PSIA|CITOLOG[IÍ]A|QUIR[UÚ]RGICO|TRANSOPERATORIO|REVISI[OÓ]N\s+DE\s+LAMINILLAS)", Rx)]
    private static partial Regex ReModDesignacion();
    [GeneratedRegex(@"nombre\s+completo", Rx)]
    private static partial Regex ReNombreCompleto();
    [GeneratedRegex(@"n[uú]mero|seguridad|sexo|folio|fecha|edad", Rx)]
    private static partial Regex ReEtiquetaGineco();
    [GeneratedRegex(@"seguridad|nss|n[uú]mero", Rx)]
    private static partial Regex ReContextoNss();
    [GeneratedRegex(@"folio|patolog|hospital|direcci", Rx)]
    private static partial Regex ReLineaNoNombre();
    [GeneratedRegex(@"\bnombre\b", Rx)]
    private static partial Regex ReNombre();
    [GeneratedRegex(@"^(paciente|resultados)\s*[:.]?\s*", Rx)]
    private static partial Regex ReBasuraInicio();
    [GeneratedRegex(@"\b(paciente|resultados)\s*[:.]?\s*", Rx)]
    private static partial Regex ReBasura();

    // ---------------------------------------------------------- helpers texto
    private static string SinAcentos(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var nfkd = s.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(nfkd.Length);
        foreach (var ch in nfkd)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().ToLowerInvariant();
    }

    private static string NormToken(string t) => ReNormToken().Replace(t.ToUpperInvariant(), "");

    private static int AnioCompleto(int y) => y < 100 ? (y < 50 ? 2000 + y : 1900 + y) : y;

    private static string FechaAIso(string? texto, int minAnio = 1990, int maxAnio = 2040)
    {
        texto ??= "";
        var m = ReFechaLarga().Match(texto);
        if (m.Success)
        {
            if (Meses.TryGetValue(SinAcentos(m.Groups[2].Value), out var mes))
            {
                var y = AnioCompleto(int.Parse(m.Groups[3].Value));
                if (minAnio <= y && y <= maxAnio)
                {
                    try { return new DateOnly(y, mes, int.Parse(m.Groups[1].Value)).ToString("yyyy-MM-dd"); }
                    catch (ArgumentOutOfRangeException) { }
                }
            }
        }
        foreach (Match mc in ReFechaCorta().Matches(texto))
        {
            var d = int.Parse(mc.Groups[1].Value);
            var mo = int.Parse(mc.Groups[2].Value);
            var y = AnioCompleto(int.Parse(mc.Groups[3].Value));
            if (mo > 12 && d <= 12) (d, mo) = (mo, d);
            if (minAnio > y || y > maxAnio) continue;
            try { return new DateOnly(y, mo, d).ToString("yyyy-MM-dd"); }
            catch (ArgumentOutOfRangeException) { }
        }
        // Último recurso: fecha con caracteres espaciados, sobre el texto sin espacios.
        var tSin = SinAcentos(ReEspacios().Replace(texto, ""));
        var m3 = ReFechaSinEspacios().Match(tSin);
        if (m3.Success && Meses.TryGetValue(m3.Groups[2].Value, out var mes3))
        {
            var y = AnioCompleto(int.Parse(m3.Groups[3].Value));
            if (minAnio <= y && y <= maxAnio)
            {
                try { return new DateOnly(y, mes3, int.Parse(m3.Groups[1].Value)).ToString("yyyy-MM-dd"); }
                catch (ArgumentOutOfRangeException) { }
            }
        }
        return "";
    }

    private static string CompactarEspaciado(string? texto)
    {
        texto ??= "";
        texto = ReLetrasEspaciadas().Replace(texto, m => ReEspacios().Replace(m.Value, ""));
        texto = ReDigitosEspaciados().Replace(texto, m => ReEspacios().Replace(m.Value, ""));
        texto = ReEspacioEntreDigitos().Replace(texto, "");
        return texto;
    }

    private static string NormalizarFolio(string? crudo)
    {
        var s = ReEspacios().Replace(crudo ?? "", "").ToUpperInvariant();
        s = s.Replace('–', '-').Replace('—', '-');
        var m = ReFolioPref().Match(s);
        if (m.Success) return $"{m.Groups[1].Value}-{m.Groups[2].Value}";
        var m2 = ReFolioNum().Match(s);
        return m2.Success ? m2.Groups[1].Value : s[..Math.Min(s.Length, 60)];
    }

    private static string ModalidadPorFolio(string? folio)
    {
        var pref = (folio ?? "").Split('-', 2)[0].ToUpperInvariant();
        if (pref.All(char.IsDigit)) return "";
        return pref switch
        {
            "TO" or "T" => "Transoperatorio",
            "Q" => "Quirúrgico",
            "B" => "Biopsia",
            "C" or "CX" => "Citología",
            "RL" => "Revisión de laminillas",
            _ => "",
        };
    }

    private static string NormalizarModalidad(string? textoCrudo)
    {
        var t = SinAcentos(textoCrudo);
        t = ReEspacios().Replace(t, " ").Trim();
        if (t.Length == 0) return "";
        if (ReModLaminillas().IsMatch(t)) return "Revisión de laminillas";
        if (ReModTrans().IsMatch(t)) return "Transoperatorio";
        if (ReModIntra().IsMatch(t)) return "Transoperatorio";
        if (ReModQuir().IsMatch(t)) return "Quirúrgico";
        if (ReModPieza().IsMatch(t)) return "Quirúrgico";
        if (ReModCito().IsMatch(t)) return "Citología";
        if (ReModBiops().IsMatch(t)) return "Biopsia";
        return "";
    }

    private static bool EsPalabraNombre(string tok)
    {
        var t = NormToken(tok);
        if (t.Length < 2) return false;
        if (t.All(char.IsDigit) || StopNombre.Contains(t)) return false;
        if (ReNss().IsMatch(t) && ReNss().Match(t).Length == t.Length) return false;
        if (ReAgregado().IsMatch(t) && ReAgregado().Match(t).Length == t.Length) return false;
        if (!t.Any(c => (c >= 'A' && c <= 'Z') || "ÁÉÍÓÚÑÜ".Contains(c))) return false;
        if (t.Any(char.IsDigit) && t.Any(c => c >= 'A' && c <= 'Z')) return false;
        return true;
    }

    private static bool EsTokenNombreOParticula(string tok) =>
        EsPalabraNombre(tok) || Particulas.Contains(NormToken(tok));

    private static string Cabecera(string texto, int porDefecto)
    {
        var corte = ReCorteMacro().Match(texto);
        return corte.Success ? texto[..corte.Index] : texto[..Math.Min(texto.Length, porDefecto)];
    }

    // ---------------------------------------------------------- extracción por columnas
    private static (string Nombre, string Nss, string Agregado) PacientePorColumnas(IReadOnlyList<PlWord> words)
    {
        if (words.Count == 0) return ("", "", "");
        PlWord? hNom = null, hNss = null, hAgr = null, hGen = null;
        foreach (var w in words)
        {
            var key = SinAcentos(w.Text).Replace(".", "");
            if (key == "nombre" && hNom is null) hNom = w;
            else if (key == "nss" && hNss is null) hNss = w;
            else if (key.StartsWith("agregado") && hAgr is null) hAgr = w;
            else if (key.StartsWith("gener") && hGen is null) hGen = w;
        }
        if (hNom is null || hNss is null) return ("", "", "");

        var yEtiqueta = hNom.Top;
        var y0 = yEtiqueta - 45;
        var y1 = yEtiqueta + 8;

        double xNomIzq = hNom.X0 - 20, xNomDer = hNss.X0 - 2;
        double xNssIzq = hNss.X0 - 5;
        double xNssDer = hAgr is not null ? hAgr.X0 - 2 : hNss.X1 + 90;
        double xAgrIzq = hAgr is not null ? hAgr.X0 - 5 : xNssDer;
        double xAgrDer = hGen is not null ? hGen.X0 - 2 : xAgrIzq + 120;

        var tokensNombre = new List<(double Top, double X0, string Txt)>();
        var nss = ""; var agregado = "";
        foreach (var w in words)
        {
            if (w.Top < y0 || w.Top > y1) continue;
            var txt = w.Text.Trim();
            if (SinAcentos(txt) is "nombre" or "nss" or "agregado" or "medico" or "genero" or "edad") continue;
            if (w.Cx >= xNomIzq && w.Cx <= xNomDer)
            {
                if (EsPalabraNombre(txt) || Particulas.Contains(NormToken(txt)))
                    tokensNombre.Add((w.Top, w.X0, txt));
            }
            else if (w.Cx >= xNssIzq && w.Cx <= xNssDer && nss.Length == 0)
            {
                var m = ReNss().Match(txt);
                if (m.Success) nss = m.Groups[1].Value;
            }
            else if (w.Cx >= xAgrIzq && w.Cx <= xAgrDer && agregado.Length == 0)
            {
                var m = ReAgregado().Match(txt);
                if (m.Success) agregado = m.Groups[1].Value.ToUpperInvariant();
            }
        }
        var nombre = string.Join(" ", tokensNombre
            .OrderBy(t => Math.Round(t.Top, 1)).ThenBy(t => t.X0).Select(t => t.Txt));
        return (ReEspacios().Replace(nombre, " ").Trim(), nss, agregado);
    }

    // ---------------------------------------------------------- extracción por texto
    private static (string Nombre, string Nss, string Agregado) PacientePorTexto(string texto)
    {
        var cabeza = Cabecera(texto, 1500);
        var lineas = cabeza.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var nombre = ""; var nss = ""; var agregado = "";

        for (var i = 0; i < lineas.Count; i++)
        {
            var ln = lineas[i];
            var mNss = ReNss().Match(ln);
            if (!mNss.Success) continue;
            nss = mNss.Groups[1].Value;
            foreach (var frag in new[] { ln, i > 0 ? lineas[i - 1] : "", i + 1 < lineas.Count ? lineas[i + 1] : "" })
            {
                var mAgr = ReAgregado().Match(frag);
                if (mAgr.Success) { agregado = ReEspacios().Replace(mAgr.Groups[1].Value, "").ToUpperInvariant(); break; }
            }

            var izq = ln[..mNss.Index].Trim();
            var partes = new List<string>();
            if (izq.Length > 0)
            {
                var toks = izq.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(EsTokenNombreOParticula).ToList();
                if (toks.Count > 0) partes.Add(string.Join(" ", toks));
            }
            if (i > 0)
            {
                var prev = lineas[i - 1];
                if (!ReNss().IsMatch(prev) && !ReLineaNoNombre().IsMatch(prev))
                {
                    var toks = prev.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(EsTokenNombreOParticula).ToList();
                    if (toks.Count > 0) partes.Insert(0, string.Join(" ", toks));
                }
            }
            if (i + 1 < lineas.Count)
            {
                var sig = lineas[i + 1];
                var mNom = ReNombre().Match(sig);
                if (mNom.Success)
                {
                    var antes = sig[..mNom.Index];
                    var toks = antes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(EsTokenNombreOParticula).ToList();
                    if (toks.Count > 0) partes.Add(string.Join(" ", toks));
                }
            }
            nombre = string.Join(" ", partes);
            if (nss.Length > 0) break;
        }

        if (nss.Length == 0) { var m = ReNss().Match(cabeza); if (m.Success) nss = m.Groups[1].Value; }
        if (agregado.Length == 0) { var m = ReAgregado().Match(cabeza); if (m.Success) agregado = ReEspacios().Replace(m.Groups[1].Value, "").ToUpperInvariant(); }
        return (ReEspacios().Replace(nombre, " ").Trim(), nss, agregado);
    }

    private static (string Nombre, string Nss, string Agregado) PacienteGineco(string texto)
    {
        var cabeza = Cabecera(texto, 2000);
        var lineas = cabeza.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var nombre = ""; var nss = ""; var agregado = "";

        for (var i = 0; i < lineas.Count && nombre.Length == 0; i++)
        {
            var ln = lineas[i];
            var mEt = ReNombreCompleto().Match(ln);
            if (!mEt.Success) continue;
            var candidatas = new List<string>();
            if (i > 0) candidatas.Add(lineas[i - 1]);
            // También la siguiente si no es otra etiqueta
            if (i + 1 < lineas.Count && !ReEtiquetaGineco().IsMatch(lineas[i + 1])) candidatas.Add(lineas[i + 1]);
            foreach (var cand in candidatas)
            {
                var toks = cand.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(EsTokenNombreOParticula).ToList();
                if (toks.Count >= 2) { nombre = string.Join(" ", toks); break; }
            }
        }

        for (var i = 0; i < lineas.Count; i++)
        {
            var mNss = ReNss().Match(lineas[i]);
            if (!mNss.Success) continue;
            var contexto = string.Join(" ", lineas.Skip(Math.Max(0, i - 1)).Take(3));
            if (ReContextoNss().IsMatch(contexto) || nss.Length == 0)
            {
                nss = mNss.Groups[1].Value;
                foreach (var frag in new[] { lineas[i], i > 0 ? lineas[i - 1] : "", i + 1 < lineas.Count ? lineas[i + 1] : "" })
                {
                    var mAgr = ReAgregado().Match(frag);
                    if (mAgr.Success) { agregado = ReEspacios().Replace(mAgr.Groups[1].Value, "").ToUpperInvariant(); break; }
                }
                if (nss.Length > 0 && (agregado.Length > 0 || ReContextoNss().IsMatch(contexto))) break;
            }
        }

        if (nss.Length == 0) { var m = ReNss().Match(cabeza); if (m.Success) nss = m.Groups[1].Value; }
        if (agregado.Length == 0) { var m = ReAgregado().Match(cabeza); if (m.Success) agregado = ReEspacios().Replace(m.Groups[1].Value, "").ToUpperInvariant(); }

        if (nombre.Length > 0)
        {
            nombre = ReBasuraInicio().Replace(nombre, "").Trim();
            nombre = string.Join(" ", nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(EsTokenNombreOParticula));
        }
        return (ReEspacios().Replace(nombre, " ").Trim(), nss, agregado);
    }

    // ---------------------------------------------------------- modalidad
    private static string ModalidadPorPosicion(IReadOnlyList<PlWord> words)
    {
        Dictionary<string, PlWord> etiquetas = new();
        foreach (var w in words)
        {
            var key = SinAcentos(w.Text.TrimEnd(':'));
            if (key is "estudio" or "modalidad" or "fecha" && !etiquetas.ContainsKey(key)) etiquetas[key] = w;
        }
        if (!etiquetas.TryGetValue("modalidad", out var h)) return "";
        double izq, der;
        if (etiquetas.ContainsKey("estudio") && etiquetas.ContainsKey("fecha"))
        {
            izq = (etiquetas["estudio"].Cx + h.Cx) / 2;
            der = (h.Cx + etiquetas["fecha"].Cx) / 2;
        }
        else { izq = h.X0 - 40; der = h.X1 + 40; }
        var candidatas = words
            .Where(w => w.Cx >= izq && w.Cx <= der && w.Bottom >= h.Top - 40 && w.Bottom <= h.Top + 2
                        && SinAcentos(w.Text) is not ("modalidad" or "estudio" or "fecha"))
            .OrderBy(w => Math.Round(w.Top)).ThenBy(w => w.X0)
            .ToList();
        return string.Join(" ", candidatas.Select(w => w.Text)).Trim();
    }

    private static string ModalidadPorTexto(string texto)
    {
        var cabeza = Cabecera(texto, 1800);
        var m = ReModExplicita().Match(cabeza);
        if (m.Success) return m.Groups[1].Value;
        var m2 = ReModEtiqueta().Match(cabeza);
        return m2.Success ? m2.Groups[1].Value.Trim() : "";
    }

    // ---------------------------------------------------------- estudio (designación completa)
    private static string EstudioPorTexto(string texto)
    {
        var t = ReEspacios().Replace(texto ?? "", " ").Trim();
        var inicio = ReDatosEstudio().Match(t);
        if (!inicio.Success) return "";
        var desde = inicio.Index + inicio.Length;
        var etiquetas = ReEtiquetasEstudio().Match(t, desde);
        if (!etiquetas.Success) return "";

        // PdfPig concatena a veces el valor de Estudio, la Modalidad y la Fecha sin espacios.
        // La última modalidad del bloque es la columna Modalidad; todo lo anterior es Estudio.
        var bloque = t[desde..etiquetas.Index].Trim();
        var modalidades = ReModDesignacion().Matches(bloque);
        if (modalidades.Count == 0) return "";
        var ultima = modalidades[modalidades.Count - 1];
        return LimpiarEstudio(bloque[..ultima.Index]);
    }

    private static string EstudioPorPosicion(IReadOnlyList<PlWord> words)
    {
        var modalidades = words.Where(w => SinAcentos(w.Text.TrimEnd(':')) == "modalidad").ToList();
        if (modalidades.Count == 0) return "";
        var modalidad = modalidades.FirstOrDefault(w => EstaBajoDatosDelEstudio(words, w));
        if (modalidad is null) return "";
        var estudio = words.Where(w => SinAcentos(w.Text.TrimEnd(':')) == "estudio"
                                       && w.X0 < modalidad.X0 && MismaFila(w, modalidad))
            .OrderBy(w => Math.Abs(w.Top - modalidad.Top)).FirstOrDefault();
        if (estudio is null) return "";
        var fecha = words.Where(w => SinAcentos(w.Text.TrimEnd(':')) == "fecha"
                                     && w.X0 > modalidad.X0 && MismaFila(w, modalidad))
            .OrderBy(w => Math.Abs(w.Top - modalidad.Top)).FirstOrDefault();

        var izquierda = estudio.X0 - 100;
        var derecha = fecha is not null ? (modalidad.Cx + fecha.Cx) / 2 : (estudio.Cx + modalidad.Cx) / 2;
        var candidatas = words
            .Where(w => w.Cx >= izquierda && w.Cx <= derecha
                        && w.Bottom >= estudio.Top - 36 && w.Bottom <= estudio.Top + 2
                        && SinAcentos(w.Text.TrimEnd(':')) is not ("estudio" or "modalidad" or "fecha"))
            .OrderBy(w => Math.Round(w.Top, 1)).ThenBy(w => w.X0)
            .Select(w => w.Text);
        return LimpiarEstudio(string.Join(" ", candidatas));
    }

    private static bool MismaFila(PlWord a, PlWord b) =>
        Math.Abs(a.Top - b.Top) <= 6 && Math.Abs(a.Bottom - b.Bottom) <= 6;

    private static bool EstaBajoDatosDelEstudio(IReadOnlyList<PlWord> words, PlWord modalidad)
    {
        for (var i = 0; i + 2 < words.Count; i++)
        {
            if (SinAcentos(words[i].Text.TrimEnd(':')) != "datos" ||
                SinAcentos(words[i + 1].Text.TrimEnd(':')) != "del" ||
                SinAcentos(words[i + 2].Text.TrimEnd(':')) != "estudio") continue;
            var cabecera = words[i];
            return modalidad.Top >= cabecera.Top - 2 && modalidad.Top - cabecera.Top <= 120;
        }
        return false;
    }

    private static string LimpiarEstudio(string? valor)
    {
        var texto = ReEspacios().Replace(valor ?? "", " ").Trim().Trim('.', ';', ':', '-', '–', '—');
        if (texto.Length == 0 || texto.Length > 255) return "";
        var normalizado = SinAcentos(texto);
        if (normalizado.Contains("DATOS DEL", StringComparison.Ordinal) ||
            normalizado.Contains("AGREGADO", StringComparison.Ordinal) ||
            normalizado.Contains("PACIENTE", StringComparison.Ordinal) ||
            normalizado.Contains("MODALIDAD", StringComparison.Ordinal) ||
            normalizado.Contains("FECHA", StringComparison.Ordinal))
            return "";
        return texto;
    }

    // ---------------------------------------------------------- folio desde nombre de archivo
    public static string FolioDesdeNombreArchivo(string? nombreArchivo)
    {
        if (string.IsNullOrEmpty(nombreArchivo)) return "";
        var base_ = ReExtPdf().Replace(nombreArchivo, "");
        base_ = base_.Replace('–', '-').Replace('—', '-');
        var todos = ReFolioSuelto().Matches(base_).Select(m => m.Groups[1].Value).ToList();
        if (todos.Count == 0)
            todos = ReFolioPermisivo().Matches(base_).Select(m => m.Groups[1].Value).ToList();
        if (todos.Count == 0) return "";
        todos = todos.Select(f => f.ToUpperInvariant()).ToList();
        foreach (var pref in new[] { "RL-", "B-", "Q-", "C-", "T-" })
            foreach (var f in todos)
                if (f.StartsWith(pref)) return f;
        return todos[^1];
    }

    // ---------------------------------------------------------- entrada principal
    public static CamposPatologia Extraer(byte[] pdf, string? nombreArchivo = null)
    {
        using var doc = PdfDocument.Open(pdf);
        if (doc.NumberOfPages == 0) throw new InvalidOperationException("El PDF no tiene páginas");
        var page = doc.GetPage(1);
        var texto = page.Text ?? "";
        var words = page.GetWords()
            .Select(w => new PlWord(w.Text, w.BoundingBox.Left, w.BoundingBox.Right,
                                    page.Height - w.BoundingBox.Top, page.Height - w.BoundingBox.Bottom))
            .ToList();
        var textoComp = CompactarEspaciado(texto);
        var esGineco = ReGineco().IsMatch(texto);
        var modalidadCruda = ModalidadPorPosicion(words);
        if (modalidadCruda.Length == 0) modalidadCruda = ModalidadPorTexto(texto);

        string nombre, nss, agregado;
        if (esGineco)
        {
            (nombre, nss, agregado) = PacienteGineco(texto);
            if (nombre.Length == 0 || nss.Length == 0)
            {
                var (nCol, nsCol, aCol) = PacientePorColumnas(words);
                var (n2, ns2, a2) = PacientePorTexto(texto);
                if (nombre.Length == 0) nombre = nCol.Length > 0 ? nCol : n2;
                if (nss.Length == 0) nss = nsCol.Length > 0 ? nsCol : ns2;
                if (agregado.Length == 0) agregado = aCol.Length > 0 ? aCol : a2;
            }
        }
        else
        {
            (nombre, nss, agregado) = PacientePorColumnas(words);
            var (n2, ns2, a2) = PacientePorTexto(texto);
            if (n2.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)
                nombre = n2;
            if (nombre.Length == 0) nombre = n2;
            if (nss.Length == 0) nss = ns2;
            if (agregado.Length == 0) agregado = a2;
        }

        if (nss.Length == 0) { var m = ReNss().Match(textoComp); if (m.Success) nss = m.Groups[1].Value; }
        if (agregado.Length == 0) { var m = ReAgregado().Match(textoComp); if (m.Success) agregado = ReEspacios().Replace(m.Groups[1].Value, "").ToUpperInvariant(); }
        if (nombre.Length == 0)
        {
            var (n2, ns2, a2) = esGineco ? PacienteGineco(textoComp) : PacientePorTexto(textoComp);
            nombre = n2;
            if (nss.Length == 0) nss = ns2;
            if (agregado.Length == 0) agregado = a2;
        }

        var cabeza = Cabecera(texto, 1200);
        var mFolio = ReFolio().Match(cabeza);
        var folio = mFolio.Success ? mFolio.Groups[1].Value.ToUpperInvariant()
            : ReFolioSuelto().Match(cabeza) is { Success: true } ms ? ms.Groups[1].Value.ToUpperInvariant() : "";
        var folioArch = FolioDesdeNombreArchivo(nombreArchivo);
        if (folioArch.Length > 0)
        {
            if (folio.Length == 0) folio = folioArch;
            else if (folioArch.StartsWith("RL") && !folio.StartsWith("RL")) folio = folioArch;
            else if (folioArch == folio || (folio.Length > 0 && folioArch.StartsWith(folio.Contains('-') ? folio.Split('-')[0] : folio)))
                folio = folioArch.Length >= folio.Length ? folioArch : folio;
        }
        if (folio.Length > 0) folio = NormalizarFolio(folio);

        var modalidad = NormalizarModalidad(modalidadCruda);
        if (modalidad.Length == 0) modalidad = NormalizarModalidad(Cabecera(texto, 1800));

        var modalidadFolio = ModalidadPorFolio(folio);
        if (modalidadFolio.Length > 0) modalidad = modalidadFolio;
        else if (folio.Length > 0 && char.IsDigit(folio[0])) modalidad = "Otro";
        else if (modalidad.Length == 0) modalidad = NormalizarModalidad(texto);
        if (modalidad.Length == 0) modalidad = "Otro";

        if (nombre.Length > 0)
        {
            nombre = ReBasura().Replace(nombre, " ");
            nombre = string.Join(" ", nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(EsTokenNombreOParticula));
        }

        var fecha = FechaAIso(texto);
        if (fecha.Length == 0) fecha = FechaAIso(textoComp);

        var estudio = EstudioPorTexto(texto);
        if (estudio.Length == 0) estudio = EstudioPorPosicion(words);
        if (estudio.Length == 0) estudio = PatoEstudio.EstudioOtros;

        return new CamposPatologia(
            folio,
            fecha,
            ReEspacios().Replace(nombre, " ").Trim(),
            nss,
            ReEspacios().Replace(agregado, "").ToUpperInvariant(),
            modalidad,
            estudio);
    }
}
