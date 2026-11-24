using System.Collections.Generic;
using System.IO;

namespace ConfReg.Domain.Abstractions
{
    /// <summary>In-memory session. For tests and for hosts with no web session (workers).</summary>
    public class DictionarySessionStore : ISessionStore
    {
        private readonly Dictionary<string, object> _items = new Dictionary<string, object>();

        public T Get<T>(string key) where T : class
        {
            object value;
            return _items.TryGetValue(key, out value) ? value as T : null;
        }

        public void Set(string key, object value) { _items[key] = value; }
        public void Remove(string key) { _items.Remove(key); }
    }

    public class StaticClientInfo : IClientInfo
    {
        public StaticClientInfo(string ipAddress = null) { IpAddress = ipAddress; }
        public string IpAddress { get; private set; }
    }

    /// <summary>Resolves "~/x" under a fixed directory. What a worker or a test uses instead of Server.MapPath.</summary>
    public class DirectoryAppPaths : IAppPaths
    {
        private readonly string _root;
        public DirectoryAppPaths(string root) { _root = root; }

        public string Resolve(string appRelativePath)
        {
            string relative = appRelativePath.StartsWith("~/") ? appRelativePath.Substring(2) : appRelativePath;
            return Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
