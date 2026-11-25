using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

/// <summary>Overall outcome of a send's tests, for coloring the Test Results summary.</summary>
public enum TestRunOutcome { None, Passed, Failed, Incomplete }

/// <summary>What the response viewer shows about a tab's last response beyond its content: the
/// tab-header counts, the status chip and the test outcome.</summary>
public sealed partial class ResponseSummary : ObservableObject
{
    private readonly RequestTabViewModel _tab;

    public ResponseSummary(RequestTabViewModel tab)
    {
        _tab = tab;
        tab.ResponseCookies.CollectionChanged += (_, _) => OnCountsChanged();
        tab.ResponseHeaders.CollectionChanged += (_, _) => OnCountsChanged();
        tab.AssertionTests.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RequestAssertionsViewModel.PassedCount) or nameof(RequestAssertionsViewModel.HasResults)
                or nameof(RequestAssertionsViewModel.Summary))
                OnCountsChanged();
        };
        tab.PropertyChanged += OnTabPropertyChanged;
    }

    public string CookiesHeader => Format("Str.ResponseTabs.Cookies", _tab.ResponseCookies.Count);
    public string HeadersHeader => Format("Str.ResponseTabs.Headers", _tab.ResponseHeaders.Count);
    public string TestResultsHeader => string.Format(CultureInfo.CurrentCulture,
        AppLocalization.Get("Str.ResponseTabs.TestResults"), _tab.AssertionTests.PassedCount, _tab.AssertionTests.Results.Count);
    public string CookiesSummary => Format("Str.Response.CookiesSummary", _tab.ResponseCookies.Count);

    public TestRunOutcome TestOutcome
    {
        get
        {
            var results = _tab.AssertionTests.Results;
            if (results.Count == 0) return TestRunOutcome.None;
            if (results.Any(result => !result.Passed && !result.Incomplete)) return TestRunOutcome.Failed;
            return results.Any(result => result.Incomplete) ? TestRunOutcome.Incomplete : TestRunOutcome.Passed;
        }
    }

    public bool HasStatus => _tab.ResponseStatusClass != ResponseStatusClass.None;

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RequestTabViewModel.ResponseStatusClass)) OnPropertyChanged(nameof(HasStatus));
    }

    private void OnCountsChanged()
    {
        OnPropertyChanged(nameof(CookiesHeader));
        OnPropertyChanged(nameof(HeadersHeader));
        OnPropertyChanged(nameof(TestResultsHeader));
        OnPropertyChanged(nameof(CookiesSummary));
        OnPropertyChanged(nameof(TestOutcome));
    }

    private static string Format(string key, int count) =>
        string.Format(CultureInfo.CurrentCulture, AppLocalization.Get(key), count);
}
