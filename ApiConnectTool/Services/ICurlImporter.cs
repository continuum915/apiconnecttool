using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface ICurlImporter
{
    /// <summary>
    /// Parses a pasted `curl ...` command into a <see cref="RequestItem"/>. Throws
    /// <see cref="CurlImportException"/> for anything it can't confidently represent - malformed
    /// quoting, an unsupported flag, a missing/duplicate URL - rather than silently producing a
    /// half-populated request.
    /// </summary>
    RequestItem Import(string command);
}
