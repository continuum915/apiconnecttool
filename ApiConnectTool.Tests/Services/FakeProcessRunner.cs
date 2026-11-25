using System;
using System.Collections.Generic;
using ApiConnectTool.Services;

namespace ApiConnectTool.Tests.Services;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<string, IReadOnlyList<string>, string?, ProcessRunResult> _handler;

    public FakeProcessRunner(Func<string, IReadOnlyList<string>, string?, ProcessRunResult> handler)
    {
        _handler = handler;
    }

    public List<(string FileName, IReadOnlyList<string> Arguments, string? StandardInput)> Calls { get; } = new();

    public ProcessRunResult Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null)
    {
        Calls.Add((fileName, arguments, standardInput));
        return _handler(fileName, arguments, standardInput);
    }
}
