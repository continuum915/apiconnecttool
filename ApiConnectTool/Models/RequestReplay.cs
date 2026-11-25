using System.Collections.Generic;
using ApiConnectTool.Services;

namespace ApiConnectTool.Models;

/// <summary>
/// In-memory preparation retained for a second HTTP exchange. Every exposed snapshot is
/// detached, so edits and script scope writes cannot change this replay. File paths remain
/// references; file contents and responses are not archived here.
/// </summary>
public sealed class RequestReplay
{
    private readonly RequestItem _request;
    private readonly RequestExecutionOptions _options;

    public RequestReplay(RequestItem request, RequestExecutionOptions options)
    {
        _request = request.CloneForEditing();
        _request.PreRequestScript = null;
        _request.TestScript = null;
        _request.Assertions.Clear();
        _options = RequestRunner.WithVariables(options,
            new Dictionary<string, string>(options.Variables ?? new Dictionary<string, string>()),
            new HashSet<string>(options.SecretVariableNames ?? new HashSet<string>()));
    }

    public RequestItem CreateRequest() => _request.CloneForEditing();

    public RequestExecutionOptions CreateOptions() => RequestRunner.WithVariables(_options,
        new Dictionary<string, string>(_options.Variables!), new HashSet<string>(_options.SecretVariableNames!));
}
