using Avalonia.Controls;
using Avalonia.Threading;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class ImportWizardHeadlessTests
{
    [Fact]
    public Task EachStepRendersWithItsBackAndNextStates() => HeadlessSession.RunAsync(() =>
    {
        var vm = new MainWindowViewModel(new FakeWorkspaceService());
        var wizard = new ImportWizardViewModel(vm.CollectionsPanel);
        var window = new ImportWizardWindow { DataContext = wizard };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var back = window.FindControl<Button>("WizardBackButton")!;
            var next = window.FindControl<Button>("WizardNextButton")!;

            void Expect(string stepPanel, bool backEnabled, bool nextEnabled)
            {
                Dispatcher.UIThread.RunJobs();
                foreach (var name in new[] { "FormatStep", "SourceStep", "OptionsStep", "ReviewStep", "ImportStep" })
                    Assert.Equal(name == stepPanel, window.FindControl<Control>(name)!.IsVisible);
                Assert.Equal(backEnabled, back.IsEffectivelyEnabled);
                Assert.Equal(nextEnabled, next.IsEffectivelyEnabled);
            }

            Expect("FormatStep", false, true);
            Assert.Equal("Step 1 of 5", window.FindControl<TextBlock>("StepCounter")!.Text);
            wizard.SelectedFormat = wizard.Formats.Single(format => format.Format == ImportFormat.OpenApi);
            wizard.NextCommand.Execute(null);
            Expect("SourceStep", true, false);

            wizard.IsFromFile = false;
            wizard.PastedText = CollectionImportTests.OpenApi;
            Expect("SourceStep", true, true);
            Assert.True(window.FindControl<TextBlock>("DetectionNoteText")!.IsEffectivelyVisible);

            wizard.NextCommand.Execute(null);
            Expect("OptionsStep", true, true);
            Assert.Equal("Shop", window.FindControl<TextBox>("CollectionNameTextBox")!.Text);

            wizard.NextCommand.Execute(null);
            Expect("ReviewStep", true, true);
            Assert.Equal("Start", next.Content);
            Assert.NotEqual(0, window.FindControl<ItemsControl>("ReviewRows")!.ItemCount);

            wizard.BackCommand.Execute(null);
            Expect("OptionsStep", true, true);
            wizard.NextCommand.Execute(null);
            wizard.NextCommand.Execute(null);
            Expect("ImportStep", false, true);
            Assert.Equal("Done", next.Content);
            Assert.Equal("Close", window.FindControl<Button>("WizardCancelButton")!.Content);
            Assert.NotEqual(0, window.FindControl<ItemsControl>("ImportLog")!.ItemCount);
        }
        finally { window.Close(); }
    });
}
