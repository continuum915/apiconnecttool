using Xunit;

namespace ApiConnectTool.Tests.Architecture;

public class TypeSizeBudgetTests
{
    [Fact]
    public void FindViolations_UnlistedTypeOverDefaultBudget_ReportsSizeAndBudget()
    {
        var sizes = MeasuredTypes();
        sizes["ApiConnectTool.Sample.NewType"] = 301;

        var violations = TypeSizeBudgets.FindViolations(sizes);

        Assert.Equal(new[] { "ApiConnectTool.Sample.NewType: measured 301 logical lines, budget 300." }, violations);
    }

    [Fact]
    public void FindViolations_ReportsEveryOversizedTypeAndMissingBudgetEntry()
    {
        var sizes = MeasuredTypes();
        sizes["ApiConnectTool.Sample.NewType"] = 301;
        sizes["ApiConnectTool.ViewModels.CollectionsViewModel"] = 551;
        sizes.Remove("ApiConnectTool.ViewModels.RequestTabViewModel");

        var violations = TypeSizeBudgets.FindViolations(sizes);

        Assert.Equal(new[]
        {
            "ApiConnectTool.Sample.NewType: measured 301 logical lines, budget 300.",
            "ApiConnectTool.ViewModels.CollectionsViewModel: measured 551 logical lines, budget 550.",
            "ApiConnectTool.ViewModels.RequestTabViewModel: measured <missing>, budget 650; listed type no longer exists."
        }, violations);
    }

    [Theory]
    [InlineData("ApiConnectTool.Sample.NewType", 300)]
    [InlineData("ApiConnectTool.ViewModels.CollectionsViewModel", 550)]
    public void FindViolations_TypeAtItsBudget_IsAllowed(string typeName, int size)
    {
        var sizes = MeasuredTypes();
        sizes[typeName] = size;

        Assert.Empty(TypeSizeBudgets.FindViolations(sizes));
    }

    [Fact]
    public void AppTypes_StayWithinBudgets_AndExplicitEntriesStillExist()
    {
        var violations = TypeSizeBudgets.FindViolations(TypeSizeMeasurement.MeasureAppProject());

        Assert.True(violations.Count == 0,
            string.Join(Environment.NewLine, violations) + Environment.NewLine +
            "Split the type or raise its budget in the same change with a reason. " +
            "Remove or update budget entries for types that no longer exist.");
    }

    private static Dictionary<string, int> MeasuredTypes() => new()
    {
        ["ApiConnectTool.ViewModels.RequestTabViewModel"] = 570,
        ["ApiConnectTool.Services.RequestExecutionService"] = 508,
        ["ApiConnectTool.ViewModels.CollectionsViewModel"] = 497,
        ["ApiConnectTool.Services.OpenApiImporter"] = 480,
        ["ApiConnectTool.ViewModels.MainWindowViewModel"] = 384,
        ["ApiConnectTool.Views.Components.SidebarControl"] = 303
    };
}
