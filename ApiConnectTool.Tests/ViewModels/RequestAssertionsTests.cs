using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class RequestAssertionsTests
{
    [Fact]
    public void Evaluation_ProducesMixedResultsAndSkipsDisabledAssertions()
    {
        var vm = new RequestAssertionsViewModel(() => { });
        vm.Assertions.Add(new() { ExpectedValue = "200" });
        vm.Assertions.Add(new() { ExpectedValue = "201" });
        vm.Assertions.Add(new() { IsEnabled = false });
        vm.Evaluate(vm.CreateSnapshot(), new() { IsSuccess = true, StatusCode = HttpStatusCode.OK });
        Assert.Equal(2, vm.Results.Count);
        Assert.True(vm.Results[0].Passed);
        Assert.False(vm.Results[1].Passed);
        Assert.Equal("200", vm.Results[1].ActualValue);
        Assert.NotEmpty(vm.Results[1].FailureMessage!);
        Assert.Equal(1, vm.PassedCount);
    }
    [Fact]
    public void EmptyAndFailedSends_ClearPreviousResults()
    {
        var vm = new RequestAssertionsViewModel(() => { });
        vm.Evaluate([], new() { IsSuccess = true, StatusCode = HttpStatusCode.OK });
        Assert.Empty(vm.Results);
        Assert.False(vm.HasResults);
        vm.Assertions.Add(new());
        vm.Evaluate(vm.CreateSnapshot(), new() { IsSuccess = true, StatusCode = HttpStatusCode.OK });
        Assert.Single(vm.Results);
        vm.Evaluate(vm.CreateSnapshot(), RequestExecutionResult.Failed(RequestExecutionFailureKind.Timeout, "timeout"));
        Assert.Empty(vm.Results);
        Assert.False(vm.HasResults);
        Assert.NotEqual("Str.Assertions.Empty", vm.Summary);
    }

    [Fact]
    public void AssertionEdits_MarkTabDirtyAndSnapshotsAreIndependent()
    {
        var request = new RequestItem { Assertions = new() { new() } };
        var tab = RequestTabViewModel.FromRequest(request);
        Assert.False(tab.IsDirty);
        Assert.Equal(request.Assertions[0].Id, tab.AssertionTests.Assertions[0].Id);
        tab.AssertionTests.Assertions[0].ExpectedValue = "204";
        Assert.True(tab.IsDirty);
        Assert.Equal("200", request.Assertions[0].ExpectedValue);
        var snapshot = tab.CreateSnapshot();
        tab.MarkSaved(request.Id);
        tab.AssertionTests.Assertions.Clear();
        Assert.True(tab.IsDirty);
        Assert.Single(snapshot.Assertions);
        tab.MarkSaved(request.Id);
        tab.AssertionTests.AddAssertionCommand.Execute(null);
        Assert.True(tab.IsDirty);
        tab.MarkSaved(request.Id);
        tab.AssertionTests.SelectedAssertion = tab.AssertionTests.Assertions[0];
        tab.AssertionTests.RemoveAssertionCommand.Execute(null);
        Assert.True(tab.IsDirty);
    }

    [Theory]
    [InlineData(AssertionOperator.Equals, "200", true)]
    [InlineData(AssertionOperator.Equals, "201", false)]
    [InlineData(AssertionOperator.NotEquals, "201", true)]
    [InlineData(AssertionOperator.NotEquals, "200", false)]
    [InlineData(AssertionOperator.GreaterThan, "199", true)]
    [InlineData(AssertionOperator.GreaterThan, "200", false)]
    [InlineData(AssertionOperator.LessThan, "201", true)]
    [InlineData(AssertionOperator.LessThan, "200", false)]
    [InlineData(AssertionOperator.Equals, "not a number", false)]
    public void NumericComparisons_ReturnExpectedOutcome(AssertionOperator op, string expected, bool passed)
    {
        var assertion = new Assertion { Operator = op, ExpectedValue = expected };
        var results = new ApiConnectTool.Services.AssertionEngine().Evaluate([assertion], new() { IsSuccess = true, StatusCode = HttpStatusCode.OK });
        var result = Assert.Single(results);
        Assert.Equal(assertion.Id, result.SourceId);
        Assert.Equal(passed, result.Passed);
        if (!passed) Assert.NotEmpty(result.FailureMessage!);
    }

    [Fact]
    public void Evaluation_RedactsLabelsActualValuesAndFailureMessages()
    {
        var results = new ApiConnectTool.Services.AssertionEngine().Evaluate(
            [new() { ExpectedValue = "private", Description = "private description" }],
            new() { IsSuccess = true, StatusCode = HttpStatusCode.OK, ResolvedSecretValues = ["private", "200"] });
        var result = Assert.Single(results);
        Assert.DoesNotContain("private", result.Label);
        Assert.DoesNotContain("private", result.FailureMessage!);
        Assert.DoesNotContain("200", result.ActualValue);
    }
    [Fact]
    public void InvalidExpectedValue_HasAnExplanatoryMessageWithoutUiInitialization()
    {
        var results = new ApiConnectTool.Services.AssertionEngine().Evaluate(
            [new() { ExpectedValue = "oops" }], new() { IsSuccess = true, StatusCode = HttpStatusCode.OK });
        Assert.Equal("Expected value \"oops\" must be a number.", Assert.Single(results).FailureMessage);
    }
    [Fact]
    public void EngineResults_AreDeterministicAcrossFormattingCultures()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var engine = new ApiConnectTool.Services.AssertionEngine();
            var assertions = new[] { new Assertion { ExpectedValue = "201" } };
            var response = new RequestExecutionResult { IsSuccess = true, StatusCode = HttpStatusCode.OK };
            System.Globalization.CultureInfo.CurrentCulture = new("en-US");
            var first = Assert.Single(engine.Evaluate(assertions, response));
            System.Globalization.CultureInfo.CurrentCulture = new("ar-SA");
            Assert.Equal(first, Assert.Single(engine.Evaluate(assertions, response)));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public void ScriptEdits_MarkDirtyAndSnapshotsCarryBothScripts()
    {
        var source = new RequestItem { PreRequestScript = "before", TestScript = "after" };
        var tab = RequestTabViewModel.FromRequest(source);
        Assert.False(tab.IsDirty);
        Assert.Equal("before", tab.AssertionTests.PreRequestScript);
        tab.AssertionTests.PreRequestScript = "changed before";
        Assert.True(tab.IsDirty);
        tab.MarkSaved(source.Id);
        tab.AssertionTests.TestScript = "changed after";
        Assert.True(tab.IsDirty);
        var snapshot = tab.CreateSnapshot();
        Assert.Equal("changed before", snapshot.PreRequestScript);
        Assert.Equal("changed after", snapshot.TestScript);
        Assert.Equal("before", source.PreRequestScript);
        Assert.Equal("after", source.TestScript);
    }
}
