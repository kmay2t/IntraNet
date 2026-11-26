using System.Security.Claims;
using System.Threading.RateLimiting;
using FellowOakDicom;
using Meditechnosys.Intranet.Dicom;
using Meditechnosys.Intranet.Models;
using Meditechnosys.Intranet.Patologia;
using Meditechnosys.Intranet.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using MySqlConnector;

// Inicializa fo-dicom una sola vez (necesario para C-FIND / C-MOVE).
new DicomSetupBuilder()
    .RegisterServices(s => s.AddFellowOakDicom())
    .Build();

// Al correr como servicio de Windows, la raíz de contenido es la carpeta del ejecutable.
var contentRoot = WindowsServiceHelpers.IsWindowsService()
    ? AppContext.BaseDirectory
    : Directory.GetCurrentDirectory();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot
});

builder.Host.UseWindowsService(o => o.ServiceName = "MeditechnosysIntranet");

builder.Services.AddRazorPages(o =>
{
    // Todo requiere sesion, excepto la pagina de login.
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AllowAnonymousToPage("/Login");
    o.Conventions.AllowAnonymousToPage("/Error");
    // Las páginas /Admin e /Ingenieria ya no se autorizan por rol fijo: ahora redirigen a la
    // página de inicio única ("/"), que muestra solo las herramientas que el usuario puede abrir
    // según sus permisos efectivos. El control fino vive en los guards de ruta y en la API.
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        // Marca que se deja en HttpContext cuando la sesión se cerró por inactividad, para que la
        // redirección de entrada pueda avisarlo en la pantalla de acceso (y no parezca un error).
        const string sesionExpiro = "meditechnosys.sesion-expiro";
        o.LoginPath = "/Login";
        o.AccessDeniedPath = "/AccesoDenegado";
        o.Cookie.Name = "meditechnosys.intranet";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        // Revisa en cada peticion que el usuario siga existiendo, activo y con el mismo rol.
        // La lectura usa una caché corta (Auth:SessionCacheSeconds): un cambio hecho aquí invalida
        // la entrada al momento; uno externo se ve como máximo al terminar esa ventana.
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var name = ctx.Principal?.Identity?.Name;
            var current = name is null ? null : ctx.HttpContext.RequestServices.GetRequiredService<IUserStore>().Find(name);
            if (current is null || !current.Activo)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }
            // Tiempo de sesión: el que cada quien elige en el botón ⚙ de la cabecera del inicio
            // (SesionPreferencias / GET-PUT /api/sesion). La cookie se renueva por deslizamiento, así
            // que la ventana se mide desde el último renuevo y significa "cuánto aguanta sin
            // actividad". Si se cumplió, se cierra la sesión y se avisa en el acceso; si ya pasó la
            // mitad de la ventana, se renueva la marca de actividad con la ventana de ESTE usuario,
            // porque el deslizamiento del servidor usa las 8 horas fijas de ExpireTimeSpan y dejaría
            // sin efecto una preferencia más corta. La caducidad absoluta que se escribe en la cookie
            // es `VidaCookie` (8 h + margen), siempre más larga que la ventana: si caducara justo al
            // cumplirse, el framework mataría el ticket antes de llegar aquí y el aviso no se pondría.
            var minutosSesion = SesionPreferencias.Minutos(
                ctx.HttpContext.RequestServices.GetRequiredService<IConfigStore>(), current.Usuario);
            var emitido = ctx.Properties.IssuedUtc ?? DateTimeOffset.UtcNow;
            var transcurrido = DateTimeOffset.UtcNow - emitido;
            if (SesionPreferencias.Expiro(transcurrido, minutosSesion))
            {
                if (!ctx.HttpContext.Request.Path.StartsWithSegments("/api"))
                    ctx.HttpContext.Items[sesionExpiro] = true;
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }
            if (SesionPreferencias.DebeRenovar(transcurrido, minutosSesion))
            {
                ctx.Properties.IssuedUtc = DateTimeOffset.UtcNow;
                ctx.Properties.ExpiresUtc = DateTimeOffset.UtcNow.Add(SesionPreferencias.VidaCookie);
                ctx.ShouldRenew = true;
            }
            // Permisos efectivos del usuario (matriz del rol + ajustes por usuario). Se recalculan en
            // cada petición y se adjuntan como claims `perm`; el resolvedor ya usa una caché corta
            // invalidable, de modo que los cambios se reflejan sin volver a iniciar sesión.
            var rolAnterior = ctx.Principal!.FindFirst(ClaimTypes.Role)?.Value;
            var resolver = ctx.HttpContext.RequestServices.GetRequiredService<IPermissionResolver>();
            var efectivos = resolver.Efectivos(current.Usuario, current.Rol);
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, current.Usuario),
                new(ClaimTypes.Role, current.Rol),
            };
            claims.AddRange(efectivos.Select(k => new Claim(GestionService.PermClaim, k)));
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            ctx.ReplacePrincipal(new ClaimsPrincipal(identity));
            // Solo se reescribe la cookie si cambió el rol persistido (los permisos se recalculan igual).
            if (!string.Equals(rolAnterior, current.Rol, StringComparison.Ordinal))
                ctx.ShouldRenew = true;
        };
        // La API responde 401/403 en vez de redirigir a la pagina de login.
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = 401;
                return Task.CompletedTask;
            }
            // Si llegamos aquí porque se cumplió el tiempo de sesión de esta persona, se lo decimos
            // en la pantalla de acceso en lugar de dejarla a ciegas.
            var destino = ctx.RedirectUri;
            if (ctx.HttpContext.Items.ContainsKey(sesionExpiro))
                destino += (destino.Contains('?') ? "&" : "?") + "sesion=expirada";
            ctx.Response.Redirect(destino);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = 403;
            else ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(o =>
{
    // Ya no se autoriza por rol fijo (Admin/Ingeniero). Cada recurso exige el permiso **efectivo**
    // del usuario —matriz del rol + ajustes por usuario— que viaja en la sesión como claim `perm`.
    // "SoloAdmin" e "Ingenieria" se conservan como sinónimo de "autenticado" para los
    // endpoints históricos. Patología usa su permiso fino directamente y, para borrar,
    // exige además el rol Admin; no depende de esos sinónimos.
    o.AddPolicy("SoloAdmin", p => p.RequireAuthenticatedUser());
    o.AddPolicy("Ingenieria", p => p.RequireAuthenticatedUser());
    foreach (var clave in CatalogoPermisos.TodasLasClaves)
        o.AddPolicy($"P:{clave}", p => p.RequireClaim(GestionService.PermClaim, clave));
});

// Protección de carga. El límite general es alto para no estorbar el trabajo normal; la consulta
// pública y el acceso tienen límites propios más bajos. Los health checks nunca se limitan.
var ventanaLimite = TimeSpan.FromSeconds(Math.Clamp(
    builder.Configuration.GetValue<int?>("RateLimiting:WindowSeconds") ?? 60, 1, 3600));
var limiteGeneral = Math.Clamp(builder.Configuration.GetValue<int?>("RateLimiting:GeneralPermitLimit") ?? 600, 30, 100_000);
var limitePublico = Math.Clamp(builder.Configuration.GetValue<int?>("RateLimiting:PublicPermitLimit") ?? 120, 5, 10_000);
var limiteLogin = Math.Clamp(builder.Configuration.GetValue<int?>("RateLimiting:LoginPermitLimit") ?? 10, 1, 1000);

static string ClienteDe(HttpContext ctx)
{
    var ip = ctx.Connection.RemoteIpAddress;
    if (ip is not null && ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
    return ip?.ToString() ?? "sin-ip";
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) && retry is TimeSpan espera)
            ctx.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(espera.TotalSeconds)).ToString();
        if (ctx.HttpContext.Request.Path.StartsWithSegments("/api"))
        {
            ctx.HttpContext.Response.ContentType = "application/json";
            await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "Demasiadas solicitudes. Espera un momento y vuelve a intentar." }, ct);
        }
        else
        {
            ctx.HttpContext.Response.ContentType = "text/plain";
            await ctx.HttpContext.Response.WriteAsync("Demasiadas solicitudes. Espera un momento y vuelve a intentar.", ct);
        }
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(resource =>
    {
        if (resource.Request.Path.StartsWithSegments("/health"))
            return RateLimitPartition.GetNoLimiter("health");
        var key = resource.User.Identity?.IsAuthenticated == true
            ? "usuario:" + resource.User.Identity!.Name
            : "ip:" + ClienteDe(resource);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limiteGeneral,
            Window = ventanaLimite,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    options.AddPolicy("consulta-publica", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ClienteDe(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limitePublico,
            Window = ventanaLimite,
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ClienteDe(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limiteLogin,
            Window = ventanaLimite,
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

// Comprime texto y JSON; el middleware no comprime PDF, ZIP ni DICOM. HTTPS se habilita porque
// toda la intranet viaja por el mismo origen y no se mezclan respuestas de terceros.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.AddHealthChecks()
    .AddCheck<MySqlHealthCheck>("mysql", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" });

// Almacen de usuarios: "mysql" (recomendado) o "json" (archivo App_Data/users.json).
// Si la configuracion pide mysql pero el servidor no responde al arrancar, se degrada al
// almacen JSON para que la intranet siga operativa; queda registrado en AuthStoreInfo y se
// muestra en /api/status y en la pagina de Administracion. Al volver MySQL, el arranque
// siguiente retoma mysql y migra los usuarios que se hubieran creado en json (vease mas abajo).
var requestedStore = builder.Configuration["Auth:Store"] ?? "json";
var mysqlRequested = string.Equals(requestedStore, "mysql", StringComparison.OrdinalIgnoreCase);
var mysqlAvailable = false;
string? mysqlDetail = null;
if (mysqlRequested)
{
    var cs = builder.Configuration.GetConnectionString("Usuarios");
    if (string.IsNullOrWhiteSpace(cs))
    {
        mysqlDetail = "Falta ConnectionStrings:Usuarios en la configuracion.";
    }
    else
    {
        try
        {
            // Sondeo corto para no retrasar el arranque si el servidor no existe.
            var probe = new MySqlConnectionStringBuilder(cs) { ConnectionTimeout = 3 };
            using var cn = new MySqlConnection(probe.ConnectionString);
            cn.Open();
            mysqlAvailable = true;
        }
        catch (Exception e)
        {
            mysqlDetail = e.Message;
        }
    }
}
var useMysql = mysqlRequested && mysqlAvailable;
builder.Services.AddSingleton(new AuthStoreInfo(
    requested: mysqlRequested ? "mysql" : "json",
    provider: useMysql ? "mysql" : "json",
    degraded: mysqlRequested && !mysqlAvailable,
    detail: mysqlDetail));
// La validación de sesión consulta el usuario en cada petición. Una caché corta e invalidable
// evita repetir esa consulta sin dejar cambios de rol/actividad visibles durante más de unos segundos.
builder.Services.AddMemoryCache(o => o.SizeLimit = 4096);
if (useMysql)
{
    builder.Services.AddSingleton<MySqlUserStore>();
    builder.Services.AddSingleton<IUserStore>(sp => new CachedUserStore(
        sp.GetRequiredService<MySqlUserStore>(), sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<IConfiguration>()));
}
else
{
    builder.Services.AddSingleton<UserStore>();
    builder.Services.AddSingleton<IUserStore>(sp => new CachedUserStore(
        sp.GetRequiredService<UserStore>(), sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<IConfiguration>()));
}
builder.Services.AddSingleton<IAuthenticator, StoreAuthenticator>();

// Bitacora de accesos: mismo proveedor que los usuarios (MySQL o JSONL de respaldo).
// El accessor permite leer la IP y el User-Agent de la petición dentro del registro, para que
// ninguna de las decenas de llamadas a Register() tenga que pasarlas a mano.
builder.Services.AddHttpContextAccessor();
// El almacén real queda como servicio keyed y la fachada de IAuditLog solo encola. Así las
// peticiones no esperan a MySQL y el worker puede vaciar los eventos al detener el servicio.
if (useMysql)
{
    builder.Services.AddSingleton<MySqlAuditLog>();
    builder.Services.AddKeyedSingleton<IAuditLog>("audit-sink", (sp, _) => sp.GetRequiredService<MySqlAuditLog>());
}
else
{
    builder.Services.AddSingleton<JsonAuditLog>();
    builder.Services.AddKeyedSingleton<IAuditLog>("audit-sink", (sp, _) => sp.GetRequiredService<JsonAuditLog>());
}
builder.Services.AddSingleton<AuditQueue>();
builder.Services.AddSingleton<IAuditLog, QueuedAuditLog>();
builder.Services.AddHostedService<AuditWorker>();

// Mapa «rango de IP → ubicación física» y nombre de equipo (DNS inverso) que la bitácora muestra
// en cada renglón. El resolver guarda en memoria lo que ya consultó, así que la pantalla no espera
// a la red en cada visita.
if (useMysql)
    builder.Services.AddSingleton<IUbicacionStore, MySqlUbicacionStore>();
else
    builder.Services.AddSingleton<IUbicacionStore, JsonUbicacionStore>();

// Lugar geográfico que el propio equipo reporta al abrir la intranet (dirección tipo
// «Vallejo, Gustavo A. Madero, 07870 Ciudad de México»). Se guarda por IP y la bitácora lo usa
// cuando el rango de esa IP no tiene una ubicación escrita a mano.
// LugarDelSitio procura solo, sin que nadie escriba nada, la dirección del sitio completo: es el
// respaldo para los equipos que todavía no han reportado, para que la Ubicación nunca quede vacía.
if (useMysql)
    builder.Services.AddSingleton<ILugarStore, MySqlLugarStore>();
else
    builder.Services.AddSingleton<ILugarStore, JsonLugarStore>();
// Punto calibrado por el administrador (el edificio marcado en el mapa): es el único dato de
// ubicación que es exacto, porque por IP solo se sabe la zona por donde sale el proveedor.
if (useMysql)
    builder.Services.AddSingleton<ISitioStore, MySqlSitioStore>();
else
    builder.Services.AddSingleton<ISitioStore, JsonSitioStore>();
builder.Services.AddHttpClient(nameof(GeocodificadorLugar));
builder.Services.AddHttpClient(nameof(LugarDelSitio));
builder.Services.AddSingleton<GeocodificadorLugar>();
builder.Services.AddSingleton<LugarDelSitio>();
builder.Services.AddSingleton<UbicacionResolver>();

// Bitácora del quemador DICOM (descargas y grabaciones reportadas por el programa del mostrador).
if (useMysql)
    builder.Services.AddSingleton<IQuemadorLog, MySqlQuemadorLog>();
else
    builder.Services.AddSingleton<IQuemadorLog, JsonQuemadorLog>();

// Calendario de actividades: mismo proveedor que los usuarios (MySQL o JSON de respaldo).
if (useMysql)
    builder.Services.AddSingleton<IActivityStore, MySqlActivityStore>();
else
    builder.Services.AddSingleton<IActivityStore, JsonActivityStore>();

// Módulo Gestión: reportes de estatus, historial de cambios, matriz de permisos y configuración.
if (useMysql)
{
    builder.Services.AddSingleton<IReportStore, MySqlReportStore>();
    builder.Services.AddSingleton<IHistorialStore, MySqlHistorialLog>();
    builder.Services.AddSingleton<IPermissionStore, MySqlPermissionStore>();
    builder.Services.AddSingleton<IRoleStore, MySqlRoleStore>();
    builder.Services.AddSingleton<IUserPermissionStore, MySqlUserPermissionStore>();
    builder.Services.AddSingleton<MySqlConfigStore>();
    builder.Services.AddSingleton<IConfigStore>(sp => new CachedConfigStore(
        sp.GetRequiredService<MySqlConfigStore>(), sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<IConfiguration>()));
    builder.Services.AddSingleton<IInventarioStore, MySqlInventarioStore>();
    builder.Services.AddSingleton<IDocumentoStore, MySqlDocumentoStore>();
    builder.Services.AddSingleton<IAlmacenStore, MySqlAlmacenStore>();
    builder.Services.AddSingleton<IAvisoStore, MySqlAvisoStore>();
    builder.Services.AddSingleton<IActaStore, MySqlActaStore>();
    builder.Services.AddSingleton<ICredencialStore, MySqlCredencialStore>();
    // Evidencia (foto o archivo) del trabajo al completar un elemento: sus metadatos van en la
    // tabla `evidencias` y el archivo en disco, dentro de App_Data/evidencias.
    builder.Services.AddSingleton<IEvidenciaStore, MySqlEvidenciaStore>();
}
else
{
    builder.Services.AddSingleton<IReportStore, JsonReportStore>();
    builder.Services.AddSingleton<IHistorialStore, JsonHistorialLog>();
    builder.Services.AddSingleton<IPermissionStore, JsonPermissionStore>();
    builder.Services.AddSingleton<IRoleStore, JsonRoleStore>();
    builder.Services.AddSingleton<IUserPermissionStore, JsonUserPermissionStore>();
    builder.Services.AddSingleton<JsonConfigStore>();
    builder.Services.AddSingleton<IConfigStore>(sp => new CachedConfigStore(
        sp.GetRequiredService<JsonConfigStore>(), sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<IConfiguration>()));
    builder.Services.AddSingleton<IInventarioStore, JsonInventarioStore>();
    builder.Services.AddSingleton<IDocumentoStore, JsonDocumentoStore>();
    builder.Services.AddSingleton<IAlmacenStore, JsonAlmacenStore>();
    builder.Services.AddSingleton<IAvisoStore, JsonAvisoStore>();
    builder.Services.AddSingleton<IActaStore, JsonActaStore>();
    builder.Services.AddSingleton<ICredencialStore, JsonCredencialStore>();
    builder.Services.AddSingleton<IEvidenciaStore, JsonEvidenciaStore>();
}
// Buzón enlazado por usuario (los correos que cada quien ve y desde donde responde). Mismo
// proveedor que los usuarios. La contrasena se guarda cifrada con la clave de App_Data.
builder.Services.AddSingleton<BuzonCrypto>();
if (useMysql)
    builder.Services.AddSingleton<IBuzonStore, MySqlBuzonStore>();
else
    builder.Services.AddSingleton<IBuzonStore, JsonBuzonStore>();
// Resuelve los permisos efectivos (matriz del rol + ajustes por usuario) que se cargan en la sesión.
builder.Services.AddSingleton<IPermissionResolver, PermissionResolver>();
builder.Services.AddSingleton<GestionService>();
builder.Services.AddSingleton<InventarioService>();
builder.Services.AddSingleton<AlmacenService>();
builder.Services.AddSingleton<PlaybookService>();
// Evidencia del cierre: reglas, archivos en disco y su registro.
builder.Services.AddSingleton<EvidenciaService>();
builder.Services.AddSingleton<HistoricoExportador>();
// Catálogos que edita la pantalla Programador (actividades, subopciones, servicios y hospitales).
builder.Services.AddSingleton<GestionCatalogos>();

// Núcleo de Patología (repositorio de reportes en C#).
builder.Services.AddSingleton<PatoDb>();
builder.Services.AddSingleton<Repositorio>();
builder.Services.AddSingleton<Importador>();
builder.Services.AddHostedService<ImportadorService>();

// ---- DICOM Q/R (fusionado desde DicomQrApi) ----
// Cuerpo grande: las descargas masivas envían miles de UIDs en JSON.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    o.MultipartBodyLengthLimit = 48 * 1024 * 1024);
// ---- Dirección segura (https): la condición para que el navegador entregue la posición real ----
// El navegador solo deja pedir el permiso de ubicación en una dirección segura; en http:// ni siquiera
// abre la ventaneta, así que la bitácora se queda con la zona por donde el equipo sale a Internet. Con
// este puerto seguro el aparato puede reportar su punto de verdad (GPS o wifi del puesto) sin que
// nadie escriba nada. El http:// de siempre (5023) sigue funcionando igual: aquí solo se agrega el
// https al lado, no se sustituye nada.
var https = new OpcionesHttps();
builder.Configuration.GetSection("Https").Bind(https);
var notasHttps = new List<string>();
var certificado = https.Activo
    ? CertificadoIntranet.Asegura(Path.Combine(contentRoot, "App_Data", "https"), https.Nombres, m => notasHttps.Add(m))
    : null;
if (certificado is not null)
{
    // La lista de direcciones viene del argumento --urls del servicio (http://0.0.0.0:5023). Se le
    // anexa el puerto seguro sin tocar el instalador: si el puerto o el host cambian ahí, se respeta.
    var direcciones = (builder.Configuration["urls"] ?? "http://0.0.0.0:5023")
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    if (!direcciones.Any(d => d.StartsWith("https", StringComparison.OrdinalIgnoreCase)))
        direcciones.Add($"https://0.0.0.0:{https.Puerto}");
    // Configurar "urls" por memoria es lo único que gana sobre el --urls con el que arranca el
    // servicio. El certificado se entrega en memoria (ConfigureKestrel, abajo), sin dejar su
    // contraseña en ningún archivo de configuración.
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["urls"] = string.Join(";", direcciones)
    });
}
builder.WebHost.ConfigureKestrel(o =>
{
    // 48 MB: los correos con adjuntos e imagenes viajan en JSON (base64), que agranda el archivo
    // ~1.4 veces; con este tope se pueden mandar adjuntos de hasta ~12 MB sin que el servidor corte.
    o.Limits.MaxRequestBodySize = 48 * 1024 * 1024;
    o.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
    o.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2);
    // ZIPs grandes: no cortar la conexión por "poca velocidad".
    o.Limits.MinResponseDataRate = null;
    o.Limits.MinRequestBodyDataRate = null;
    // El certificado para cualquier dirección https:// de la lista de puertos (el http no se toca).
    if (certificado is not null) o.ConfigureHttpsDefaults(h => h.ServerCertificate = certificado.Certificado);
});

// Uno o varios nodos PACS a consultar/descargar (soporta múltiples AE Titles).
var pacsNodes = builder.Configuration.GetSection("PacsNodes").Get<List<PacsNode>>();
if (pacsNodes is null || pacsNodes.Count == 0)
{
    var legacy = builder.Configuration.GetSection("Pacs").Get<PacsNode>();
    pacsNodes = legacy is not null && !string.IsNullOrWhiteSpace(legacy.AeTitle)
        ? new List<PacsNode> { legacy }
        : new List<PacsNode>();
}
builder.Services.AddSingleton<IEnumerable<PacsNode>>(pacsNodes);
builder.Services.Configure<LocalOptions>(builder.Configuration.GetSection("Local"));
builder.Services.Configure<ServiciosOptions>(builder.Configuration.GetSection("Servicios"));
builder.Services.AddSingleton<DicomQrService>();
builder.Services.AddSingleton<DownloadJobService>();
// Envío de estudios (pantalla /dicom/enviar): C-STORE con el AE emisor y reenvío entre nodos PACS.
builder.Services.AddSingleton<DicomSendService>();
// Lista de trabajo MWL: SCU independiente, sin otro puerto de escucha.
builder.Services.AddSingleton<IWorklistService, WorklistService>();
// Registro permanente de las pruebas técnicas enviadas (App_Data/dicom-pruebas.jsonl), en el mismo
// proveedor de archivos de respaldo que la bitácora y el historial: siempre es JSONL, haya MySQL o no.
builder.Services.AddSingleton<IDicomPruebaStore, JsonDicomPruebaStore>();
builder.Services.AddSingleton<ReenvioJobService>();
// Depuración interna de descargas DICOM: repasa la carpeta cada hora (Local:DepurarCadaMinutos) y
// se registra como singleton para que /api/status pueda informar cuánto ocupa y cuándo se depuró.
builder.Services.AddSingleton<LimpiadorDescargas>();
builder.Services.AddHostedService(p => p.GetRequiredService<LimpiadorDescargas>());
// Bandeja de soporte: lector IMAP (MailKit). Las credenciales viven en Correo:Imap; la
// contrasena la coloca el administrador en appsettings.json y nunca se registra ni se expone.
builder.Services.Configure<CorreoImapOptions>(builder.Configuration.GetSection("Correo:Imap"));
builder.Services.AddSingleton<CorreoService>();
builder.Services.AddControllers();

var app = builder.Build();

// Deja en bitacora que almacen de usuarios quedo activo y por que.
var storeInfo = app.Services.GetRequiredService<AuthStoreInfo>();
if (storeInfo.Degraded)
    app.Logger.LogWarning("MySQL de usuarios no disponible ({Detalle}); se usa el almacen JSON de respaldo.", storeInfo.Detail);
else
    app.Logger.LogInformation("Almacen de usuarios activo: {Proveedor}.", storeInfo.ProviderLabel);

var patoDb = app.Services.GetRequiredService<PatoDb>();
patoDb.AsegurarCarpetas();
try
{
    // Agrega el campo Estudio con respaldo previo y completa los registros antiguos desde su PDF.
    // Si un archivo no se puede leer, queda pendiente para el siguiente arranque sin detener el servicio.
    await PatoEstudio.InicializarAsync(patoDb, app.Logger);
}
catch (Exception e)
{
    app.Logger.LogError(e, "No se pudo preparar el campo Estudio de Patología; se reintentará al reiniciar.");
}
app.Services.GetRequiredService<PlaybookService>().AsegurarCarpeta();
// Carpeta de la evidencia (foto o archivo) que se adjunta al completar un elemento del calendario.
app.Services.GetRequiredService<EvidenciaService>().AsegurarCarpeta();

// Arranca el receptor C-STORE (destino de los C-MOVE) y lo libera al cerrar.
var dicomQr = app.Services.GetRequiredService<DicomQrService>();
dicomQr.StartListener();
app.Lifetime.ApplicationStopping.Register(dicomQr.Dispose);

// Primer arranque: si no hay usuarios, se crean los de Auth:Users (si existen).
var store = app.Services.GetRequiredService<IUserStore>();
if (useMysql)
{
    var mysqlStore = app.Services.GetRequiredService<MySqlUserStore>();
    mysqlStore.EnsureSchema();
    app.Services.GetRequiredService<MySqlAuditLog>().EnsureSchema();
    (app.Services.GetRequiredService<IUbicacionStore>() as MySqlUbicacionStore)?.EnsureSchema();
    (app.Services.GetRequiredService<ILugarStore>() as MySqlLugarStore)?.EnsureSchema();
    (app.Services.GetRequiredService<ISitioStore>() as MySqlSitioStore)?.EnsureSchema();
    (app.Services.GetRequiredService<IQuemadorLog>() as MySqlQuemadorLog)?.EnsureSchema();
    (app.Services.GetRequiredService<IActivityStore>() as MySqlActivityStore)?.EnsureSchema();
    (app.Services.GetRequiredService<IReportStore>() as MySqlReportStore)?.EnsureSchema();
    (app.Services.GetRequiredService<IHistorialStore>() as MySqlHistorialLog)?.EnsureSchema();
    (app.Services.GetRequiredService<IPermissionStore>() as MySqlPermissionStore)?.EnsureSchema();
    (app.Services.GetRequiredService<IRoleStore>() as MySqlRoleStore)?.EnsureSchema();
    (app.Services.GetRequiredService<IUserPermissionStore>() as MySqlUserPermissionStore)?.EnsureSchema();
    app.Services.GetRequiredService<MySqlConfigStore>().EnsureSchema();
    (app.Services.GetRequiredService<IInventarioStore>() as MySqlInventarioStore)?.EnsureSchema();
    (app.Services.GetRequiredService<IDocumentoStore>() as MySqlDocumentoStore)?.EnsureSchema();
    (app.Services.GetRequiredService<IAlmacenStore>() as MySqlAlmacenStore)?.EnsureSchema();
    (app.Services.GetRequiredService<IAvisoStore>() as MySqlAvisoStore)?.EnsureSchema();
    (app.Services.GetRequiredService<IActaStore>() as MySqlActaStore)?.EnsureSchema();
    (app.Services.GetRequiredService<ICredencialStore>() as MySqlCredencialStore)?.EnsureSchema();
    (app.Services.GetRequiredService<IBuzonStore>() as MySqlBuzonStore)?.EnsureSchema();
    (app.Services.GetRequiredService<IEvidenciaStore>() as MySqlEvidenciaStore)?.EnsureSchema();
    if (mysqlStore.List().Count == 0)
    {
        // Primera vez con MySQL: se traen los usuarios que ya existian en users.json.
        var legacy = new UserStore(app.Services.GetRequiredService<IWebHostEnvironment>(), app.Configuration);
        foreach (var u in legacy.List()) mysqlStore.Import(u);
        if (legacy.List().Count > 0)
            app.Logger.LogInformation("Migrados {N} usuario(s) de users.json a MySQL.", legacy.List().Count);
    }
}
if (store.List().Count == 0)
{
    var seed = app.Configuration.GetSection("Auth:Users").Get<List<SeedUserEntry>>() ?? new();
    foreach (var u in seed) store.Create(u.User, u.Password, u.Role, validatePassword: false);
}

// Los hospitales que agregue el Programador se cargan antes de atender la primera petición, para
// que la validación del servidor los acepte en todos los módulos (calendario, inventarios, Playbook).
app.Services.GetRequiredService<GestionCatalogos>().AplicarHospitales();

// La dirección geográfica del sitio se procura sola desde que arranca el servicio, en un hilo aparte
// y sin esperar la respuesta: así la bitácora ya muestra ubicación en la primera pantalla que alguien
// abra, sin depender de que algún equipo se haya conectado antes. Si el servidor no tiene salida a
// Internet, se usa lo que reporten los equipos y se rehace sola al consultar.
app.Services.GetRequiredService<LugarDelSitio>().RefrescaEnSegundoPlano();

// Deja escrito qué pasó con la dirección segura (certificado creado, renovado o imposible).
foreach (var nota in notasHttps) app.Logger.LogInformation("[https] {Nota}", nota);

// El salto de http:// a https:// (config Https:Redirigir). Viene apagado por omisión: primero hay que
// confiar el certificado en las PC del hospital, para no dejar a nadie fuera de la intranet. La hojita
// del certificado y las pruebas locales del propio servidor nunca se redirigen.
if (certificado is not null && https.Redirigir)
{
    app.Use(async (ctx, next) =>
    {
        var esHttp = string.Equals(ctx.Request.Scheme, "http", StringComparison.OrdinalIgnoreCase);
        var esLocal = ctx.Connection.LocalIpAddress is { } local && System.Net.IPAddress.IsLoopback(local);
        // La consulta publica se atiende directamente en 5023; no debe saltar al HTTPS 5024 de la
        // intranet. Sus archivos y su API tambien comparten esa excepcion para no mezclar puertos.
        var esConsultaPublica = ctx.Request.Path.StartsWithSegments("/consulta")
            || ctx.Request.Path.StartsWithSegments("/consulta-patologia")
            || ctx.Request.Path.StartsWithSegments("/api/consulta-patologia");
        if (!esHttp || esLocal || esConsultaPublica || ctx.Request.Path.Equals("/intranet.cer", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }
        var destino = $"https://{ctx.Request.Host.Host}:{https.Puerto}{ctx.Request.Path}{ctx.Request.QueryString}";
        ctx.Response.Redirect(destino, permanent: ctx.Request.Method != HttpMethods.Get);
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseResponseCompression();

// Consulta pública independiente. Queda fuera de /patologia y no pasa por el guard de sesión.
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/consulta-patologia"), b =>
{
    b.UseStaticFiles(CacheEstatico("/consulta-patologia"));
});

// HTML/JS/CSS se revalidan con ETag/Last-Modified (no-cache) para no quedar atrapados en una
// versión vieja ni transferir siempre el mismo contenido. Imágenes y fuentes son estables y
// pueden quedarse una semana en el navegador. Los PDF y DICOM no pasan por este middleware.
static StaticFileOptions CacheEstatico(string requestPath = "") => new()
{
    RequestPath = requestPath,
    OnPrepareResponse = ctx =>
    {
        var nombre = ctx.File.Name ?? "";
        if (nombre.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || nombre.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || nombre.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache";
        }
        else if (nombre.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                 || nombre.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                 || nombre.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                 || nombre.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                 || nombre.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                 || nombre.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                 || nombre.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
                 || nombre.EndsWith(".woff", StringComparison.OrdinalIgnoreCase)
                 || nombre.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)
                 || nombre.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=604800";
        }
    }
};

// Autorización de pantallas por permiso efectivo. `perm` es el claim que la sesión adjunta en cada
// petición (matriz del rol + ajustes por usuario). Si falta la sesión se va al login; si la tiene
// pero no el permiso, a /AccesoDenegado. El backend (API) vuelve a validar cada operación.
static bool Sesion(HttpContext ctx) => ctx.User.Identity?.IsAuthenticated == true;
static bool Puede(HttpContext ctx, string clave) => Sesion(ctx) && ctx.User.HasClaim(GestionService.PermClaim, clave);
static void Denegar(HttpContext ctx)
{
    if (!Sesion(ctx)) ctx.Response.Redirect("/Login");
    else ctx.Response.Redirect("/AccesoDenegado");
}

// Obtiene la configuración de servicios para verificar si están habilitados
var servicios = app.Services.GetRequiredService<IOptions<ServiciosOptions>>().Value;

app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/patologia"), b =>
{
    b.UseAuthentication();
    b.Use(async (ctx, next) =>
    {
        if (!servicios.PatologiaConsultar)
        {
            ctx.Response.StatusCode = 404;
            return;
        }
        if (!Puede(ctx, CatalogoPermisos.PatologiaConsultar)) { Denegar(ctx); return; }
        await next();
    });
    b.UseStaticFiles(CacheEstatico("/patologia"));
});

// Bajo /dicom conviven dos pantallas con dos permisos: el visor (dicom.ver) y el envío
// (dicom.enviar). Se comprueba el que corresponda a la ruta pedida, no uno solo para las dos,
// para que alguien con permiso de enviar no necesite el de descargar y viceversa.
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/dicom"), b =>
{
    b.UseAuthentication();
    b.Use(async (ctx, next) =>
    {
        var esEnvio = ctx.Request.Path.StartsWithSegments("/dicom/enviar");
        var esListaTrabajo = ctx.Request.Path.StartsWithSegments("/dicom/lista-trabajo");

        // Verificar si el servicio está deshabilitado
        if (esEnvio && !servicios.DicomEnviar)
        {
            ctx.Response.StatusCode = 404;
            return;
        }
        if (!esEnvio && esListaTrabajo && !servicios.DicomListaTrabajo)
        {
            ctx.Response.StatusCode = 404;
            return;
        }
        if (!esEnvio && !esListaTrabajo && !servicios.DicomVer)
        {
            ctx.Response.StatusCode = 404;
            return;
        }

        var clave = esEnvio ? CatalogoPermisos.DicomEnviar : CatalogoPermisos.DicomVer;
        if (!Puede(ctx, clave)) { Denegar(ctx); return; }
        await next();
    });
    b.UseStaticFiles(CacheEstatico("/dicom"));
});

// Paneles de administración: cada subpantalla exige su propio permiso efectivo.
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/admin"), b =>
{
    b.UseAuthentication();
    b.Use(async (ctx, next) =>
    {
        if (!Sesion(ctx)) { ctx.Response.Redirect("/Login"); return; }
        var path = ctx.Request.Path;
        // La pantalla de administrativos abre con cualquiera de los dos permisos del módulo
        // (actas o credenciales); cada pestaña y cada endpoint filtran después por su clave.
        if (path.StartsWithSegments("/admin/administrativos")
            && !Puede(ctx, CatalogoPermisos.ActasAdministrar)
            && !Puede(ctx, CatalogoPermisos.CredencialesAdministrar))
        { ctx.Response.Redirect("/AccesoDenegado"); return; }
        // La bitácora es una sola pantalla desde que se unificaron accesos y quemador: basta con
        // permiso de ver alguna de las dos. Dentro, cada quien ve solo la mitad que le corresponde.
        if (path.StartsWithSegments("/admin/bitacora")
            && !Puede(ctx, CatalogoPermisos.BitacoraVer)
            && !Puede(ctx, CatalogoPermisos.QuemadorVer))
        { ctx.Response.Redirect("/AccesoDenegado"); return; }
        var clave =
            path.StartsWithSegments("/admin/usuarios") ? CatalogoPermisos.UsuariosAdministrar :
            path.StartsWithSegments("/admin/roles") ? CatalogoPermisos.RolesAdministrar :
            path.StartsWithSegments("/admin/config") ? CatalogoPermisos.ConfiguracionAdministrar :
            path.StartsWithSegments("/admin/programador") ? CatalogoPermisos.ProgramadorAdministrar :
            path.StartsWithSegments("/admin/bitacora") ? null :
            path.StartsWithSegments("/admin/quemador") ? CatalogoPermisos.QuemadorVer :
            path.StartsWithSegments("/admin/correos") ? CatalogoPermisos.CorreoVer :
            path.StartsWithSegments("/admin/reportes") ? CatalogoPermisos.ReportesConsultar :
            null;
        // Las subpantallas sensibles exigen su permiso. La ruta raíz /Admin (clave null) solo
        // redirige a la página de inicio, así que se deja pasar al enrutado.
        if (clave is not null && !Puede(ctx, clave)) { ctx.Response.Redirect("/AccesoDenegado"); return; }
        await next();
    });
    b.UseStaticFiles(CacheEstatico("/admin"));
});

// Calendario de actividades: respeta el permiso calendario.acceder.
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/calendario"), b =>
{
    b.UseAuthentication();
    b.Use(async (ctx, next) =>
    {
        if (!Puede(ctx, CatalogoPermisos.CalendarioAcceder)) { Denegar(ctx); return; }
        await next();
    });
    b.UseStaticFiles(CacheEstatico("/calendario"));
});

// Pantallas del módulo Gestión: cada una exige el permiso de su herramienta. El Histórico es
// exclusivamente administrativo (historico.actividades); los inventarios y el Playbook usan su clave.
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/gestion"), b =>
{
    b.UseAuthentication();
    b.Use(async (ctx, next) =>
    {
        if (!Sesion(ctx)) { ctx.Response.Redirect("/Login"); return; }
        var path = ctx.Request.Path;
        var clave =
            path.StartsWithSegments("/gestion/historico") ? CatalogoPermisos.HistoricoActividades :
            path.StartsWithSegments("/gestion/inventario-equipos") ? CatalogoPermisos.AlmacenConsultar :
            path.StartsWithSegments("/gestion/inventario-ips") ? CatalogoPermisos.InventarioConsultar :
            path.StartsWithSegments("/gestion/inventario-aetitles") ? CatalogoPermisos.InventarioConsultar :
            path.StartsWithSegments("/gestion/playbook") ? CatalogoPermisos.PlaybookConsultar :
            null;
        if (clave is not null && !Puede(ctx, clave)) { ctx.Response.Redirect("/AccesoDenegado"); return; }
        await next();
    });
    b.UseStaticFiles(CacheEstatico("/gestion"));
});

