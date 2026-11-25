using System.Collections.ObjectModel;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class EnvironmentEditorViewModelTests
{
    [Fact]
    public void AddEnvironment_AddsUniquelyNamedEnvironmentAndSelectsIt()
    {
        var environments = new ObservableCollection<RequestEnvironment>();
        var changeCount = 0;
        var vm = new EnvironmentEditorViewModel(environments, () => changeCount++);

        vm.AddEnvironmentCommand.Execute(null);
        vm.AddEnvironmentCommand.Execute(null);

        Assert.Equal(2, environments.Count);
        Assert.Equal("New Environment", environments[0].Name);
        Assert.Equal("New Environment 1", environments[1].Name);
        Assert.Same(environments[1], vm.SelectedEnvironment);
        Assert.Equal(2, changeCount);
    }

    [Fact]
    public void DeleteEnvironment_RemovesItAndReselectsRemaining()
    {
        var first = new RequestEnvironment { Name = "First" };
        var second = new RequestEnvironment { Name = "Second" };
        var environments = new ObservableCollection<RequestEnvironment> { first, second };
        var vm = new EnvironmentEditorViewModel(environments, () => { }) { SelectedEnvironment = first };

        vm.DeleteEnvironmentCommand.Execute(first);

        Assert.Single(environments);
        Assert.Same(second, vm.SelectedEnvironment);
    }

    [Fact]
    public void AddVariable_AddsToSelectedEnvironment()
    {
        var environment = new RequestEnvironment { Name = "Staging" };
        var environments = new ObservableCollection<RequestEnvironment> { environment };
        var vm = new EnvironmentEditorViewModel(environments, () => { }) { SelectedEnvironment = environment };

        vm.AddVariableCommand.Execute(null);

        Assert.Single(environment.Variables);
    }

    [Fact]
    public void RemoveVariable_RemovesFromSelectedEnvironment()
    {
        var variable = new EnvironmentVariable { Key = "token" };
        var environment = new RequestEnvironment { Name = "Staging" };
        environment.Variables.Add(variable);
        var environments = new ObservableCollection<RequestEnvironment> { environment };
        var vm = new EnvironmentEditorViewModel(environments, () => { }) { SelectedEnvironment = environment };

        vm.RemoveVariableCommand.Execute(variable);

        Assert.Empty(environment.Variables);
    }

    [Fact]
    public void ToggleSecretVisibility_FlipsFlag()
    {
        var vm = new EnvironmentEditorViewModel(new ObservableCollection<RequestEnvironment>(), () => { });

        vm.ToggleSecretVisibilityCommand.Execute(null);

        Assert.True(vm.IsSecretVisible);
    }
}
