using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ApiConnectTool.Services;

/// <summary>
/// Real <see cref="IProcessRunner"/> backed by <see cref="Process"/>. Uses ArgumentList
/// (never a concatenated command string) so argument values are never re-interpreted by a shell.
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    public ProcessRunResult Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new System.InvalidOperationException($"Failed to start process '{fileName}'.");

        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        stdOut.Append(process.StandardOutput.ReadToEnd());
        stdErr.Append(process.StandardError.ReadToEnd());
        process.WaitForExit();

        return new ProcessRunResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }
}
