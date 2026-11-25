namespace ApiConnectTool.Tests.Architecture;

internal static class TypeSizeBudgets
{
    // Largest unlisted type: PostmanCollectionConverter, 270 lines; 10% headroom rounded up to 25.
    private const int DefaultBudget = 300;

    // Limits are measured sizes plus 10% headroom, rounded up to the next multiple of 25.
    // Change a limit only with a reason in the same change, or split the growing type.
    private static readonly IReadOnlyDictionary<string, int> ExplicitBudgets = new Dictionary<string, int>
    {
        ["ApiConnectTool.ViewModels.RequestTabViewModel"] = 650, // 570 lines: request editing/execution state; refactoring is deferred.
        ["ApiConnectTool.Services.RequestExecutionService"] = 575, // 508 lines: HTTP execution, auth, and response capture.
        ["ApiConnectTool.ViewModels.CollectionsViewModel"] = 550, // 497 lines: extracted collection operations, tree, and search.
        ["ApiConnectTool.Services.OpenApiImporter"] = 550, // 480 lines: OpenAPI parsing and request projection.
        ["ApiConnectTool.ViewModels.MainWindowViewModel"] = 500, // 434 lines: tabs, saves, prompts, workspace coordination, and the collection environment picker.
        ["ApiConnectTool.Views.Components.SidebarControl"] = 350 // 303 lines: sidebar event and dialog adapters.
    };

    public static IReadOnlyList<string> FindViolations(IReadOnlyDictionary<string, int> sizes)
    {
        var violations = new List<string>();
        foreach (var (name, size) in sizes.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var budget = ExplicitBudgets.GetValueOrDefault(name, DefaultBudget);
            if (size > budget)
            {
                violations.Add($"{name}: measured {size} logical lines, budget {budget}.");
            }
        }

        foreach (var (name, budget) in ExplicitBudgets.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!sizes.ContainsKey(name))
            {
                violations.Add($"{name}: measured <missing>, budget {budget}; listed type no longer exists.");
            }
        }

        return violations;
    }
}
