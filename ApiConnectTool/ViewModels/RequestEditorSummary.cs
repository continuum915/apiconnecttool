using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

/// <summary>What the request editor shows about a tab beyond its fields: the tab-header counts
/// (enabled rows with a key, and enabled assertions) and the production strip, shown when the
/// tab's collection environment or the active global environment is marked production.</summary>
public sealed partial class RequestEditorSummary : ObservableObject
{
    private readonly RequestTabViewModel _tab;
    private readonly Func<RequestEnvironment?> _globalEnvironment;

    [ObservableProperty] private int _paramsCount;
    [ObservableProperty] private int _headersCount;
    [ObservableProperty] private int _assertionsCount;
    [ObservableProperty] private string? _productionWarning;

    public RequestEditorSummary(RequestTabViewModel tab, Func<RequestEnvironment?> globalEnvironment)
    {
        _tab = tab;
        _globalEnvironment = globalEnvironment;
        Recount();
        RefreshProduction();
    }

    /// <summary>The active global environment, set by the main window; falls back to the
    /// provider the tab was created with.</summary>
    public Func<RequestEnvironment?>? GlobalEnvironmentProvider { get; set; }

    public string ParamsHeader => Format("Str.RequestTabs.Params", ParamsCount);
    public string HeadersHeader => Format("Str.RequestTabs.Headers", HeadersCount);
    public string AssertionsHeader => Format("Str.RequestTabs.Assertions", AssertionsCount);
    public bool IsProduction => ProductionWarning is not null;

    partial void OnParamsCountChanged(int value) => OnPropertyChanged(nameof(ParamsHeader));
    partial void OnHeadersCountChanged(int value) => OnPropertyChanged(nameof(HeadersHeader));
    partial void OnAssertionsCountChanged(int value) => OnPropertyChanged(nameof(AssertionsHeader));
    partial void OnProductionWarningChanged(string? value) => OnPropertyChanged(nameof(IsProduction));

    /// <summary>Rereads the active environments; called when environments or the workspace change.</summary>
    public void RefreshProduction()
    {
        var collection = _tab.CollectionProvider?.Invoke();
        var targets = ProductionTarget.Collect(GlobalEnvironmentProvider?.Invoke() ?? _globalEnvironment(), collection?.ActiveEnvironment, collection?.Name);
        if (targets.Count == 0)
        {
            ProductionWarning = null;
            return;
        }

        var mode = _tab.WorkspaceProvider?.Invoke()?.ProductionWarningMode ?? ProductionWarningMode.AlwaysWarn;
        ProductionWarning = string.Join(" ",
            AppLocalization.Get(targets.Count == 1 ? "Str.ProductionSend.IntroOne" : "Str.ProductionSend.IntroMany"),
            string.Join(", ", targets.Select(ProductionSendMessage.Describe)) + ".",
            AppLocalization.Get("Str.ProductionStrip.Mode." + mode));
    }

    /// <summary>Recounts rows. The tab calls it on every edit; subscribing to the row collections
    /// here would trip ObservableCollection's reentrancy guard for editors that adjust rows while
    /// handling a change.</summary>
    public void Recount()
    {
        ParamsCount = _tab.QueryParams.Count(IsCountedRow);
        HeadersCount = _tab.RequestHeaders.Count(IsCountedRow);
        AssertionsCount = _tab.AssertionTests.Assertions.Count(assertion => assertion.IsEnabled);
    }

    private static bool IsCountedRow(KeyValueItem row) => row.IsEnabled && !string.IsNullOrWhiteSpace(row.Key);

    private static string Format(string key, int count) =>
        string.Format(CultureInfo.CurrentCulture, AppLocalization.Get(key), count);
}
