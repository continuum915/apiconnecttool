using System.Collections.Generic;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

/// <summary>Result details for an OpenAPI import, including security mappings that need user attention.</summary>
public sealed class OpenApiImportReport
{
    public int SecuredRequestCount { get; internal set; }
    public int MappedRequestCount { get; internal set; }
    public IReadOnlyList<string> Warnings => _warnings;
    public bool HasWarnings => _warnings.Count > 0;

    private readonly List<string> _warnings = [];

    internal void AddWarning(string warning)
    {
        if (!_warnings.Contains(warning))
        {
            _warnings.Add(warning);
        }
    }

    public string ToUserMessage() => HasWarnings
        ? $"{SecuredRequestCount} secured request(s); {MappedRequestCount} mapped. {string.Join(" ", Warnings)}"
        : SecuredRequestCount == 0
            ? string.Empty
            : $"{SecuredRequestCount} secured request(s) mapped.";
}

public sealed record OpenApiImportResult(RequestCollection Collection, OpenApiImportReport Report);
