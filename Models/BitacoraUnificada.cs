using Meditechnosys.Intranet.Services;

namespace Meditechnosys.Intranet.Models;

/// <summary>
/// Un renglón de la bitácora unificada: la misma forma para lo que pasó en la intranet (inicios de
/// sesión, páginas abiertas, altas y bajas) y para lo que pasó en el programa del quemador DICOM
/// (búsquedas, descargas y grabaciones en USB o CD).
///
/// Lo que se guarda en cada almacén no cambia —son tablas distintas con columnas distintas—; aquí lo
/// que se hace es traducir cada fila original a seis datos comunes: <see cref="Actividad"/> (una
/// palabra legible: «Inicio de sesión», «Intento de sesión fallido», «Edición», «Quemado en USB»),
/// <see cref="Ip"/>, <see cref="Ubicacion"/> y <see cref="Detalle"/> (qué hizo exactamente, en frase:
/// «buscó estudios del paciente PEREZ JUAN», «descargó el estudio MH-2026-11 · 296 imágenes»).
///
/// <see cref="Equipo"/>, <see cref="Ubicacion"/>, <see cref="FuenteUbicacion"/>,
/// <see cref="PrecisionMetros"/> y <see cref="Coordenadas"/> no viven en ninguna de las dos tablas:
/// los calcula <c>UbicacionResolver</c> al consultar, para que corregir un rango de IP acomode también
/// los renglones viejos. Por eso son <c>init</c> y empiezan vacíos.
/// </summary>
public record Movimiento(
    /// <summary>De qué bitácora viene la fila: «Intranet» o «Quemador DICOM».</summary>
    string Origen,
    long Id,
    /// <summary>Siempre en UTC; la pantalla y el Excel lo muestran en hora local.</summary>
    DateTime Momento,
    string Usuario,
    /// <summary>Etiqueta corta que va en la columna Actividad.</summary>
    string Actividad,
    /// <summary>Familia de la actividad, para el desplegable de filtros: sesión, visita, consulta,
    /// descarga, quemado, modificación.</summary>
    string Grupo,
    /// <summary> Módulo de la intranet donde ocurrió (Visor DICOM, Usuarios…) o «Quemador DICOM».</summary>
    string Modulo,
    /// <summary>«Qué fue lo que hizo», en frase legible.</summary>
    string Detalle,
    string Ip,
    /// <summary>Direcciones que la propia máquina declaró (solo el quemador las reporta).</summary>
    string IpLocal,
    string Equipo,
    string Ubicacion,
    string FuenteUbicacion,
    int PrecisionMetros,
    string Coordenadas,
    string Dispositivo,
    /// <summary>«éxito», «parcial», «error» o «cancelado». Vacío cuando la fila no lo reporta.</summary>
    string Resultado,
    /// <summary>Número de lote del quemador: las filas que generó una misma acción.</summary>
    string Lote,
    string Paciente,
    string PacienteId,
    string Acceso,
    string EstudioUid)
{
    public string Fecha => Momento.ToLocalTime().ToString("dd/MM/yyyy");
    public string Hora => Momento.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>La ubicación tal y como se escribe en el Excel: punto, margen y de dónde salió.</summary>
    public string UbicacionCompleta =>
        (Ubicacion.Length > 0 ? Ubicacion : "Sin confirmar") +
        (FuenteUbicacion.Length == 0 ? ""
            : PrecisionMetros > 0 ? $" — {FuenteUbicacion} (±{PrecisionMetros} m)"
            : $" — {FuenteUbicacion}");

    /// <summary>Todo el texto libre de la fila, para la caja de búsqueda y para «donde».</summary>
    public string TextoBuscable => string.Join(' ',
        Ip, IpLocal, Equipo, Ubicacion, Dispositivo, Usuario, Actividad, Modulo, Detalle,
        Paciente, PacienteId, Acceso, EstudioUid, Unidad, Medio, Lote, Resultado);

    /// <summary>Medio y unidad del quemador (USB, grabadora, letra de la unidad). Vacíos en la intranet.</summary>
    public string Unidad { get; init; } = "";
    public string Medio { get; init; } = "";
}

