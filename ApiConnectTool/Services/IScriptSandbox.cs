using System.Collections.Generic;
using System.Threading;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface IScriptSandbox
{
    ScriptRunResult Run(string script, RequestItem request, RequestExecutionResult? response,
        IReadOnlyDictionary<string, string> variables, IReadOnlySet<string> secretNames,
        IReadOnlyList<string> secretValues, int timeoutMilliseconds = 2000,
        CancellationToken cancellationToken = default, ScriptVariableScopes? scopes = null);
}