// Almacén de equipos (rutas legacy): las rige el permiso almacen.consultar; las operaciones las valida la API.
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/almacen"), b =>
{
    b.UseAuthentication();
    b.Use(async (ctx, next) =>
    {
        if (!Puede(ctx, CatalogoPermisos.AlmacenConsultar)) { Denegar(ctx); return; }
        await next();
    });
    b.UseStaticFiles(CacheEstatico("/almacen"));
});

app.UseStaticFiles(CacheEstatico());
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// El tema de cada cuenta (bloques «Tema» del botón ⚙) se pinta antes de que nadie llame a una API, así
// que viaja en la cookie `meditechnosys.tema`. La cookie es una **copia**: manda lo guardado en la cuenta,
// y aquí se mantiene al día —quien cambió su tema desde otro equipo lo ve corregido en cuanto navega o
// llama a la API— y se retira cuando no hay sesión abierta, para que la persona que entre después en este
// mismo equipo no herede el tema de la anterior. Las pantallas autónomas (`/dicom/`, `/gestion/`…) las
// sirve `UseStaticFiles` más arriba, sin tocar sesión: ellas usan la cookie que el navegador ya trae
// puesta de la última pantalla de la intranet o del último ⚙.
app.Use(async (ctx, next) =>
{
    var usuario = ctx.User.Identity?.IsAuthenticated == true ? ctx.User.Identity?.Name : null;
    if (usuario is not null)
    {
        var deseado = TemaPreferencias.Valor(ctx.RequestServices.GetRequiredService<IConfigStore>(), usuario);
        if (TemaPreferencias.LeerDe(ctx.Request.Cookies) != deseado)
            TemaPreferencias.EscribirEnEn(ctx, deseado);
    }
    else if (TemaPreferencias.LeerDe(ctx.Request.Cookies) is not null)
    {
        TemaPreferencias.Borrar(ctx.Response.Cookies);
    }
    await next();
});

// Bitacora de movimientos: deja rastro de las pantallas que abre y las acciones que hace
// cada usuario durante el dia. Los archivos estaticos no llegan aqui (los sirve UseStaticFiles
// antes), asi que solo se registran navegaciones y acciones reales. Nunca se registra
// /api/status (sondeo del panel) ni la propia bitacora, para no generar ruido.
app.Use(async (ctx, next) =>
{
    await next();
    var usuario = ctx.User.Identity?.IsAuthenticated == true ? ctx.User.Identity.Name : null;
    if (usuario is null) return;

    var path = ctx.Request.Path.Value ?? "/";
    var method = ctx.Request.Method;
    var esPagina = method == "GET" && (path == "/" || path.StartsWith("/Admin") || path.StartsWith("/Ingenieria")
                                       || path.StartsWith("/patologia") || path.StartsWith("/dicom")
                                       || path.StartsWith("/admin") || path.StartsWith("/gestion")
                                       || path.StartsWith("/calendario"));
    var esAccion = method != "GET" && path.StartsWith("/api/")
                   && !path.StartsWith("/api/login") && !path.StartsWith("/api/logout")
                   && !path.StartsWith("/api/status") && !path.StartsWith("/api/bitacora")
                   && !path.StartsWith("/api/quemador")    // el quemador tiene su propia bitácora, sin duplicar en la de accesos
                   && !path.StartsWith("/api/usuarios")      // los cambios de usuarios se registran aparte, con su evento propio
                   && !path.StartsWith("/api/actividades")   // las actividades/tickets tambien tienen evento propio
                   && !path.StartsWith("/api/inventario")    // los inventarios tienen evento propio
                   && !path.StartsWith("/api/playbook")      // el Playbook tiene evento propio
                   && !path.StartsWith("/api/roles")         // los cambios de permisos tienen evento propio
                   && !path.StartsWith("/api/config")        // los cambios de configuracion tienen evento propio
                   && !path.StartsWith("/api/sesion")        // el tiempo de sesion se anota con su propio evento
                   && !path.StartsWith("/api/tema")          // el tema de la cuenta tampoco: tiene evento propio
                   && !path.StartsWith("/api/actas")         // las actas tienen evento propio
                   && !path.StartsWith("/api/credenciales")  // las credenciales tienen evento propio
                   && !path.StartsWith("/api/ubicacion");    // el lugar lo reporta el equipo solo: no es una acción de la persona
    var esConsulta = method == "GET" && (path.StartsWith("/api/patologia") || path.StartsWith("/api/dicom"));
    if (!esPagina && !esAccion && !esConsulta) return;

    var query = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : "";
    ctx.RequestServices.GetRequiredService<IAuditLog>().Register(
        usuario, AuditEvents.Acceso, $"{method} {path}{query} → {ctx.Response.StatusCode}",
        ctx.Connection.RemoteIpAddress?.ToString());
});

app.MapRazorPages();
// Liveness no toca dependencias externas; readiness solo acepta tráfico si MySQL responde.
// No devuelven detalles de conexión ni datos de pacientes.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();
// Direccion corta y publica: sirve la misma pantalla sin redirigir, para que la barra del navegador
// muestre solamente /consulta. El grupo de la API sigue siendo /api/consulta-patologia.
app.MapGet("/consulta", (IWebHostEnvironment env, HttpContext ctx) =>
{
    ctx.Response.Headers["Cache-Control"] = "no-store";
    ctx.Response.Headers["Pragma"] = "no-cache";
    return Results.File(Path.Combine(env.ContentRootPath, "wwwroot", "consulta-patologia", "index.html"), "text/html");
}).AllowAnonymous();
app.MapGet("/consulta-patologia", () => Results.Redirect("/consulta")).AllowAnonymous();

// Consulta pública independiente: /consulta-patologia/ queda fuera de la ruta protegida /patologia.
// Su API solo permite buscar y leer PDF; no expone alta, edición ni eliminación.
app.MapPatoConsultaPublicaApi();

// Mapear API de patología solo si el servicio está habilitado
if (servicios.PatologiaConsultar)
{
    app.MapPatoApi();
}

// Middleware para bloquear APIs de servicios deshabilitados (se ejecuta antes de los controladores)
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";

    // Bloquear API de patología si está deshabilitado
    if (!servicios.PatologiaConsultar && path.StartsWith("/api/patologia"))
    {
        ctx.Response.StatusCode = 404;
        return;
    }

    // Bloquear API de visor DICOM si está deshabilitado
    if (!servicios.DicomVer && path.StartsWith("/api/dicom") && !path.StartsWith("/api/dicom/enviar") && !path.StartsWith("/api/dicom/worklist"))
    {
        ctx.Response.StatusCode = 404;
        return;
    }

    // Bloquear API de envío DICOM si está deshabilitado
    if (!servicios.DicomEnviar && path.StartsWith("/api/dicom/enviar"))
    {
        ctx.Response.StatusCode = 404;
        return;
    }

    // Bloquear API de lista de trabajo DICOM si está deshabilitado
    if (!servicios.DicomListaTrabajo && path.StartsWith("/api/dicom/worklist"))
    {
        ctx.Response.StatusCode = 404;
        return;
    }

    await next();
});

// Mapear controladores (incluye DICOM controllers)
app.MapControllers();

// ---- API ----

// Inicio de sesion para clientes como Postman: devuelve la cookie de sesion.
app.MapPost("/api/login", async (LoginRequest req, IAuthenticator auth, IAuditLog bitacora, IConfigStore cfg, HttpContext ctx) =>
{
    var nombre = (req.Usuario ?? "").Trim();
    var ip = ctx.Connection.RemoteIpAddress?.ToString();
    var user = await auth.ValidateAsync(nombre, req.Password ?? "");
    if (user is null)
    {
        bitacora.Register(nombre, AuditEvents.LoginFallido, "API", ip);
        return Results.Json(new { error = "Usuario o contraseña incorrectos." }, statusCode: 401);
    }

    var identity = new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Role, user.Role) },
        CookieAuthenticationDefaults.AuthenticationScheme);
    // Misma razón que en `Pages/Login.cshtml.cs`: la caducidad absoluta va un poco más allá de la
    // ventana más larga para que sea `SesionPreferencias.Expiro` quien cierre la sesión y deje el
    // aviso `sesion=expirada`, y no el vencimiento del ticket, que redirige en silencio.
    var propsLogin = new AuthenticationProperties
    {
        ExpiresUtc = DateTimeOffset.UtcNow.Add(SesionPreferencias.VidaCookie),
    };
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), propsLogin);
    // Misma copia del tema en la cookie que en el acceso por pantalla (`Pages/Login.cshtml.cs`).
    TemaPreferencias.EscribirEnEn(ctx, TemaPreferencias.Valor(cfg, user.Name));
    bitacora.Register(user.Name, AuditEvents.Login, "API", ip);
    return Results.Ok(new { usuario = user.Name, rol = user.Role });
}).AllowAnonymous().RequireRateLimiting("login");