/// <summary>
/// Traduce filas de cada bitácora a <see cref="Movimiento"/>. Las reglas están en un solo lugar para
/// que la pantalla, la búsqueda y el Excel digan exactamente lo mismo de un mismo renglón.
/// </summary>
public static class Movimientos
{
    public const string OrigenWeb = "Intranet";
    public const string OrigenQuemador = "Quemador DICOM";

    public static readonly IReadOnlyList<string> Origenes = new[] { OrigenWeb, OrigenQuemador };

    // Familias de actividad (valores de Movimiento.Grupo).
    public const string Sesion = "sesión";
    public const string Visita = "visita";
    public const string Consulta = "consulta";
    public const string Descarga = "descarga";
    public const string Quemado = "quemado";
    public const string Modificacion = "modificación";

    /// <summary>En el mismo orden que se muestran en el desplegable de la pantalla.</summary>
    public static readonly IReadOnlyList<string> Grupos = new[]
        { Sesion, Visita, Consulta, Descarga, Quemado, Modificacion };

    /// <summary>Etiqueta común para los intentos de entrada con credenciales incorrectas, vengan de
    /// la web o del programa del mostrador. Es lo que el administrador busca cuando alguien «le falló
    /// la contraseña».</summary>
    public const string IntentoFallido = "Intento de sesión fallido";

    /// <summary>
    /// Renglón de la bitácora de accesos. `Equipo`, `Ubicacion` y su fuente quedan vacíos: los rellena
    /// el enriquecedor al consultar.
    /// </summary>
    public static Movimiento DesdeWeb(AuditEntry e)
    {
        var (actividad, grupo, modulo, detalle, resultado) = DesglosarWeb(e);
        return new Movimiento(OrigenWeb, e.Id, e.Momento, e.Usuario, actividad, grupo, modulo, detalle,
            e.Ip, "", e.Equipo, e.Ubicacion, e.FuenteUbicacion, e.PrecisionMetros, "", e.Dispositivo,
            resultado, "", "", "", "", "")
        {
            Unidad = "",
            Medio = "",
        };
    }

    /// <summary>Renglón de la bitácora del quemador: una fila por estudio tocado.</summary>
    public static Movimiento DesdeQuemador(QuemadorEvento q)
    {
        var (actividad, grupo) = ActividadDeQuemador(q.Evento);
        var estudio = Estudio(q);
        var detalle = actividad switch
        {
            _ when q.Evento == QuemadorEventos.InicioSesion =>
                "Entró al programa del quemador" + (q.Mensaje.Length > 0 ? $" — {q.Mensaje}" : ""),
            _ when q.Evento == QuemadorEventos.IntentoFallido =>
                "Intento de entrada al programa del quemador con usuario o contraseña incorrectos" +
                (q.Mensaje.Length > 0 ? $" — {q.Mensaje}" : ""),
            _ when q.Evento == QuemadorEventos.CierreSesion =>
                "Salió del programa del quemador" + (q.Mensaje.Length > 0 ? $" — {q.Mensaje}" : ""),
            _ when q.Evento == QuemadorEventos.Busqueda =>
                "Buscó en el PACS " + estudio + (q.Imagines > 0 ? $" · {q.Imagines} estudio(s) o imagen(es)" : ""),
            _ when q.Evento == QuemadorEventos.Descarga =>
                "Descargó del PACS " + estudio + $" · {q.Imagines} imagen(es)",
            _ => "Quemó " + estudio + " en " + (Medio(q).Length > 0 ? Medio(q) : "medio externo") +
                 $" · {q.Imagines} imagen(es)" + (q.Mensaje.Length > 0 ? $" — {q.Mensaje}" : ""),
        };

        return new Movimiento(OrigenQuemador, q.Id, q.Momento, q.Usuario, actividad, grupo, OrigenQuemador,
            detalle, q.Ip, q.IpLocal, q.Equipo, "", "", 0, "", "",
            q.Resultado, q.Lote, q.Paciente, q.PacienteId, q.Acceso, q.EstudioUid)
        {
            Unidad = q.Unidad,
            Medio = q.Medio,
        };
    }

