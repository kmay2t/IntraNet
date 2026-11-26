using System.Text.Json;
using Meditechnosys.Intranet.Models;

namespace Meditechnosys.Intranet.Services;

/// Subopciones de una actividad: `Etiqueta` es el nombre con que se muestra la lista en el
/// formulario (p. ej. «Aplicativo» para una Capacitación) y `Opciones` sus valores.
public record SubopcionActividad(string Etiqueta, IReadOnlyList<string> Opciones);

/// Catálogos que edita la pantalla **Programador** y consumen los formularios del módulo Gestión:
/// las actividades que se eligen al crear un elemento, las subopciones de alguna de ellas (los
/// aplicativos de una Capacitación), los servicios, los hospitales y las regiones médicas con su
/// desglose de hospitales.
/// Se guardan como JSON en el almacén de configuración (`configuracion` en MySQL o
/// App_Data/configuracion.json), así que las actividades ya registradas no cambian de forma: lo
/// elegido se sigue guardando dentro del título.
public class GestionCatalogos(IConfigStore cfg)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// Máximo de elementos por catálogo y longitud de cada texto.
    public const int MaximoItems = 40;
    public const int LongitudMaxima = 80;

    /// Los hospitales se guardan en una columna de 10 caracteres (ver `Actividades`), así que los
    /// códigos nuevos que agregue el Programador no pueden pasar de ahí.
    public const int LongitudHospital = 10;

    // ---- lectura ----

    public IReadOnlyList<string> Actividades() => Lista(ConfigKeys.ActividadesLista);

    public IReadOnlyList<string> Servicios() => Lista(ConfigKeys.ServiciosLista);

    /// Servicios por hospital tal como los edita el Programador. Mientras el catálogo no se haya
    /// configurado (objeto vacío), se regresa la lista general repetida para cada hospital, para que
    /// los formularios sigan mostrando lo de siempre hasta que alguien lo particularice.
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ServiciosPorHospital()
    {
        var general = Servicios();
        var hospitales = HospitalesConfig();
        if (AnalizarServicios(cfg.Get(ConfigKeys.ServiciosPorHospital, "{}")) is not { } guardado || guardado.Count == 0)
            return hospitales.ToDictionary(h => h, _ => general, StringComparer.OrdinalIgnoreCase);

        var salida = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in hospitales)
            salida[h] = guardado.TryGetValue(h, out var lista) ? lista : Array.Empty<string>();
        // Hospitales que siguen en el catálogo guardado pero ya no están en la lista de hospitales:
        // se devuelven igual; así el Programador no pierde lo escrito si se recarga la pantalla.
        foreach (var (h, lista) in guardado)
            if (!salida.ContainsKey(h)) salida[h] = lista;
        return salida;
    }

    /// Los servicios de un hospital concreto (vacío si ese hospital no tiene lista propia y no hay
    /// catálogo general).
    public IReadOnlyList<string> ServiciosDe(string? hospital)
    {
        var h = Hospitales.Normalizar(hospital, permiteGeneral: true);
        if (h.Length == 0) return Array.Empty<string>();
        return ServiciosPorHospital().TryGetValue(h, out var lista) ? lista : Array.Empty<string>();
    }

    /// null si el JSON no se puede leer; el diccionario limpio (puede venir vacío) en otro caso.
    private IReadOnlyDictionary<string, IReadOnlyList<string>>? AnalizarServicios(string crudo)
    {
        try
        {
            var leido = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(crudo, Json);
            if (leido is null) return null;
            var salida = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (hospital, valores) in leido)
            {
                var h = Hospitales.Normalizar(hospital, permiteGeneral: true);
                if (h.Length == 0) continue;
                var lista = Alistar(valores);
                if (lista.Count == 0) continue;
                salida[h] = lista;
            }
            return salida;
        }
        catch
        {
            return null;
        }
    }

    /// Lista configurada de hospitales. Si el catálogo está vacío o roto se devuelven los cuatro
    /// de siempre, para que ningún formulario se quede sin opciones.
    public IReadOnlyList<string> HospitalesConfig()
    {
        var lista = Lista(ConfigKeys.HospitalesLista);
        return lista.Count > 0 ? lista : Hospitales.Todos;
    }

    // ---- regiones médicas ----

    /// Regiones médicas (p. ej. «CMN La Raza»). Vacío mientras el Programador no agregue ninguna:
    /// sin regiones, el diálogo + Nuevo no muestra el campo Región y se ve igual que siempre.
    public IReadOnlyList<string> Regiones() => Lista(ConfigKeys.RegionesLista);

    /// Desglose `región → hospitales` tal como lo edita el Programador. Se conservan también las
    /// regiones que ya no están en la lista de regiones, para que nadie pierda lo escrito al recargar
    /// la pantalla; al guardar la lista se recortan (ver `RecortarDesglose`).
    public IReadOnlyDictionary<string, IReadOnlyList<string>> HospitalesPorRegion()
    {
        var guardado = AnalizarRegiones(cfg.Get(ConfigKeys.HospitalesPorRegion, "{}"));
        var salida = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Regiones())
            salida[r] = guardado is not null && guardado.TryGetValue(r, out var lista) ? lista : Array.Empty<string>();
        if (guardado is not null)
            foreach (var (r, lista) in guardado)
                if (!salida.ContainsKey(r)) salida[r] = lista;
        return salida;
    }

    /// Hospital → región: es lo que consume cualquier formulario o consulta. La región **no** se
    /// guarda en el registro, se deriva del hospital, así que mover un hospital de región reordena
    /// también su histórico sin tocar un solo dato ya capturado.
    public IReadOnlyDictionary<string, string> RegionPorHospital()
    {
        var salida = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (region, hospitales) in HospitalesPorRegion())
            foreach (var h in hospitales)
                salida[h] = region;
        return salida;
    }

    /// Región de un hospital, o cadena vacía si no está asignado a ninguna («Sin región»).
    public string RegionDe(string? hospital)
    {
        var h = Hospitales.Normalizar(hospital);
        return h.Length > 0 && RegionPorHospital().TryGetValue(h, out var region) ? region : "";
    }

    /// Hospitales de una región (vacío si la región no existe o todavía no tiene desglose).
    public IReadOnlyList<string> HospitalesDe(string? region)
    {
        var r = NormalizarTexto(region);
        return r.Length > 0 && HospitalesPorRegion().TryGetValue(r, out var lista) ? lista : Array.Empty<string>();
    }

    // ---- región que ve cada usuario ----

    /// Lo que la pantalla **Usuarios** guarda por persona: sus regiones (lista vacía = sin región) y si puede
    /// ver todas. `Todas` gana sobre `Regiones`.
    public record RegionUsuario(IReadOnlyList<string> Regiones, bool Todas);

    /// El mapa `usuario → regiones/opción` tal como está guardado. Un JSON ilegible se lee como mapa
    /// vacío —nadie tiene región, todos ven todo— para no dejar la intranet sin pantallas.
    private IReadOnlyDictionary<string, RegionUsuario> RegionesPorUsuario()
    {
        var salida = new Dictionary<string, RegionUsuario>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var leido = JsonSerializer.Deserialize<Dictionary<string, RegionUsuarioCrudo>>(
                cfg.Get(ConfigKeys.RegionesPorUsuario, "{}"), Json);
            foreach (var (usuario, valor) in leido ?? new())
            {
                var u = NormalizarTexto(usuario);
                if (u.Length == 0 || valor is null) continue;
                var regiones = (valor.Regiones ?? new List<string>())
                    .Select(r => NormalizarTexto(r))
                    .Where(r => r.Length > 0)
                    .ToList();
                salida[u] = new RegionUsuario(regiones, valor.Todas);
            }
        }
        catch { }
        return salida;
    }

    /// Si el usuario ve todas las regiones. root siempre (es el superusuario y así se pidió); los
    /// demás cuando marcan la opción del perfil, cuando no tienen región asignada, o mientras el
    /// Programador no haya guardado ninguna región: sin desglose no hay nada que filtrar.
    public bool VeTodasRegiones(string? usuario)
    {
        var u = NormalizarTexto(usuario);
        if (u.Length == 0 || UserRules.IsRoot(u)) return true;
        if (Regiones().Count == 0) return true;
        if (!RegionesPorUsuario().TryGetValue(u, out var asignado)) return true;
        return asignado.Todas || asignado.Regiones.Count == 0;
    }

    /// Regiones asignadas al usuario, o lista vacía si ve todas las regiones.
    public IReadOnlyList<string> RegionesDeUsuario(string? usuario)
    {
        var u = NormalizarTexto(usuario);
        if (u.Length == 0 || UserRules.IsRoot(u) || VeTodasRegiones(u)) return Array.Empty<string>();
        return RegionesPorUsuario().TryGetValue(u, out var asignado) ? asignado.Regiones : Array.Empty<string>();
    }

    /// Región asignada al usuario (primera de la lista si hay múltiples), o cadena vacía si ve todas las regiones.
    /// Método legacy para compatibilidad con código existente.
    public string RegionDeUsuario(string? usuario)
    {
        var regiones = RegionesDeUsuario(usuario);
        return regiones.Count > 0 ? regiones[0] : "";
    }

    /// Hospitales que le tocan a este usuario: los del catálogo si ve todas; si no, los escritos en sus
    /// regiones. Si sus regiones todavía no tienen hospitales se devuelven todos, para no dejarlo sin
    /// opciones mientras se termina el desglose (la pantalla Usuarios lo avisa al asignarla).
    public IReadOnlyList<string> HospitalesPara(string? usuario)
    {
        if (VeTodasRegiones(usuario)) return HospitalesConfig();
        var regiones = RegionesDeUsuario(usuario);
        var hospitales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in regiones)
        {
            foreach (var h in HospitalesDe(r))
                hospitales.Add(h);
        }
        return hospitales.Count > 0 ? hospitales.ToList() : HospitalesConfig();
    }

    /// Región de la lista de hospitales que se le muestra al usuario, o vacía si ve todas. Sirve para
    /// que una sola lista de hospitales ya traiga su región en `/api/permisos/mios`.
    public string RegionDeLista(IReadOnlyList<string> hospitales)
    {
        foreach (var h in hospitales)
        {
            var r = RegionDe(h);
            if (r.Length > 0) return r;
        }
        return "";
    }

    /// El motivo por el que un hospital no le toca a este usuario, o null si sí le toca. Se usa en el
    /// alta y en los filtros: la pantalla no es la única puerta.
    public string? ErrorRegionDe(string? usuario, string? hospital)
    {
        if (VeTodasRegiones(usuario)) return null;
        var h = Hospitales.Normalizar(hospital, permiteGeneral: true);
        if (h.Length == 0) return null;                       // lo desconocido ya lo rechaza Hospitales
        if (string.Equals(h, Hospitales.General, StringComparison.OrdinalIgnoreCase)) return null;
        var regiones = RegionesDeUsuario(usuario);
        if (regiones.Count == 0) return null;                 // sin regiones: no se le limita
        foreach (var region in regiones)
        {
            var lista = HospitalesDe(region);
            if (lista.Count == 0) continue;                   // región sin desglose: no se le limita
            if (lista.Any(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase))) return null;
        }
        return $"«{h}» no está en ninguna de las regiones asignadas a su usuario.";
    }

    /// Deja solo los elementos cuyo hospital le toca al usuario. Quien ve todas las regiones no pierde
    /// nada, y una región todavía sin desglose tampoco filtra (así el catálogo se puede armar por
    /// partes). Los renglones sin hospital o con «General» se quedan siempre: no son de ninguna región
    /// y ocultarlos sería hacerlos desaparecer de la pantalla sin que nadie los diera de baja.
    public IReadOnlyList<T> FiltraPorRegion<T>(string? usuario, IEnumerable<T> elementos, Func<T, string?> hospital)
    {
        if (VeTodasRegiones(usuario)) return elementos.ToList();
        var permitidos = HospitalesPara(usuario).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return elementos.Where(x =>
        {
            var h = Hospitales.Normalizar(hospital(x), permiteGeneral: true);
            if (h.Length == 0 || h.Equals(Hospitales.General, StringComparison.OrdinalIgnoreCase)) return true;
            return permitidos.Contains(h);
        }).ToList();
    }

    /// Asigna (o quita) las regiones de un usuario. Se guarda en el mismo almacén que las regiones: no hace
    /// falta una columna nueva en la tabla de usuarios y lo guardado sigue vigente aunque se apague o se
    /// reinicie el servidor. La pantalla ya no ofrece la opción `todas`: «Todas las regiones» es el
    /// renglón vacío de la lista, que borra el asignado. Se sigue aceptando el campo por compatibilidad
    /// con los renglones guardados en entregas anteriores.
    public (int Status, string? Error) GuardarRegionPorUsuario(string usuario, IReadOnlyList<string>? regiones, bool todas)
    {
        var u = NormalizarTexto(usuario);
        if (u.Length == 0 || !UserRules.ValidName(u)) return (400, UserRules.NameError);
        if (UserRules.IsRoot(u))
            return (400, "root es el superusuario: siempre ve todas las regiones, no necesita asignación.");

        var regionesNormalizadas = new List<string>();
        if (regiones is not null)
        {
            foreach (var r in regiones)
            {
                var normalizada = NormalizarTexto(r);
                if (normalizada.Length == 0) continue;
                // Se guarda la región escrita exactamente como está en la lista del Programador: la
                // comparación ya ignora mayúsculas, pero así la pantalla Usuarios y el mensaje de error
                // muestran siempre el mismo nombre, no el que alguien tecleó distinto.
                var gemela = Regiones().FirstOrDefault(x => string.Equals(x, normalizada, StringComparison.OrdinalIgnoreCase));
                if (gemela is null)
                    return (400, $"La región «{normalizada}» no está en la lista de regiones del Programador.");
                if (regionesNormalizadas.Any(x => string.Equals(x, gemela, StringComparison.OrdinalIgnoreCase)))
                    continue; // Duplicado, ignorar
                regionesNormalizadas.Add(gemela);
            }
        }

        if (regionesNormalizadas.Count == 0 && !todas)
        {
            // Lista vacía («Todas las regiones»): se quita el renglón, que es lo mismo que «ve todas».
            var sinUsuario = new Dictionary<string, RegionUsuarioCrudo>(StringComparer.OrdinalIgnoreCase);
            foreach (var (clave, valor) in RegionesPorUsuario())
                if (!string.Equals(clave, u, StringComparison.OrdinalIgnoreCase))
                    sinUsuario[clave] = new RegionUsuarioCrudo { Regiones = valor.Regiones.ToList(), Todas = valor.Todas };
            return cfg.Set(ConfigKeys.RegionesPorUsuario, JsonSerializer.Serialize(sinUsuario, Json));
        }

        // Validar que las regiones tengan hospitales
        foreach (var r in regionesNormalizadas)
        {
            if (HospitalesDe(r).Count == 0)
                return (400, $"La región «{r}» todavía no tiene hospitales: escriba primero los suyos en el " +
                             "Programador, o elija «Todas las regiones» en la lista.");
        }

        var mapa = new Dictionary<string, RegionUsuarioCrudo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (clave, valor) in RegionesPorUsuario())
            mapa[clave] = new RegionUsuarioCrudo { Regiones = valor.Regiones.ToList(), Todas = valor.Todas };
        mapa[u] = new RegionUsuarioCrudo { Regiones = regionesNormalizadas, Todas = todas };
        return cfg.Set(ConfigKeys.RegionesPorUsuario, JsonSerializer.Serialize(mapa, Json));
    }

    /// Sobrecarga para compatibilidad con código existente que usaba una sola región.
    public (int Status, string? Error) GuardarRegionPorUsuario(string usuario, string? region, bool todas)
    {
        var regiones = string.IsNullOrEmpty(region) ? null : new List<string> { region };
        return GuardarRegionPorUsuario(usuario, regiones, todas);
    }

    /// Región y opción guardadas de un usuario, para pintar la pantalla Usuarios.
    public RegionUsuario RegionGuardadaDe(string? usuario)
    {
        var u = NormalizarTexto(usuario);
        return u.Length > 0 && RegionesPorUsuario().TryGetValue(u, out var v) ? v : new RegionUsuario(Array.Empty<string>(), false);
    }

    /// El JSON del desglose tal como está guardado. null si no se puede leer. A diferencia de los
    /// servicios, aquí sí se devuelven las regiones con lista vacía: la pantalla necesita mostrar que
    /// una región todavía no tiene hospitales asignados.
    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? AnalizarRegiones(string crudo)
    {
        try
        {
            var leido = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(crudo, Json);
            if (leido is null) return null;
            var salida = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (region, valores) in leido)
            {
                var r = NormalizarTexto(region);
                if (r.Length == 0) continue;
                salida[r] = Alistar(valores);
            }
            return salida;
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyDictionary<string, SubopcionActividad> Subopciones()
    {
        var guardado = cfg.Get(ConfigKeys.ActividadesSubopciones, ConfigKeys.Defaults[ConfigKeys.ActividadesSubopciones]);
        // Un catálogo vacío legítimamente guardado (el Programador quitó todas las subopciones) se
        // respeta. Solo si el texto no se puede leer —por ejemplo un valor truncado en un servidor
        // que no aceptó ampliar la columna— se regresa el catálogo por defecto, para que
        // Capacitación siga ofreciendo sus aplicativos en lugar de quedarse sin lista.
        return Analizar(guardado) ?? Analizar(ConfigKeys.Defaults[ConfigKeys.ActividadesSubopciones])!;
    }

    /// Tipos que ofrece cada aplicativo (`gestion.subopcionesTipos`), el tercer escalón de la cadena
    /// actividad → aplicativo → tipo. El catálogo llega **vacío mientras el Programador no escriba
    /// ninguno**, y así lo interpretan los formularios: sin lista para el aplicativo elegido, el campo
    /// «Tipo de aplicativo» no aparece. Las claves que ya no corresponden a ningún aplicativo de
    /// `gestion.subopciones` se leen igual —para no perder lo escrito si se recarga la pantalla— y se
    /// recortan al guardar las subopciones.
    public IReadOnlyDictionary<string, IReadOnlyList<string>> TiposPorSubopcion()
    {
        var salida = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var leido = AnalizarTipos(cfg.Get(ConfigKeys.SubopcionesTipos, "{}"));
        if (leido is null) return salida;
        foreach (var (subopcion, valores) in leido)
        {
            var nombre = NormalizarTexto(subopcion);
            if (nombre.Length == 0) continue;
            var lista = Alistar(valores);
            if (lista.Count == 0) continue;
            salida[nombre] = lista;
        }
        return salida;
    }

    /// Los tipos del aplicativo que se acaba de elegir en el formulario (vacío si no tiene lista).
    public IReadOnlyList<string> TiposDe(string? subopcion)
    {
        var s = NormalizarTexto(subopcion);
        if (s.Length == 0) return Array.Empty<string>();
        return TiposPorSubopcion().TryGetValue(s, out var lista) ? lista : Array.Empty<string>();
    }

    /// null cuando el JSON no se puede leer; el diccionario leído (posiblemente vacío) en otro caso.
    private static Dictionary<string, List<string>?>? AnalizarTipos(string crudo)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, List<string>?>>(crudo, Json); }
        catch { return null; }
    }

    /// null cuando el JSON no es válido; el diccionario leído (puede venir vacío) en otro caso.
    private IReadOnlyDictionary<string, SubopcionActividad>? Analizar(string crudo)
    {
        try
        {
            var leido = JsonSerializer.Deserialize<Dictionary<string, SubopcionCrudo>>(crudo, Json);
            var salida = new Dictionary<string, SubopcionActividad>(StringComparer.Ordinal);
            foreach (var (actividad, valor) in leido ?? new())
            {
                var nombre = NormalizarTexto(actividad);
                if (nombre.Length == 0 || valor is null) continue;
                var opciones = Alistar(valor.Opciones);
                if (opciones.Count == 0) continue;
                var etiqueta = NormalizarTexto(valor.Etiqueta);
                salida[nombre] = new SubopcionActividad(
                    etiqueta.Length > 0 ? etiqueta : "Opción", opciones);
            }
            return salida;
        }
        catch
        {
            return null;
        }
    }

    /// Todo lo que la pantalla Programador muestra y edita, en un solo objeto para la API.
    public object Todo() => new
    {
        actividades = Actividades(),
        subopciones = Subopciones().ToDictionary(kv => kv.Key, kv => new { etiqueta = kv.Value.Etiqueta, opciones = kv.Value.Opciones }),
        // El escalón siguiente de la cadena: los tipos de cada aplicativo. Llega vacío si el
        // Programador todavía no escribió ninguno y el formulario entonces no pinta el campo.
        subopcionesTipos = TiposPorSubopcion(),
        servicios = Servicios(),
        serviciosPorHospital = ServiciosPorHospital(),
        hospitales = HospitalesConfig(),
        regiones = Regiones(),
        hospitalesPorRegion = HospitalesPorRegion(),
    };

    /// Lo que necesita cualquier formulario (no solo el Programador): se manda en
    /// `/api/permisos/mios` para no crear una ruta extra por la que preguntar. `servicios` queda
    /// como respaldo de las pantallas que aún no conocen la lista por hospital. `regiones` y
    /// `regionPorHospital` vienen para que el diálogo + Nuevo ponga el campo Región antes que el
    /// hospital sin tener que preguntar; si no hay regiones la lista llega vacía y el campo no se pinta.
    public object ParaFormularios() => new
    {
        actividades = Actividades(),
        subopciones = Subopciones().ToDictionary(kv => kv.Key, kv => new { etiqueta = kv.Value.Etiqueta, opciones = kv.Value.Opciones }),
        // El escalón siguiente de la cadena: los tipos de cada aplicativo. Llega vacío si el
        // Programador todavía no escribió ninguno y el formulario entonces no pinta el campo.
        subopcionesTipos = TiposPorSubopcion(),
        servicios = Servicios(),
        serviciosPorHospital = ServiciosPorHospital(),
        regiones = Regiones(),
        regionPorHospital = RegionPorHospital(),
    };

    /// Lo mismo pero acotado a los hospitales que le tocan al usuario en sesión (ver
    /// `HospitalesPara`): el desplegable **Hospital** solo ofrece los de su región, el **Servicio** los
    /// de esos hospitales y el campo **Región** su región. Con `HospitalesConfig()` —el usuario que ve
    /// todas— el resultado es idéntico al de arriba, así que nadie se entera de que existe el filtro.
    public object ParaFormularios(IReadOnlyList<string> hospitalesPermitidos)
    {
        var permitidos = new HashSet<string>(hospitalesPermitidos, StringComparer.OrdinalIgnoreCase);
        var porHospital = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (h, lista) in ServiciosPorHospital())
            if (permitidos.Contains(h)) porHospital[h] = lista;
        var regiones = new List<string>();
        var regionPorHospital = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (h, r) in RegionPorHospital())
        {
            if (!permitidos.Contains(h)) continue;
            regionPorHospital[h] = r;
            if (!regiones.Any(x => string.Equals(x, r, StringComparison.OrdinalIgnoreCase))) regiones.Add(r);
        }
        return new
        {
            actividades = Actividades(),
            subopciones = Subopciones().ToDictionary(kv => kv.Key, kv => new { etiqueta = kv.Value.Etiqueta, opciones = kv.Value.Opciones }),
            // El escalón siguiente de la cadena: los tipos de cada aplicativo. Llega vacío si el
            // Programador todavía no escribió ninguno y el formulario entonces no pinta el campo.
            subopcionesTipos = TiposPorSubopcion(),
            servicios = Servicios(),
            serviciosPorHospital = porHospital,
            regiones,
            regionPorHospital,
        };
    }

    /// Refleja el catálogo de hospitales en `Hospitales` para que el servidor acepte los códigos
    /// nuevos en cualquier módulo. Se llama al arrancar y después de guardar.
    public void AplicarHospitales() => Hospitales.ActualizarExtras(HospitalesConfig());

    // ---- escritura ----

    /// Guarda una lista simple (actividades, servicios generales, hospitales o regiones) tras validarla.
    public (int Status, string? Error) GuardarLista(string clave, IEnumerable<string>? valores)
    {
        if (!ConfigKeys.DeCatalogo.Contains(clave) ||
            clave is ConfigKeys.ActividadesSubopciones or ConfigKeys.SubopcionesTipos
                or ConfigKeys.ServiciosPorHospital or ConfigKeys.HospitalesPorRegion)
            return (400, "La clave de catálogo no es válida.");
        var sonHospitales = clave == ConfigKeys.HospitalesLista;
        var lista = new List<string>();
        foreach (var bruto in valores ?? Array.Empty<string>())
        {
            var texto = NormalizarTexto(bruto);
            if (texto.Length == 0) continue;
            if (sonHospitales)
            {
                texto = texto.ToUpperInvariant();
                if (texto.Length > LongitudHospital)
                    return (400, $"El código de hospital «{texto}» no puede pasar de {LongitudHospital} caracteres.");
                if (texto.Any(c => !char.IsLetterOrDigit(c)))
                    return (400, $"El código de hospital «{texto}» solo puede llevar letras y números.");
            }
            else if (texto.Length > LongitudMaxima)
            {
                return (400, $"«{texto}» no puede pasar de {LongitudMaxima} caracteres.");
            }
            if (lista.Any(otra => string.Equals(otra, texto, StringComparison.OrdinalIgnoreCase))) continue;
            lista.Add(texto);
        }
        if (lista.Count > MaximoItems) return (400, $"Un catálogo no puede pasar de {MaximoItems} elementos.");
        if (sonHospitales)
        {
            // Los cuatro de siempre no se pueden quitar: hay datos históricos que los usan.
            foreach (var fijo in Hospitales.Todos)
                if (!lista.Any(h => string.Equals(h, fijo, StringComparison.OrdinalIgnoreCase))) lista.Add(fijo);
        }
        var (status, error) = cfg.Set(clave, JsonSerializer.Serialize(lista, Json));
        if (status != 204) return (status, error);
        if (sonHospitales) AplicarHospitales();
        // Al cambiar la lista de actividades se quitan del almacén las subopciones de las que ya
        // no existen; si no, quedarían guardadas para siempre aunque el calendario no las muestre.
        if (clave == ConfigKeys.ActividadesLista)
        {
            RecortarSubopciones(lista);
            // Y una cascada más abajo: los aplicativos que acababan de quedarse sin actividad tampoco
            // conservan su lista de tipos.
            RecortarTipos(Subopciones().Values.SelectMany(s => s.Opciones).ToList());
        }
        // Igual con el desglose por región: al borrar o renombrar una región, o al quitar un hospital
        // del catálogo, lo contrario sería dejar asignaciones que la pantalla ya no puede mostrar.
        else if (clave == ConfigKeys.RegionesLista) RecortarDesglose(regiones: lista);
        else if (sonHospitales) RecortarDesglose(hospitales: lista);
        return (status, error);
    }

    /// Reescribe `gestion.hospitalesPorRegion` dejando solo las regiones que siguen en su lista y los
    /// hospitales que siguen en la de hospitales. Se llama al guardar cualquiera de las dos.
    private void RecortarDesglose(IReadOnlyList<string>? regiones = null, IReadOnlyList<string>? hospitales = null)
    {
        var guardado = AnalizarRegiones(cfg.Get(ConfigKeys.HospitalesPorRegion, "{}"));
        if (guardado is null || guardado.Count == 0) return;
        var restantes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (region, lista) in guardado)
        {
            if (regiones is not null && !regiones.Any(r => string.Equals(r, region, StringComparison.OrdinalIgnoreCase))) continue;
            var valores = (lista ?? Array.Empty<string>())
                .Where(h => hospitales is null ||
                            hospitales.Any(v => string.Equals(v, h, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (valores.Count == 0) continue;
            restantes[region] = valores;
        }
        if (restantes.Count == guardado.Count &&
            restantes.Sum(kv => kv.Value.Count) == guardado.Sum(kv => kv.Value.Count)) return;
        cfg.Set(ConfigKeys.HospitalesPorRegion, JsonSerializer.Serialize(restantes, Json));
    }

    /// Reescribe `gestion.subopciones` dejando solo las actividades que siguen en la lista.
    private void RecortarSubopciones(IReadOnlyList<string> actividades)
    {
        var crudas = SubopcionesCrudas();
        if (crudas is null || crudas.Count == 0) return;
        var restantes = crudas
            .Where(kv => actividades.Any(a => string.Equals(a, kv.Key, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        if (restantes.Count == crudas.Count) return;
        cfg.Set(ConfigKeys.ActividadesSubopciones, JsonSerializer.Serialize(restantes, Json));
    }

    /// El JSON de subopciones tal como está guardado. null si la clave no existe (los valores por
    /// defecto no se escriben: deben seguir siendo respaldo, no un catálogo ya fijado).
    private Dictionary<string, SubopcionCrudo>? SubopcionesCrudas()
    {
        if (!cfg.Todo().TryGetValue(ConfigKeys.ActividadesSubopciones, out var crudo)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, SubopcionCrudo>>(crudo, Json); }
        catch { return null; }
    }

    /// Guarda las subopciones por actividad. Las actividades sin opciones se dejan fuera del JSON.
    public (int Status, string? Error) GuardarSubopciones(IEnumerable<SubopcionCrudo>? subopciones, IReadOnlyList<string>? actividadesValidas = null)
    {
        var salida = new Dictionary<string, SubopcionCrudo>(StringComparer.Ordinal);
        foreach (var s in subopciones ?? Array.Empty<SubopcionCrudo>())
        {
            var actividad = NormalizarTexto(s.Actividad);
            if (actividad.Length == 0) return (400, "Toda subopción necesita la actividad a la que pertenece.");
            if (actividadesValidas is not null &&
                !actividadesValidas.Any(a => string.Equals(a, actividad, StringComparison.OrdinalIgnoreCase)))
                return (400, $"La actividad «{actividad}» no está en la lista de actividades.");
            var etiqueta = NormalizarTexto(s.Etiqueta);
            var opciones = Alistar(s.Opciones);
            if (opciones.Count == 0) continue;
            if (etiqueta.Length > LongitudMaxima) return (400, $"La etiqueta «{etiqueta}» es demasiado larga.");
            salida[actividad] = new SubopcionCrudo { Actividad = actividad, Etiqueta = etiqueta, Opciones = opciones };
        }
        var (status, error) = cfg.Set(ConfigKeys.ActividadesSubopciones, JsonSerializer.Serialize(salida, Json));
        if (status != 204) return (status, error);
        // El escalón siguiente cuelga del nombre del aplicativo: si aquí se renombró o se borró uno, su
        // lista de tipos ya no la ofrece nadie y se quita del almacén en el mismo guardado.
        RecortarTipos(salida.Values.SelectMany(s => s.Opciones ?? new List<string>()).ToList());
        return (status, error);
    }

    /// Guarda los tipos que ofrece cada aplicativo (`gestion.subopcionesTipos`). Cada clave tiene que
    /// ser uno de los aplicativos que ya están en la lista de subopciones —se escribe en el bloque de
    /// arriba—, así que el escalón nuevo no puede quedar huérfano. Los aplicativos cuya lista queda
    /// vacía se dejan fuera del JSON: al no aparecer, el formulario no muestra el campo para ellos.
    /// Se recibe siempre el mapa completo (no solo el aplicativo visible), igual que con los servicios
    /// por hospital.
    public (int Status, string? Error) GuardarTiposPorSubopcion(IReadOnlyDictionary<string, List<string>?>? porSubopcion)
    {
        var validos = Subopciones().Values
            .SelectMany(s => s.Opciones)
            .ToList();
        var salida = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (clave, valores) in porSubopcion ?? new Dictionary<string, List<string>?>())
        {
            var subopcion = NormalizarTexto(clave);
            if (subopcion.Length == 0) continue;
            if (!validos.Any(o => string.Equals(o, subopcion, StringComparison.OrdinalIgnoreCase)))
                return (400, $"El aplicativo «{subopcion}» no está en la lista de subopciones. " +
                             "Escríbalo primero en el bloque «Subopciones por actividad». " +
                             "Si lo renombró o lo borró de ahí, quite también sus tipos.");
            var lista = Alistar(valores);
            if (lista.Count == 0) continue;
            salida[subopcion] = lista;
        }
        return cfg.Set(ConfigKeys.SubopcionesTipos, JsonSerializer.Serialize(salida, Json));
    }

    /// Reescribe `gestion.subopcionesTipos` dejando solo los aplicativos que siguen en la lista de
    /// subopciones. Se llama al guardar esas subopciones: un aplicativo renombrado o quitado no puede
    /// seguir arrastrando una lista de tipos que ninguna pantalla ya puede mostrar.
    private void RecortarTipos(IReadOnlyList<string> subopciones)
    {
        if (!cfg.Todo().TryGetValue(ConfigKeys.SubopcionesTipos, out var crudo)) return;
        var guardado = AnalizarTipos(crudo);
        if (guardado is null || guardado.Count == 0) return;
        var restantes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (subopcion, valores) in guardado)
        {
            var nombre = NormalizarTexto(subopcion);
            if (nombre.Length == 0) continue;
            if (!subopciones.Any(o => string.Equals(o, nombre, StringComparison.OrdinalIgnoreCase))) continue;
            var lista = Alistar(valores);
            if (lista.Count == 0) continue;
            restantes[nombre] = lista;
        }
        if (restantes.Count == guardado.Count) return;
        cfg.Set(ConfigKeys.SubopcionesTipos, JsonSerializer.Serialize(restantes, Json));
    }

    /// Guarda los servicios por hospital. Cada clave tiene que ser un hospital del catálogo; los
    /// hospitales sin opciones se dejan fuera del JSON. La lista general (`gestion.servicios`) se
    /// actualiza con la unión de todo lo escrito, para que siga diciendo lo mismo que ve el
    /// formulario; si se vacían todos los hospitales, la general se conserva como estaba.
    public (int Status, string? Error) GuardarServiciosPorHospital(IReadOnlyDictionary<string, List<string>?>? porHospital)
    {
        var validos = HospitalesConfig();
        var salida = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (clave, valores) in porHospital ?? new Dictionary<string, List<string>?>())
        {
            var hospital = Hospitales.Normalizar(clave, permiteGeneral: true);
            if (hospital.Length == 0)
                return (400, $"«{NormalizarTexto(clave)}» no es un hospital de la lista.");
            if (!validos.Any(h => string.Equals(h, hospital, StringComparison.OrdinalIgnoreCase)))
                return (400, $"El hospital «{hospital}» no está en la lista de hospitales. Agréguelo primero.");
            var lista = Alistar(valores);
            if (lista.Count == 0) continue;
            salida[hospital] = lista;
        }
        var (status, error) = cfg.Set(ConfigKeys.ServiciosPorHospital, JsonSerializer.Serialize(salida, Json));
        if (status != 204 || salida.Count == 0) return (status, error);

        var union = new List<string>();
        foreach (var lista in salida.Values)
            foreach (var s in lista)
                if (!union.Any(u => string.Equals(u, s, StringComparison.OrdinalIgnoreCase))) union.Add(s);
        cfg.Set(ConfigKeys.ServiciosLista, JsonSerializer.Serialize(union, Json));
        return (status, error);
    }

    /// Guarda el desglose de las regiones médicas. Cada clave tiene que ser una región de la lista,
    /// los hospitales **se escriben** (no se marcan): un código que todavía no esté en la lista de
    /// hospitales del sistema (`gestion.hospitales`) se agrega ahí mismo, con las mismas reglas con que
    /// siempre se validó —hasta <see cref="LongitudHospital"/> caracteres, solo letras y números, en
    /// mayúsculas— porque viaja
    /// en la columna `hospital` de los registros. «General» se rechaza: es la etiqueta del Playbook,
    /// no un hospital. **Un hospital solo puede pertenecer a una región**, así que repetir uno entre
    /// dos regiones del mismo envío se rechaza nombrando a las dos. Las regiones sin hospitales se
    /// dejan fuera del JSON: al no aparecer, sus hospitales quedan «Sin región». Se recibe siempre la
    /// lista completa (no solo la región visible), por eso un `null` o un objeto vacío se rechaza:
    /// borrar todas las asignaciones se hace enviando las regiones con su lista vacía.
    public (int Status, string? Error) GuardarHospitalesPorRegion(IReadOnlyDictionary<string, List<string>?>? porRegion)
    {
        if (porRegion is null || porRegion.Count == 0)
            return (400, "No se envió ninguna región: escriba al menos un hospital o deje el desglose como estaba.");
        var regionesValidas = Regiones();
        if (regionesValidas.Count == 0)
            return (400, "Agregue primero las regiones en el bloque de Regiones médicas.");
        // Copia del catálogo de hospitales: lo escrito que aún no existe se va sumando aquí. Se guarda
        // ANTES del desglose, para que nunca quede una asignación sobre un código que la lista no
        // conoce —el recorte que se hace al guardar hospitales lo borraría en el siguiente guardado—.
        var catalogo = new List<string>(HospitalesConfig());
        var nuevos = new List<string>();
        var salida = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        // Hospital → región que ya lo tomó, para detectar la repetición entre dos regiones del mismo
        // envío (la pantalla ya lo avisa, pero el servidor no puede depender de eso).
        var dueno = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (clave, valores) in porRegion)
        {
            var region = NormalizarTexto(clave);
            if (region.Length == 0) return (400, "Toda región necesita su nombre.");
            if (region.Length > LongitudMaxima) return (400, $"La región «{region}» es demasiado larga.");
            if (!regionesValidas.Any(r => string.Equals(r, region, StringComparison.OrdinalIgnoreCase)))
                return (400, $"La región «{region}» no está en la lista de regiones. Agréguela primero.");
            var lista = new List<string>();
            foreach (var bruto in valores ?? new List<string>())
            {
                // Se normaliza a mano y no con `Hospitales.Normalizar`: esa función devuelve vacío
                // para lo que aún no está en el catálogo, y aquí lo nuevo justamente se va a agregar.
                var hospital = NormalizarTexto(bruto).ToUpperInvariant();
                if (hospital.Length == 0) continue;      // fila en blanco: el botón deja una y no es error
                if (hospital.Length > LongitudHospital)
                    return (400, $"El hospital «{hospital}» no puede pasar de {LongitudHospital} caracteres.");
                if (hospital.Any(c => !char.IsLetterOrDigit(c)))
                    return (400, $"El hospital «{hospital}» solo puede llevar letras y números, sin espacios ni acentos.");
                if (string.Equals(hospital, Hospitales.General, StringComparison.OrdinalIgnoreCase))
                    return (400, "«General» no es un hospital: es la etiqueta del Playbook, no puede estar dentro de una región.");
                if (!catalogo.Any(h => string.Equals(h, hospital, StringComparison.OrdinalIgnoreCase)))
                {
                    if (catalogo.Count + 1 > MaximoItems)
                        return (400, $"La lista de hospitales no puede pasar de {MaximoItems} elementos.");
                    catalogo.Add(hospital);
                    nuevos.Add(hospital);
                }
                if (dueno.TryGetValue(hospital, out var otra))
                    return (400, $"El hospital «{hospital}» está en «{otra}» y en «{region}»: un hospital solo puede estar en una región.");
                if (!lista.Any(h => string.Equals(h, hospital, StringComparison.OrdinalIgnoreCase))) lista.Add(hospital);
            }
            if (lista.Count > MaximoItems) return (400, $"La región «{region}» no puede pasar de {MaximoItems} hospitales.");
            foreach (var h in lista) dueno[h] = region;
            if (lista.Count == 0) continue;
            salida[region] = lista;
        }
        if (nuevos.Count > 0)
        {
            // `cfg.Set` directo y no `GuardarLista`: esa ruta recorta el desglose con la lista nueva,
            // y aquí el recorte borraría justamente las asignaciones que se están escribiendo.
            var (status, error) = cfg.Set(ConfigKeys.HospitalesLista, JsonSerializer.Serialize(catalogo, Json));
            if (status != 204) return (status, error);
            AplicarHospitales();  // para que el resto del servidor ya acepte los códigos nuevos
        }
        return cfg.Set(ConfigKeys.HospitalesPorRegion, JsonSerializer.Serialize(salida, Json));
    }

    // ---- utilidades ----

    private IReadOnlyList<string> Lista(string clave)
    {
        var crudo = cfg.Get(clave, ConfigKeys.Defaults.TryGetValue(clave, out var d) ? d : "[]");
        try { return Alistar(JsonSerializer.Deserialize<List<string>>(crudo, Json)); }
        catch { return Alistar(JsonSerializer.Deserialize<List<string>>(ConfigKeys.Defaults[clave], Json)); }
    }

    /// Limpia una lista leída o escrita: sin vacíos, sin duplicados (ignorando mayúsculas) y con
    /// sus límites de tamaño.
    private static List<string> Alistar(IEnumerable<string>? valores)
    {
        var salida = new List<string>();
        foreach (var bruto in valores ?? Array.Empty<string>())
        {
            var texto = NormalizarTexto(bruto);
            if (texto.Length == 0 || texto.Length > LongitudMaxima) continue;
            if (salida.Any(o => string.Equals(o, texto, StringComparison.OrdinalIgnoreCase))) continue;
            salida.Add(texto);
            if (salida.Count >= MaximoItems) break;
        }
        return salida;
    }

    private static string NormalizarTexto(string? texto) => (texto ?? "").Trim();

    /// Formo JSON de una subopción: `{ actividad, etiqueta, opciones }`.
    public sealed class SubopcionCrudo
    {
        public string Actividad { get; set; } = "";
        public string Etiqueta { get; set; } = "";
        public List<string>? Opciones { get; set; }
    }

    /// Forma JSON de las regiones de un usuario: `{ regiones, todas }` (ver `GuardarRegionPorUsuario`).
    public sealed class RegionUsuarioCrudo
    {
        public List<string> Regiones { get; set; } = new();
        public bool Todas { get; set; }
    }
}
