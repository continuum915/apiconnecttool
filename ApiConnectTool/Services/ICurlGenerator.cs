using System.Collections.Generic;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface ICurlGenerator
{
    /// <summary>
    /// Builds a POSIX-shell-quoted, runnable curl command for <paramref name="request"/>,
    /// resolving any {{variable}} tokens against <paramref name="variables"/> first (so the
    /// command works standalone, without ApiConnectTool, and without a live send). By default the result
    /// is redacted (Bearer/API-key token, Basic password, OAuth2 client secret) exactly like
    /// history and other non-live surfaces; pass <paramref name="revealSecrets"/> true for an
    /// explicit, user-driven "reveal and copy the runnable command" action.
    /// </summary>
    string Generate(RequestItem request, IReadOnlyDictionary<string, string> variables, bool revealSecrets);
}