    /// <summary>«PEREZ JUAN» (ID 1234 · Acceso MH-2026-11 · US 2026-10-30). Vacío si la fila no trae estudio.</summary>
    private static string Estudio(QuemadorEvento q)
    {
        var nombre = q.Paciente.Length > 0 ? $"«{q.Paciente}»"
            : q.PacienteId.Length > 0 ? $"ID {q.PacienteId}"
            : q.Acceso.Length > 0 ? $"acceso {q.Acceso}"
            : q.EstudioUid.Length > 0 ? $"UID {Recortar(q.EstudioUid, 40)}"
            : q.Mensaje.Length > 0 ? q.Mensaje : "un estudio sin identificación";
        var extras = new List<string>();
        if (q.PacienteId.Length > 0 && q.Paciente.Length > 0) extras.Add("ID " + q.PacienteId);
        if (q.Acceso.Length > 0) extras.Add("Acceso " + q.Acceso);
        if (q.Modalidad.Length > 0 || q.FechaEstudio.Length > 0)
            extras.Add(string.Join(' ', new[] { q.Modalidad, q.FechaEstudio }.Where(x => x.Length > 0)));
        if (q.EstudioUid.Length > 0 && extras.Count == 0) extras.Add("UID " + Recortar(q.EstudioUid, 40));
        return extras.Count == 0 ? nombre : nombre + " (" + string.Join(" · ", extras) + ")";
    }

    private static string Medio(QuemadorEvento q) =>
        string.Join(' ', new[] { q.Medio, q.Unidad }.Where(x => x.Length > 0)).Trim();

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>Nombre de actividad y familia que reporta el programa del mostrador.</summary>
    public static (string Actividad, string Grupo) ActividadDeQuemador(string evento) => evento switch
    {
        QuemadorEventos.InicioSesion => (AuditEvents.Login, Sesion),
        QuemadorEventos.IntentoFallido => (IntentoFallido, Sesion),
        QuemadorEventos.CierreSesion => (AuditEvents.Logout, Sesion),
        QuemadorEventos.Busqueda => ("Búsqueda de estudios", Consulta),
        QuemadorEventos.Descarga => ("Descarga de estudios", Descarga),
        QuemadorEventos.GrabacionUsb => ("Quemado en USB", Quemado),
        QuemadorEventos.GrabacionCd => ("Quemado en CD/DVD", Quemado),
        _ => (evento.Length > 0 ? evento : "Movimiento del quemador", Modificacion),
    };

