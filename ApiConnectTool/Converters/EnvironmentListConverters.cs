using System.Collections.Generic;
using Avalonia.Data.Converters;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Converters;

/// <summary>Row markers in the environment editor's list. Each takes the row's environment and
/// the editor; a third binding only re-triggers evaluation when the list or active one changes.</summary>
public static class EnvironmentListConverters
{
    public static readonly IMultiValueConverter IsFirstEnvironment = new FuncMultiValueConverter<object?, bool>(values =>
    {
        var list = new List<object?>(values);
        return list.Count >= 2 && list[0] is RequestEnvironment environment && list[1] is EnvironmentEditorViewModel editor
            && editor.IsFirstListedEnvironment(environment);
    });

    public static readonly IMultiValueConverter IsActive = new FuncMultiValueConverter<object?, bool>(values =>
    {
        var list = new List<object?>(values);
        return list.Count >= 2 && list[0] is RequestEnvironment environment && list[1] is EnvironmentEditorViewModel editor
            && ReferenceEquals(editor.ActiveEnvironment, environment);
    });
}
