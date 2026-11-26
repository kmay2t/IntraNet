using Meditechnosys.Intranet.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Meditechnosys.Intranet.Services;

/// <summary>
/// Capa corta de caché para las lecturas que se repiten en cada petición: validación de sesión,
/// tiempo de sesión y tema. No sustituye al almacén: los cambios hechos por esta aplicación
/// invalidan la entrada afectada y, como máximo, un cambio externo tarda el TTL en verse.
/// </summary>
public sealed class CachedUserStore(IUserStore inner, IMemoryCache cache, IConfiguration config) : IUserStore
{
    private sealed record Entrada(UserRecord? Usuario);
    private readonly TimeSpan _ttl = Ventana(config, "Auth:SessionCacheSeconds", 15);

    public IReadOnlyList<UserRecord> List() => inner.List();

    public UserRecord? Find(string usuario)
    {
        var nombre = (usuario ?? "").Trim();
        // Los nombres válidos tienen como máximo 40 caracteres. No guardar entradas enormes
        // procedentes de una cookie o de un formulario manipulado.
        if (nombre.Length is 0 or > 40) return inner.Find(nombre);
        var key = "usuario:" + nombre.ToLowerInvariant();
        if (cache.TryGetValue(key, out Entrada? entrada)) return entrada!.Usuario;

        var encontrado = inner.Find(nombre);
        cache.Set(key, new Entrada(encontrado), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _ttl,
            Size = 1
        });
        return encontrado;
    }

    public (int Status, string? Error, UserRecord? User) Create(string usuario, string password, string rol,
        bool validatePassword = true, string nombre = "", string cargo = "")
    {
        Quitar(usuario);
        var resultado = inner.Create(usuario, password, rol, validatePassword, nombre, cargo);
        if (resultado.Status == 201) Quitar(usuario);
        return resultado;
    }

    public (int Status, string? Error) Delete(string usuario)
    {
        var resultado = inner.Delete(usuario);
        if (resultado.Status == 204) Quitar(usuario);
        return resultado;
    }

    public (int Status, string? Error) SetPassword(string usuario, string password)
    {
        var resultado = inner.SetPassword(usuario, password);
        if (resultado.Status == 204) Quitar(usuario);
        return resultado;
    }

    public (int Status, string? Error) SetRole(string usuario, string rol)
    {
        var resultado = inner.SetRole(usuario, rol);
        if (resultado.Status == 204) Quitar(usuario);
        return resultado;
    }

    public (int Status, string? Error) SetActive(string usuario, bool activo)
    {
        var resultado = inner.SetActive(usuario, activo);
        if (resultado.Status == 204) Quitar(usuario);
        return resultado;
    }

    public (int Status, string? Error) SetDatos(string usuario, string nombre, string cargo)
    {
        var resultado = inner.SetDatos(usuario, nombre, cargo);
        if (resultado.Status == 204) Quitar(usuario);
        return resultado;
    }

    private void Quitar(string? usuario)
    {
        var nombre = (usuario ?? "").Trim();
        if (nombre.Length is > 0 and <= 40)
            cache.Remove("usuario:" + nombre.ToLowerInvariant());
    }

    private static TimeSpan Ventana(IConfiguration config, string clave, int segundos)
    {
        var valor = config.GetValue<int?>(clave) ?? segundos;
        return TimeSpan.FromSeconds(Math.Clamp(valor, 1, 60));
    }
}

/// <summary>
/// Evita volver a consultar MySQL por el tema y el tiempo de sesión en cada petición. Un Set
/// correcto invalida la clave y el catálogo completo; otros cambios externos se ven como máximo
/// al terminar la ventana corta.
/// </summary>
public sealed class CachedConfigStore(IConfigStore inner, IMemoryCache cache, IConfiguration config) : IConfigStore
{
    private sealed record Entrada(string Valor);
    private readonly TimeSpan _ttl = Ventana(config, "Cache:ConfigSeconds", 15);

    public string Get(string clave, string predeterminado)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 60) return inner.Get(clave, predeterminado);
        var key = "config:" + clave;
        if (cache.TryGetValue(key, out Entrada? entrada)) return entrada!.Valor;

        var valor = inner.Get(clave, predeterminado);
        cache.Set(key, new Entrada(valor), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _ttl,
            Size = 1
        });
        return valor;
    }

    public (int Status, string? Error) Set(string clave, string valor)
    {
        cache.Remove("config:" + clave);
        cache.Remove("config:todo");
        var resultado = inner.Set(clave, valor);
        if (resultado.Status == 204)
        {
            cache.Remove("config:" + clave);
            cache.Remove("config:todo");
        }
        return resultado;
    }

    public IReadOnlyDictionary<string, string> Todo()
    {
        const string key = "config:todo";
        if (cache.TryGetValue(key, out IReadOnlyDictionary<string, string>? datos)) return datos!;

        var valor = inner.Todo();
        cache.Set(key, valor, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _ttl,
            Size = 1
        });
        return valor;
    }

    private static TimeSpan Ventana(IConfiguration config, string clave, int segundos)
    {
        var valor = config.GetValue<int?>(clave) ?? segundos;
        return TimeSpan.FromSeconds(Math.Clamp(valor, 1, 60));
    }
}
