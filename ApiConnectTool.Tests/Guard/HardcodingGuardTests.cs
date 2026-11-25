using Xunit;

namespace ApiConnectTool.Tests.Guard;

public class HardcodingGuardTests
{
    [Fact]
    public void AppSources_AddNoColorLiteralsOrLiteralLabelsBeyondTheBaseline()
    {
        var root = HardcodingScanner.FindRepositoryRoot();
        var baselinePath = Path.Combine(root, HardcodingScanner.BaselineRelativePath);
        var occurrences = HardcodingScanner.ScanRepository(root);
        if (Environment.GetEnvironmentVariable("APICONNECTTOOL_UPDATE_HARDCODED_BASELINE") == "1")
            HardcodingScanner.WriteBaseline(baselinePath, occurrences);

        var remaining = HardcodingScanner.ReadBaseline(baselinePath);
        var added = new List<HardcodingOccurrence>();
        foreach (var occurrence in occurrences)
        {
            if (remaining.TryGetValue(occurrence.Key, out var count) && count > 0)
                remaining[occurrence.Key] = count - 1;
            else
                added.Add(occurrence);
        }
        var stale = remaining.Where(entry => entry.Value > 0).Select(entry => entry.Key).ToArray();

        Assert.True(added.Count == 0,
            "Use a ApiConnectTool.Brush.* token or a Str.* resource instead of:" + Environment.NewLine +
            string.Join(Environment.NewLine, added.Select(item => $"  {item.File}:{item.Line}: {item.Text}")));
        Assert.True(stale.Length == 0,
            "Remove these entries from " + HardcodingScanner.BaselineRelativePath + "; they no longer occur:" +
            Environment.NewLine + string.Join(Environment.NewLine, stale.Select(item => "  " + item)));
    }

    [Theory]
    [InlineData("<TextBlock Foreground=\"#FF0000\"/>")]
    [InlineData("<Border Background=\"#80FFFFFF\"/>")]
    [InlineData("<TextBlock Foreground=\"IndianRed\"/>")]
    [InlineData("<Setter Property=\"Background\" Value=\"Orange\"/>")]
    [InlineData("<TextBlock Text=\"Hello\"/>")]
    [InlineData("<Button Content=\"Save\"/>")]
    [InlineData("<TabItem Header=\"Body\"/>")]
    [InlineData("<TextBox PlaceholderText=\"Search\"/>")]
    [InlineData("<Button ToolTip.Tip=\"Close tab\"/>")]
    [InlineData("<Button AutomationProperties.Name=\"Close\"/>")]
    public void Scan_FlagsXamlLiterals(string line) =>
        Assert.Single(HardcodingScanner.Scan("ApiConnectTool/Views/Sample.axaml", line));

    [Theory]
    [InlineData("<TextBlock Text=\"{DynamicResource Str.Sample}\" Foreground=\"{DynamicResource ApiConnectTool.Brush.Text}\"/>")]
    [InlineData("<TextBlock Text=\"{Binding Name}\"/>")]
    [InlineData("<Border Background=\"Transparent\"/>")]
    [InlineData("<TextBlock Text=\"\"/>")]
    [InlineData("<!-- <TextBlock Text=\"comment\"/> -->")]
    [InlineData("<Grid ColumnDefinitions=\"Auto,*\"/>")]
    public void Scan_IgnoresTokensBindingsAndTransparent(string line) =>
        Assert.Empty(HardcodingScanner.Scan("ApiConnectTool/Views/Sample.axaml", line));

    [Theory]
    [InlineData("var brush = Brushes.Orange;", true)]
    [InlineData("var color = Color.Parse(\"#20B2AA\");", true)]
    [InlineData("var color = Color.FromRgb(1, 2, 3);", true)]
    [InlineData("var color = Colors.Red;", true)]
    [InlineData("var brush = Brushes.Transparent;", false)]
    [InlineData("// Brushes.Orange in a comment", false)]
    [InlineData("var key = \"ApiConnectTool.Brush.Danger\";", false)]
    public void Scan_FlagsCSharpColorLiterals(string line, bool flagged) =>
        Assert.Equal(flagged, HardcodingScanner.Scan("ApiConnectTool/ViewModels/Sample.cs", line).Any());
}
