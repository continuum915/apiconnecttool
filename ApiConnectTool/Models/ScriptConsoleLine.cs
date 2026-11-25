namespace ApiConnectTool.Models;

public enum ScriptConsoleLevel { Log, Info, Warn, Error, Debug }

public enum ScriptPhase { PreRequest, Test }

/// <summary>One line a script wrote with <c>console.*</c>, already redacted. The phase is set by
/// the request runner, which knows which script produced it.</summary>
public sealed record ScriptConsoleLine(ScriptConsoleLevel Level, string Text, ScriptPhase Phase = ScriptPhase.PreRequest)
{
    /// <summary>Most lines a single script run keeps.</summary>
    public const int MaxLinesPerRun = 200;

    /// <summary>Most characters a single script run keeps (16 KiB of text).</summary>
    public const int MaxCharactersPerRun = 16 * 1024;

    public string LevelText => Level.ToString().ToUpperInvariant();
}
