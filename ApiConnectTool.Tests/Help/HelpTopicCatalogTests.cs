using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ApiConnectTool.Help;
using ApiConnectTool.Tests.Views;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Help;

public class HelpTopicCatalogTests
{
    [Fact]
    public void EnvironmentsTopic_ExplainsCollectionAndGlobalEnvironmentsAndTheirPrecedence()
    {
        var topic = Assert.Single(HelpTopicCatalog.Load(assembly: typeof(HelpTopicCatalog).Assembly), t => t.Id == "environments");
        var body = topic.BodyMarkdown;

        foreach (var expected in new[]
                 {
                     "collection environment", "global environment", "Structure a multi-API workspace",
                     "Use '<name>' in every collection", "Copy to Collection...", "Move to Collection...",
                     "pm.environment", "OAuth", "Ctrl+Shift+E", "baseUrl"
                 })
        {
            Assert.Contains(expected, body);
        }

        var order = new[]
        {
            "Run variables", "The active collection environment.", "The active global environment.",
            "Collection variables of the request's collection.", "Globals."
        }.Select(text => body.IndexOf(text, StringComparison.Ordinal)).ToArray();
        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.OrderBy(index => index), order);
    }

    [Fact]
    public void Load_AllEmbeddedTopics_HaveValidFrontMatter_UniqueIds_AndResolvableTopicLinks()
    {
        var assembly = typeof(HelpTopicCatalog).Assembly;
        var embeddedMarkdownResources = assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith("ApiConnectTool.Help.", StringComparison.OrdinalIgnoreCase) &&
                           name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(embeddedMarkdownResources);

        var topics = HelpTopicCatalog.Load(assembly: assembly);
        Assert.Equal(embeddedMarkdownResources.Length + 1, topics.Count);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var topic in topics)
        {
            Assert.False(string.IsNullOrWhiteSpace(topic.Id));
            Assert.False(string.IsNullOrWhiteSpace(topic.Title));
            Assert.True(topic.Order > 0, $"Topic '{topic.Id}' should have a positive order.");
            Assert.False(string.IsNullOrWhiteSpace(topic.BodyMarkdown));
            Assert.True(ids.Add(topic.Id), $"Duplicate topic ID '{topic.Id}'.");
        }

        Assert.Contains(topics, t => t.Id == "contents");
        Assert.Contains(topics, t => t.Id == "assertions-and-scripts");
        Assert.Contains(topics, t => t.Id == KeyboardShortcuts.HelpTopicId);
        Assert.Equal(
            topics.OrderBy(t => t.Order).ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase).Select(t => t.Id),
            topics.Select(t => t.Id));

        var totalLinksChecked = 0;
        foreach (var topic in topics)
        {
            var links = HelpMarkdownParser.ExtractLinks(topic.BodyMarkdown);
            foreach (var link in links)
            {
                if (link.TopicId is { } targetTopicId)
                {
                    totalLinksChecked++;
                    Assert.True(
                        ids.Contains(targetTopicId),
                        $"Topic '{topic.Id}' contains a link to unknown topic '{targetTopicId}' (raw target '{link.Target}').");
                }
            }
        }

        Assert.True(totalLinksChecked > 0, "Expected at least one topic link across embedded help topics.");
    }

    [Fact]
    public void AllHelpTopicIdsUsedInXaml_ExistAsTopicsInCatalog()
    {
        var topics = HelpTopicCatalog.Load();
        var validIds = topics.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var repoRoot = FindRepositoryRoot();
        var appDir = Path.Combine(repoRoot, "ApiConnectTool");
        var regex = new Regex(@"HelpContext\.TopicId\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        var filesWithTopicId = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveredIds = new List<(string File, string TopicId)>();

        foreach (var axamlPath in Directory.EnumerateFiles(appDir, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(axamlPath)) continue;
            var content = File.ReadAllText(axamlPath);
            var matches = regex.Matches(content);
            if (matches.Count == 0) continue;

            var relative = Path.GetRelativePath(appDir, axamlPath).Replace('\\', '/');
            filesWithTopicId.Add(relative);
            foreach (Match match in matches)
            {
                discoveredIds.Add((relative, match.Groups[1].Value));
            }
        }

        Assert.NotEmpty(discoveredIds);
        Assert.Contains("Views/Components/RequestEditorControl.axaml", filesWithTopicId);
        Assert.Contains("Views/Components/SidebarControl.axaml", filesWithTopicId);
        Assert.Contains("Views/EnvironmentEditorWindow.axaml", filesWithTopicId);
        Assert.Contains("Views/CollectionRunnerWindow.axaml", filesWithTopicId);
        Assert.Contains("Views/SettingsWindow.axaml", filesWithTopicId);
        Assert.Contains("Views/ImportWizardWindow.axaml", filesWithTopicId);

        foreach (var (file, topicId) in discoveredIds)
        {
            Assert.True(
                validIds.Contains(topicId),
                $"XAML file '{file}' references HelpContext.TopicId=\"{topicId}\", which does not exist in HelpTopicCatalog.");
        }
    }

    [Fact]
    public void GeneratedKeyboardShortcutsPage_ListsExactlyShortcutTableEntries() =>
        HeadlessSession.RunAsync(() =>
        {
            var topics = HelpTopicCatalog.Load();
            var shortcutsTopic = Assert.Single(topics, t => t.Id == KeyboardShortcuts.HelpTopicId);

            var repoRoot = FindRepositoryRoot();
            var helpDir = Path.Combine(repoRoot, "ApiConnectTool", "Help");
            Assert.Empty(Directory.EnumerateFiles(helpDir, "*shortcut*.md", SearchOption.AllDirectories));

            var blocks = HelpMarkdownParser.Parse(shortcutsTopic.BodyMarkdown);
            var table = Assert.Single(blocks.OfType<TableBlock>());

            Assert.Equal(KeyboardShortcuts.Table.Count, table.Rows.Count);
            for (var i = 0; i < KeyboardShortcuts.Table.Count; i++)
            {
                var expected = KeyboardShortcuts.Table[i];
                var row = table.Rows[i];
                Assert.Equal(2, row.Count);

                var gestureInline = Assert.Single(row[0]);
                var codeInline = Assert.IsType<CodeInline>(gestureInline);
                Assert.Equal(KeyboardShortcuts.FormatGesture(expected.Gesture), codeInline.Code);

                var descriptionInline = Assert.Single(row[1]);
                var textInline = Assert.IsType<TextInline>(descriptionInline);
                Assert.Equal(expected.Description, textInline.Text);
            }
        });

    [Theory]
    [InlineData("# No front matter")]
    [InlineData("---\nid: test\ntitle: Test\norder: 1\nBody without closing delimiter")]
    [InlineData("---\ntitle: Missing Id\norder: 1\n---\nBody")]
    [InlineData("---\nid: missing-title\norder: 1\n---\nBody")]
    [InlineData("---\nid: missing-order\ntitle: Missing Order\n---\nBody")]
    [InlineData("---\nid: bad-order\ntitle: Bad Order\norder: abc\n---\nBody")]
    public void ParseTopic_InvalidFrontMatter_ThrowsFormatException(string rawMarkdown)
    {
        Assert.Throws<FormatException>(() => HelpTopicCatalog.ParseTopic(rawMarkdown, "invalid.md"));
    }

    private static bool IsBuildOutput(string path) =>
        path.Split('/', '\\').Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "ApiConnectTool.slnx")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate ApiConnectTool.slnx.");
    }
}
