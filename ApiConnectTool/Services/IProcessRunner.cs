using System.Collections.Generic;

namespace ApiConnectTool.Services;

/// <summary>
/// Thin seam over external process execution so secret-store implementations that shell out
/// to a platform CLI (macOS `security`, Linux `secret-tool`) can be unit tested without the
/// real binary present.
/// </summary>
public interface IProcessRunner
{
    ProcessRunResult Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null);
}

public readonly record struct ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);