    /// <summary>
    /// Descompone un renglón de la bitácora de accesos. Los eventos con nombre («Alta de usuario»,
    /// «Cambio de permisos»…) ya son una actividad legible, así que se muestran tal cual y el detalle
    /// queda en lo que el sistema anotó después. Los renglones de «Acceso» —uno por petición— sí
    /// necesitan traducirse: llevan «GET /api/dicom/studies?patientId=X → 200».
    /// </summary>
    public static (string Actividad, string Grupo, string Modulo, string Detalle, string Resultado)
        DesglosarWeb(AuditEntry e)
    {
        switch (e.Evento)
        {
            case AuditEvents.Login:
                return (AuditEvents.Login, Sesion, ModuloDe(""),
                    "Entró a la intranet" + (EsApi(e.Detalle) ? " (por API)" : ""), "éxito");

            case AuditEvents.LoginFallido:
                return (IntentoFallido, Sesion, ModuloDe(""),
                    "La contraseña o el usuario no coincidieron" + (EsApi(e.Detalle) ? " (por API)" : ""),
                    "error");

            case AuditEvents.Logout:
                return (AuditEvents.Logout, Sesion, ModuloDe(""), "Cerró la sesión", "éxito");

            case AuditEvents.ExportarExcel:
                return ("Descarga de Excel", Descarga, ModuloDe(""),
                    Recorta(e.Detalle).Length > 0 ? e.Detalle : "Descargó un Excel", "éxito");

            case AuditEvents.Acceso:
                var (ruta, metodo, estado, query) = Petición(e.Detalle);
                var modulo = ModuloDe(ruta);
                if (EsBusquedaDicom(ruta, metodo))
                {
                    var pid = Parametro(query, "patientId") ?? "";
                    if (pid.Length == 0) pid = Parametro(query, "patientName") ?? "";
                    return ("Búsqueda de estudios", Consulta, modulo,
                        "Buscó estudios" + (pid.Length > 0 ? $" del paciente «{pid}»" : " en el visor DICOM"),
                        Estado(estado));
                }
                if (DescargaDicom(ruta))
                    return ("Descarga de estudios", Descarga, modulo, "Descargó estudios del visor DICOM",
                        Estado(estado));
                if (EsConsultaReportes(ruta, metodo))
                    return ("Consulta de reportes", Consulta, modulo, "Consultó reportes de Patología",
                        Estado(estado));
                if (metodo == "GET")
                    return ("Abrió una página", Visita, modulo, "Abrió " + ruta, Estado(estado));
                if (metodo == "POST")
                    return ("Creación", Modificacion, modulo, "Creó o envió " + ruta, Estado(estado));
                if (metodo == "PUT" || metodo == "PATCH")
                    return ("Edición", Modificacion, modulo, "Editó " + ruta, Estado(estado));
                if (metodo == "DELETE")
                    return ("Baja", Modificacion, modulo, "Eliminó " + ruta, Estado(estado));
                return ("Acceso", Visita, modulo, metodo + " " + ruta, Estado(estado));

            default:
                // Evento con nombre propio (Alta de usuario, Cambio de permisos, Edición de actividad…).
                return (Recorta(e.Evento).Length > 0 ? e.Evento : "Movimiento",
                        GrupoDeEvento(e.Evento), ModuloDe(""),
                        Recorta(e.Detalle).Length > 0 ? e.Detalle : e.Evento, "éxito");
        }
    }

    /// <summary>«API» cuando el inicio de sesión vino de /api/login; «Web» (o vacío) si no.</summary>
    private static bool EsApi(string detalle) =>
        detalle.Trim().Equals("API", StringComparison.OrdinalIgnoreCase);

    /// <summary>«POST /api/x?y=1 → 200» en sus cuatro piezas. Tolera renglones antiguos incompletos.</summary>
    private static (string Ruta, string Metodo, string Estado, string Query) Petición(string detalle)
    {
        var resto = (detalle ?? "").Trim();
        var flecha = resto.IndexOf(" → ", StringComparison.Ordinal);
        var estado = "";
        if (flecha >= 0)
        {
            estado = resto[(flecha + 3)..].Trim();
            resto = resto[..flecha].Trim();
        }
        var espacio = resto.IndexOf(' ');
        var metodo = espacio > 0 ? resto[..espacio].ToUpperInvariant() : "";
        var objetivo = espacio > 0 ? resto[(espacio + 1)..] : resto;
        var interrogacion = objetivo.IndexOf('?');
        var ruta = interrogacion >= 0 ? objetivo[..interrogacion] : objetivo;
        var query = interrogacion >= 0 ? objetivo[interrogacion..] : "";
        return (ruta, metodo, estado, query);
    }

    private static string? Parametro(string query, string nombre)
    {
        foreach (var par in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var igual = par.IndexOf('=');
            if (igual <= 0) continue;
            if (!par[..igual].Equals(nombre, StringComparison.OrdinalIgnoreCase)) continue;
            try { return Uri.UnescapeDataString(par[(igual + 1)..].Replace('+', ' ')); }
            catch { return par[(igual + 1)..]; }
        }
        return null;
    }

    private static bool EsBusquedaDicom(string ruta, string metodo) =>
        metodo == "GET" && (ruta.StartsWith("/api/dicom/studies", StringComparison.OrdinalIgnoreCase) ||
                             ruta.StartsWith("/dicom/studies", StringComparison.OrdinalIgnoreCase));

