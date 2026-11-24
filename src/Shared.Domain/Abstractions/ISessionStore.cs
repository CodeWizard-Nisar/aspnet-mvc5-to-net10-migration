namespace ConfReg.Domain.Abstractions
{
    /// <summary>
    /// Per-visitor state that survives between requests. Replaces reaching into HttpContext.Current.Session
    /// (BASELINE-PROBLEM #1). Legacy.Web supplies one backed by HttpSessionState; ASP.NET Core supplies one
    /// backed by ISession; tests and workers use DictionarySessionStore.
    /// </summary>
    public interface ISessionStore
    {
        T Get<T>(string key) where T : class;
        void Set(string key, object value);
        void Remove(string key);
    }
}
