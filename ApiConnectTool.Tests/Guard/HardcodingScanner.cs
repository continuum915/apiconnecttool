using System.Reflection;
using System.Text.RegularExpressions;

namespace ApiConnectTool.Tests.Guard;

/// <summary>One hardcoded color or label found in a source file. Identity is the file and the
/// trimmed line text, not the line number, so unrelated edits do not churn the baseline.</summary>
public sealed record HardcodingOccurrence(string File, int Line, string Text)
{
    public string Key => $"{File} | {Text}";
}

/// <summary>Scans app views, styles and C# sources for color literals and literal UI text that
/// should come from <c>ApiConnectTool.Brush.*</c> tokens and <c>Str.*</c> resources.</summary>
public static partial class HardcodingScanner
{
    public const string BaselineRelativePath = "ApiConnectTool.Tests/Guard/hardcoded-baseline.txt";

    /// <summary>Token dictionaries are the one place color values belong.</summary>
    private static readonly string[] ExcludedFiles =
    [
        "ApiConnectTool/Styles/DesignSystem.axaml",
        "ApiConnectTool/Styles/AeroDesignSystem.axaml"
    ];

    private static readonly HashSet<string> NamedColors = typeof(Avalonia.Media.Colors)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Select(property => property.Name)
        .Where(name => name != "Transparent")
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private const string BrushProperties =
        "Foreground|Background|BorderBrush|Fill|Stroke|Color|CaretBrush|SelectionBrush|SelectionForegroundBrush|" +
        "LineNumbersForeground|OpacityMask|TextFill|PlaceholderForeground";

    [GeneratedRegex("=\"#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\"")]
    private static partial Regex XamlHexColor();

    [GeneratedRegex(">\\s*#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\\s*<")]
    private static partial Regex XamlElementHexColor();

    [GeneratedRegex("(?<![\\w.:])(?:" + BrushProperties + ")=\"(?<name>[A-Za-z]+)\"")]
    private static partial Regex XamlNamedColorAttribute();

    [GeneratedRegex("Property=\"(?:" + BrushProperties + ")\"\\s+Value=\"(?<name>[A-Za-z]+)\"")]
    private static partial Regex XamlNamedColorSetter();

    [GeneratedRegex("(?<=\\s)(?<attr>Text|Content|Header|PlaceholderText|ToolTip\\.Tip|AutomationProperties\\.Name)=\"(?<value>[^\"]*)\"")]
    private static partial Regex XamlTextAttribute();

    [GeneratedRegex("\\bColor\\.(?:FromRgb|FromArgb|FromUInt32|Parse|TryParse)\\s*\\(|\\b(?:Brushes|Colors)\\.(?!Transparent\\b)[A-Z]\\w*|\"#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\"")]
    private static partial Regex CSharpColor();

    public static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ApiConnectTool.slnx")))
                return directory.FullName;
        }
        throw new InvalidOperationException("Could not find ApiConnectTool.slnx above the test output directory.");
    }

    public static IReadOnlyList<HardcodingOccurrence> ScanRepository(string root)
    {
        var app = Path.Combine(root, "ApiConnectTool");
        var xaml = new[] { "Views", "Styles" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(app, folder), "*.axaml", SearchOption.AllDirectories));
        var csharp = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories);
        return xaml.Concat(csharp)
            .Select(path => (Path: path, Relative: Path.GetRelativePath(root, path).Replace('\\', '/')))
            .Where(file => !IsGenerated(file.Relative) && !ExcludedFiles.Contains(file.Relative))
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .SelectMany(file => Scan(file.Relative, File.ReadAllText(file.Path)))
            .ToArray();
    }

    private static bool IsGenerated(string relative) =>
        relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal);

    public static IEnumerable<HardcodingOccurrence> Scan(string relativePath, string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var isXaml = relativePath.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (IsFlagged(line, isXaml))
                yield return new HardcodingOccurrence(relativePath, index + 1, line.Trim());
        }
    }

    private static bool IsFlagged(string line, bool isXaml)
    {
        if (!isXaml)
            return !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && CSharpColor().IsMatch(line);

        if (line.TrimStart().StartsWith("<!--", StringComparison.Ordinal)) return false;
        if (XamlHexColor().IsMatch(line) || XamlElementHexColor().IsMatch(line)) return true;
        if (XamlNamedColorAttribute().Matches(line).Concat(XamlNamedColorSetter().Matches(line))
            .Any(match => NamedColors.Contains(match.Groups["name"].Value)))
            return true;
        return XamlTextAttribute().Matches(line).Any(match => IsLiteralText(match.Groups["value"].Value));
    }

    private static bool IsLiteralText(string value) =>
        value.Length > 0 && !value.StartsWith('{');

    /// <summary>Baseline entries as a multiset: a line may legitimately appear more than once.</summary>
    public static Dictionary<string, int> ReadBaseline(string path) =>
        File.Exists(path)
            ? File.ReadAllLines(path)
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .GroupBy(line => line, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);

    public static void WriteBaseline(string path, IEnumerable<HardcodingOccurrence> occurrences)
    {
        var header = new[]
        {
            "# Hardcoded colors and UI labels that predate the classic desktop UI guard.",
            "# Format: <file> | <trimmed line text>. Remove entries when a ticket rewrites the control.",
            "# Regenerate with APICONNECTTOOL_UPDATE_HARDCODED_BASELINE=1 dotnet test --filter HardcodingGuardTests"
        };
        File.WriteAllLines(path, header.Concat(occurrences.Select(occurrence => occurrence.Key)));
    }
}
