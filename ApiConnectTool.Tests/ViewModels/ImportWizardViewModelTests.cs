using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class ImportWizardViewModelTests
{
    private static (ImportWizardViewModel Wizard, MainWindowViewModel Vm, FakeWorkspaceService Service) Arrange(ImportFormat? format = null)
    {
        var service = new FakeWorkspaceService();
        var vm = new MainWindowViewModel(service);
        return (new ImportWizardViewModel(vm.CollectionsPanel, format), vm, service);
    }

    [Fact]
    public void StartsOnFormat_AndWalksEveryStepToImport()
    {
        var (wizard, vm, service) = Arrange();
        Assert.Equal(ImportWizardStep.Format, wizard.Step);
        Assert.Equal("Step 1 of 5", wizard.StepCounterText);
        Assert.False(wizard.BackCommand.CanExecute(null));

        wizard.SelectedFormat = wizard.Formats.Single(format => format.Format == ImportFormat.Postman);
        wizard.NextCommand.Execute(null);
        Assert.Equal(ImportWizardStep.Source, wizard.Step);
        Assert.False(wizard.NextCommand.CanExecute(null));

        wizard.LoadFile(@"C:\files\pets.json", CollectionImportTests.Postman);
        Assert.NotNull(wizard.DetectionNote);
        Assert.Contains("1 folders, 2 requests", wizard.DetectionNote);
        Assert.True(wizard.NextCommand.CanExecute(null));
        wizard.NextCommand.Execute(null);

        Assert.Equal(ImportWizardStep.Options, wizard.Step);
        Assert.Equal("Pets", wizard.CollectionName);
        wizard.CollectionName = " ";
        Assert.False(wizard.NextCommand.CanExecute(null));
        wizard.CollectionName = "Pets API";
        wizard.NextCommand.Execute(null);

        Assert.Equal(ImportWizardStep.Review, wizard.Step);
        Assert.Equal("Start", wizard.NextText);
        Assert.Equal(new[] { "Admin", "Purge", "List pets" }, wizard.ReviewItems.Select(item => item.Name));
        var savesBeforeStart = service.SaveCallCount;
        Assert.Empty(vm.CollectionsPanel.Collections);

        wizard.NextCommand.Execute(null);
        Assert.Equal(ImportWizardStep.Import, wizard.Step);
        Assert.Equal(savesBeforeStart + 1, service.SaveCallCount);
        var created = Assert.Single(vm.CollectionsPanel.Collections);
        Assert.Equal("Pets API", created.Name);
        Assert.Equal(2, created.Requests.Count);
        Assert.Equal("Done", wizard.NextText);
        Assert.Equal("Close", wizard.CancelText);
        Assert.False(wizard.BackCommand.CanExecute(null));
        Assert.Equal(1, wizard.Progress);
        Assert.Equal(3, wizard.Log.Count);
        Assert.NotNull(wizard.FinishText);

        var closed = false;
        wizard.CloseRequested += () => closed = true;
        wizard.NextCommand.Execute(null);
        Assert.True(closed);
    }

    [Fact]
    public void UntickingAFolderSkipsItsRequests_AndReviewNeedsOneIncludedItem()
    {
        var (wizard, vm, _) = Arrange(ImportFormat.Postman);
        wizard.IsFromFile = false;
        wizard.PastedText = CollectionImportTests.Postman;
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);

        var admin = wizard.ReviewItems.Single(item => item.Name == "Admin");
        admin.IsIncluded = false;
        Assert.Equal(ImportAction.Skip, wizard.ReviewItems.Single(item => item.Name == "Purge").Action);
        Assert.Equal("1 new, 0 merged, 0 overwritten, 2 skipped.", wizard.ReviewSummary);

        wizard.ReviewItems.Single(item => item.Name == "List pets").IsIncluded = false;
        Assert.False(wizard.NextCommand.CanExecute(null));
        wizard.ReviewItems.Single(item => item.Name == "List pets").IsIncluded = true;

        wizard.NextCommand.Execute(null);
        var created = Assert.Single(vm.CollectionsPanel.Collections);
        Assert.Equal("List pets", Assert.Single(created.Requests).Name);
        Assert.Empty(created.Folders);
    }

    [Theory]
    [InlineData(ImportFormat.ApiConnectTool)]
    [InlineData(ImportFormat.Postman)]
    [InlineData(ImportFormat.OpenApi)]
    [InlineData(ImportFormat.Curl)]
    public void FormatEntriesPreselectAndStartOnSource(ImportFormat format)
    {
        var (wizard, _, _) = Arrange(format);
        Assert.Equal(ImportWizardStep.Source, wizard.Step);
        Assert.Equal(format, wizard.SelectedFormat.Format);
        Assert.Equal(format == ImportFormat.Curl, wizard.IsPaste);
        Assert.True(wizard.BackCommand.CanExecute(null));
    }

    [Fact]
    public void ImportersErrorIsShownAndBlocksNext()
    {
        var (wizard, _, service) = Arrange(ImportFormat.Curl);
        wizard.PastedText = "wget https://example.test";
        Assert.NotNull(wizard.SourceError);
        Assert.Null(wizard.DetectionNote);
        Assert.False(wizard.NextCommand.CanExecute(null));

        wizard.PastedText = CollectionImportTests.Curl;
        Assert.Null(wizard.SourceError);
        Assert.True(wizard.NextCommand.CanExecute(null));
        Assert.Equal(0, service.SaveCallCount);
    }

    [Fact]
    public void ApiConnectToolImportCanLeaveCollectionEnvironmentsBehind()
    {
        var (wizard, vm, _) = Arrange(ImportFormat.ApiConnectTool);
        wizard.LoadFile("orders.json", CollectionImportTests.ApiConnectToolExport());
        wizard.NextCommand.Execute(null);
        Assert.True(wizard.CanImportEnvironments);
        wizard.ImportEnvironments = false;
        wizard.NextCommand.Execute(null);
        Assert.DoesNotContain(wizard.ReviewItems, item => item.Kind == ImportItemKind.Environment);
        wizard.NextCommand.Execute(null);
        Assert.Empty(Assert.Single(vm.CollectionsPanel.Collections).Environments);

        var (second, secondVm, _) = Arrange(ImportFormat.ApiConnectTool);
        second.LoadFile("orders.json", CollectionImportTests.ApiConnectToolExport());
        second.NextCommand.Execute(null);
        second.NextCommand.Execute(null);
        Assert.Single(second.ReviewItems, item => item.Kind == ImportItemKind.Environment);
        second.NextCommand.Execute(null);
        var environments = Assert.Single(secondVm.CollectionsPanel.Collections).Environments;
        Assert.Equal("dev", Assert.Single(environments).Name);
        Assert.Null(secondVm.CollectionsPanel.Collections[0].ActiveEnvironmentId);
    }
}
