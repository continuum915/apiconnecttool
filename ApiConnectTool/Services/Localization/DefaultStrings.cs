using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace ApiConnectTool.Services.Localization;

/// <summary>The bundled default catalog, available to pure services without starting Avalonia.</summary>
public static class DefaultStrings
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Catalog = new(() =>
    {
        using var stream = typeof(DefaultStrings).Assembly.GetManifestResourceStream("ApiConnectTool.Localization.DefaultStrings")
            ?? throw new InvalidOperationException("The default localization catalog is missing.");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(stream).Root!.Elements(x + "String")
            .ToDictionary(element => (string)element.Attribute(x + "Key")!, element => element.Value, StringComparer.Ordinal);
    });

    public static string Get(string key) => Catalog.Value.TryGetValue(key, out var value) ? value : key;
}
