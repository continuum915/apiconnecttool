using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace ApiConnectTool.Services.Localization;

/// <summary>Thin localization infrastructure: one merged <see cref="ResourceInclude"/> per
/// supported culture, swapped into <see cref="Application.Resources"/>. Only "en-US" is
/// populated today - this exists so adding a second locale is a new
/// resource file plus an entry below, not a code change anywhere that consumes strings.</summary>
public static class AppLocalization
{
    public static readonly CultureInfo DefaultCulture = new("en-US");

    public static readonly IReadOnlyDictionary<string, string> SupportedCultures = new Dictionary<string, string>
    {
        ["en-US"] = "avares://ApiConnectTool/Localization/Strings.en-US.axaml",
    };

    private static ResourceInclude? _active;

    /// <summary>Merges the given culture's string resources into the application, replacing
    /// whichever culture (if any) was previously active. Falls back to <see cref="DefaultCulture"/>
    /// when the requested culture has no resource file yet. Call once at startup; safe to call
    /// again later (e.g. from a future "Language" setting) to switch cultures at runtime.</summary>
    public static void Apply(CultureInfo culture)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        if (!SupportedCultures.TryGetValue(culture.Name, out var source))
        {
            source = SupportedCultures[DefaultCulture.Name];
        }

        if (_active is not null)
        {
            app.Resources.MergedDictionaries.Remove(_active);
        }

        _active = new ResourceInclude(new Uri("avares://ApiConnectTool/"))
        {
            Source = new Uri(source)
        };
        app.Resources.MergedDictionaries.Add(_active);
    }

    /// <summary>Code-behind lookup for strings that can't be a plain XAML
    /// <c>{DynamicResource}</c> (e.g. a message built with <see cref="string.Format(string, object)"/>).
    /// Falls back to the bundled default catalog when no application resource is available.
    /// Returns the key itself if it is also missing from the default catalog.</summary>
    public static string Get(string key) =>
        Application.Current?.Resources.TryGetResource(key, null, out var value) == true && value is string s
            ? s
            : DefaultStrings.Get(key);
}
