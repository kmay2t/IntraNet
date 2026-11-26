namespace Meditechnosys.Intranet.Services;

/// Tiempo de sesión de cada usuario (el que se elige en el botón ⚙ de la cabecera del inicio).
///
/// La cookie de la intranet se renueva **por deslizamiento**: mientras la persona haga algo, la
/// sesión sigue viva. Por eso "tiempo de sesión" significa *cuánto aguanta sin actividad* antes de
/// pedir de nuevo la contraseña. Cada usuario guarda el suyo en la misma tabla de configuración,
/// bajo la clave `sesion.minutos.<usuario>`, y `Program.cs` lo consulta en cada petición.
///
/// Ninguna opción pasa de 8 horas porque es la ventana más larga que se ofrece y porque esa sigue
/// siendo la vida con la que se renueva la cookie. Lo que de verdad acorta la sesión no es la
/// caducidad de la cookie, sino `Expiro`: mide cuánto tiempo lleva esta persona sin hacer nada y,
/// al pasarse de su ventana, cierra la sesión y manda el aviso a la pantalla de acceso.
public static class SesionPreferencias
{
    /// Lo que venía haciendo la cookie: 8 horas. Si el usuario no eligió nada, sigue siendo 8 horas.
    public const int MinutosPorDefecto = 480;

    /// Vida que se escribe en la cookie. SIEMPRE supera a la ventana más larga (8 horas) con un
    /// margen: si la cookie caducara en el mismo instante en que se cumple la ventana elegida, el
    /// framework mataría el ticket *antes* de llegar a `OnValidatePrincipal`, y la persona volvería
    /// al acceso sin el aviso «tu sesión terminó por inactividad». La sesión la sigue cerrando
    /// `Expiro`, que mide la inactividad con la ventana de cada quien.
    public static TimeSpan VidaCookie { get; } = TimeSpan.FromMinutes(MinutosPorDefecto + 10);

    /// Prefijo de la clave por usuario en el almacén de configuración.
    public const string PrefijoClave = "sesion.minutos.";

    /// Ventanas disponibles. Son las únicas que se aceptan por la API: no vale cualquier número.
    public static readonly IReadOnlyList<SesionOpcion> Opciones = new[]
    {
        new SesionOpcion(15, "15 minutos"),
        new SesionOpcion(30, "30 minutos"),
        new SesionOpcion(60, "1 hora"),
        new SesionOpcion(120, "2 horas"),
        new SesionOpcion(240, "4 horas"),
        new SesionOpcion(MinutosPorDefecto, "8 horas · lo de siempre"),
    };

    /// Clave donde vive la preferencia de ese usuario. Se normaliza a minúsculas porque los
    /// nombres de cuenta se guardan sin distinción de mayúsculas.
    public static string Clave(string usuario)
        => PrefijoClave + (usuario ?? "").Trim().ToLowerInvariant();

    public static bool EsOpcion(int minutos) => Opciones.Any(o => o.Minutos == minutos);

    /// Minutos elegidos por ese usuario. Un valor viejo, vacío o fuera de la lista se lee como el
    /// predeterminado, de modo que una preferencia escrita a mano nunca puede dejar sin sesión a nadie.
    public static int Minutos(IConfigStore config, string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return MinutosPorDefecto;
        var crudo = config.Get(Clave(usuario!), MinutosPorDefecto.ToString());
        return int.TryParse(crudo, out var minutos) && EsOpcion(minutos) ? minutos : MinutosPorDefecto;
    }

    /// ¿Ya se cumplió la ventana sin actividad? Entonces la sesión se cierra y hay que volver a entrar.
    public static bool Expiro(TimeSpan transcurrido, int minutos)
        => transcurrido > TimeSpan.FromMinutes(minutos);

    /// ¿Se pasó de la mitad de la ventana y todavía sigue viva? Se vuelve a escribir la cookie: la
    /// marca de actividad (`IssuedUtc`) vuelve al presente —así la ventana corta se mide contra este
    /// usuario y no contra las 8 horas del servidor— y la caducidad absoluta se pone en `VidaCookie`,
    /// más larga que cualquier ventana, para que el cierre lo decida `Expiro`.
    public static bool DebeRenovar(TimeSpan transcurrido, int minutos)
        => !Expiro(transcurrido, minutos) && transcurrido > TimeSpan.FromMinutes(minutos / 2.0);
}

/// Una ventana de sesión que ofrece la API (`minutos` + cómo se le llama en pantalla).
public record SesionOpcion(int Minutos, string Etiqueta);