    private static bool DescargaDicom(string ruta) =>
        ruta.StartsWith("/dicom", StringComparison.OrdinalIgnoreCase) &&
        ruta.Contains("download", StringComparison.OrdinalIgnoreCase);

    private static bool EsConsultaReportes(string ruta, string metodo) =>
        metodo == "GET" && (ruta.StartsWith("/api/patologia", StringComparison.OrdinalIgnoreCase) ||
                            ruta.StartsWith("/patologia", StringComparison.OrdinalIgnoreCase));

    /// <summary>Un HTTP 4xx o 5xx también es un tropiezo: se marca igual que un error del quemador.</summary>
    private static string Estado(string codigo) =>
        int.TryParse(codigo.Length > 0 ? codigo.Split(' ')[0] : "", out var n)
            ? (n >= 400 ? "error" : "éxito")
            : "";

    /// <summary>
    /// Familia de un evento con nombre. Todo lo que no sea entrar, salir, buscar o descargar es una
    /// modificación sobre algo, así que la regla es corta y no depende de tener registrada cada clave.
    /// </summary>
    public static string GrupoDeEvento(string evento) => evento switch
    {
        AuditEvents.Login or AuditEvents.LoginFallido or AuditEvents.Logout => Sesion,
        AuditEvents.Acceso => Visita,
        AuditEvents.ExportarExcel => Descarga,
        _ when Contiene(evento, "búsqueda", "busqueda", "consulta") => Consulta,
        _ when Contiene(evento, "sesión", "sesion") => Sesion,
        _ when Contiene(evento, "descarga", "grabación", "grabacion") => Descarga,
        _ => Modificacion,
    };

    private static bool Contiene(string s, params string[] posibles) =>
        posibles.Any(p => Texto.Contiene(s, p));

    /// <summary>Módulo donde ocurrió, a partir de la ruta (mismo reparto que usaba la pantalla de accesos).</summary>
    public static string ModuloDe(string ruta)
    {
        if (ruta.Length == 0) return "Intranet";
        if (ruta.StartsWith("/api/dicom", StringComparison.OrdinalIgnoreCase) ||
            ruta.StartsWith("/dicom", StringComparison.OrdinalIgnoreCase)) return "Visor DICOM";
        if (ruta.StartsWith("/api/patologia", StringComparison.OrdinalIgnoreCase) ||
            ruta.StartsWith("/patologia", StringComparison.OrdinalIgnoreCase)) return "Consulta de reportes";
        if (System.Text.RegularExpressions.Regex.IsMatch(ruta, @"^/(api/)?(actividades|inventario|playbook)") ||
            ruta.StartsWith("/gestion", StringComparison.OrdinalIgnoreCase)) return "Gestión";
        if (ruta.StartsWith("/calendario", StringComparison.OrdinalIgnoreCase)) return "Calendario";
        if (System.Text.RegularExpressions.Regex.IsMatch(ruta, @"^/(api/)?(actas|credenciales)") ||
            ruta.StartsWith("/admin/administrativos", StringComparison.OrdinalIgnoreCase)) return "Administrativos";
        if (System.Text.RegularExpressions.Regex.IsMatch(ruta, @"^/(api/)?usuarios")) return "Usuarios";
        if (System.Text.RegularExpressions.Regex.IsMatch(ruta, @"^/(api/)?(roles|config)")) return "Configuración";
        if (System.Text.RegularExpressions.Regex.IsMatch(ruta, @"^/(api/)?avisos")) return "Avisos";
        if (ruta.StartsWith("/api/bitacora", StringComparison.OrdinalIgnoreCase) ||
            ruta.StartsWith("/admin/bitacora", StringComparison.OrdinalIgnoreCase)) return "Bitácora";
        if (ruta.StartsWith("/api/quemador", StringComparison.OrdinalIgnoreCase) ||
            ruta.StartsWith("/admin/quemador", StringComparison.OrdinalIgnoreCase)) return "Quemador";
        if (ruta.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)) return "Administración";
        if (ruta == "/" || ruta.Length == 0) return "Inicio";
        return "Sistema";
    }

    private static string Recorta(string? s) => (s ?? "").Trim();
}