app.MapPost("/api/logout", async (HttpContext ctx, IAuditLog bitacora) =>
{
    bitacora.Register(ctx.User.Identity?.Name ?? "", AuditEvents.Logout, "API", ctx.Connection.RemoteIpAddress?.ToString());
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    TemaPreferencias.Borrar(ctx.Response.Cookies);
    return Results.NoContent();
}).RequireAuthorization();

// /api/status: panel SISTEMAS. Combina los sistemas externos fijos (GenDocM, Correo, VPN) con
// sondeos REALES: base de datos (ping MySQL), visor de estudios (alcance al PACS) y consulta de
// reportes (carpeta del repositorio), depuración automática de descargas DICOM y el almacén de
// usuarios. El navegador lo refresca cada minuto.
app.MapGet("/api/status", async (AuthStoreInfo auth, IConfiguration cfg, PatoDb pato, DicomQrService dicom, LimpiadorDescargas depurador) =>
{
    var reales = new[]
    {
        await Salud.BaseDeDatosAsync(cfg.GetConnectionString("Usuarios")),
        await Salud.VisorDicomAsync(dicom.Nodes),
        Salud.ConsultaReportes(pato),
        Salud.DepuracionDescargas(depurador)
    };
    return Results.Json(
        Catalog.Status().Concat(reales).Concat(new[] { auth.Status() }),
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
})
    .RequireAuthorization();

// Lista ligera de usuarios asignables (usuario, rol, nombre de persona y cargo) para los diálogos de
// asignación/edición del módulo Gestión y para autocompletar nombre/puesto en actas y credenciales.
// No exige administrar usuarios: basta con poder asignar o supervisar, para que un
// rol personalizado con tickets.asignar pueda elegir ingeniero sin ver el panel de cuentas.
app.MapGet("/api/usuarios/asignables", (IUserStore s) =>
    Results.Json(s.List().Where(u => u.Activo).Select(u => new { usuario = u.Usuario, rol = u.Rol, nombre = u.Nombre, cargo = u.Cargo })))
    .RequireAuthorization(p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.UsuariosAdministrar)
        || ctx.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.TicketsAsignar)
        || ctx.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.TicketsCambiarIngeniero)
        || ctx.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.SupervisionIngeniero)
        || ctx.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.HistoricoActividades)
        || ctx.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.BitacoraVer)
        || ctx.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.ActasAdministrar)
        || ctx.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.CredencialesAdministrar)));

// Opciones de JSON compartidas por los endpoints que devuelven texto con acentos o eñes (nombres de
// región, servicios y hospitales). Se declaran aquí, antes del primer uso, porque son locales al
// programa: antes `json` nacía más abajo y cualquier endpoint nuevo de arriba no podía verlo.
var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

// Gestion de usuarios: exige el permiso efectivo usuarios.administrar. Cada cambio queda en la
// bitacora con el administrador que lo hizo (me) y el usuario afectado.
var users = app.MapGroup("/api/usuarios").RequireAuthorization($"P:{CatalogoPermisos.UsuariosAdministrar}");

// La región asignada viaja en la misma lista: la pantalla la muestra en su columna sin tener que
// pedir un renglón por usuario. `regiones` es la lista escrita (vacía = sin asignación) y `veTodas` lo que
// realmente ve, que es cierto para root y para quien no tenga región.
users.MapGet("/", (IUserStore s, GestionCatalogos catalogos) =>
    s.List().Select(u => new
    {
        u.Usuario, u.Rol, u.Creado, u.Activo, u.Nombre, u.Cargo,
        regiones = catalogos.RegionGuardadaDe(u.Usuario).Regiones,
        verTodas = catalogos.RegionGuardadaDe(u.Usuario).Todas,
        veTodas = catalogos.VeTodasRegiones(u.Usuario),
    }));

// Regiones disponibles para el desplegable de la pantalla Usuarios. Se lee aquí y no desde
// /api/programador/catalogos porque ese lo contesta solo root; el Administrador que asigna regiones
// no tiene por qué serlo.
users.MapGet("/regiones", (GestionCatalogos catalogos) => Results.Json(new
{
    regiones = catalogos.Regiones(),
    hospitalesPorRegion = catalogos.HospitalesPorRegion(),
}, json));

users.MapPost("/", (CreateUserRequest r, IUserStore s, IRoleStore roles, IAuditLog bitacora, ClaimsPrincipal me,
    CorreoService correo, HttpContext ctx) =>
{
    var rol = (r.Rol ?? "").Trim();
    if (rol.Length > 0 && !roles.Existe(rol))
        return Results.Json(new { error = "El rol indicado no existe." }, statusCode: 400);
    var (status, error, user) = s.Create(r.Usuario ?? "", r.Password ?? "", rol, true, r.Nombre ?? "", r.Cargo ?? "");
    if (status != 201) return Results.Json(new { error }, statusCode: status);
    bitacora.Register(me.Identity?.Name ?? "", AuditEvents.CrearUsuario,
        $"{user!.Usuario} ({Roles.Display(user.Rol)})", ctx.Connection.RemoteIpAddress?.ToString());

    // Buzón del usuario: se enlaza en la misma alta si el formulario trajo datos. Si la cuenta ya
    // se creo pero el buzon no sirvio, se avisa en `buzonError` (el alta no se revierte).
    string? buzonError = null;
    if (r.Buzon is { } b && !string.IsNullOrWhiteSpace(b.Correo))
    {
        var (bs, be) = correo.GuardarBuzon(user.Usuario, b);
        buzonError = bs == 204 ? null : be;
        if (bs == 204)
            bitacora.Register(me.Identity?.Name ?? "", AuditEvents.EnlazarBuzon,
                $"{user.Usuario} · {b.Correo}", ctx.Connection.RemoteIpAddress?.ToString());
    }

    return Results.Created($"/api/usuarios/{user!.Usuario}", new
    {
        user.Usuario, user.Rol, user.Creado, user.Activo, user.Nombre, user.Cargo,
        buzon = correo.Publico(user.Usuario), buzonError
    });
});

users.MapPut("/{usuario}/password", (string usuario, PasswordRequest r, IUserStore s, IAuditLog bitacora, ClaimsPrincipal me, HttpContext ctx) =>
{
    if (UserRules.IsRoot(usuario) && !UserRules.IsRoot(me.Identity?.Name))
        return Results.Json(new { error = "Solo root puede cambiar su contraseña." }, statusCode: 403);
    var (status, error) = s.SetPassword(usuario, r.Password ?? "");
    if (status == 204)
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.CambiarPassword, usuario, ctx.Connection.RemoteIpAddress?.ToString());
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
});

users.MapPut("/{usuario}/rol", (string usuario, RoleRequest r, ClaimsPrincipal me, IUserStore s, IRoleStore roles, IAuditLog bitacora, IPermissionResolver resolver, HttpContext ctx) =>
{
    if (string.Equals(me.Identity?.Name, usuario, StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "No puedes cambiar tu propio rol." }, statusCode: 400);
    var rol = (r.Rol ?? "").Trim();
    if (rol.Length == 0 || !roles.Existe(rol))
        return Results.Json(new { error = "El rol indicado no existe." }, statusCode: 400);
    var (status, error) = s.SetRole(usuario, rol);
    if (status == 204)
    {
        resolver.Invalidar();
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.CambiarRol, $"{usuario} → {Roles.Display(rol)}", ctx.Connection.RemoteIpAddress?.ToString());
    }
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
});

// Activa o desactiva una cuenta sin borrarla: una cuenta desactivada no puede iniciar sesion.
users.MapPut("/{usuario}/activo", (string usuario, ActivoRequest r, ClaimsPrincipal me, IUserStore s, IAuditLog bitacora, HttpContext ctx) =>
{
    if (string.Equals(me.Identity?.Name, usuario, StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "No puedes desactivar tu propio usuario." }, statusCode: 400);
    var activo = r.Activo ?? true;
    var (status, error) = s.SetActive(usuario, activo);
    if (status == 204)
        bitacora.Register(me.Identity?.Name ?? "", activo ? AuditEvents.Activar : AuditEvents.Desactivar, usuario, ctx.Connection.RemoteIpAddress?.ToString());
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
});

// Nombre de persona y cargo: datos "de ficha" que se imprimen en actas/credenciales y que se
// autocompletan al elegir al usuario. Se validan y recortan en el servidor (UserRules) aunque el
// formulario ya limite la longitud, y ambos campos son opcionales.
users.MapPut("/{usuario}/datos", (string usuario, DatosUsuarioRequest r, IUserStore s, IAuditLog bitacora, ClaimsPrincipal me, HttpContext ctx) =>
{
    var nombre = UserRules.LimpiarTexto(r.Nombre, UserRules.NombreMax);
    var cargo = UserRules.LimpiarTexto(r.Cargo, UserRules.CargoMax);
    if ((r.Nombre ?? "").Length > UserRules.NombreMax)
        return Results.Json(new { error = $"El nombre no puede pasar de {UserRules.NombreMax} caracteres." }, statusCode: 400);
    if ((r.Cargo ?? "").Length > UserRules.CargoMax)
        return Results.Json(new { error = $"El cargo no puede pasar de {UserRules.CargoMax} caracteres." }, statusCode: 400);
    var (status, error) = s.SetDatos(usuario, nombre, cargo);
    if (status == 204)
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.CambiarDatos, usuario, ctx.Connection.RemoteIpAddress?.ToString());
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
});

// Regiones que ve un usuario. Se guarda en el catálogo de configuración (no en la tabla de usuarios),
// así que sobrevive reinicios y no necesita migración. root no usa esta clave: siempre ve todas.
users.MapGet("/{usuario}/region", (string usuario, IUserStore s, GestionCatalogos catalogos) =>
{
    if (s.Find(usuario) is null) return Results.NotFound(new { error = "El usuario no existe." });
    var guardado = catalogos.RegionGuardadaDe(usuario);
    return Results.Json(new
    {
        regiones = guardado.Regiones,
        todas = guardado.Todas,
        veTodas = catalogos.VeTodasRegiones(usuario),
        esRoot = UserRules.IsRoot(usuario),
        regionesDisponibles = catalogos.Regiones(),
        hospitales = catalogos.HospitalesPara(usuario),
    }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
});

// `regiones` vacía = sin asignación, que es lo que manda la lista «Todas las regiones» de la pantalla
// Usuarios. El campo `todas` ya no lo usa la pantalla (hacía lo mismo que el renglón vacío), pero se
// sigue aceptando: los renglones guardados en entregas anteriores pueden traerlo marcado.
users.MapPut("/{usuario}/region", (string usuario, RegionUsuarioRequest r, IUserStore s,
    GestionCatalogos catalogos, IAuditLog bitacora, ClaimsPrincipal me, HttpContext ctx) =>
{
    if (s.Find(usuario) is null) return Results.NotFound(new { error = "El usuario no existe." });
    var (status, error) = catalogos.GuardarRegionPorUsuario(usuario, r.Regiones, r.Todas ?? false);
    if (status == 204)
    {
        var guardado = catalogos.RegionGuardadaDe(usuario);
        // En la bitácora se escribe lo que la persona va a ver, no cómo se guardó: sin región o con la
        // antigua opción marcada, las dos cosas son «todas las regiones».
        var regionesTexto = guardado.Regiones.Count > 0 ? string.Join(", ", guardado.Regiones) : "todas las regiones";
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.AsignarRegion,
            $"{usuario} → {regionesTexto}",
            ctx.Connection.RemoteIpAddress?.ToString());
        return Results.NoContent();
    }
    return Results.Json(new { error }, statusCode: status);
});

users.MapDelete("/{usuario}", (string usuario, ClaimsPrincipal me, IUserStore s, IAuditLog bitacora,
    IBuzonStore buzones, GestionCatalogos catalogos, HttpContext ctx) =>
{
    if (string.Equals(me.Identity?.Name, usuario, StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "No puedes eliminar tu propio usuario." }, statusCode: 400);
    var (status, error) = s.Delete(usuario);
    if (status == 204)
    {
        // La baja de la cuenta arrastra su buzon enlazado: no quedan contrasenas huespedes. Lo mismo
        // con la región asignada, para que no quede un renglón suelto en el catálogo.
        buzones.Eliminar(usuario);
        catalogos.GuardarRegionPorUsuario(usuario, (IReadOnlyList<string>?)null, false);
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.EliminarUsuario, usuario, ctx.Connection.RemoteIpAddress?.ToString());
    }
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
});

// Bitacora de accesos: exige el permiso efectivo bitacora.ver. Filtros opcionales ?usuario= y ?dia=yyyy-mm-dd.
// Cada renglón sale además con el equipo (DNS inverso de la IP) y la ubicación (tabla de rangos).
app.MapGet("/api/bitacora", async (string? usuario, DateTime? dia, IAuditLog bitacora,
    UbicacionResolver ubicaciones, CancellationToken ct) =>
    Results.Json(await ubicaciones.EnriquecerAsync(bitacora.Query(usuario, dia, limite: 1000), ct),
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))
    .RequireAuthorization($"P:{CatalogoPermisos.BitacoraVer}");

// Resumen por usuario para la bitacora: ultima vez que inicio sesion, si la cuenta esta activa
// y si esta "Conectado" (tuvo actividad reciente) o "Desconectado". La actividad reciente se
// deduce del ultimo movimiento de cualquier tipo dentro del umbral (config Bitacora:UmbralConectadoMinutos,
// 15 min por defecto), porque la web no mantiene un estado de conexion permanente.
app.MapGet("/api/bitacora/resumen", async (IUserStore usuarios, IAuditLog bitacora, IConfiguration config,
    UbicacionResolver ubicaciones, CancellationToken ct) =>
{
    var umbralMin = config.GetValue<int?>("Bitacora:UmbralConectadoMinutos") ?? 15;
    if (umbralMin <= 0) umbralMin = 15;
    var corte = DateTime.UtcNow.AddMinutes(-umbralMin);

    var ultimos = bitacora.UltimosAccesos();
    var actividades = bitacora.UltimasActividades();
    var detalles = bitacora.UltimosDetalles();
    var ahora = DateTime.UtcNow;

    // De cada usuario se toma su último movimiento para decir desde dónde entró: la IP se resuelve
    // a nombre de equipo (DNS inverso, en memoria) y a ubicación (tabla de rangos del admin).
    var filas = usuarios.List()
        .Select(u =>
        {
            var ultimoAcceso = ultimos.TryGetValue(u.Usuario, out var m) ? m : (DateTime?)null;
            var ultimaActividad = actividades.TryGetValue(u.Usuario, out var a) ? a : (DateTime?)null;
            var conectado = ultimaActividad.HasValue && ultimaActividad.Value >= corte;
            detalles.TryGetValue(u.Usuario, out var d);
            return new
            {
                usuario = u.Usuario,
                activo = u.Activo,
                ultimoAcceso,
                ultimaActividad,
                conectado,
                umbralMinutos = umbralMin,
                ip = d?.Ip ?? "",
                dispositivo = d?.Dispositivo ?? "",
                eventoReciente = d?.Evento ?? "",
                momentoReciente = d?.Momento
            };
        })
        .OrderByDescending(x => x.conectado)
        .ThenByDescending(x => x.ultimaActividad ?? x.ultimoAcceso ?? DateTime.MinValue)
        .ThenBy(x => x.usuario, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var nombres = await ubicaciones.EquiposAsync(filas.Select(f => f.ip), ct);
    // Una sola decisión por IP: la marca dice de dónde salió la ubicación —posición real del equipo,
    // punto aprendido de su rango, o «Sin confirmar»— y cuánto vale ese punto en metros.
    var marcas = filas.Select(f => f.ip).Distinct(StringComparer.OrdinalIgnoreCase)
        .ToDictionary(ip => ip, ip => ubicaciones.MarcaDe(ip), StringComparer.OrdinalIgnoreCase);
    var lista = filas.Select(f =>
    {
        var marca = marcas.TryGetValue(f.ip, out var m) ? m : (Marca: "", Metros: 0);
        return new
        {
            f.usuario, f.activo, f.ultimoAcceso, f.ultimaActividad, f.conectado, f.umbralMinutos,
            f.ip, f.dispositivo, f.eventoReciente, f.momentoReciente,
            equipo = nombres.TryGetValue(f.ip, out var n) ? n : "",
            ubicacion = ubicaciones.UbicacionDe(f.ip),
            fuenteUbicacion = marca.Marca,
            precisionMetros = marca.Metros
        };
    }).ToList();
    return Results.Json(lista, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
})
    .RequireAuthorization($"P:{CatalogoPermisos.BitacoraVer}");

// Descarga en Excel de los movimientos de la bitácora. Exporta exactamente lo que muestra la
// pantalla: mismos filtros (usuario, día y hora) aplicados en hora LOCAL para que coincidan con
// las filas visibles. Se genera .xlsx real (ClosedXML); nunca CSV.
app.MapGet("/api/bitacora/export", async (string? usuario, DateTime? dia, int? hora, string? donde,
    ClaimsPrincipal me, IAuditLog bitacora, UbicacionResolver ubicaciones, HttpContext ctx, CancellationToken ct) =>
{
    var entradas = await ubicaciones.EnriquecerAsync(bitacora.Query(usuario, dia, limite: 5000), ct);
    // El mismo texto libre de la caja «Equipo · IP · ubicación» aplica al Excel, para que lo que
    // se descarga coincida con lo que se ve en pantalla (sin distinguir acentos ni mayúsculas).
    var filtro = (donde ?? "").Trim();
    var filas = entradas
        .Select(e => new { e, local = e.Momento.ToLocalTime() })
        .Where(x => hora is null || x.local.Hour == hora.Value)
        .Where(x => filtro.Length == 0 ||
            Texto.Contiene($"{x.e.Ip} {x.e.Equipo} {x.e.Ubicacion} {x.e.Dispositivo}", filtro))
        .OrderByDescending(x => x.local)
        .Select(x => (IReadOnlyList<string>)new[]
        {
            x.local.ToString("dd/MM/yyyy"),
            x.local.ToString("HH:mm:ss"),
            x.e.Usuario,
            x.e.Evento,
            x.e.Detalle,
            x.e.Ip,
            x.e.Equipo,
            x.e.Ubicacion,
            // El punto del mapa que respalda esa ubicación: junto con la dirección es lo que sostiene
            // una justificación de falta ante Recursos Humanos.
            ubicaciones.CoordenadasDe(x.e.Ip),
            // Y de dónde salió: si la dio el propio aparato, se anota su margen en metros.
            x.e.FuenteUbicacion.Length == 0 ? ""
                : x.e.PrecisionMetros > 0 ? $"{x.e.FuenteUbicacion} (±{x.e.PrecisionMetros} m)"
                : x.e.FuenteUbicacion,
            x.e.Dispositivo
        })
        .ToList();

    var encabezados = new[] { "Fecha", "Hora", "Usuario", "Evento", "Detalle", "IP", "Equipo", "Ubicación", "Coordenadas", "Ubicación obtenida", "Dispositivo" };
    var archivo = Excel.Tabla("Bitácora", encabezados, filas);

    var partes = new List<string> { "bitacora" };
    if (!string.IsNullOrWhiteSpace(usuario)) partes.Add(usuario.Trim());
    if (dia is not null) partes.Add(dia.Value.ToString("yyyy-MM-dd"));
    if (hora is not null) partes.Add($"{hora.Value:D2}h");
    var nombre = string.Join("_", partes) + ".xlsx";

    bitacora.Register(me.Identity?.Name ?? "", AuditEvents.ExportarExcel,
        $"Bitácora de accesos · {filas.Count} movimiento(s)", Ip(ctx));
    return Results.File(archivo, Excel.ContentType, nombre);
})
    .RequireAuthorization($"P:{CatalogoPermisos.BitacoraVer}");

// ---- Bitácora unificada: accesos + quemador en una sola hoja ----
// Las dos bitácoras se siguen guardando cada una en su tabla —con sus columnas y su historia—; lo que
// se unifica aquí es la lectura. Cada renglón se traduce a la misma forma (fecha, hora, usuario,
// actividad, IP, ubicación geográfica y «qué fue lo que hizo») y los dos listados se mezclan por hora,
// así que en una sola pantalla se ve que fulano entró mal con la contraseña, después movió un ticket y
// más tarde quemó un estudio en USB, sin brincar de una pantalla a otra.
// El alcance se respeta: quien tiene «bitácora.ver» ve la parte de accesos; quien tiene solo
// «quemador.ver», la del quemador; quien tiene ambas, todo junto. Nunca se mezclan filas que la persona
// que consulta no puede ver.
static bool VeAccesos(HttpContext ctx) => Puede(ctx, CatalogoPermisos.BitacoraVer);
static bool VeQuemador(HttpContext ctx) => Puede(ctx, CatalogoPermisos.QuemadorVer);

// Las dos mitades de la bitácora, traducidas a renglones idénticos y mezcladas por hora. El usuario,
// el día y el rango de fechas se empujan a cada almacén (son índices de sus tablas); lo demás se filtra
// después sobre la mezcla, para que «actividad», «fuente» o «qué hizo» puedan buscar a la vez en las dos
// procedencias.
async Task<List<Movimiento>> MovimientosUnificados(string? usuario, DateTime? dia, DateTime? desde,
    DateTime? hasta, int limite,
    HttpContext ctx, CancellationToken ct, UbicacionResolver ubicaciones, IAuditLog accesos,
    IQuemadorLog quemador)
{
    var filas = new List<Movimiento>();

    // 1) La parte de la intranet: el enriquecedor ya resuelve nombre de equipo, ubicación y su fuente.
    if (VeAccesos(ctx))
        filas.AddRange((await ubicaciones.EnriquecerAsync(accesos.Query(usuario, dia, limite, desde, hasta), ct))
            .Select(Movimientos.DesdeWeb));

    // 2) La parte del quemador: una fila por estudio tocado, con su lote, su resultado y su medio.
    if (VeQuemador(ctx))
    {
        var eventos = quemador.Consultar(new QuemadorFiltro(usuario, null, null, null, null, dia, limite,
            Desde: desde, Hasta: hasta));
        var nombres = await ubicaciones.EquiposAsync(eventos.Select(e => e.Ip), ct);
        filas.AddRange(eventos.Select(q =>
        {
            var ip = q.Ip ?? "";
            var (marca, metros) = ubicaciones.MarcaDe(ip);
            var equipo = (q.Equipo ?? "").Trim();
            return Movimientos.DesdeQuemador(q) with
            {
                // El mostrador reporta el nombre de su propio equipo; si viene vacío, se saca del DNS.
                Equipo = equipo.Length > 0 ? equipo
                        : nombres.TryGetValue(ip, out var n) ? n : "",
                Ubicacion = ubicaciones.UbicacionDe(ip),
                FuenteUbicacion = marca,
                PrecisionMetros = metros,
                Coordenadas = ubicaciones.CoordenadasDe(ip),
            };
        }));
    }

    // El punto del mapa que respalda cada renglón de accesos: la ubicación ya está decidida, solo se
    // anota su coordenada para que el Excel la lleve junto con la dirección.
    for (var i = 0; i < filas.Count; i++)
        if (filas[i].Coordenadas.Length == 0)
            filas[i] = filas[i] with { Coordenadas = ubicaciones.CoordenadasDe(filas[i].Ip) };

    return filas.OrderByDescending(m => m.Momento).ThenByDescending(m => m.Id).ToList();
}

// Filtros que se aplican sobre la mezcla ya armada (hora local, actividad, familia, origen, lote y
// texto libre). Son los mismos que usa la pantalla, para que el Excel coincida con lo que se ve.
// `hora`/`horaHasta` son un rango de horas inclusivo en hora local; `desde`/`hasta` reafirman el rango
// de fechas sobre la mezcla, por si algún almacén no lo empujó a su tabla.
static IEnumerable<Movimiento> FiltraMovimientos(IReadOnlyList<Movimiento> ms, int? hora, int? horaHasta,
    string? actividad, string? grupo, string? fuente, string? lote, string? texto,
    DateTime? desde = null, DateTime? hasta = null)
{
    var f = (fuente ?? "").Trim().ToLowerInvariant();
    var d0 = desde?.Date;
    var d1 = hasta?.Date;
    return ms
        .Where(m => d0 is null || m.Momento.ToLocalTime().Date >= d0)
        .Where(m => d1 is null || m.Momento.ToLocalTime().Date <= d1)
        .Where(m => hora is null ||
                    (m.Momento.ToLocalTime().Hour >= hora.Value &&
                     m.Momento.ToLocalTime().Hour <= (horaHasta ?? hora).Value))
        .Where(m => string.IsNullOrWhiteSpace(actividad) ||
                    m.Actividad.Equals(actividad.Trim(), StringComparison.OrdinalIgnoreCase))
        .Where(m => string.IsNullOrWhiteSpace(grupo) ||
                    m.Grupo.Equals(grupo.Trim(), StringComparison.OrdinalIgnoreCase))
        .Where(m => f.Length == 0 || (f.StartsWith("quem")
            ? m.Origen == Movimientos.OrigenQuemador : m.Origen == Movimientos.OrigenWeb))
        .Where(m => string.IsNullOrWhiteSpace(lote) ||
                    m.Lote.Equals(lote.Trim(), StringComparison.OrdinalIgnoreCase))
        .Where(m => Texto.Contiene(m.TextoBuscable, texto));
}

static IReadOnlyList<string> FilaDeExcel(Movimiento m) => new[]
{
    m.Fecha, m.Hora, m.Usuario, m.Actividad, m.Ip, m.UbicacionCompleta,
    (m.Modulo.Length > 0 && m.Modulo != Movimientos.OrigenQuemador ? m.Modulo + " → " : "") + m.Detalle,
    m.FuenteUbicacion, m.Coordenadas, m.Equipo, m.Dispositivo, m.Origen, m.Resultado,
    m.Paciente, m.PacienteId, m.Acceso, m.EstudioUid,
    m.Medio.Length > 0 ? $"{m.Medio} · {m.Unidad}" : "", m.Unidad, m.Lote
};

// Basta con uno de los dos permisos para abrir la bitácora unificada; dentro de ella cada quien ve solo
// la mitad que le toca (vea MovimientosUnificados).
app.MapGet("/api/bitacora/unificada", async (string? usuario, DateTime? dia, int? limite,
    DateTime? desde, DateTime? hasta,
    HttpContext ctx, CancellationToken ct, UbicacionResolver ubicaciones, IAuditLog accesos,
    IQuemadorLog quemador) =>
{
    var tope = Math.Clamp(limite ?? 1000, 1, 5000);
    var movimientos = await MovimientosUnificados(usuario, dia, desde, hasta, tope,
        ctx, ct, ubicaciones, accesos, quemador);
    return Results.Json(new
    {
        puedeAccesos = VeAccesos(ctx),
        puedeQuemador = VeQuemador(ctx),
        limite = tope,
        // Avisa cuando el corte dejó renglones sin mostrar, para no leer «no hay más» donde sí los hay.
        cortado = movimientos.Count >= tope,
        actividades = movimientos.Select(m => m.Actividad).Distinct()
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
        movimientos
    }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
})
    .RequireAuthorization(p => p.RequireAssertion(c =>
        c.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.BitacoraVer) ||
        c.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.QuemadorVer)));

// Excel con exactamente los filtros que muestra la pantalla (la hora y el rango de fechas se
// interpretan en hora local, igual que en la ventana).
app.MapGet("/api/bitacora/unificada/export", async (string? usuario, DateTime? dia, int? hora,
    int? horaHasta, DateTime? desde, DateTime? hasta,
    string? actividad, string? grupo, string? fuente, string? lote, string? texto,
    ClaimsPrincipal me, HttpContext ctx, CancellationToken ct, UbicacionResolver ubicaciones,
    IAuditLog accesos, IQuemadorLog quemador) =>
{
    var filas = FiltraMovimientos(
            await MovimientosUnificados(usuario, dia, desde, hasta, 5000,
                ctx, ct, ubicaciones, accesos, quemador),
            hora, horaHasta, actividad, grupo, fuente, lote, texto, desde, hasta)
        .Select(FilaDeExcel).ToList();

    // Las seis columnas que se ven en pantalla van primero, en el mismo orden; detrás viene el detalle
    // que solo trae una de las dos bitácoras (paciente, UID, medio, unidad, lote).
    IReadOnlyList<string> encabezados = new[]
    {
        "Fecha", "Hora", "Usuario", "Actividad", "IP", "Ubicación geográfica", "Qué hizo",
        "Ubicación obtenida", "Coordenadas", "Equipo", "Dispositivo", "Fuente", "Resultado",
        "Paciente", "ID paciente", "Acceso", "UID del estudio", "Medio", "Unidad", "Lote"
    };
    var archivo = Excel.Tabla("Bitácora", encabezados, filas);
    var partes = new List<string> { "bitacora" };
    if (!string.IsNullOrWhiteSpace(usuario)) partes.Add(usuario.Trim());
    if (dia is not null) partes.Add(dia.Value.ToString("yyyy-MM-dd"));
    if (desde is not null) partes.Add("desde" + desde.Value.ToString("yyyy-MM-dd"));
    if (hasta is not null) partes.Add("hasta" + hasta.Value.ToString("yyyy-MM-dd"));
    if (hora is not null) partes.Add($"{hora.Value:D2}h");
    if (horaHasta is not null) partes.Add($"a{horaHasta.Value:D2}h");
    if (!string.IsNullOrWhiteSpace(fuente)) partes.Add(fuente.Trim().ToLowerInvariant());
    var nombre = string.Join("_", partes) + ".xlsx";

    accesos.Register(me.Identity?.Name ?? "", AuditEvents.ExportarExcel,
        $"Bitácora unificada · {filas.Count} movimiento(s)", Ip(ctx));
    return Results.File(archivo, Excel.ContentType, nombre);
})
    .RequireAuthorization(p => p.RequireAssertion(c =>
        c.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.BitacoraVer) ||
        c.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.QuemadorVer)));

