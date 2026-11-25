using System.Collections.Generic;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public sealed class PostmanImportReport
{
    private readonly List<string> _warnings = new();
    private readonly List<string> _notes = new();
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Informational lines about changes import made that need no action.</summary>
    public IReadOnlyList<string> Notes => _notes;
    public bool HasWarnings => _warnings.Count > 0;
    internal void AddWarning(string warning) => _warnings.Add(warning);
    internal void AddNote(string note) => _notes.Add(note);

    /// <summary>Notes first, then warnings behind a localized prefix; empty when there is neither.</summary>
    public string ToUserMessage()
    {
        var parts = new List<string>(_notes);
        if (HasWarnings) parts.Add(Localization.DefaultStrings.Get("Str.Postman.WarningPrefix") + " " + string.Join(" ", _warnings));
        return string.Join(" ", parts);
    }
}
public sealed record PostmanImportResult(RequestCollection Collection, PostmanImportReport Report);
