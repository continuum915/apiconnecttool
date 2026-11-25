using System.Net;

namespace ApiConnectTool.Services;

public interface ICookieJarService
{
    /// <summary>The single, workspace-lifetime cookie jar shared by every HTTP path
    /// (pooled client and per-request proxy/TLS-bypass handlers alike).</summary>
    CookieContainer Container { get; }
    string? PersistenceWarning => null;

    /// <summary>Persists the jar's current contents to disk.</summary>
    void Save();

    /// <summary>Expires every cookie currently held and persists that.</summary>
    void Clear();
}