// Índice de días del usuario: cuántos registros dejó en cada día local, sin bajar ningún renglón.
// La hoja de periodo de la ventana lo usa para marcar en dorado los días que sí tienen historia,
// para ofrecer solo meses y años reales y para saber si un periodo viejo cabe o no dentro del tope
// de renglones que se muestran. Sale de las dos tablas (o los dos JSONL) que la persona puede ver,
// así que quien solo tiene «quemador.ver» recibe el conteo del mostrador y nada más.
app.MapGet("/api/bitacora/periodos", (string? usuario, HttpContext ctx, IAuditLog accesos,
    IQuemadorLog quemador) =>
{
    var dias = new Dictionary<string, int>(StringComparer.Ordinal);
    void Sumar(IReadOnlyDictionary<string, int> de)
    {
        foreach (var (k, v) in de) dias[k] = dias.TryGetValue(k, out var n) ? n + v : v;
    }
    if (VeAccesos(ctx)) Sumar(accesos.DiasConMovimientos(usuario));
    if (VeQuemador(ctx)) Sumar(quemador.DiasConMovimientos(usuario));
    var ks = dias.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
    return Results.Json(new
    {
        dias,
        desde = ks.Count > 0 ? ks[0] : "",
        hasta = ks.Count > 0 ? ks[^1] : "",
        puedeAccesos = VeAccesos(ctx),
        puedeQuemador = VeQuemador(ctx)
    }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
})
    .RequireAuthorization(p => p.RequireAssertion(c =>
        c.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.BitacoraVer) ||
        c.User.HasClaim(GestionService.PermClaim, CatalogoPermisos.QuemadorVer)));

