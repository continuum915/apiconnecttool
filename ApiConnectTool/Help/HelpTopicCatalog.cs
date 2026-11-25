using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Help;

public sealed record HelpTopic(
    string Id,
    string Title,
    int Order,
    string BodyMarkdown,
    string ResourceName);

public static class HelpTopicCatalog
{
    public static IReadOnlyList<HelpTopic> Load(string? cultureName = null, Assembly? assembly = null)
    {
        var targetAssembly = assembly ?? typeof(HelpTopicCatalog).Assembly;
        var requestedCulture = string.IsNullOrWhiteSpace(cultureName)
            ? AppLocalization.DefaultCulture.Name
            : cultureName;

        var resourceNames = FindResourceNamesForCulture(targetAssembly, requestedCulture);
        if (resourceNames.Count == 0 && !string.Equals(requestedCulture, AppLocalization.DefaultCulture.Name, StringComparison.OrdinalIgnoreCase))
        {
            resourceNames = FindResourceNamesForCulture(targetAssembly, AppLocalization.DefaultCulture.Name);
        }

        var topics = new List<HelpTopic>(resourceNames.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in resourceNames)
        {
            using var stream = targetAssembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing embedded help topic stream '{resourceName}'.");
            using var reader = new StreamReader(stream);
            var raw = reader.ReadToEnd();
            var topic = ParseTopic(raw, resourceName);
            if (!seenIds.Add(topic.Id))
            {
                throw new FormatException($"Duplicate help topic id '{topic.Id}' in '{resourceName}'.");
            }

            topics.Add(topic);
        }

        var generatedShortcuts = ApiConnectTool.Views.KeyboardShortcuts.BuildHelpTopic();
        if (!seenIds.Add(generatedShortcuts.Id))
        {
            throw new FormatException($"Duplicate help topic id '{generatedShortcuts.Id}'.");
        }

        topics.Add(generatedShortcuts);

        return topics
            .OrderBy(topic => topic.Order)
            .ThenBy(topic => topic.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(topic => topic.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static HelpTopic ParseTopic(string rawMarkdown, string sourceName = "<inline>")
    {
        ArgumentNullException.ThrowIfNull(rawMarkdown);

        var text = rawMarkdown.TrimStart('\uFEFF');
        using var reader = new StringReader(text);
        var firstLine = reader.ReadLine();
        if (firstLine is null || firstLine.Trim() != "---")
        {
            throw new FormatException($"Help topic '{sourceName}' is missing opening front matter delimiter '---'.");
        }

        string? id = null;
        string? title = null;
        int? order = null;
        var closed = false;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed == "---")
            {
                closed = true;
                break;
            }

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
            {
                throw new FormatException($"Help topic '{sourceName}' has malformed front matter line '{line}'.");
            }

            var key = line[..colonIndex].Trim();
            var value = StripQuotes(line[(colonIndex + 1)..].Trim());

            switch (key.ToLowerInvariant())
            {
                case "id":
                    id = value;
                    break;
                case "title":
                    title = value;
                    break;
                case "order":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOrder))
                    {
                        throw new FormatException($"Help topic '{sourceName}' has non-integer front matter order '{value}'.");
                    }

                    order = parsedOrder;
                    break;
            }
        }

        if (!closed)
        {
            throw new FormatException($"Help topic '{sourceName}' has unclosed front matter.");
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new FormatException($"Help topic '{sourceName}' is missing required front matter 'id'.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new FormatException($"Help topic '{sourceName}' is missing required front matter 'title'.");
        }

        if (order is null)
        {
            throw new FormatException($"Help topic '{sourceName}' is missing required front matter 'order'.");
        }

        var body = reader.ReadToEnd().Trim();
        return new HelpTopic(id, title, order.Value, body, sourceName);
    }

    private static List<string> FindResourceNamesForCulture(Assembly assembly, string cultureName)
    {
        var normalized = cultureName.Replace('-', '_');
        var prefixDash = $"ApiConnectTool.Help.{cultureName}.";
        var prefixUnderscore = $"ApiConnectTool.Help.{normalized}.";

        return assembly
            .GetManifestResourceNames()
            .Where(name =>
                name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                (name.StartsWith(prefixDash, StringComparison.OrdinalIgnoreCase) ||
                 name.StartsWith(prefixUnderscore, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static string StripQuotes(string value) =>
        value.Length >= 2 &&
        ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
            ? value[1..^1]
            : value;
}
