using System.Web;
using System.Web.SessionState;
using ConfReg.Domain.Abstractions;

namespace ConfReg.Legacy.Infrastructure
{
    /// <summary>
    /// The legacy answer to ISessionStore: the same HttpContext.Current.Session that Phase 1's business logic reached
    /// for directly, now confined to the web project, where System.Web belongs. In Phase 5 the modern app supplies an
    /// ISession-backed one, and the two share state through SystemWebAdapters.
    /// </summary>
    public class HttpSessionStore : ISessionStore
    {
        private static HttpSessionState Session { get { return HttpContext.Current.Session; } }

        public T Get<T>(string key) where T : class { return Session[key] as T; }
        public void Set(string key, object value) { Session[key] = value; }
        public void Remove(string key) { Session.Remove(key); }
    }

    public class HttpClientInfo : IClientInfo
    {
        // Null-safe, unlike Phase 1: a caller outside a request now gets "no address" instead of a NullReferenceException.
        public string IpAddress { get { var c = HttpContext.Current; return c == null ? null : c.Request.UserHostAddress; } }
    }

    public class ServerAppPaths : IAppPaths
    {
        public string Resolve(string appRelativePath) { return HttpContext.Current.Server.MapPath(appRelativePath); }
    }
}
