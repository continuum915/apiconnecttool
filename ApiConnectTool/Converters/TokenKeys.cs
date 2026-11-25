using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Converters;

/// <summary>Maps semantic state to <c>ApiConnectTool.Brush.*</c> token keys. Views apply the key with
/// <see cref="ApiConnectTool.Views.TokenBrush"/> so the color follows theme switches.</summary>
public static class TokenKeys
{
    public static string ForMethod(string? method) => (method ?? string.Empty).ToUpperInvariant() switch
    {
        "GET" => "ApiConnectTool.Brush.Method.Get",
        "POST" => "ApiConnectTool.Brush.Method.Post",
        "PUT" => "ApiConnectTool.Brush.Method.Put",
        "PATCH" => "ApiConnectTool.Brush.Method.Patch",
        "DELETE" => "ApiConnectTool.Brush.Method.Delete",
        _ => "ApiConnectTool.Brush.Method.Other"
    };

    public static string ForStatusClass(ResponseStatusClass statusClass) => statusClass switch
    {
        ResponseStatusClass.Success => "ApiConnectTool.Brush.Success",
        ResponseStatusClass.Sending or ResponseStatusClass.Redirect or ResponseStatusClass.Informational => "ApiConnectTool.Brush.Info",
        ResponseStatusClass.ClientError or ResponseStatusClass.ServerError or ResponseStatusClass.TransportError => "ApiConnectTool.Brush.Danger",
        _ => "ApiConnectTool.Brush.TextMuted"
    };

    public static string ForConsoleLevel(ApiConnectTool.Models.ScriptConsoleLevel level) => level switch
    {
        ApiConnectTool.Models.ScriptConsoleLevel.Info => "ApiConnectTool.Brush.Info",
        ApiConnectTool.Models.ScriptConsoleLevel.Warn => "ApiConnectTool.Brush.WarningText",
        ApiConnectTool.Models.ScriptConsoleLevel.Error => "ApiConnectTool.Brush.Danger",
        ApiConnectTool.Models.ScriptConsoleLevel.Debug => "ApiConnectTool.Brush.TextMuted",
        _ => "ApiConnectTool.Brush.Text"
    };

    public static readonly IValueConverter ConsoleLevel =
        new FuncValueConverter<ApiConnectTool.Models.ScriptConsoleLevel, string>(ForConsoleLevel);

    /// <summary>Status chip fill: tinted for success and errors, plain pane otherwise.</summary>
    public static string ForStatusChipBackground(ResponseStatusClass statusClass) => statusClass switch
    {
        ResponseStatusClass.Success => "ApiConnectTool.Brush.SuccessBackground",
        ResponseStatusClass.ClientError or ResponseStatusClass.ServerError or ResponseStatusClass.TransportError => "ApiConnectTool.Brush.DangerBackground",
        _ => "ApiConnectTool.Brush.Pane"
    };

    public static string ForStatusChipBorder(ResponseStatusClass statusClass) => statusClass switch
    {
        ResponseStatusClass.Success => "ApiConnectTool.Brush.SuccessBorder",
        ResponseStatusClass.ClientError or ResponseStatusClass.ServerError or ResponseStatusClass.TransportError => "ApiConnectTool.Brush.DangerBorder",
        ResponseStatusClass.Redirect or ResponseStatusClass.Informational or ResponseStatusClass.Sending => "ApiConnectTool.Brush.Info",
        _ => "ApiConnectTool.Brush.Border"
    };

    public static string ForTestOutcome(TestRunOutcome outcome) => outcome switch
    {
        TestRunOutcome.Passed => "ApiConnectTool.Brush.Success",
        TestRunOutcome.Failed => "ApiConnectTool.Brush.Danger",
        TestRunOutcome.Incomplete => "ApiConnectTool.Brush.WarningText",
        _ => "ApiConnectTool.Brush.TextMuted"
    };

    public static string ForAssertionResult(ApiConnectTool.Models.AssertionResult? result) =>
        result is null ? "ApiConnectTool.Brush.Text"
        : result.Incomplete ? "ApiConnectTool.Brush.WarningText"
        : result.Passed ? "ApiConnectTool.Brush.Success" : "ApiConnectTool.Brush.Danger";

    public static readonly IValueConverter StatusChipBackground =
        new FuncValueConverter<ResponseStatusClass, string>(ForStatusChipBackground);

    public static readonly IValueConverter StatusChipBorder =
        new FuncValueConverter<ResponseStatusClass, string>(ForStatusChipBorder);

    public static readonly IValueConverter TestOutcome =
        new FuncValueConverter<TestRunOutcome, string>(ForTestOutcome);

    public static readonly IValueConverter AssertionResult =
        new FuncValueConverter<ApiConnectTool.Models.AssertionResult?, string>(ForAssertionResult);

    public static string ForRunOutcome(ApiConnectTool.Models.RunEntryOutcome outcome) => outcome switch
    {
        ApiConnectTool.Models.RunEntryOutcome.Passed => "ApiConnectTool.Brush.Success",
        ApiConnectTool.Models.RunEntryOutcome.Failed => "ApiConnectTool.Brush.Danger",
        _ => "ApiConnectTool.Brush.Warning"
    };

    public static readonly IValueConverter RunOutcome =
        new FuncValueConverter<ApiConnectTool.Models.RunEntryOutcome, string>(ForRunOutcome);

    public static readonly IValueConverter Method = new FuncValueConverter<string?, string>(ForMethod);

    public static readonly IValueConverter StatusClass =
        new FuncValueConverter<ResponseStatusClass, string>(ForStatusClass);

    public static readonly IValueConverter StatusCode =
        new FuncValueConverter<int?, string>(code => ForStatusClass(code is int value
            ? ResponseStatusClassifier.FromStatusCode(value)
            : ResponseStatusClass.TransportError));
}
