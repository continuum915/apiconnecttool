using System.Collections.ObjectModel;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class ImportIntoExistingTests
{
    private static (ImportWizardViewModel Wizard, MainWindowViewModel Vm, RequestCollection Pets, RequestItem ListPets) Arrange()
    {
        var listPets = new RequestItem { Name = "list pets", Url = "https://old.test/pets" };
        var pets = new RequestCollection { Name = "Pets", Requests = { listPets } };
        var vm = new MainWindowViewModel(new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace { Collections = new ObservableCollection<RequestCollection> { pets } }
        });
        var wizard = new ImportWizardViewModel(vm.CollectionsPanel, ImportFormat.Postman);
        wizard.IsFromFile = false;
        wizard.PastedText = CollectionImportTests.Postman;
        wizard.NextCommand.Execute(null);
        return (wizard, vm, pets, listPets);
    }

    [Fact]
    public void OverwriteOptionChangesReviewActionsLive()
    {
        var (wizard, _, pets, _) = Arrange();
        wizard.ImportIntoExisting = true;
        Assert.Same(pets, wizard.ExistingCollection);
        Assert.True(wizard.NextCommand.CanExecute(null));
        wizard.NextCommand.Execute(null);

        ImportReviewItem Row(string name) => wizard.ReviewItems.Single(item => item.Name == name);
        Assert.Equal(ImportAction.Skip, Row("List pets").Action);
        Assert.Equal(ImportAction.New, Row("Purge").Action);
        Assert.Equal("2 new, 0 merged, 0 overwritten, 1 skipped.", wizard.ReviewSummary);

        wizard.OverwriteExisting = true;
        Assert.Equal(ImportAction.Overwrite, Row("List pets").Action);
        Assert.Equal("2 new, 0 merged, 1 overwritten, 0 skipped.", wizard.ReviewSummary);

        Row("List pets").IsIncluded = false;
        Assert.Equal(ImportAction.Skip, Row("List pets").Action);
    }

    [Fact]
    public void StartMergesIntoTheChosenCollectionAndReloadsOverwrittenTabs()
    {
        var (wizard, vm, pets, listPets) = Arrange();
        vm.OpenRequestCommand.Execute(listPets);
        var oldTab = vm.ActiveTab!;
        wizard.ImportIntoExisting = true;
        wizard.OverwriteExisting = true;
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);

        Assert.Single(vm.CollectionsPanel.Collections);
        Assert.Equal(2, pets.Requests.Count);
        Assert.Equal("Admin", Assert.Single(pets.Folders).Name);
        var replaced = pets.Requests.Single(request => request.Id == listPets.Id);
        Assert.Equal("https://pets.test/pets", replaced.Url);
        Assert.NotSame(oldTab, vm.ActiveTab);
        Assert.Equal(listPets.Id, vm.ActiveTab!.RequestId);
        Assert.Equal("https://pets.test/pets", vm.ActiveTab.RequestUrl);
        Assert.Contains("1 overwritten", wizard.FinishText);
    }

    [Fact]
    public void DirtyTabsAskBeforeReloading()
    {
        var (wizard, vm, _, listPets) = Arrange();
        vm.OpenRequestCommand.Execute(listPets);
        var dirty = vm.ActiveTab!;
        dirty.RequestUrl = "https://edited.test";
        var asked = 0;
        vm.UnsavedChangesPromptHandler = _ => { asked++; return Task.FromResult(UnsavedChangesChoice.Cancel); };
        wizard.ImportIntoExisting = true;
        wizard.OverwriteExisting = true;
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);

        Assert.Equal(1, asked);
        Assert.Same(dirty, vm.ActiveTab);
        Assert.Equal("https://edited.test", dirty.RequestUrl);
    }
}
