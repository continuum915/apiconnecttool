using System.Collections.Generic;
using Avalonia;
using Avalonia.Data.Converters;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Converters;

/// <summary>Small converters for the import wizard's format list and review rows.</summary>
public static class ImportWizardConverters
{
    public static readonly IMultiValueConverter SameFormat = new FuncMultiValueConverter<object?, bool>(values =>
    {
        var list = new List<object?>(values);
        return list.Count == 2 && list[0] is ImportFormatChoice item && list[1] is ImportFormatChoice selected && item.Format == selected.Format;
    });

    public static readonly IValueConverter LeftMargin = new FuncValueConverter<double, Thickness>(indent => new Thickness(indent, 0, 0, 0));

    public static readonly IValueConverter IsNew = new FuncValueConverter<ImportAction, bool>(action => action == ImportAction.New);

    public static readonly IValueConverter IsOverwrite = new FuncValueConverter<ImportAction, bool>(action => action == ImportAction.Overwrite);

    public static readonly IValueConverter IsSkip = new FuncValueConverter<ImportAction, bool>(action => action == ImportAction.Skip);
}
