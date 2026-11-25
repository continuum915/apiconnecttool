using System;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

/// <summary>Owns the optional Aero layer; Fluent remains the base for every theme.</summary>
public sealed class AppThemeManager(Application application)
{
    private readonly StyleInclude _aero = new(new Uri("avares://ApiConnectTool/"))
    {
        Source = new Uri(Win7AeroTheme.Win7AeroTheme.StyleUri)
    };
    private readonly StyleInclude _aeroDesignSystem = new(new Uri("avares://ApiConnectTool/"))
    {
        Source = new Uri("avares://ApiConnectTool/Styles/AeroDesignSystem.axaml")
    };

    public void Apply(AppTheme theme)
    {
        application.Styles.Remove(_aeroDesignSystem);
        application.Styles.Remove(_aero);
        application.RequestedThemeVariant = theme == AppTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (theme == AppTheme.Win7Light)
        {
            // Keep ApiConnectTool's shared sizing conventions after the imported control styles.
            application.Styles.Insert(application.Styles.Count - 1, _aero);
            application.Styles.Add(_aeroDesignSystem);
        }
    }
}