// ---- Mapa «rango de IP → ubicación física» que usa la bitácora ----
// Lo mantiene el administrador desde la misma pantalla. La ubicación NO se guarda en cada renglón:
// se deduce de la IP al consultar, así que corregir un rango acomoda también los registros viejos.
// El GET devuelve además los equipos que se han conectado (IP, nombre en la red y cuántas veces
// entraron), para que asignar una ubicación sea señalar la fila y escribir dónde está ese equipo.
app.MapGet("/api/bitacora/ubicaciones", async (IUbicacionStore store, UbicacionResolver resolver,
    IAuditLog bitacora, HttpContext ctx, CancellationToken ct) =>
{
    var vistas = bitacora.IpsRegistradas();
    var nombres = await resolver.EquiposAsync(vistas.Select(v => v.Ip), ct);
    var sitio = resolver.Sitio();
    return Results.Json(new
    {
        /// <summary>Si quien consulta puede editar el mapa (permiso configuracion.administrar).</summary>
        puedeAdministrar = Puede(ctx, CatalogoPermisos.ConfiguracionAdministrar),
        // La dirección geográfica del sitio: la calibrada si el administrador marcó el edificio, y si
        // no la que la intranet se procuró sola (aproximada: es la zona por donde sale el proveedor).
        sitio = new
        {
            direccion = sitio?.Direccion ?? "",
            fuente = sitio?.Fuente ?? "",
            momento = sitio?.MomentoUtc,
            latitud = sitio?.Latitud ?? 0,
            longitud = sitio?.Longitud ?? 0,
            actualizadoPor = sitio?.ActualizadoPor ?? ""
        },
        rangos = store.Listar(),
        equipos = vistas.Select(v =>
        {
            var lugar = resolver.LugarDe(v.Ip);
            return new
            {
                ip = v.Ip,
                usos = v.Usos,
                equipo = nombres.TryGetValue(v.Ip, out var n) ? n : "",
                // Lo que verá la bitácora: el rango escrito a mano, o si no hay, la dirección geográfica.
                ubicacion = resolver.UbicacionDe(v.Ip),
                // La dirección geográfica reportada por ese equipo, con cuándo y desde dónde salió.
                lugarGeografico = lugar?.Direccion ?? "",
                reportadoEn = lugar?.Momento,
                fuenteLugar = lugar?.Fuente ?? "",
                precisionMetros = lugar?.PrecisionMetros ?? 0,
                // El punto del mapa que respalda lo que muestra la columna Ubicación (mismo orden de
                // decisión que la bitácora): sirve para verificar la dirección antes de usarla.
                coordenadas = resolver.CoordenadasDe(v.Ip)
            };
        }).ToList()
    }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
})
    .RequireAuthorization($"P:{CatalogoPermisos.BitacoraVer}");

app.MapPut("/api/bitacora/ubicaciones", (RangoUbicacionRequest r, IUbicacionStore store,
    UbicacionResolver resolver, IAuditLog bitacora, ClaimsPrincipal me, HttpContext ctx) =>
{
    var rango = (r.Rango ?? "").Trim();
    var ubicacion = (r.Ubicacion ?? "").Trim();
    var (status, error) = store.Guardar(rango, ubicacion);
    if (status != 204) return Results.Json(new { error }, statusCode: status);

    resolver.InvalidaRangos();
    bitacora.Register(me.Identity?.Name ?? "", AuditEvents.CambiarConfig,
        $"Ubicación de la red: {rango} → {(ubicacion.Length > 0 ? ubicacion : "(borrada)")}", Ip(ctx));
    return Results.NoContent();
})
    .RequireAuthorization($"P:{CatalogoPermisos.ConfiguracionAdministrar}");

app.MapDelete("/api/bitacora/ubicaciones", (string? rango, IUbicacionStore store,
    UbicacionResolver resolver, IAuditLog bitacora, ClaimsPrincipal me, HttpContext ctx) =>
{
    var (status, error) = store.Borrar((rango ?? "").Trim());
    if (status != 204) return Results.Json(new { error }, statusCode: status);
    resolver.InvalidaRangos();
    bitacora.Register(me.Identity?.Name ?? "", AuditEvents.CambiarConfig,
        $"Quitó el rango de ubicación {(rango ?? "").Trim()}", Ip(ctx));
    return Results.NoContent();
})
    .RequireAuthorization($"P:{CatalogoPermisos.ConfiguracionAdministrar}");

// ---- La ubicación exacta del sitio: el administrador marca el edificio en el mapa ----
// Lo automático por IP solo sabe la zona por donde sale el proveedor de Internet, que suele fallar de
// colonia (aquí, por ejemplo, sale «Centro, Cuauhtémoc» cuando el edificio está en Vallejo). Este punto
// es el que corrige eso: se guarda el latitud/longitud marcado, OpenStreetMap lo traduce a dirección
// postal COMPLETA (calle y número, colonia, código postal, alcaldía, ciudad) y esa dirección manda sobre
// todo lo automático. Si además se quiere el puesto exacto de cada aparato, se escribe su rango arriba;
// la calibración es para el edificio completo.
app.MapPost("/api/bitacora/sitio", async (SitioCalibrarRequest r, ClaimsPrincipal me, ISitioStore tienda,
    GeocodificadorLugar geocoder, UbicacionResolver resolver, IAuditLog bitacora, HttpContext ctx,
    CancellationToken ct) =>
{
    var lat = r.Latitud ?? double.NaN;
    var lon = r.Longitud ?? double.NaN;
    var hayPunto = GeocodificadorLugar.EsCoordenada(lat, lon);
    var escrita = (r.Direccion ?? "").Trim();

    if (!hayPunto && escrita.Length == 0)
        return Results.Json(new { error = "Marque el punto en el mapa o escriba la dirección." }, statusCode: 400);

    var direccion = escrita;
    var edificio = "";
    if (hayPunto)
    {
        // Lo que el administrador dejó en el campo es lo que se guarda: él ya vio la dirección que el
        // mapa sacó del punto (la vista previa la escribe ahí) y pudo corregirla. El mapa solo llena el
        // campo cuando viene vacío, para no pisar una corrección a mano.
        var leido = await geocoder.LeerExactoAsync(lat, lon, ct);
        edificio = leido.Edificio;
        if (direccion.Length == 0)
        {
            if (leido.Direccion.Length == 0)
                return Results.Json(new { error = "El mapa no contestó: escriba la dirección en el campo." }, statusCode: 502);
            direccion = leido.Direccion;
        }
    }
    else { lat = 0; lon = 0; }

    if (direccion.Length > 200) direccion = direccion[..200];
    var cuenta = me.Identity?.Name ?? "";
    try { tienda.Guardar(new SitioCalibrado(direccion, lat, lon, DateTime.UtcNow, cuenta)); }
    catch (Exception e) { return Results.Json(new { error = "No se pudo guardar: " + e.Message }, statusCode: 500); }

    resolver.InvalidaSitio();
    bitacora.Register(cuenta, AuditEvents.CambiarConfig,
        $"Ubicación exacta del sitio: {direccion}" +
        (hayPunto ? $" (punto {lat.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)}, " +
                   $"{lon.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)})" : " (escrita a mano)"),
        Ip(ctx));
    return Results.Json(new { direccion, edificio, latitud = lat, longitud = lon, fuente = "calibrado" },
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
})
    .RequireAuthorization($"P:{CatalogoPermisos.ConfiguracionAdministrar}");

// Vista previa: el mapa pregunta aquí en cuanto el administrador suelta el punto, para que lea la
// dirección completa ANTES de guardarla. Es lectura nomás: no toca la calibración ni la bitácora.
app.MapGet("/api/bitacora/sitio/preview", async (double? lat, double? lon,
    GeocodificadorLugar geocoder, HttpContext ctx, CancellationToken ct) =>
{
    var y = lat ?? double.NaN;
    var x = lon ?? double.NaN;
    if (!GeocodificadorLugar.EsCoordenada(y, x))
        return Results.Json(new { error = "Coordenadas inválidas." }, statusCode: 400);

    var leido = await geocoder.LeerExactoAsync(y, x, ct);
    if (leido.Direccion.Length == 0 && leido.Edificio.Length == 0)
        // Sin respuesta del mapa la pantalla avisa, pero el administrador puede escribir y guardar.
        return Results.Json(new { error = "El mapa no pudo leer ese punto (¿sin Internet?). Escriba la dirección." },
            statusCode: 502);
    return Results.Json(new { direccion = leido.Direccion, edificio = leido.Edificio, latitud = y, longitud = x },
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
})
    .RequireAuthorization($"P:{CatalogoPermisos.ConfiguracionAdministrar}");

// Las coordenadas exactas guardadas, para que aparezcan junto a la dirección en la bitácora y en el
// Excel (una falta se justifica con dirección y con el punto del mapa, no con el nombre de una colonia).
app.MapGet("/api/bitacora/sitio", (UbicacionResolver resolver, HttpContext ctx) =>
{
    var sitio = resolver.Calibracion();
    if (sitio is null) return Results.NoContent();
    return Results.Json(new
    {
        direccion = sitio.Direccion,
        latitud = sitio.Latitud,
        longitud = sitio.Longitud,
        coordenadas = $"{sitio.Latitud.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)}, " +
                      $"{sitio.Longitud.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)}",
        actualizadoEn = sitio.ActualizadoEn,
        actualizadoPor = sitio.ActualizadoPor,
    }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
})
    .RequireAuthorization($"P:{CatalogoPermisos.BitacoraVer}");

// Quitar la calibración: la intranet vuelve a la dirección automática (por IP del servidor o de los equipos).
app.MapDelete("/api/bitacora/sitio", (ISitioStore tienda, UbicacionResolver resolver, IAuditLog bitacora,
    ClaimsPrincipal me, HttpContext ctx) =>
{
    var cuenta = me.Identity?.Name ?? "";
    bool habia;
    try { habia = tienda.Borrar(); }
    catch (Exception e) { return Results.Json(new { error = "No se pudo quitar: " + e.Message }, statusCode: 500); }
    if (!habia) return Results.Json(new { error = "No había calibración que quitar." }, statusCode: 404);

    resolver.InvalidaSitio();
    bitacora.Register(cuenta, AuditEvents.CambiarConfig,
        "Quitó la calibración de la ubicación del sitio: vuelve a la dirección automática", Ip(ctx));
    return Results.NoContent();
})
    .RequireAuthorization($"P:{CatalogoPermisos.ConfiguracionAdministrar}");

// ---- El propio equipo avisa en qué punto del mapa está ----
// Al abrir la intranet, la página (js/ubicacion.js) pregunta a Internet en qué coordenadas está ese
// aparato y las manda aquí. El servidor las traduce a dirección legible —«Vallejo, Gustavo A. Madero,
// 07870 Ciudad de México»— y la guarda junto con la IP y la cuenta de la sesión. La bitácora muestra
// esa dirección en los renglones de esa IP cuando el administrador no escribió una ubicación a mano.
// Solo se necesita una lectura por sesión: después, la última dirección conocida sigue sirviendo.
app.MapPost("/api/ubicacion", async (LugarRequest r, ClaimsPrincipal me, ILugarStore lugares,
    GeocodificadorLugar geocoder, HttpContext ctx, CancellationToken ct) =>
{
    var lat = r.Latitud ?? double.NaN;
    var lon = r.Longitud ?? double.NaN;
    if (!GeocodificadorLugar.EsCoordenada(lat, lon))
        return Results.Json(new { error = "Coordenadas inválidas." }, statusCode: 400);

    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
    var usuario = me.Identity?.Name ?? "";
    var precision = r.Precision is > 0 and <= 100000 ? r.Precision.Value : 0;
    var fuente = r.Fuente == "navegador" ? "navegador" : "ip";

    // La dirección la arma el servidor (un solo lugar donde cambiar el formato). Cuando el aparato dio
    // sus coordenadas REALES (GPS o wifi del navegador) se pide la lectura exacta: calle con número,
    // colonia, código postal y alcaldía, que es la que sirve para justificar una falta. Cuando el punto
    // solo salió de geolocalizar la IP se queda en la lectura aproximada: pedirle calle y número a un
    // punto que pertenece al proveedor de Internet daría una dirección finísima pero mentira.
    // Si el mapa no contesta, se conserva la que ya trae el navegador.
    var direccion = fuente == "navegador"
        ? await geocoder.DireccionExactaAsync(lat, lon, ct)
        : await geocoder.DireccionAsync(lat, lon, ct);
    if (direccion.Length == 0) direccion = (r.Direccion ?? "").Trim();
    if (direccion.Length > 200) direccion = direccion[..200];

    lugares.Guardar(new LugarReportado(usuario, ip, DateTime.UtcNow, lat, lon, precision, direccion, fuente));
    return Results.NoContent();
})
    .RequireAuthorization();

// ---- La hojita del certificado, para que las PC del hospital lo den por bueno ----
// Solo la llave pública (no la privada), así que se puede bajar sin sesión: es el archivo que
// «confiar-certificado.bat» instala en «Entidades de certificación raíz de confianza» de cada PC
// para que el navegador deje de advertir y la intranet se abra en https sin sustos.
app.MapGet("/intranet.cer", () =>
{
    if (certificado is null || !File.Exists(certificado.RutaCer)) return Results.NotFound();
    return Results.File(File.ReadAllBytes(certificado.RutaCer), "application/x-x509-ca-cert", "intranet.cer");
});

// ---- Cómo está saliendo la ubicación de cada equipo (para el administrador de la bitácora) ----
// Una línea que dice cuántos aparatos ya dieron su punto de verdad, cuántos lo heredaron de un vecino
// de su rango y cuántos siguen sin confirmarse, y si la intranet ya se puede abrir en dirección segura
// (sin https el navegador no entrega la posición real, ni aunque el personal quiera darla).
app.MapGet("/api/bitacora/ubicacion-estado", async (UbicacionResolver resolver, IAuditLog bitacora,
    CancellationToken ct) =>
{
    var vistas = bitacora.IpsRegistradas();
    var nombres = await resolver.EquiposAsync(vistas.Select(v => v.Ip), ct);
    var equipos = vistas.Select(v =>
    {
        var lugar = resolver.LugarDe(v.Ip);
        var (marca, metros) = resolver.MarcaDe(v.Ip);
        return new
        {
            ip = v.Ip,
            equipo = nombres.TryGetValue(v.Ip, out var n) ? n : "",
            ubicacion = resolver.UbicacionDe(v.Ip),
            fuente = lugar?.Fuente ?? "",
            marca,
            precisionMetros = metros,
            direccion = lugar?.Direccion ?? "",
            reportadoEn = lugar?.Momento
        };
    }).ToList();
    return Results.Json(new
    {
        seguro = certificado is not null,
        puerto = https.Puerto,
        redirigir = https.Redirigir,
        total = equipos.Count,
        conPosicionReal = equipos.Count(e => e.marca == "posición real del equipo"),
        conPuntoAprendido = equipos.Count(e => e.marca == "punto aprendido del mismo rango de IP"),
        conDireccionEscrita = equipos.Count(e => e.marca == "escrita para su rango de IP"),
        sitioCalibrado = resolver.SitioCalibrado(),
        sinConfirmar = equipos.Count(e => e.ubicacion == Ubicaciones.SinConfirmar),
        equipos
    }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
})
    .RequireAuthorization($"P:{CatalogoPermisos.BitacoraVer}");

// ---- Bitácora del quemador DICOM (programa del mostrador) ----
// El quemador inicia sesión con una cuenta normal de la intranet y, de vez en cuando,
// manda un lote de eventos: entradas al programa, búsquedas, descargas y grabaciones
// en USB o CD. Cada evento se guarda con la IP real de la conexión y el usuario de la
// sesión, ignorando los que el cliente quiera declarar, para que no se pueda falsear.

// Acepta una arreglo JSON de eventos. Devuelve cuántos quedaron registrados.
app.MapPost("/api/quemador/eventos", (QuemadorEventoEntrada[]? eventos, ClaimsPrincipal me,
    IQuemadorLog quemador, HttpContext ctx) =>
{
    if (eventos is null || eventos.Length == 0) return Results.Ok(new { ok = true, registrados = 0 });
    if (eventos.Length > 500)
        return Results.Json(new { error = "Demasiados eventos en una sola petición (máximo 500)." }, statusCode: 400);

    var usuario = me.Identity?.Name ?? "";
    var ip = Ip(ctx) ?? "";
    // Hora de recepción: solo se acepta la del cliente si cae en una ventana razonable,
    // para que una workstation con el reloj mal puesto no reescriba el pasado.
    var ahora = DateTime.UtcNow;
    var lista = eventos.Select(e => new QuemadorEvento(
        0,
        e.Momento is { } m && m <= ahora.AddMinutes(10) && m >= ahora.AddDays(-30)
            ? m.ToUniversalTime() : ahora,
        usuario,
        e.Equipo ?? "",
        ip,
        e.IpLocal ?? "",
        e.Evento ?? "",
        e.Resultado ?? QuemadorResultados.Exito,
        e.Lote ?? "",
        e.Paciente ?? "",
        e.PacienteId ?? "",
        e.Acceso ?? "",
        e.EstudioUid ?? "",
        e.Modalidad ?? "",
        e.FechaEstudio ?? "",
        e.Imagines ?? 0,
        e.Medio ?? "",
        e.Unidad ?? "",
        e.Mensaje ?? "")).ToList();

    var guardados = quemador.Registrar(lista);
    return guardados == 0 && lista.Count > 0
        ? Results.Json(new { error = "El servidor no pudo guardar la bitácora." }, statusCode: 500)
        : Results.Ok(new { ok = true, registrados = guardados });
}).RequireAuthorization();

// Consulta para la pantalla de administración. Filtros opcionales: ?usuario= &equipo=
// &evento= &resultado= &dia=yyyy-mm-dd &texto= (paciente, ID, acceso, UID, unidad o mensaje)
// &lote= (todas las filas de una misma acción del mostrador) &limite=.
app.MapGet("/api/quemador/eventos", (string? usuario, string? equipo, string? evento, string? resultado,
    DateTime? dia, string? texto, string? lote, int? limite, IQuemadorLog quemador) =>
    Results.Json(quemador.Consultar(new QuemadorFiltro(
        usuario, equipo, evento, resultado, texto, dia, Math.Clamp(limite ?? 500, 1, 5000), lote)),
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))
    .RequireAuthorization($"P:{CatalogoPermisos.QuemadorVer}");

// Valores distintos para poblar los desplegables de la pantalla.
app.MapGet("/api/quemador/valores", (IQuemadorLog quemador) =>
{
    var (usuarios, equipos) = quemador.Valores();
    return Results.Json(new
    {
        usuarios,
        equipos,
        eventos = QuemadorEventos.Todos,
        resultados = QuemadorResultados.Todos
    }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
})
    .RequireAuthorization($"P:{CatalogoPermisos.QuemadorVer}");

// Excel con exactamente los filtros que muestra la pantalla (el día se interpreta en hora local).
app.MapGet("/api/quemador/export", (string? usuario, string? equipo, string? evento, string? resultado,
    DateTime? dia, string? texto, string? lote, ClaimsPrincipal me, IQuemadorLog quemador, IAuditLog bitacora, HttpContext ctx) =>
{
    var entradas = quemador.Consultar(new QuemadorFiltro(usuario, equipo, evento, resultado, texto, dia, 5000, lote));
    var filas = entradas
        .OrderByDescending(e => e.Momento)
        .Select(e => (IReadOnlyList<string>)new[]
        {
            e.Momento.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
            e.Usuario, e.Equipo, e.Ip, e.IpLocal, e.Evento, e.Resultado,
            e.Paciente, e.PacienteId, e.Acceso, e.EstudioUid, e.Modalidad, e.FechaEstudio,
            e.Imagines.ToString(), e.Medio, e.Unidad, e.Mensaje, e.Lote
        })
        .ToList();
    var encabezados = new[]
    {
        "Fecha y hora", "Usuario", "Equipo", "IP", "IP local", "Evento", "Resultado",
        "Paciente", "ID paciente", "Acceso", "UID del estudio", "Modalidad", "Fecha del estudio",
        "Imágenes", "Medio", "Unidad", "Mensaje", "Lote"
    };
    var archivo = Excel.Tabla("Bitácora quemador", encabezados, filas);

    var partes = new List<string> { "bitacora_quemador" };
    if (!string.IsNullOrWhiteSpace(usuario)) partes.Add(usuario.Trim());
    if (!string.IsNullOrWhiteSpace(equipo)) partes.Add(equipo.Trim());
    if (dia is not null) partes.Add(dia.Value.ToString("yyyy-MM-dd"));
    if (!string.IsNullOrWhiteSpace(lote)) partes.Add("lote_" + lote.Trim());
    var nombre = string.Join("_", partes) + ".xlsx";

    bitacora.Register(me.Identity?.Name ?? "", AuditEvents.ExportarExcel,
        $"Bitácora del quemador · {filas.Count} fila(s)", Ip(ctx));
    return Results.File(archivo, Excel.ContentType, nombre);
})
    .RequireAuthorization($"P:{CatalogoPermisos.QuemadorVer}");

// ---- Bandeja de correos (IMAP/SMTP) ----
// La bandeja maneja DOS cuentas a la vez: el buzon de soporte de appsettings.json (Correo:Imap),
// que todos tienen siempre y es la que viene marcada al abrir y al redactar, y el segundo correo
// que el administrador enlaza al usuario en /admin/usuarios. Cada peticion dice cual con el
// parametro `cuenta` (`soporte` | `propio`); sin parametro se usa soporte. La bandeja es de lectura
// y no permite enlazar ni cambiar nada: solo elegir con cual cuenta lee y con cual envia.
// La contrasena del segundo buzon vive cifrada en App_Data/la base y jamas se devuelve al navegador
// ni se escribe en la bitacora.
var jsonCorreo = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

// Opciones del buzon de la cuenta pedida, o un JSON con `configurado = false` y la explicacion legible.
(CorreoImapOptions? o, IResult? mal) BuzonActivo(ClaimsPrincipal me, CorreoService correo, string? cuenta)
{
    var (o, error) = correo.Resolver(me.Identity?.Name ?? "", cuenta);
    return o is null
        ? (null, Results.Json(new { configurado = false, mensajes = Array.Empty<object>(), error }, jsonCorreo))
        : (o, null!);
}

// Por que la cuenta pedida no se pudo abrir, en una frase que puede leer el usuario. Lo usan los
// envios, que devuelven el aviso en el cuerpo del 400 en lugar de una bandeja vacia.
string CuentaError(CorreoService correo, ClaimsPrincipal me, string? cuenta) =>
    correo.Resolver(me.Identity?.Name ?? "", cuenta).Error ?? "No hay buzon disponible para esa cuenta.";

// El dueno del buzon, o quien administra usuarios (el Admin lo configura al dar el alta).
static bool PuedeMandarle(ClaimsPrincipal me, string usuario, string? extra)
{
    var yo = string.Equals(me.Identity?.Name, usuario, StringComparison.OrdinalIgnoreCase);
    return yo || me.HasClaim(GestionService.PermClaim, extra ?? CatalogoPermisos.UsuariosAdministrar);
}

// Cabeceras de los correos de una carpeta de la cuenta pedida (recibidos, enviados o
// borradores; por defecto recibidos). La cuenta es `soporte` o `propio`; sin valor, soporte.
app.MapGet("/api/correos", async (int? max, string? carpeta, string? cuenta, ClaimsPrincipal me, CorreoService correo) =>
{
    var (o, mal) = BuzonActivo(me, correo, cuenta);
    if (o is null) return mal!;
    return Results.Json(await correo.RecientesAsync(o, max, carpeta), jsonCorreo);
})
    .RequireAuthorization($"P:{CatalogoPermisos.CorreoVer}");

// Estado de las dos cuentas de la bandeja: { correo, esPropio, hayEnlace, cuentas, predeterminada }.
// `cuentas` trae soporte y el segundo correo enlazado, con `Disponible`, `Enlazado` y el `Aviso` de por
// que una de las dos no se puede abrir aun. Solo lectura: el buzón se enlaza al crear o editar el usuario
// (/admin/usuarios), la bandeja no lo cambia.
app.MapGet("/api/correos/mio", (ClaimsPrincipal me, CorreoService correo) =>
{
    var usuario = me.Identity?.Name ?? "";
    var e = correo.EstadoEnlace(usuario);
    return Results.Json(new
    {
        e.Correo,
        e.EsPropio,
        e.HayEnlace,
        cuentas = correo.Cuentas(usuario),
        predeterminada = CorreoService.CuentaSoporte,
    }, jsonCorreo);
})
    .RequireAuthorization($"P:{CatalogoPermisos.CorreoVer}");

// Prueba un correo y contrasena contra IMAP y SMTP sin guardar nada; lo usa el alta y la edicion
// de usuario al enlazar el segundo correo. Toma como plantilla la configuracion de soporte (servidor
// y puertos) y, con contrasena en blanco y la misma direccion del enlace propio, prueba la guardada.
app.MapPost("/api/correos/probar", async (BuzonPruebaRequest r, ClaimsPrincipal me, CorreoService correo) =>
{
    var o = BuzonOpciones.Copiar(correo.Predeterminado);
    o.Usuario = (r.Correo ?? "").Trim();
    o.Host = (r.ImapHost ?? o.Host).Trim();
    o.Port = r.ImapPort ?? o.Port;
    o.Ssl = r.ImapSsl ?? o.Ssl;
    o.SmtpHost = string.IsNullOrWhiteSpace(r.SmtpHost) ? o.SmtpHost : r.SmtpHost.Trim();
    o.SmtpPort = r.SmtpPort ?? o.SmtpPort;
    o.SmtpSsl = r.SmtpSsl ?? o.SmtpSsl;
    var guardado = correo.Publico(me.Identity?.Name ?? "");
    var reusar = string.IsNullOrWhiteSpace(r.Password) &&
                 string.Equals(guardado?.Correo, o.Usuario, StringComparison.OrdinalIgnoreCase);
    if (reusar)
    {
        var (propio, _) = correo.Resolver(me.Identity?.Name ?? "", CorreoService.CuentaPropia);
        o.Contrasena = propio?.Contrasena ?? "";
    }
    else o.Contrasena = r.Password ?? "";
    var error = await correo.ProbarAsync(o);
    return error is null
        ? Results.Json(new { ok = true, mensaje = "El buzon responde: puede leer y enviar." }, jsonCorreo)
        : Results.Json(new { error }, statusCode: 400);
})
    .RequireAuthorization($"P:{CatalogoPermisos.CorreoResponder}");

// Contenido completo de un mensaje (cabeceras, cuerpo en texto/HTML, adjuntos e imagenes internas).
// La carpeta (recibidos/enviados/borradores) identifica donde esta el uid: los uid son por carpeta,
// y cada cuenta tiene sus propias carpetas, asi que la cuenta tambien hay que decirla.
app.MapGet("/api/correos/{uid:long}", async (long uid, string? carpeta, string? cuenta, ClaimsPrincipal me, CorreoService correo) =>
{
    if (uid < 0 || uid > uint.MaxValue) return Results.StatusCode(404);
    var (o, mal) = BuzonActivo(me, correo, cuenta);
    if (o is null) return mal!;
    var d = await correo.DetalleAsync(o, (uint)uid, carpeta);
    return d is null
        ? Results.Json(new { error = "El mensaje no existe o ya no esta en la bandeja." }, statusCode: 404)
        : Results.Json(d, jsonCorreo);
})
    .RequireAuthorization($"P:{CatalogoPermisos.CorreoVer}");

// Descarga de un adjunto por su indice (el que devolvio el detalle del mensaje).
app.MapGet("/api/correos/{uid:long}/adjuntos/{indice:int}", async (long uid, int indice, string? carpeta, string? cuenta, ClaimsPrincipal me, CorreoService correo) =>
{
    if (uid < 0 || uid > uint.MaxValue) return Results.StatusCode(404);
    var (o, mal) = BuzonActivo(me, correo, cuenta);
    if (o is null) return mal!;
    var a = await correo.AdjuntoAsync(o, (uint)uid, indice, carpeta);
    return a is null ? Results.StatusCode(404) : Results.File(a.Bytes, a.ContentType, a.Nombre);
})
    .RequireAuthorization($"P:{CatalogoPermisos.CorreoVer}");

// Imagen incrustada en el cuerpo HTML de un mensaje (src="cid:..."): se sirve por su indice
// dentro de la coleccion `inline` que devolvio el detalle.
app.MapGet("/api/correos/{uid:long}/inline/{indice:int}", async (long uid, int indice, string? carpeta, string? cuenta, ClaimsPrincipal me, CorreoService correo) =>
{
    if (uid < 0 || uid > uint.MaxValue) return Results.StatusCode(404);
    var (o, mal) = BuzonActivo(me, correo, cuenta);
    if (o is null) return mal!;
    var a = await correo.InlineAsync(o, (uint)uid, indice, carpeta);
    return a is null ? Results.StatusCode(404) : Results.File(a.Bytes, a.ContentType);
})
    .RequireAuthorization($"P:{CatalogoPermisos.CorreoVer}");

// Enviar desde la bandeja: mensaje nuevo, responder, responder a todos o reenviar, con Para/CC/CCO,
// cuerpo enriquecido, imagenes incrustadas (cid), adjuntos y el original como adjunto si se reenvia.
// El cuerpo trae `cuenta`: `soporte` (lo de siempre) o `propio` (el segundo correo enlazado).
app.MapPost("/api/correos/enviar", async (CorreoEnviarRequest r, ClaimsPrincipal me, CorreoService correo, HttpContext ctx) =>
{
    var (o, mal) = BuzonActivo(me, correo, r.Cuenta);
    if (o is null) return Results.Json(new { error = CuentaError(correo, me, r.Cuenta) }, statusCode: 400);
    var total = (r.Adjuntos ?? Array.Empty<AdjuntoBase64>()).Sum(a => (long)(a.Base64?.Length ?? 0));
    if (total > 40 * 1024 * 1024)
        return Results.Json(new { error = "Los archivos pesan demasiado (limite ~30 MB por correo). Quita alguno o usa imagenes mas ligeras." }, statusCode: 400);
    var error = await correo.EnviarAsync(o, r);
    return error is null
        ? Results.Json(new { ok = true })
        : Results.Json(new { error }, statusCode: 400);
})
    .RequireAuthorization($"P:{CatalogoPermisos.CorreoResponder}");

// Guarda el mensaje como borrador en la carpeta de Borradores de la cuenta elegida (sin enviarlo).
// Acepta el mismo cuerpo que /enviar, pero admite destinatarios o texto vacios (es lo que se guarda a medias).
app.MapPost("/api/correos/borrador", async (CorreoEnviarRequest r, ClaimsPrincipal me, CorreoService correo) =>
{
    var (o, _) = BuzonActivo(me, correo, r.Cuenta);
    if (o is null) return Results.Json(new { error = CuentaError(correo, me, r.Cuenta) }, statusCode: 400);
    var total = (r.Adjuntos ?? Array.Empty<AdjuntoBase64>()).Sum(a => (long)(a.Base64?.Length ?? 0));
    if (total > 40 * 1024 * 1024)
        return Results.Json(new { error = "Los archivos pesan demasiado para guardarlos como borrador (limite ~30 MB). Quita alguno." }, statusCode: 400);
    var error = await correo.GuardarBorradorAsync(o, r);
    return error is null
        ? Results.Json(new { ok = true })
        : Results.Json(new { error }, statusCode: 400);
})
    .RequireAuthorization($"P:{CatalogoPermisos.CorreoResponder}");

// Elimina un mensaje de la carpeta indicada (se usa para borrar borradores).
app.MapDelete("/api/correos/{uid:long}", async (long uid, string? carpeta, string? cuenta, ClaimsPrincipal me, CorreoService correo) =>
{
    if (uid < 0 || uid > uint.MaxValue) return Results.StatusCode(404);
    var (o, _) = BuzonActivo(me, correo, cuenta);
    if (o is null) return Results.Json(new { error = CuentaError(correo, me, cuenta) }, statusCode: 400);
    var error = await correo.EliminarAsync(o, (uint)uid, string.IsNullOrWhiteSpace(carpeta) ? "borradores" : carpeta);
    return error is null
        ? Results.Json(new { ok = true })
        : Results.Json(new { error }, statusCode: 400);
})
    .RequireAuthorization($"P:{CatalogoPermisos.CorreoResponder}");

// Responder al remitente original con texto plano (endpoint antiguo, sigue sirviendo).
app.MapPost("/api/correos/{uid:long}/responder", async (long uid, RespuestaRequest r, string? cuenta, ClaimsPrincipal me, CorreoService correo) =>
{
    if (uid < 0 || uid > uint.MaxValue) return Results.StatusCode(404);
    var (o, mal) = BuzonActivo(me, correo, cuenta);
    if (o is null) return mal!;
    var error = await correo.ResponderAsync(o, (uint)uid, r.Mensaje ?? "");
    return error is null ? Results.Json(new { ok = true }) : Results.Json(new { error }, statusCode: 400);
})
    .RequireAuthorization($"P:{CatalogoPermisos.CorreoResponder}");

// Buzon enlazado de un usuario (sin contrasena): lo ve el Admin al editarlo y el propio usuario.
app.MapGet("/api/usuarios/{usuario}/buzon", (string usuario, ClaimsPrincipal me, CorreoService correo) =>
    PuedeMandarle(me, usuario, null)
        ? Results.Json(new { buzon = correo.Publico(usuario) }, jsonCorreo)
        : Results.Json(new { error = "No tienes acceso a este buzon." }, statusCode: 403))
    .RequireAuthorization($"P:{CatalogoPermisos.CorreoVer}");

// Enlaza o desvincula el buzon de un usuario. El Admin lo hace al dar de alta/editar la cuenta;
// el usuario solo puede cambiar el suyo (desde su bandeja).
app.MapPut("/api/usuarios/{usuario}/buzon", (string usuario, BuzonRequest r, ClaimsPrincipal me,
    IUserStore usuarios, CorreoService correo, IAuditLog bitacora, HttpContext ctx) =>
{
    if (!PuedeMandarle(me, usuario, null))
        return Results.Json(new { error = "No puedes cambiar el buzon de otra persona." }, statusCode: 403);
    if (usuarios.Find(usuario) is null)
        return Results.Json(new { error = "El usuario no existe." }, statusCode: 404);
    var (status, error) = correo.GuardarBuzon(usuario, r);
    if (status == 204)
    {
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.EnlazarBuzon,
            r.Limpiar ? $"{usuario} · buzon desvinculado" : $"{usuario} · {r.Correo}", Ip(ctx));
        return Results.Json(new { ok = true, buzon = correo.Publico(usuario) }, jsonCorreo);
    }
    return Results.Json(new { error }, statusCode: status);
})
    .RequireAuthorization($"P:{CatalogoPermisos.CorreoResponder}");

// Lista de buzones enlazados (correo y servidor, sin contrasenas) para la tabla de usuarios.
app.MapGet("/api/buzones", (CorreoService correo) => Results.Json(correo.TodosEnlazados(), jsonCorreo))
    .RequireAuthorization($"P:{CatalogoPermisos.UsuariosAdministrar}");


// ---- Módulo Gestión: calendario de actividades, incidentes y tickets ----
// Las reglas de negocio (permisos, transiciones, asignación, reportes, historial) viven en
// GestionService; aquí solo se traducen peticiones/respuestas HTTP.
static string? Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString();
static DateTime? Fecha(string? s) => DateTime.TryParse(s, out var f) ? f.Date : null;

app.MapGet("/api/actividades", (ClaimsPrincipal me, GestionService g) =>
    Results.Json(g.Visibles(GestionService.De(me)), json))
    .RequireAuthorization("Ingenieria");

app.MapGet("/api/actividades/{id:long}", (long id, ClaimsPrincipal me, GestionService g) =>
{
    var s = GestionService.De(me);
    var item = g.Find(id);
    if (item is null) return Results.Json(new { error = "La actividad no existe." }, statusCode: 404);
    if (!GestionService.EsDuenoOAdmin(s, item) && !string.Equals(item.CreadoPor, s.Usuario, StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "No tienes acceso a este elemento." }, statusCode: 403);
    return Results.Json(item, json);
}).RequireAuthorization("Ingenieria");

// Alta de un elemento. La misma ruta acepta dos cuerpos distintos:
//   · JSON  → el alta de siempre (el Administrador registra y deja el elemento Pendiente para
//             asignarlo; el Ingeniero sigue pudiendo crear así sus **incidentes** y **tickets**).
//             Una **Actividad** del Ingeniero por JSON se rechaza con 400: «directamente al crear una
//             actividad debe estar en completo, no puede quedar pendiente».
//   · multipart → el alta que **se completa en el mismo momento**, con `informe` (opcional) y
//             `archivos` (evidencia obligatoria): crea, adjunta y pasa a Completada en un solo envío;
//             si algo falla después de crear, se deshace el elemento para que no quede nada a medias.
app.MapPost("/api/actividades", async (HttpContext ctx, ClaimsPrincipal me, GestionService g) =>
{
    var s = GestionService.De(me);
    if (ctx.Request.HasFormContentType)
    {
        var form = await ctx.Request.ReadFormAsync();
        var archivos = new List<(string, byte[]?)>();
        foreach (var archivo in form.Files)
        {
            if (!string.Equals(archivo.Name, "archivos", StringComparison.Ordinal)) continue;
            using var ms = new MemoryStream();
            await archivo.CopyToAsync(ms);
            archivos.Add((archivo.FileName ?? "", ms.ToArray()));
        }
        System.DateTime? fechaAlta = null;
        if (System.DateTime.TryParse(form["fecha"].ToString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var fFecha)) fechaAlta = fFecha;
        var (statusC, errorC, itemC) = g.CrearYCerrada(s, form["tipo"].ToString(), form["hospital"].ToString(),
            form["titulo"].ToString(), form["descripcion"].ToString(), fechaAlta, form["informe"].ToString(),
            archivos, Ip(ctx));
        return statusC == 201
            ? Results.Json(itemC, json, statusCode: 201)
            : Results.Json(new { error = errorC }, statusCode: statusC);
    }

    ActividadRequest? r;
    try { r = await ctx.Request.ReadFromJsonAsync<ActividadRequest>(); }
    catch { return Results.Json(new { error = "El cuerpo debe ser JSON o un formulario con la evidencia." }, statusCode: 400); }
    if (r is null) return Results.Json(new { error = "Falta el cuerpo de la petición." }, statusCode: 400);

    var (status, error, item) = g.Crear(s, r.Tipo ?? TiposItem.Actividad, r.Hospital ?? "",
        r.Titulo ?? "", r.Descripcion ?? "", r.Usuario ?? "", r.Fecha ?? default, r.FechaLimite,
        r.Prioridad ?? Prioridades.Media, r.PeriodicidadDias ?? 0, r.RelacionId, Ip(ctx));
    return status == 201
        ? Results.Json(item, json, statusCode: 201)
        : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("Ingenieria");

app.MapPut("/api/actividades/{id:long}", (long id, ActividadRequest r, ClaimsPrincipal me, GestionService g, HttpContext ctx) =>
{
    if (r.Fecha is null) return Results.Json(new { error = "La fecha es obligatoria." }, statusCode: 400);
    var (status, error, item) = g.Editar(GestionService.De(me), id, r.Hospital ?? "", r.Titulo ?? "", r.Descripcion ?? "",
        r.Usuario ?? "", r.Fecha.Value, r.FechaLimite, r.Prioridad ?? Prioridades.Media, Ip(ctx));
    return status == 200
        ? Results.Json(item, json)
        : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("Ingenieria");

app.MapDelete("/api/actividades/{id:long}", (long id, ClaimsPrincipal me, GestionService g, HttpContext ctx) =>
{
    var (status, error) = g.Eliminar(GestionService.De(me), id, Ip(ctx));
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("Ingenieria");

// Transiciones de estado: **Completada** (informe opcional + evidencia ya adjunta) y **Cancelada**
// (justificación, solo quien tiene `items.cancelar`). Ya no existen "En proceso" ni el cierre formal.
app.MapPut("/api/actividades/{id:long}/estado", (long id, EstadoRequest r, ClaimsPrincipal me, GestionService g, HttpContext ctx) =>
{
    var texto = r.Estado == EstadosActividad.Cancelada ? r.Justificacion ?? "" : r.Informe ?? r.Comentario ?? "";
    var (status, error, item) = g.CambiarEstado(GestionService.De(me), id, r.Estado ?? "", texto, Ip(ctx));
    return status == 200
        ? Results.Json(item, json)
        : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("Ingenieria");

// Reabrir: el único camino de vuelta de una actividad **Completada** a Pendiente. Es administrativo
// (`items.reabrir`) porque el alta del Ingeniero ya se completa en el mismo momento, así que si el
// trabajo en realidad no estaba hecho, lo deshace quien puede volver a asignarla y ponerle fecha
// límite. La evidencia y el informe se conservan. Lo **Cancelado** no se reabre: solo queda el registro.
app.MapPost("/api/actividades/{id:long}/reabrir", (long id, ReabrirRequest r, ClaimsPrincipal me, GestionService g, HttpContext ctx) =>
{
    var (status, error, item) = g.Reabrir(GestionService.De(me), id, r.Motivo ?? "", Ip(ctx));
    return status == 200
        ? Results.Json(item, json)
        : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("Ingenieria");

// Cerrar el elemento con su evidencia: un solo envío multipart que adjunta la foto o el archivo
// (uno o varios) y lo pasa a Completada con el informe. Es el camino que usa la tarjeta de detalle;
// `PUT .../estado` sigue existiendo pero ya exige evidencia adjunta, así que no se puede saltar.
app.MapPost("/api/actividades/{id:long}/completar", async (long id, HttpContext ctx, ClaimsPrincipal me,
    GestionService g, EvidenciaService evidencias) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var archivos = new List<(string, byte[]?)>();
    foreach (var archivo in form.Files)
    {
        if (!string.Equals(archivo.Name, "archivos", StringComparison.Ordinal)) continue;
        using var ms = new MemoryStream();
        await archivo.CopyToAsync(ms);
        archivos.Add((archivo.FileName ?? "", ms.ToArray()));
    }
    var s = GestionService.De(me);
    var (statusA, errorA, _) = evidencias.Adjuntar(s, id, archivos, Ip(ctx));
    if (statusA != 201) return Results.Json(new { error = errorA ?? "No se pudo adjuntar la evidencia." }, statusCode: statusA);
    var (status, error, item) = g.CambiarEstado(s, id, EstadosActividad.Completada, form["informe"].ToString(), Ip(ctx));
    return status == 200 ? Results.Json(item, json) : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("Ingenieria");

// Evidencia de un elemento: la lista que pinta la tarjeta (miniaturas de fotos y descarga de documentos).
app.MapGet("/api/actividades/{id:long}/evidencias", (long id, ClaimsPrincipal me, EvidenciaService evidencias) =>
    Results.Json(evidencias.Listar(GestionService.De(me), id), json))
    .RequireAuthorization("Ingenieria");

// El archivo en sí. Las imágenes se abren en el navegador (para verlas a tamaño completo) y el
// resto baja como adjunto. El tipo MIME sale de la extensión validada al subir, nunca de lo que
// envió el navegador, y se sirve sin olisqueo de contenido.
app.MapGet("/api/evidencias/{id:long}", (long id, ClaimsPrincipal me, EvidenciaService evidencias, HttpContext ctx) =>
{
    if (evidencias.ParaAbrir(GestionService.De(me), id) is not (var e, var ruta))
        return Results.Json(new { error = "La evidencia no existe o el archivo ya no está disponible." }, statusCode: 404);
    var ascii = new string(e.NombreArchivo.Select(c => c < 128 && char.IsLetterOrDigit(c) ? c : '_').ToArray());
    if (ascii.Length > 80) ascii = ascii[..80];
    ctx.Response.Headers["Cache-Control"] = "private, max-age=120";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Content-Disposition"] =
        $"{(e.EsImagen ? "inline" : "attachment")}; filename=\"{ascii}{EvidenciaRules.Extension(e.NombreArchivo)}\"; " +
        $"filename*=UTF-8''{Uri.EscapeDataString(e.NombreArchivo)}";
    return Results.File(ruta, e.Tipo, enableRangeProcessing: true);
}).RequireAuthorization("Ingenieria");

// Asignación formal (solo Admin): ingeniero responsable, prioridad, fecha límite y periodicidad.
app.MapPut("/api/actividades/{id:long}/asignar", (long id, AsignacionRequest r, ClaimsPrincipal me, GestionService g, HttpContext ctx) =>
{
    var (status, error, item) = g.Asignar(GestionService.De(me), id, r.Usuario ?? "", r.Prioridad ?? Prioridades.Media,
        r.FechaLimite, r.PeriodicidadDias ?? 0, r.Comentario ?? "", Ip(ctx));
    return status == 200
        ? Results.Json(item, json)
        : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("SoloAdmin");

// Observaciones del Admin (quedan en el historial de cambios del elemento).
app.MapPost("/api/actividades/{id:long}/observaciones", (long id, ObservacionRequest r, ClaimsPrincipal me, GestionService g, HttpContext ctx) =>
{
    var (status, error) = g.Observacion(GestionService.De(me), id, r.Comentario ?? "", Ip(ctx));
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("SoloAdmin");

// Historial de cambios del elemento (dueño o Admin).
app.MapGet("/api/actividades/{id:long}/historial", (long id, ClaimsPrincipal me, GestionService g) =>
    Results.Json(g.HistorialDe(GestionService.De(me), id), json))
    .RequireAuthorization("Ingenieria");

// Reportes de estatus del elemento (dueño o Admin).
app.MapGet("/api/actividades/{id:long}/reportes", (long id, ClaimsPrincipal me, GestionService g) =>
    Results.Json(g.ReportesDe(GestionService.De(me), id), json))
    .RequireAuthorization("Ingenieria");

// Entregar un reporte de estatus (Ingeniero responsable o Admin).
app.MapPost("/api/actividades/{id:long}/reportes", (long id, ReporteRequest r, ClaimsPrincipal me, GestionService g, HttpContext ctx) =>
{
    var (status, error, reporte) = g.Reportar(GestionService.De(me), id, r.Estado ?? "", r.Avance ?? 0,
        r.DescripcionAvance ?? "", r.Problemas ?? "", r.Pendientes ?? "", r.ProximasAcciones ?? "",
        r.FechaEstimada, r.Comentarios ?? "", Ip(ctx));
    return status == 201
        ? Results.Json(reporte, json, statusCode: 201)
        : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("Ingenieria");

// ---- Reportes de estatus (consulta global, solo Admin) ----
app.MapGet("/api/reportes", (string? autor, DateTime? desde, DateTime? hasta, IReportStore reportes, IActivityStore items, GestionService g, GestionCatalogos catalogos, ClaimsPrincipal me) =>
{
    var s = GestionService.De(me);
    if (!g.Tiene(s, CatalogoPermisos.ReportesConsultar))
        return Results.Json(new { error = "Tu rol no puede consultar los reportes de estatus." }, statusCode: 403);
    var porId = items.List().ToDictionary(a => a.Id);
    var lista = reportes.Query(null, autor, desde, hasta).Select(r =>
    {
        porId.TryGetValue(r.ItemId, out var item);
        return new
        {
            r.Id,
            r.ItemId,
            Elemento = item?.Etiqueta ?? "(eliminado)",
            Hospital = item?.Hospital ?? "",
            r.Momento,
            r.Autor,
            r.Estado,
            r.Avance,
            r.DescripcionAvance,
            r.Problemas,
            r.Pendientes,
            r.ProximasAcciones,
            r.FechaEstimada,
            r.Comentarios,
        };
    })
    // Los reportes de una actividad de otra región no se listan. Los de un elemento ya borrado
    // (hospital vacío) sí: no son de ninguna región y ocultarlos perdería histórico.
    .Where(r => catalogos.ErrorRegionDe(s.Usuario, r.Hospital) is null);
    return Results.Json(lista, json);
}).RequireAuthorization("SoloAdmin");

// Descarga en Excel del histórico de reportes (solo Admin): exporta lo mismo que consulta
// /api/reportes con los filtros de la pantalla aplicados del lado del servidor.
app.MapGet("/api/reportes/excel", (string? autor, DateTime? desde, DateTime? hasta, string? hospital, string? texto,
    IReportStore reportes, IActivityStore items, GestionService g, GestionCatalogos catalogos, ClaimsPrincipal me, IAuditLog bitacora, HttpContext ctx) =>
{
    var s = GestionService.De(me);
    if (!g.Tiene(s, CatalogoPermisos.ReportesConsultar))
        return Results.Json(new { error = "Tu rol no puede consultar los reportes de estatus." }, statusCode: 403);
    // Los reportes antiguos guardaron el estado de cinco valores que ya no existen; se traducen para
    // que la descarga en Excel de ese histórico siga siendo legible.
    static string EstadoTexto(string e) => e switch
    {
        EstadosActividad.EnProceso => "En proceso",
        EstadosActividad.Completada => "Completada",
        EstadosActividad.Cancelada => "Cancelada",
        EstadosActividad.Cerrada => "Cerrada",
        _ => e,
    };
    var porId = items.List().ToDictionary(a => a.Id);
    var t = Texto.Normalizar(texto);
    var filas = reportes.Query(null, autor, desde, hasta)
        .Select(r => { porId.TryGetValue(r.ItemId, out var item); return (r, item); })
        .Where(x => string.IsNullOrWhiteSpace(hospital) || string.Equals(x.item?.Hospital, hospital.Trim(), StringComparison.Ordinal))
        .Where(x => catalogos.ErrorRegionDe(s.Usuario, x.item?.Hospital ?? "") is null)
        .Where(x => t.Length == 0
            || Texto.Normalizar((x.item?.Etiqueta ?? "") + " " + x.r.DescripcionAvance).Contains(t, StringComparison.Ordinal))
        .Select(x => (IReadOnlyList<string>)new[]
        {
            x.r.Momento.ToString("dd/MM/yyyy HH:mm"), x.r.Autor, x.item?.Etiqueta ?? "(eliminado)",
            x.item?.Hospital ?? "", EstadoTexto(x.r.Estado), x.r.Avance + " %", x.r.DescripcionAvance,
            x.r.Problemas, x.r.Pendientes, x.r.ProximasAcciones,
            x.r.FechaEstimada?.ToString("dd/MM/yyyy") ?? "", x.r.Comentarios,
        }).ToList();
    var archivo = Excel.Tabla("Reportes de estatus",
        new[] { "Momento", "Ingeniero", "Elemento", "Hospital", "Estatus", "Avance",
                "Descripción del avance", "Problemas", "Pendientes", "Próximas acciones",
                "Conclusión estimada", "Comentarios" },
        filas);
    bitacora.Register(s.Usuario, AuditEvents.ExportarExcel, $"Reportes de estatus · {filas.Count} registro(s)", Ip(ctx));
    return Results.File(archivo, Excel.ContentType, "reportes-estatus.xlsx");
}).RequireAuthorization("SoloAdmin");

// ---- Roles y permisos ----
// Catálogo + roles dinámicos + matriz activa de cada rol (para pintar la página /admin/roles/).
// Exige el permiso efectivo roles.administrar.
app.MapGet("/api/roles", (IPermissionStore permisos, IRoleStore roles) =>
{
    var lista = roles.List();
    var matriz = lista.ToDictionary(r => r.Nombre, r => (IReadOnlyCollection<string>)permisos.Activos(r.Nombre).ToList(), StringComparer.Ordinal);
    return Results.Json(new
    {
        grupos = CatalogoPermisos.Grupos,
        roles = lista.Select(r => new { r.Nombre, r.EsSistema, r.Creado }),
        matriz,
    }, json);
}).RequireAuthorization($"P:{CatalogoPermisos.RolesAdministrar}");

// Crear un rol nuevo (nombre libre). Arranca sin permisos: el Admin se los asigna a continuación.
app.MapPost("/api/roles", (RolRequest r, ClaimsPrincipal me, IRoleStore roles, IAuditLog bitacora, IPermissionResolver resolver, HttpContext ctx) =>
{
    var (status, error) = roles.Crear(r.Nombre ?? "");
    if (status == 201)
    {
        resolver.Invalidar();
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.CambiarPermisos, $"Rol creado: {ReglasRol.Normalizar(r.Nombre)}", Ip(ctx));
    }
    return status == 201 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization($"P:{CatalogoPermisos.RolesAdministrar}");

// Renombrar un rol personalizado: reasigna usuarios y matriz al nuevo nombre.
app.MapPut("/api/roles/{nombre}/renombrar", (string nombre, RolRequest r, ClaimsPrincipal me, IRoleStore roles, IAuditLog bitacora, IPermissionResolver resolver, HttpContext ctx) =>
{
    var (status, error) = roles.Renombrar(nombre, r.Nombre ?? "");
    if (status == 204)
    {
        resolver.Invalidar();
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.CambiarPermisos, $"Rol renombrado: {nombre} → {ReglasRol.Normalizar(r.Nombre)}", Ip(ctx));
    }
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization($"P:{CatalogoPermisos.RolesAdministrar}");

// Eliminar un rol personalizado (solo si no tiene usuarios asignados).
app.MapDelete("/api/roles/{nombre}", (string nombre, ClaimsPrincipal me, IRoleStore roles, IAuditLog bitacora, IPermissionResolver resolver, HttpContext ctx) =>
{
    var (status, error) = roles.Eliminar(nombre);
    if (status == 204)
    {
        resolver.Invalidar();
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.CambiarPermisos, $"Rol eliminado: {nombre}", Ip(ctx));
    }
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization($"P:{CatalogoPermisos.RolesAdministrar}");

// Permisos activos del usuario en sesión (las páginas ocultan lo que no puede hacer).
app.MapGet("/api/permisos/mios", (ClaimsPrincipal me, GestionService g, GestionCatalogos catalogos) =>
{
    var s = GestionService.De(me);
    var activos = CatalogoPermisos.TodasLasClaves.Where(k => g.Tiene(s, k)).ToList();
    // Los hospitales que le tocan al usuario en sesión: los del catálogo si ve todas las regiones; si
    // no, solo los escritos en su región (ver GestionCatalogos.HospitalesPara). De esta lista salen los
    // desplegables de hospital de toda la intranet —/js/gestion.js la copia en G.HOSPITALES—, así que
    // acotarla aquí es lo que hace que cada pantalla ofrezca «los hospitales que existen» para él.
    var hospitales = catalogos.HospitalesPara(s.Usuario);
    return Results.Json(new
    {
        rol = s.Rol,
        permisos = activos,
        // La fecha límite solo se usa si el usuario tiene el permiso de configuración correspondiente
        // y la opción está habilitada (ya no depende de ser Admin a secas).
        fechaLimite = s.Puede(CatalogoPermisos.ConfiguracionFechaLimite) && g.FechaLimiteHabilitada(),
        hospitales,
        hospitalesConGeneral = hospitales.Append(Hospitales.General).ToList(),
        // Regiones del usuario (lista vacía si ve todas) y si ve todas: las pantallas lo muestran y el
        // servidor vuelve a comprobarlo en cada consulta, no solo en la pantalla.
        regiones = catalogos.RegionesDeUsuario(s.Usuario),
        region = catalogos.RegionDeUsuario(s.Usuario), // Primera región para compatibilidad
        veTodasRegiones = catalogos.VeTodasRegiones(s.Usuario),
        // Catálogos del Programador que usan los formularios: actividades disponibles, sus
        // subopciones (aplicativos) y servicios, ya acotados a los hospitales de arriba.
        catalogos = catalogos.ParaFormularios(hospitales),
    }, json);
}).RequireAuthorization();

// Guardar la matriz de un rol (cualquier rol, con el permiso efectivo roles.administrar). El rol
// Administrador conserva siempre roles.administrar para no quedar bloqueado fuera de la página.
app.MapPut("/api/roles", (PermisoRequest r, ClaimsPrincipal me, IPermissionStore permisos, IRoleStore roles, IAuditLog bitacora, GestionService g, IPermissionResolver resolver, HttpContext ctx) =>
{
    var s = GestionService.De(me);
    if (!g.Tiene(s, CatalogoPermisos.RolesAdministrar))
        return Results.Json(new { error = "No tienes permiso para cambiar los permisos." }, statusCode: 403);
    var rol = (r.Rol ?? "").Trim();
    if (rol.Length == 0) return Results.Json(new { error = "Falta indicar el rol." }, statusCode: 400);
    if (!roles.Existe(rol)) return Results.Json(new { error = "El rol no existe." }, statusCode: 404);
    var recibidas = (r.Permisos ?? new List<string>()).Distinct(StringComparer.Ordinal).ToList();
    var claves = recibidas.Where(CatalogoPermisos.EsClaveValida).ToList();
    if (claves.Count != recibidas.Count)
        return Results.Json(new { error = "Hay permisos desconocidos en la lista." }, statusCode: 400);
    if (string.Equals(rol, Roles.Admin, StringComparison.OrdinalIgnoreCase) && !claves.Contains(CatalogoPermisos.RolesAdministrar))
        claves.Add(CatalogoPermisos.RolesAdministrar);
    var (status, error) = permisos.Guardar(rol, claves);
    if (status == 204)
    {
        resolver.Invalidar();
        bitacora.Register(s.Usuario, AuditEvents.CambiarPermisos, $"{Roles.Display(rol)} · {claves.Count} permiso(s)", Ip(ctx));
    }
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization($"P:{CatalogoPermisos.RolesAdministrar}");

// ---- Ajustes de permisos por usuario (rol como plantilla + excepciones por persona) ----
// GET: rol actual, permisos base del rol, ajustes guardados y permisos efectivos resultantes.
// Incluye el catálogo (grupos) para que el editor pinte la matriz sin depender de /api/roles.
app.MapGet("/api/usuarios/{usuario}/permisos", (string usuario, IUserStore usuarios, IUserPermissionStore overrides, IPermissionStore permisos, IPermissionResolver resolver) =>
{
    var u = usuarios.Find(usuario);
    if (u is null) return Results.Json(new { error = "El usuario no existe." }, statusCode: 404);
    return Results.Json(new
    {
        usuario = u.Usuario,
        rol = u.Rol,
        grupos = CatalogoPermisos.Grupos,
        rolPermisos = permisos.Activos(u.Rol).ToList(),
        ajustes = overrides.Overrides(usuario).Select(kv => new { clave = kv.Key, concedido = kv.Value }),
        efectivos = CatalogoPermisos.TodasLasClaves.Where(k => resolver.Efectivos(usuario, u.Rol).Contains(k)),
    }, json);
}).RequireAuthorization($"P:{CatalogoPermisos.UsuariosAdministrar}");

// PUT: sustituye los ajustes del usuario. `ajustes` = [{ clave, concedido }]; lista vacía = sin ajustes.
app.MapPut("/api/usuarios/{usuario}/permisos", (string usuario, UsuarioPermisosRequest r, ClaimsPrincipal me,
    IUserStore usuarios, IUserPermissionStore overrides, IAuditLog bitacora, IPermissionResolver resolver, HttpContext ctx) =>
{
    var u = usuarios.Find(usuario);
    if (u is null) return Results.Json(new { error = "El usuario no existe." }, statusCode: 404);
    var ajustes = (r.Ajustes ?? new List<AjustePermiso>()).Select(a => (a.Clave ?? "", a.Concedido)).ToList();
    var (status, error) = overrides.Guardar(usuario, ajustes);
    if (status == 204)
    {
        resolver.Invalidar();
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.CambiarPermisos, $"Ajustes de {usuario} · {ajustes.Count}", Ip(ctx));
    }
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization($"P:{CatalogoPermisos.UsuariosAdministrar}");

// ---- Avisos del inicio ----
// Leer los avisos **activos** no exige permiso: cualquier usuario con sesión los ve en el panel
// "Avisos" de la página de inicio. Escribir (crear/editar/eliminar) exige `avisos.administrar`.
app.MapGet("/api/avisos", (IAvisoStore avisos) =>
    Results.Json(avisos.List().Where(a => a.Activo), json)).RequireAuthorization();

// Lista completa (incluye pausados) solo para quien administra avisos; la usa el editor del inicio.
app.MapGet("/api/avisos/todos", (IAvisoStore avisos) =>
    Results.Json(avisos.List(), json)).RequireAuthorization($"P:{CatalogoPermisos.AvisosAdministrar}");

app.MapPost("/api/avisos", (AvisoRequest r, ClaimsPrincipal me, IAvisoStore avisos, IAuditLog bitacora, HttpContext ctx) =>
{
    var (status, error) = avisos.Crear(r.Titulo ?? "", r.Cuerpo ?? "", r.Etiqueta ?? "", r.Activo, me.Identity?.Name ?? "");
    if (status == 201)
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.CrearAviso, (r.Titulo ?? "").Trim(), Ip(ctx));
    return status == 201 ? Results.StatusCode(201) : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization($"P:{CatalogoPermisos.AvisosAdministrar}");

app.MapPut("/api/avisos/{id:int}", (int id, AvisoRequest r, ClaimsPrincipal me, IAvisoStore avisos, IAuditLog bitacora, HttpContext ctx) =>
{
    var (status, error) = avisos.Actualizar(id, r.Titulo ?? "", r.Cuerpo ?? "", r.Etiqueta ?? "", r.Activo);
    if (status == 204)
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.EditarAviso, $"#{id} · {(r.Titulo ?? "").Trim()}", Ip(ctx));
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization($"P:{CatalogoPermisos.AvisosAdministrar}");

app.MapDelete("/api/avisos/{id:int}", (int id, ClaimsPrincipal me, IAvisoStore avisos, IAuditLog bitacora, HttpContext ctx) =>
{
    var (status, error) = avisos.Eliminar(id);
    if (status == 204)
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.EliminarAviso, $"#{id}", Ip(ctx));
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization($"P:{CatalogoPermisos.AvisosAdministrar}");

// ---- Administrativos: actas administrativas ----
// Todo el módulo exige el permiso efectivo actas.administrar; cada cambio queda en la bitácora.
app.MapGet("/api/actas", (IActaStore actas) =>
    Results.Json(actas.List(), json)).RequireAuthorization($"P:{CatalogoPermisos.ActasAdministrar}");

app.MapPost("/api/actas", (ActaRequest r, ClaimsPrincipal me, IActaStore actas, IAuditLog bitacora, HttpContext ctx) =>
{
    var (status, error, acta) = actas.Crear(r.Usuario ?? "", r.Nombre ?? "", r.Puesto ?? "", r.Lugar ?? "",
        r.FechaHechos ?? DateTime.UtcNow, r.Hechos ?? "", r.Acuerdos ?? "", r.Testigos ?? "", me.Identity?.Name ?? "",
        r.Tipo ?? "", r.Hora ?? "");
    if (status == 201)
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.CrearActa, $"{acta!.Folio} · {acta.Nombre}", Ip(ctx));
    return status == 201
        ? Results.Json(acta, json, statusCode: 201)
        : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization($"P:{CatalogoPermisos.ActasAdministrar}");

app.MapPut("/api/actas/{id:int}", (int id, ActaRequest r, ClaimsPrincipal me, IActaStore actas, IAuditLog bitacora, HttpContext ctx) =>
{
    var (status, error) = actas.Actualizar(id, r.Usuario ?? "", r.Nombre ?? "", r.Puesto ?? "", r.Lugar ?? "",
        r.FechaHechos ?? DateTime.UtcNow, r.Hechos ?? "", r.Acuerdos ?? "", r.Testigos ?? "", r.Tipo ?? "", r.Hora ?? "");
    if (status == 204)
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.EditarActa, $"#{id} · {(r.Nombre ?? "").Trim()}", Ip(ctx));
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization($"P:{CatalogoPermisos.ActasAdministrar}");

app.MapDelete("/api/actas/{id:int}", (int id, ClaimsPrincipal me, IActaStore actas, IAuditLog bitacora, HttpContext ctx) =>
{
    var (status, error) = actas.Eliminar(id);
    if (status == 204)
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.EliminarActa, $"#{id}", Ip(ctx));
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization($"P:{CatalogoPermisos.ActasAdministrar}");

// ---- Administrativos: credenciales ligadas a usuarios ----
// El alta es multipart (campos + foto tamaño infantil); la foto se guarda como archivo en
// App_Data/credenciales/ y se sirve solo a quien tiene credenciales.administrar.
app.MapGet("/api/credenciales", (ICredencialStore credenciales) =>
    Results.Json(credenciales.List(), json)).RequireAuthorization($"P:{CatalogoPermisos.CredencialesAdministrar}");

app.MapPost("/api/credenciales", async (HttpContext ctx, ClaimsPrincipal me, ICredencialStore credenciales,
    IUserStore usuarios, IWebHostEnvironment env, IAuditLog bitacora) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var archivo = form.Files.GetFile("foto");
    byte[]? bytes = null;
    if (archivo is not null)
    {
        using var ms = new MemoryStream();
        await archivo.CopyToAsync(ms);
        bytes = ms.ToArray();
    }
    if (bytes is null || bytes.Length == 0)
        return Results.Json(new { error = "La foto tamaño infantil es obligatoria." }, statusCode: 400);
    if (FotosCredencial.Validar(archivo!.ContentType, bytes.Length, bytes) is { } errorFoto)
        return Results.Json(new { error = errorFoto }, statusCode: 400);
    var usuario = form["usuario"].ToString();
    if (usuarios.Find(usuario) is null)
        return Results.Json(new { error = "El usuario ligado a la credencial no existe." }, statusCode: 400);

    // La foto se escribe primero con nombre temporal: si el registro falla, no queda basura.
    var dir = FotosCredencial.Directorio(env);
    Directory.CreateDirectory(dir);
    var tmp = Path.Combine(dir, "tmp-" + Guid.NewGuid().ToString("n") + FotosCredencial.Extension(archivo.ContentType));
    await File.WriteAllBytesAsync(tmp, bytes);
    var (status, error, id) = credenciales.Crear(usuario, form["nombre"].ToString(), form["puesto"].ToString(),
        form["nss"].ToString(), form["cedula"].ToString(), form["vigencia"].ToString(), "", me.Identity?.Name ?? "");
    if (status != 201)
    {
        try { File.Delete(tmp); } catch { /* sin efecto */ }
        return Results.Json(new { error }, statusCode: status);
    }
    var nombreFoto = FotosCredencial.NombreArchivo(id, archivo.ContentType);
    File.Move(tmp, Path.Combine(dir, nombreFoto), overwrite: true);
    credenciales.CambiarFoto(id, nombreFoto);
    bitacora.Register(me.Identity?.Name ?? "", AuditEvents.CrearCredencial, $"#{id} · {form["nombre"].ToString().Trim()}", Ip(ctx));
    return Results.Json(credenciales.Find(id), json, statusCode: 201);
}).RequireAuthorization($"P:{CatalogoPermisos.CredencialesAdministrar}");

app.MapPut("/api/credenciales/{id:int}", (int id, CredencialRequest r, ClaimsPrincipal me,
    ICredencialStore credenciales, IAuditLog bitacora, HttpContext ctx) =>
{
    var (status, error) = credenciales.Actualizar(id, r.Nombre ?? "", r.Puesto ?? "", r.Nss ?? "", r.Cedula ?? "", r.Vigencia ?? "");
    if (status == 204)
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.EditarCredencial, $"#{id} · {(r.Nombre ?? "").Trim()}", Ip(ctx));
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization($"P:{CatalogoPermisos.CredencialesAdministrar}");

// Reemplaza la foto tamaño infantil de una credencial existente.
app.MapPost("/api/credenciales/{id:int}/foto", async (int id, HttpContext ctx, ClaimsPrincipal me,
    ICredencialStore credenciales, IWebHostEnvironment env, IAuditLog bitacora) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var archivo = form.Files.GetFile("foto");
    byte[]? bytes = null;
    if (archivo is not null)
    {
        using var ms = new MemoryStream();
        await archivo.CopyToAsync(ms);
        bytes = ms.ToArray();
    }
    if (bytes is null || bytes.Length == 0)
        return Results.Json(new { error = "La foto tamaño infantil es obligatoria." }, statusCode: 400);
    if (FotosCredencial.Validar(archivo!.ContentType, bytes.Length, bytes) is { } errorFoto)
        return Results.Json(new { error = errorFoto }, statusCode: 400);
    var dir = FotosCredencial.Directorio(env);
    Directory.CreateDirectory(dir);
    var nombreFoto = FotosCredencial.NombreArchivo(id, archivo.ContentType);
    var tmp = Path.Combine(dir, "tmp-" + Guid.NewGuid().ToString("n") + FotosCredencial.Extension(archivo.ContentType));
    await File.WriteAllBytesAsync(tmp, bytes);
    var (status, error, anterior) = credenciales.CambiarFoto(id, nombreFoto);
    if (status != 204)
    {
        try { File.Delete(tmp); } catch { /* sin efecto */ }
        return Results.Json(new { error }, statusCode: status);
    }
    File.Move(tmp, Path.Combine(dir, nombreFoto), overwrite: true);
    if (anterior is not null && anterior != nombreFoto)
    {
        var rutaAnterior = Path.Combine(dir, Path.GetFileName(anterior));
        try { File.Delete(rutaAnterior); } catch { /* sin efecto */ }
    }
    bitacora.Register(me.Identity?.Name ?? "", AuditEvents.EditarCredencial, $"#{id} · foto reemplazada", Ip(ctx));
    return Results.NoContent();
}).RequireAuthorization($"P:{CatalogoPermisos.CredencialesAdministrar}");

// Foto de la credencial (miniaturas y render); solo quien administra credenciales.
app.MapGet("/api/credenciales/{id:int}/foto", (int id, ICredencialStore credenciales, IWebHostEnvironment env) =>
{
    var c = credenciales.Find(id);
    if (c is null) return Results.Json(new { error = "La credencial no existe." }, statusCode: 404);
    var ruta = FotosCredencial.Ruta(env, c.FotoArchivo);
    if (ruta is null) return Results.Json(new { error = "La credencial aún no tiene foto." }, statusCode: 404);
    var tipo = c.FotoArchivo.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
    return Results.File(ruta, tipo);
}).RequireAuthorization($"P:{CatalogoPermisos.CredencialesAdministrar}");

app.MapDelete("/api/credenciales/{id:int}", (int id, ClaimsPrincipal me, ICredencialStore credenciales,
    IWebHostEnvironment env, IAuditLog bitacora, HttpContext ctx) =>
{
    var (status, error, foto) = credenciales.Eliminar(id);
    if (status == 204)
    {
        if (foto is not null)
        {
            var ruta = Path.Combine(FotosCredencial.Directorio(env), Path.GetFileName(foto));
            try { File.Delete(ruta); } catch { /* sin efecto */ }
        }
        bitacora.Register(me.Identity?.Name ?? "", AuditEvents.EliminarCredencial, $"#{id}", Ip(ctx));
    }
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization($"P:{CatalogoPermisos.CredencialesAdministrar}");

// ---- Configuración del módulo Gestión (solo Admin) ----
app.MapGet("/api/config", (IConfigStore cfg, GestionService g, ClaimsPrincipal me) =>
{
    if (!g.Tiene(GestionService.De(me), CatalogoPermisos.ConfiguracionAdministrar))
        return Results.Json(new { error = "Tu rol no puede ver la configuración." }, statusCode: 403);
    // Las claves `sesion.minutos.*` son preferencias personales (viven en la misma tabla), no
    // parámetros del módulo: se quedan fuera de esta respuesta.
    return Results.Json(cfg.Todo()
        .Where(kv => !kv.Key.StartsWith(SesionPreferencias.PrefijoClave, StringComparison.Ordinal))
        .ToDictionary(kv => kv.Key, kv => kv.Value), json);
}).RequireAuthorization("SoloAdmin");

app.MapPut("/api/config", (ConfigRequest r, IConfigStore cfg, IAuditLog bitacora, GestionService g, ClaimsPrincipal me, HttpContext ctx) =>
{
    var s = GestionService.De(me);
    if (!g.Tiene(s, CatalogoPermisos.ConfiguracionAdministrar))
        return Results.Json(new { error = "No tienes permiso para cambiar la configuración." }, statusCode: 403);
    foreach (var (clave, valor) in r.Valores ?? new Dictionary<string, string>())
    {
        if (!ConfigKeys.EsClaveConocida(clave))
            return Results.Json(new { error = $"Clave de configuración desconocida: {clave}" }, statusCode: 400);
        if (clave == ConfigKeys.PeriodicidadReporteDias &&
            (!int.TryParse(valor, out var dias) || dias is < 1 or > 365))
            return Results.Json(new { error = "La periodicidad debe ser un número de días entre 1 y 365." }, statusCode: 400);
        if (clave == ConfigKeys.FechaLimiteHabilitada)
        {
            if (!g.Tiene(s, CatalogoPermisos.ConfiguracionFechaLimite))
                return Results.Json(new { error = "Tu rol no puede habilitar o deshabilitar la fecha límite." }, statusCode: 403);
            if (!bool.TryParse(valor, out var habilitada))
                return Results.Json(new { error = "El valor debe ser true (habilitada) o false (deshabilitada)." }, statusCode: 400);
            cfg.Set(clave, habilitada ? "true" : "false");
            continue;
        }
        cfg.Set(clave, valor);
    }
    bitacora.Register(s.Usuario, AuditEvents.CambiarConfig, string.Join(", ", (r.Valores ?? new()).Select(kv => $"{kv.Key}={kv.Value}")), Ip(ctx));
    return Results.NoContent();
}).RequireAuthorization("SoloAdmin");

// ---- Programador: catálogos del módulo Gestión ----
// Los edita por defecto solo la cuenta root (permiso `programador.administrar` marcado SoloRaiz);
// cualquier otro rol lo concede el Administrador desde "Roles y permisos". Se guardan como JSON en
// el almacén de configuración, así que las actividades ya registradas no se tocan.
var programador = app.MapGroup("/api/programador").RequireAuthorization($"P:{CatalogoPermisos.ProgramadorAdministrar}");

programador.MapGet("/catalogos", (GestionCatalogos catalogos) => Results.Json(catalogos.Todo(), json));

// Lista simple: actividades, servicios u hospitales.
programador.MapPut("/lista", (CatalogoListaRequest r, GestionCatalogos catalogos, IAuditLog bitacora,
    ClaimsPrincipal me, HttpContext ctx) =>
{
    var (status, error) = catalogos.GuardarLista(r.Clave ?? "", r.Valores);
    if (status != 204) return Results.Json(new { error }, statusCode: status);
    bitacora.Register(GestionService.De(me).Usuario, AuditEvents.CambiarConfig,
        $"{r.Clave} = {string.Join(" | ", r.Valores ?? new())}", Ip(ctx));
    return Results.NoContent();
});

// Subopciones por actividad (los aplicativos de una Capacitación, por ejemplo).
programador.MapPut("/subopciones", (CatalogoSubopcionesRequest r, GestionCatalogos catalogos, IAuditLog bitacora,
    ClaimsPrincipal me, HttpContext ctx) =>
{
    var (status, error) = catalogos.GuardarSubopciones(r.Subopciones, catalogos.Actividades());
    if (status != 204) return Results.Json(new { error }, statusCode: status);
    bitacora.Register(GestionService.De(me).Usuario, AuditEvents.CambiarConfig,
        $"{ConfigKeys.ActividadesSubopciones} = {string.Join(" | ", (r.Subopciones ?? new()).Select(s => $"{s.Actividad}:{s.Etiqueta}:{s.Opciones?.Count ?? 0}"))}", Ip(ctx));
    return Results.NoContent();
});

// Tipos por aplicativo: `{ "Aplicativos Fuji": ["Estación de trabajo", …], … }`. Es el tercer escalón
// de la cadena que se configura aquí —actividad → aplicativo → tipo—, así que el formulario del
// calendario lo pinta solo cuando el aplicativo elegido tiene lista propia; sin ella el campo no existe.
// Como el aplicativo y el servicio, lo elegido no ocupa una columna nueva: se arma dentro del título.
programador.MapPut("/tipos", (CatalogoTiposRequest r, GestionCatalogos catalogos, IAuditLog bitacora,
    ClaimsPrincipal me, HttpContext ctx) =>
{
    var (status, error) = catalogos.GuardarTiposPorSubopcion(r.PorSubopcion);
    if (status != 204) return Results.Json(new { error }, statusCode: status);
    bitacora.Register(GestionService.De(me).Usuario, AuditEvents.CambiarConfig,
        $"{ConfigKeys.SubopcionesTipos} = {string.Join(" | ", (r.PorSubopcion ?? new()).Select(kv => $"{kv.Key}:{kv.Value?.Count ?? 0}"))}", Ip(ctx));
    return Results.NoContent();
});

// Servicios por hospital: `{ HES: ["Anatomía Patológica", …], … }`. El formulario del calendario
// muestra solo los del hospital elegido, así que la lista depende de este catálogo.
programador.MapPut("/servicios", (CatalogoServiciosRequest r, GestionCatalogos catalogos, IAuditLog bitacora,
    ClaimsPrincipal me, HttpContext ctx) =>
{
    var (status, error) = catalogos.GuardarServiciosPorHospital(r.PorHospital);
    if (status != 204) return Results.Json(new { error }, statusCode: status);
    bitacora.Register(GestionService.De(me).Usuario, AuditEvents.CambiarConfig,
        $"{ConfigKeys.ServiciosPorHospital} = {string.Join(" | ", (r.PorHospital ?? new()).Select(kv => $"{kv.Key}:{kv.Value?.Count ?? 0}"))}", Ip(ctx));
    return Results.NoContent();
});

// Regiones médicas y su desglose: `{ "CMN La Raza": ["HGR","HGO"], … }`. Los hospitales se escriben,
// así que un código que no esté todavía en `gestion.hospitales` se agrega ahí mismo (el servicio deja
// constancia en la bitácora). La región no se guarda en el registro: los formularios y las consultas la
// derivan del hospital, así que reorganizarla reordena también lo ya capturado. Un hospital repetido
// entre dos regiones se rechaza.
programador.MapPut("/regiones", (CatalogoRegionesRequest r, GestionCatalogos catalogos, IAuditLog bitacora,
    ClaimsPrincipal me, HttpContext ctx) =>
{
    var antes = catalogos.HospitalesConfig();
    var (status, error) = catalogos.GuardarHospitalesPorRegion(r.PorRegion);
    if (status != 204) return Results.Json(new { error }, statusCode: status);
    var usuario = GestionService.De(me).Usuario;
    bitacora.Register(usuario, AuditEvents.CambiarConfig,
        $"{ConfigKeys.HospitalesPorRegion} = {string.Join(" | ", (r.PorRegion ?? new()).Select(kv => $"{kv.Key}:{string.Join(",", kv.Value ?? new())}"))}", Ip(ctx));
    var nuevos = catalogos.HospitalesConfig()
        .Where(h => !antes.Any(a => string.Equals(a, h, StringComparison.OrdinalIgnoreCase)))
        .ToList();
    if (nuevos.Count > 0)
        bitacora.Register(usuario, AuditEvents.CambiarConfig,
            $"{ConfigKeys.HospitalesLista} += {string.Join(",", nuevos)} (desde el desglose de regiones)", Ip(ctx));
    return Results.NoContent();
});

// ---- Tiempo de sesión de cada usuario (botón ⚙ de la cabecera del inicio) ----
// Se lee y se escribe siempre sobre la cuenta que viene en la cookie: la ruta no recibe nombre de
// usuario, así que nadie puede alargar ni cortar la sesión de otra persona. Solo se aceptan las
// ventanas de `SesionPreferencias.Opciones` y el cambio surte efecto en la sesión abierta.
app.MapGet("/api/sesion", (IConfigStore cfg, ClaimsPrincipal me) =>
    Results.Json(new
    {
        minutos = SesionPreferencias.Minutos(cfg, me.Identity?.Name),
        opciones = SesionPreferencias.Opciones,
    }, json)).RequireAuthorization();

app.MapPut("/api/sesion", async (SesionRequest r, IConfigStore cfg, IAuditLog bitacora, ClaimsPrincipal me, HttpContext ctx) =>
{
    var usuario = me.Identity?.Name ?? "";
    if (usuario.Length == 0)
        return Results.Json(new { error = "Necesitas una sesión abierta." }, statusCode: 401);
    if (!SesionPreferencias.EsOpcion(r.Minutos))
        return Results.Json(new { error = "Ese tiempo de sesión no está entre las opciones." }, statusCode: 400);

    var antes = SesionPreferencias.Minutos(cfg, usuario);
    var (status, error) = cfg.Set(SesionPreferencias.Clave(usuario), r.Minutos.ToString());
    if (status != 204)
        return Results.Json(new { error = error ?? "No se pudo guardar tu tiempo de sesión." }, statusCode: status);

    // Reemitir la cookie con la ventana nueva, para que se note en la sesión que ya está abierta y
    // no haya que cerrar sesión. Se conserva si era persistente («Recordarme») o de navegador.
    var actual = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    var props = new AuthenticationProperties { IsPersistent = actual.Properties?.IsPersistent ?? false };
    props.IssuedUtc = DateTimeOffset.UtcNow;
    props.ExpiresUtc = DateTimeOffset.UtcNow.Add(SesionPreferencias.VidaCookie);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, me, props);

    bitacora.Register(usuario, AuditEvents.CambiarSesion, $"{antes} → {r.Minutos} minutos", Ip(ctx));
    return Results.NoContent();
}).RequireAuthorization();

// ---- Tema (Oscuro · Claro) de cada usuario (botón ⚙ de la cabecera) ----
// Mismo reparto que el tiempo de sesión: la ruta no recibe nombre de usuario, así que cada quien solo
// mueve su cuenta, y lo elegido se guarda en el almacén de configuración bajo `tema.<usuario>`. Se
// responde además la cookie `meditechnosys.tema` para que la pantalla se pinta con el tema de la cuenta
// **antes** del primer fotograma —las pantallas autónomas son archivos estáticos y no pueden esperar a
// esta llamada—. La cookie es una copia: la cuenta manda.
app.MapGet("/api/tema", (IConfigStore cfg, ClaimsPrincipal me) =>
    Results.Json(new
    {
        tema = TemaPreferencias.Valor(cfg, me.Identity?.Name),
        opciones = TemaPreferencias.Opciones,
        porDefecto = TemaPreferencias.PorDefecto,
    }, json)).RequireAuthorization();

app.MapPut("/api/tema", (TemaRequest r, IConfigStore cfg, IAuditLog bitacora, ClaimsPrincipal me, HttpContext ctx) =>
{
    var usuario = me.Identity?.Name ?? "";
    if (usuario.Length == 0)
        return Results.Json(new { error = "Necesitas una sesión abierta." }, statusCode: 401);
    var tema = TemaPreferencias.Normalizar(r.Tema);
    if (tema is null)
        return Results.Json(new { error = "Ese tema no está entre las opciones." }, statusCode: 400);

    var antes = TemaPreferencias.Valor(cfg, usuario);
    var (status, error) = cfg.Set(TemaPreferencias.Clave(usuario), tema);
    if (status != 204)
        return Results.Json(new { error = error ?? "No se pudo guardar tu tema." }, statusCode: status);

    TemaPreferencias.EscribirEnEn(ctx, tema);
    bitacora.Register(usuario, AuditEvents.CambiarTema, $"{TemaPreferencias.Etiqueta(antes)} → {TemaPreferencias.Etiqueta(tema)}", Ip(ctx));
    return Results.NoContent();
}).RequireAuthorization();

// ---- Histórico de actividades: descarga en Excel ----
// Exporta exactamente lo que muestra la pantalla: los mismos filtros se aplican del lado del
// servidor. Se genera .xlsx real (ClosedXML); nunca CSV.
app.MapGet("/api/historico/excel", (string? texto, string? hospital, string? ingeniero, string? tipo, string? estado,
    string? prioridad, string? campoFecha, DateTime? desde, DateTime? hasta, string? creadoPor,
    ClaimsPrincipal me, GestionService g, HistoricoExportador exportador, IAuditLog bitacora, HttpContext ctx) =>
{
    var s = GestionService.De(me);
    if (!g.Tiene(s, CatalogoPermisos.HistoricoActividades))
        return Results.Json(new { error = "Tu rol no puede consultar el Histórico de actividades." }, statusCode: 403);
    if (!g.Tiene(s, CatalogoPermisos.HistoricoExportar))
        return Results.Json(new { error = "Tu rol no puede descargar el histórico en Excel." }, statusCode: 403);
    var usaFechaLimite = g.FechaLimiteHabilitada();
    var filtro = new FiltroHistorico(texto, hospital, ingeniero, tipo, estado, prioridad,
        usaFechaLimite ? campoFecha : "fecha", desde, hasta, creadoPor);
    var lista = HistoricoExportador.Aplicar(g.Visibles(s), filtro);
    var (nombre, archivo) = exportador.Exportar(lista, usaFechaLimite, filtro);
    bitacora.Register(s.Usuario, AuditEvents.ExportarExcel, $"Histórico de actividades · {lista.Count} registro(s)", Ip(ctx));
    return Results.File(archivo, Excel.ContentType, nombre);
}).RequireAuthorization("SoloAdmin");

// ---- Inventarios: equipos, IPs y AETitle ----
// Tres módulos independientes que comparten API; cada uno se consulta, filtra y exporta por hospital.
// Consultar y descargar: Admin e Ingeniero. Dar de alta, editar y eliminar: solo el Administrador.
app.MapGet("/api/inventario", () => Results.Json(InventarioCatalogo.Colecciones, json)).RequireAuthorization("Ingenieria");

app.MapGet("/api/inventario/{coleccion}", (string coleccion, string? hospital, string? q, ClaimsPrincipal me, InventarioService inv) =>
{
    var s = GestionService.De(me);
    if (inv.Coleccion(coleccion) is null)
        return Results.Json(new { error = "El inventario no existe." }, statusCode: 404);
    if (!inv.Tiene(s, CatalogoPermisos.InventarioConsultar))
        return Results.Json(new { error = "Tu rol no puede consultar los inventarios." }, statusCode: 403);
    return Results.Json(inv.Filtrar(s, coleccion, hospital, q), json);
}).RequireAuthorization("Ingenieria");

app.MapGet("/api/inventario/{coleccion}/excel", (string coleccion, string? hospital, string? q, ClaimsPrincipal me,
    InventarioService inv, IAuditLog bitacora, HttpContext ctx) =>
{
    var s = GestionService.De(me);
    var (status, error, nombre, archivo) = inv.Exportar(s, coleccion, hospital, q);
    if (status != 200 || archivo is null) return Results.Json(new { error }, statusCode: status);
    var c = inv.Coleccion(coleccion)!;
    bitacora.Register(s.Usuario, AuditEvents.ExportarExcel,
        $"{c.Nombre} · {(Hospitales.Normalizar(hospital).Length > 0 ? Hospitales.Normalizar(hospital) : "Todos")}", Ip(ctx));
    return Results.File(archivo, Excel.ContentType, nombre);
}).RequireAuthorization("Ingenieria");

app.MapPost("/api/inventario/{coleccion}", (string coleccion, InventarioRequest r, ClaimsPrincipal me, InventarioService inv, HttpContext ctx) =>
{
    var (status, error, item) = inv.Crear(GestionService.De(me), coleccion, r.Hospital ?? "", r.Datos ?? new(), Ip(ctx));
    return status == 201
        ? Results.Json(item, json, statusCode: 201)
        : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("SoloAdmin");

app.MapPut("/api/inventario/{coleccion}/{id:long}", (string coleccion, long id, InventarioRequest r, ClaimsPrincipal me,
    InventarioService inv, HttpContext ctx) =>
{
    var (status, error, item) = inv.Editar(GestionService.De(me), coleccion, id, r.Hospital ?? "", r.Datos ?? new(), Ip(ctx));
    return status == 200
        ? Results.Json(item, json)
        : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("SoloAdmin");

app.MapDelete("/api/inventario/{coleccion}/{id:long}", (string coleccion, long id, ClaimsPrincipal me, InventarioService inv, HttpContext ctx) =>
{
    var (status, error) = inv.Eliminar(GestionService.De(me), coleccion, id, Ip(ctx));
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("SoloAdmin");

// ---- Almacén de equipos: existencias, entradas, salidas, traslados, historial y hojas ----
// Las existencias salen del catálogo de equipos; los movimientos son el documento y la
// trazabilidad. Todos los endpoints validan el permiso en el servidor (no solo la interfaz).
app.MapGet("/api/almacen/existencias", (string? hospital, string? q, ClaimsPrincipal me, AlmacenService alm) =>
    Results.Json(alm.Existencias(GestionService.De(me), hospital, q), json))
    .RequireAuthorization("Ingenieria");

app.MapGet("/api/almacen/existencias/excel", (string? hospital, string? q, ClaimsPrincipal me,
    AlmacenService alm, IAuditLog bitacora, HttpContext ctx) =>
{
    var s = GestionService.De(me);
    if (!alm.Tiene(s, CatalogoPermisos.AlmacenExportar))
        return Results.Json(new { error = "Tu rol no puede descargar el almacén en Excel." }, statusCode: 403);
    var c = InventarioCatalogo.ColeccionEquipos;
    var lista = alm.Existencias(s, hospital, q);
    var encabezados = new List<string> { "ID", "Hospital" };
    encabezados.AddRange(c.Campos.Select(f => f.Etiqueta));
    var filas = lista.Select(i =>
    {
        var fila = new List<string> { i.Id.ToString(), i.Hospital };
        fila.AddRange(c.Campos.Select(f => i.Valor(f.Clave)));
        return (IReadOnlyList<string>)fila;
    }).ToList();
    bitacora.Register(s.Usuario, AuditEvents.ExportarExcel, "Existencias de almacén", Ip(ctx));
    return Results.File(Excel.Tabla("Existencias", encabezados, filas), Excel.ContentType, "Existencias_Almacen.xlsx");
}).RequireAuthorization("Ingenieria");

app.MapGet("/api/almacen/movimientos", (string? tipo, string? estado, string? hospital, DateTime? desde, DateTime? hasta,
    string? q, ClaimsPrincipal me, AlmacenService alm) =>
    Results.Json(alm.Movimientos(GestionService.De(me), tipo, estado, hospital, desde, hasta, q), json))
    .RequireAuthorization("Ingenieria");

app.MapGet("/api/almacen/movimientos/{id:long}", (long id, ClaimsPrincipal me, AlmacenService alm) =>
{
    var m = alm.Find(id);
    return m is null ? Results.Json(new { error = "El movimiento no existe." }, statusCode: 404) : Results.Json(m, json);
}).RequireAuthorization("Ingenieria");

app.MapPost("/api/almacen/movimientos", (AlmacenMovimientoRequest r, ClaimsPrincipal me, AlmacenService alm, HttpContext ctx) =>
{
    var borrador = new MovimientoInventario(0, "", r.Tipo ?? "", r.Motivo ?? "",
        r.HospitalOrigen ?? "", r.UbicacionOrigen ?? "", r.HospitalDestino ?? "", r.UbicacionDestino ?? "",
        EstadosMovimiento.Borrador, r.Fecha ?? DateTime.Now,
        r.ResponsableEntrega ?? "", r.ResponsableRecibe ?? "", "", r.DocumentoReferencia ?? "", r.Observaciones ?? "",
        DateTime.Now, null, null, "",
        (r.Detalles ?? new()).Select(d => new MovimientoDetalle(d.EquipoId, d.NumInventario ?? "", d.Serie ?? "",
            d.Nombre ?? "", d.Marca ?? "", d.Modelo ?? "", d.Cantidad)).ToList());
    var (status, error, item) = alm.Crear(GestionService.De(me), borrador, Ip(ctx));
    return status == 201 ? Results.Json(item, json, statusCode: 201) : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("Ingenieria");

app.MapPut("/api/almacen/movimientos/{id:long}/confirmar", (long id, ClaimsPrincipal me, AlmacenService alm, HttpContext ctx) =>
{
    var (status, error, item) = alm.Confirmar(GestionService.De(me), id, Ip(ctx));
    return status == 200 ? Results.Json(item, json) : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("Ingenieria");

app.MapPut("/api/almacen/movimientos/{id:long}/recibir", (long id, ClaimsPrincipal me, AlmacenService alm, HttpContext ctx) =>
{
    var (status, error, item) = alm.Recibir(GestionService.De(me), id, Ip(ctx));
    return status == 200 ? Results.Json(item, json) : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("Ingenieria");

app.MapPut("/api/almacen/movimientos/{id:long}/cancelar", (long id, bool rechazo, ClaimsPrincipal me, AlmacenService alm, HttpContext ctx) =>
{
    var (status, error, item) = alm.Cancelar(GestionService.De(me), id, rechazo, Ip(ctx));
    return status == 200 ? Results.Json(item, json) : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("Ingenieria");

app.MapGet("/api/almacen/historial/excel", (string? tipo, string? estado, string? hospital, DateTime? desde, DateTime? hasta,
    string? q, ClaimsPrincipal me, AlmacenService alm, IAuditLog bitacora, HttpContext ctx) =>
{
    var s = GestionService.De(me);
    if (!alm.Tiene(s, CatalogoPermisos.AlmacenExportar))
        return Results.Json(new { error = "Tu rol no puede descargar el historial en Excel." }, statusCode: 403);
    var lista = alm.Movimientos(s, tipo, estado, hospital, desde, hasta, q);
    var encabezados = new[] { "Folio", "Tipo", "Motivo", "Estado", "Fecha", "Origen", "Destino",
        "Entrega", "Recibe", "Registra", "Autoriza", "Equipos", "Observaciones" };
    var filas = lista.Select(m => (IReadOnlyList<string>)new[]
    {
        m.Folio, m.Tipo, m.Motivo, m.Estado, Excel.FmtFechaHora(m.Fecha), m.Origen, m.Destino,
        m.ResponsableEntrega, m.ResponsableRecibe, m.UsuarioRegistra, m.UsuarioAutoriza,
        string.Join("; ", m.Detalles.Select(d => $"{d.Etiqueta} x{d.Cantidad}")), m.Observaciones
    }).ToList();
    bitacora.Register(s.Usuario, AuditEvents.ExportarExcel, "Historial de almacén", Ip(ctx));
    return Results.File(Excel.Tabla("Historial almacen", encabezados, filas), Excel.ContentType, "Historial_Almacen.xlsx");
}).RequireAuthorization("Ingenieria");

// Hoja de ingreso/salida/traslado en Word (.doc): misma plantilla institucional que la versión
// imprimible, generada desde los datos reales del movimiento (sin recaptura).
app.MapGet("/api/almacen/movimientos/{id:long}/word", (long id, ClaimsPrincipal me, AlmacenService alm) =>
{
    var s = GestionService.De(me);
    if (!alm.Tiene(s, CatalogoPermisos.AlmacenConsultar))
        return Results.Json(new { error = "Tu rol no puede consultar el almacén." }, statusCode: 403);
    var m = alm.Find(id);
    if (m is null) return Results.Json(new { error = "El movimiento no existe." }, statusCode: 404);
    var html = HojaAlmacenHtml(m);
    var nombre = $"{m.Folio}_{(m.Tipo == TiposMovimiento.Entrada ? "Ingreso" : m.Tipo == TiposMovimiento.Salida ? "Salida" : "Traslado")}.doc";
    return Results.File(System.Text.Encoding.UTF8.GetBytes(html), "application/msword", nombre);
}).RequireAuthorization("Ingenieria");

// Misma plantilla institucional pero servida como HTML en línea (sin descarga) para que
// /almacen/hoja.html la muestre y permita imprimirla o guardarla en PDF. Una sola fuente de
// verdad: la hoja imprimible y la versión Word comparten HojaAlmacenHtml.
app.MapGet("/api/almacen/movimientos/{id:long}/hoja", (long id, ClaimsPrincipal me, AlmacenService alm) =>
{
    var s = GestionService.De(me);
    if (!alm.Tiene(s, CatalogoPermisos.AlmacenConsultar))
        return Results.Json(new { error = "Tu rol no puede consultar el almacén." }, statusCode: 403);
    var m = alm.Find(id);
    if (m is null) return Results.Json(new { error = "El movimiento no existe." }, statusCode: 404);
    return Results.Content(HojaAlmacenHtml(m), "text/html; charset=utf-8");
}).RequireAuthorization("Ingenieria");

// Plantilla institucional de la hoja de ingreso/salida/traslado (misma que Entrada.docx /
// Salida.docx). Se usa tanto para la versión Word como para la imprimible en línea.
string HojaAlmacenHtml(MovimientoInventario m)
{
    var cfg = app.Configuration;
    var contrato = cfg["Almacen:Contrato"] ?? "D260012SI";
    var vigInicio = cfg["Almacen:VigenciaInicio"] ?? "01/JULIO/2026";
    var vigFin = cfg["Almacen:VigenciaFin"] ?? "31/DICIEMBRE/2026";
    var titulo = m.Tipo switch
    {
        TiposMovimiento.Entrada => "ENTRADA DE EQUIPO",
        TiposMovimiento.Salida => "SALIDA DE EQUIPO",
        _ => "TRASLADO DE EQUIPO",
    };
    var unidad = m.Tipo == TiposMovimiento.Salida ? m.HospitalOrigen : m.HospitalDestino;
    if (string.Equals(unidad, "General", StringComparison.Ordinal)) unidad = "ALMACÉN CENTRAL";
    var area = m.Tipo == TiposMovimiento.Salida ? m.UbicacionOrigen : m.UbicacionDestino;
    var sb = new System.Text.StringBuilder();
    sb.Append("<html xmlns:w='urn:schemas-microsoft-com:office:word'><head><meta charset='utf-8'><style>");
    sb.Append("body{font-family:Calibri,Arial,sans-serif;font-size:12px} ");
    sb.Append("h1{font-size:16px;text-align:center;margin:6px 0} ");
    sb.Append("p{margin:8px 0} ");
    sb.Append("table{border-collapse:collapse;width:100%} ");
    sb.Append("td,th{border:1px solid #000;padding:6px;font-size:12px;vertical-align:top} ");
    sb.Append("th{background:#eee;text-align:left} ");
    sb.Append(".firmas{margin-top:26px} .firmas td{border:none;padding:2px 4px} ");
    sb.Append(".firma{height:60px;vertical-align:bottom} .sep{width:16px} ");
    sb.Append("</style></head><body>");
    sb.Append("<h1>").Append(titulo).Append("</h1>");
    // Encabezado institucional (misma tabla que Entrada.docx / Salida.docx).
    sb.Append("<table>");
    sb.Append("<tr><td colspan='2'><b>MEDITECHNOSYS GROUP S.A. DE C.V.</b></td></tr>");
    sb.Append("<tr><td colspan='2'>UNIDAD MÉDICA: ").Append(System.Net.WebUtility.HtmlEncode(unidad)).Append("</td></tr>");
    sb.Append("<tr><td colspan='2'>AREA: ").Append(System.Net.WebUtility.HtmlEncode(area)).Append("</td></tr>");
    sb.Append("<tr><td>NÚMERO DE CONTRATO:</td><td>").Append(contrato).Append("</td></tr>");
    sb.Append("<tr><td>FECHA DE INICIO DE VIGENCIA:</td><td>").Append(vigInicio).Append("</td></tr>");
    sb.Append("<tr><td>FECHA DE TERMINO DE VIGENCIA:</td><td>").Append(vigFin).Append("</td></tr>");
    sb.Append("</table>");
    sb.Append("<p>").Append(m.Fecha.ToString("dddd, dd \\de MMMM \\de yyyy", new System.Globalization.CultureInfo("es-MX")))
      .Append(" &nbsp;&nbsp; FOLIO: ").Append(m.Folio).Append("</p>");
    if (m.Tipo == TiposMovimiento.Traslado)
        sb.Append("<p><b>ORIGEN:</b> ").Append(System.Net.WebUtility.HtmlEncode(m.Origen)).Append(" &nbsp; <b>DESTINO:</b> ").Append(System.Net.WebUtility.HtmlEncode(m.Destino)).Append("</p>");
    // Tabla de equipos; OBSERVACIONES va DENTRO de la tabla, como en los formatos originales.
    sb.Append("<table><tr><th>EQUIPO</th><th>NUMERO DE SERIE</th><th>UNIDADES</th></tr>");
    foreach (var d in m.Detalles)
        sb.Append("<tr><td>").Append(System.Net.WebUtility.HtmlEncode(d.Nombre))
          .Append(string.IsNullOrWhiteSpace(d.Marca) ? "" : " " + System.Net.WebUtility.HtmlEncode(d.Marca))
          .Append(string.IsNullOrWhiteSpace(d.Modelo) ? "" : " " + System.Net.WebUtility.HtmlEncode(d.Modelo))
          .Append("</td><td>").Append(System.Net.WebUtility.HtmlEncode(d.Serie))
          .Append("</td><td>").Append(d.Cantidad).Append("</td></tr>");
    sb.Append("<tr><td>OBSERVACIONES</td><td colspan='2'>").Append(System.Net.WebUtility.HtmlEncode(m.Observaciones)).Append("</td></tr>");
    sb.Append("</table>");
    // Firmas: solo la línea en blanco y el puesto, SIN nombres (como en los formatos originales).
    sb.Append("<table class='firmas'>");
    sb.Append("<tr><td class='firma'>").Append(new string('_', 30)).Append("</td><td class='sep'></td>")
      .Append("<td class='firma'>").Append(new string('_', 28)).Append("</td><td class='sep'></td>")
      .Append("<td class='firma'>").Append(new string('_', 29)).Append("</td></tr>");
    sb.Append("<tr><td>Nombre y firma del jefe de servicio o coordinador clínico de la unidad médica o administrador de la unidad medica</td><td class='sep'></td>")
      .Append("<td>Nombre y firma del jefe del departamento de finanzas</td><td class='sep'></td>")
      .Append("<td>Nombre y firma del supervisor por parte del proveedor</td></tr>");
    sb.Append("</table>");
    sb.Append("</body></html>");
    return sb.ToString();
}

// ---- Playbook: documentación técnica en PDF clasificada por hospital ----
// Admin e Ingeniero consultan y abren los PDF; solo el Administrador los sube, renombra,
// cambia de versión, reemplaza el archivo o los elimina.
app.MapGet("/api/playbook/documentos", (string? hospital, string? q, string? estado, ClaimsPrincipal me, PlaybookService pb) =>
{
    var s = GestionService.De(me);
    if (!pb.Tiene(s, CatalogoPermisos.PlaybookConsultar))
        return Results.Json(new { error = "Tu rol no puede consultar el Playbook." }, statusCode: 403);
    return Results.Json(pb.Filtrar(s, hospital, q, estado), json);
}).RequireAuthorization("Ingenieria");

app.MapGet("/api/playbook/documentos/{id:long}/pdf", (long id, ClaimsPrincipal me, PlaybookService pb, HttpContext ctx) =>
{
    var s = GestionService.De(me);
    if (!pb.Tiene(s, CatalogoPermisos.PlaybookConsultar))
        return Results.Json(new { error = "Tu rol no puede consultar el Playbook." }, statusCode: 403);
    var doc = pb.Find(id);
    if (doc is null) return Results.Json(new { error = "El documento no existe." }, statusCode: 404);
    var ruta = pb.RutaPdf(doc);
    if (ruta is null) return Results.Json(new { error = "El archivo PDF no está disponible." }, statusCode: 404);
    var crudo = Path.GetFileNameWithoutExtension(doc.NombreArchivo.Length > 0 ? doc.NombreArchivo : doc.Nombre);
    var ascii = new string(crudo.Select(c => c < 128 && char.IsLetterOrDigit(c) ? c : '_').ToArray());
    if (ascii.Length > 80) ascii = ascii[..80];
    ctx.Response.Headers["Cache-Control"] = "private, max-age=120";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Content-Disposition"] =
        $"inline; filename=\"{ascii}.pdf\"; filename*=UTF-8''{Uri.EscapeDataString(crudo + ".pdf")}";
    return Results.File(ruta, "application/pdf", enableRangeProcessing: true);
}).RequireAuthorization("Ingenieria");

app.MapPost("/api/playbook/documentos", async (HttpContext ctx, ClaimsPrincipal me, PlaybookService pb) =>
{
    var form = await ctx.Request.ReadFormAsync();
    byte[]? contenido = null;
    var archivo = form.Files.GetFile("archivo");
    var nombreArchivo = archivo?.FileName ?? "";
    if (archivo is not null)
    {
        using var ms = new MemoryStream();
        await archivo.CopyToAsync(ms);
        contenido = ms.ToArray();
    }
    var (status, error, doc) = pb.Subir(GestionService.De(me), form["nombre"].ToString(), form["tipo"].ToString(),
        form["version"].ToString(),
        form["hospital"].ToString(), Fecha(form["fechaPublicacion"].ToString()),
        string.IsNullOrWhiteSpace(form["estado"].ToString()) ? EstadosDocumento.Vigente : form["estado"].ToString(),
        nombreArchivo, contenido, Ip(ctx));
    return status == 201
        ? Results.Json(doc, json, statusCode: 201)
        : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("SoloAdmin");

app.MapPut("/api/playbook/documentos/{id:long}", (long id, DocumentoRequest r, ClaimsPrincipal me, PlaybookService pb, HttpContext ctx) =>
{
    var (status, error, doc) = pb.Editar(GestionService.De(me), id, r.Nombre ?? "", r.Tipo ?? "", r.Version ?? "",
        r.Hospital ?? "", r.FechaPublicacion, r.Estado ?? EstadosDocumento.Vigente, Ip(ctx));
    return status == 200
        ? Results.Json(doc, json)
        : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("SoloAdmin");

app.MapPost("/api/playbook/documentos/{id:long}/archivo", async (long id, HttpContext ctx, ClaimsPrincipal me, PlaybookService pb) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var archivo = form.Files.GetFile("archivo");
    byte[]? contenido = null;
    if (archivo is not null)
    {
        using var ms = new MemoryStream();
        await archivo.CopyToAsync(ms);
        contenido = ms.ToArray();
    }
    var (status, error, doc) = pb.Reemplazar(GestionService.De(me), id, archivo?.FileName ?? "", contenido, Ip(ctx));
    return status == 200
        ? Results.Json(doc, json)
        : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("SoloAdmin");

app.MapDelete("/api/playbook/documentos/{id:long}", (long id, ClaimsPrincipal me, PlaybookService pb, HttpContext ctx) =>
{
    var (status, error) = pb.Eliminar(GestionService.De(me), id, Ip(ctx));
    return status == 204 ? Results.NoContent() : Results.Json(new { error }, statusCode: status);
}).RequireAuthorization("SoloAdmin");

app.Run();

record LoginRequest(string? Usuario, string? Password);
// Buzón que se enlaza al dar de alta al usuario (opcional: si viene vacio, el usuario vera el
// buzón de soporte). La contrasena se cifra antes de guardarse y nunca se devuelve.
record BuzonPruebaRequest(string? Correo, string? Password, string? ImapHost, int? ImapPort, bool? ImapSsl,
    string? SmtpHost, int? SmtpPort, bool? SmtpSsl);
record CreateUserRequest(string? Usuario, string? Password, string? Rol, string? Nombre, string? Cargo,
    BuzonRequest? Buzon);
record DatosUsuarioRequest(string? Nombre, string? Cargo);
record RegionUsuarioRequest(IReadOnlyList<string>? Regiones, bool? Todas);
record PasswordRequest(string? Password);
record RoleRequest(string? Rol);
record ActivoRequest(bool? Activo);
record ActividadRequest(string? Tipo, string? Hospital, string? Titulo, string? Descripcion, string? Usuario, DateTime? Fecha,
    DateTime? FechaLimite, string? Prioridad, int? PeriodicidadDias, long? RelacionId);
record EstadoRequest(string? Estado, string? Informe, string? Justificacion, string? Comentario);
/// Reabrir una actividad completada: el motivo es opcional y queda en el historial.
record ReabrirRequest(string? Motivo);
record AsignacionRequest(string? Usuario, string? Prioridad, DateTime? FechaLimite, int? PeriodicidadDias, string? Comentario);
record ObservacionRequest(string? Comentario);
record ReporteRequest(string? Estado, int? Avance, string? DescripcionAvance, string? Problemas, string? Pendientes,
    string? ProximasAcciones, DateTime? FechaEstimada, string? Comentarios);
record PermisoRequest(string? Rol, List<string>? Permisos);
record RolRequest(string? Nombre);
record AjustePermiso(string? Clave, bool Concedido);
record UsuarioPermisosRequest(List<AjustePermiso>? Ajustes);
record AvisoRequest(string? Titulo, string? Cuerpo, string? Etiqueta, bool Activo);

record ActaRequest(string? Usuario, string? Nombre, string? Puesto, string? Lugar,
    DateTime? FechaHechos, string? Hechos, string? Acuerdos, string? Testigos, string? Tipo, string? Hora);

record CredencialRequest(string? Nombre, string? Puesto, string? Nss, string? Cedula, string? Vigencia);
record ConfigRequest(Dictionary<string, string>? Valores);

// Catálogos de la pantalla Programador: una lista (`clave` + `valores`) o las subopciones por
// actividad, que se envían como `[{ actividad, etiqueta, opciones }]`.
record CatalogoListaRequest(string? Clave, List<string>? Valores);
record CatalogoSubopcionesRequest(List<GestionCatalogos.SubopcionCrudo>? Subopciones);
record CatalogoTiposRequest(Dictionary<string, List<string>?>? PorSubopcion);
record CatalogoServiciosRequest(Dictionary<string, List<string>?>? PorHospital);
// Desglose por región médica: `{ "CMN La Raza": ["HGR", "HGO"], … }`, siempre la lista completa.
record CatalogoRegionesRequest(Dictionary<string, List<string>?>? PorRegion);
record SesionRequest(int Minutos);
record TemaRequest(string? Tema);
record RangoUbicacionRequest(string? Rango, string? Ubicacion);
record LugarRequest(double? Latitud, double? Longitud, int? Precision, string? Direccion, string? Fuente);
record SitioCalibrarRequest(double? Latitud, double? Longitud, string? Direccion);
record InventarioRequest(string? Hospital, Dictionary<string, string>? Datos);
record DocumentoRequest(string? Nombre, string? Tipo, string? Version, string? Hospital, DateTime? FechaPublicacion, string? Estado);
record AlmacenDetalleRequest(long EquipoId, string? NumInventario, string? Serie, string? Nombre, string? Marca, string? Modelo, int Cantidad);
record AlmacenMovimientoRequest(string? Tipo, string? Motivo, string? HospitalOrigen, string? UbicacionOrigen,
    string? HospitalDestino, string? UbicacionDestino, string? ResponsableEntrega, string? ResponsableRecibe,
    string? DocumentoReferencia, string? Observaciones, DateTime? Fecha, List<AlmacenDetalleRequest>? Detalles);
