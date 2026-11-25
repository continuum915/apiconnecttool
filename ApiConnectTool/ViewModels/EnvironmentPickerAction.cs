using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApiConnectTool.ViewModels;

/// <summary>A command shown at the end of an environment picker's drop-down. Choosing it runs
/// the command and leaves the selected environment unchanged.</summary>
public sealed partial class EnvironmentPickerAction : ObservableObject
{
    [ObservableProperty] private string _text;

    public EnvironmentPickerAction(string text, ICommand command)
    {
        _text = text;
        Command = command;
        command.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(IsEnabled));
    }

    public ICommand Command { get; }

    public bool IsEnabled => Command.CanExecute(null);

    public void Run()
    {
        if (Command.CanExecute(null)) Command.Execute(null);
    }
}
