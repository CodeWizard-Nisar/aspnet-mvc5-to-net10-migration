using ConfReg.Domain.Abstractions;
using Microsoft.AspNetCore.SystemWebAdapters;

namespace ConfReg.Modern.Infrastructure;

/// <summary>
/// ISessionStore over the SHARED session, read through SystemWebAdapters (the modern counterpart of Legacy.Web's
/// HttpSessionStore). Values are deserialised by the session serializer using the keys registered from SessionKeys, which is
/// why a value only appears here if BOTH apps registered its key.
/// </summary>
public class SharedSessionStore(IHttpContextAccessor accessor) : ISessionStore
{
    private System.Web.SessionState.HttpSessionState Session => accessor.HttpContext!.AsSystemWeb().Session;

    public T Get<T>(string key) where T : class => Session[key] as T;
    public void Set(string key, object value) => Session[key] = value;
    public void Remove(string key) => Session.Remove(key);
}

/// <summary>IClientInfo for ASP.NET Core. Behind the gateway the address arrives in X-Forwarded-For; UseForwardedHeaders applies it.</summary>
public class HttpContextClientInfo(IHttpContextAccessor accessor) : IClientInfo
{
    public string IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
