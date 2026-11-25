using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using ApiConnectTool.Tests.Views;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Help;

public class HelpMarkdownRendererTests
{
    [Fact]
    public void Render_HeadingsAndParagraphs_ProducesExpectedControls() =>
        HeadlessSession.RunAsync(() =>
        {
            const string markdown = """
                # Top Heading

                ## Section Heading

                ### Subsection Heading

                First line of paragraph
                wrapped onto a second line.
                """;

            var panel = HelpMarkdownRenderer.Render(markdown);
            Assert.Equal(4, panel.Children.Count);

            var h1 = Assert.IsType<TextBlock>(panel.Children[0]);
            Assert.Equal("h1", h1.Tag);
            Assert.Equal(FontWeight.Bold, h1.FontWeight);
            Assert.Equal("Top Heading", ExtractPlainText(h1));

            var h2 = Assert.IsType<TextBlock>(panel.Children[1]);
            Assert.Equal("h2", h2.Tag);
            Assert.Equal(FontWeight.SemiBold, h2.FontWeight);
            Assert.Equal("Section Heading", ExtractPlainText(h2));

            var h3 = Assert.IsType<TextBlock>(panel.Children[2]);
            Assert.Equal("h3", h3.Tag);
            Assert.Equal("Subsection Heading", ExtractPlainText(h3));

            var paragraph = Assert.IsType<TextBlock>(panel.Children[3]);
            Assert.Equal("p", paragraph.Tag);
            Assert.Equal("First line of paragraph wrapped onto a second line.", ExtractPlainText(paragraph));
        });

    [Fact]
    public void Render_InlineFormatting_SupportsBoldItalicAndInlineCode() =>
        HeadlessSession.RunAsync(() =>
        {
            const string markdown = "Use **bold**, *italic*, _also italic_, `inline_code()`, and keep snake_case_var intact.";

            var panel = HelpMarkdownRenderer.Render(markdown);
            var paragraph = Assert.IsType<TextBlock>(Assert.Single(panel.Children));
            var inlines = paragraph.Inlines!;

            Assert.Contains(inlines.OfType<Bold>(), b => b.Inlines.OfType<Run>().Any(r => r.Text == "bold"));
            Assert.Equal(2, inlines.OfType<Italic>().Count());
            Assert.Contains(inlines.OfType<Run>(), r => r.Text == "inline_code()");
            Assert.Contains(inlines.OfType<Run>(), r => r.Text != null && r.Text.Contains("snake_case_var"));
        });

    [Fact]
    public void Render_UnorderedAndOrderedLists_ProducesListRows() =>
        HeadlessSession.RunAsync(() =>
        {
            const string markdown = """
                - First bullet
                  continued on next line
                - Second **bullet**

                1. Step one
                2. Step two
                """;

            var panel = HelpMarkdownRenderer.Render(markdown);
            Assert.Equal(2, panel.Children.Count);

            var unordered = Assert.IsType<StackPanel>(panel.Children[0]);
            Assert.Equal("ul", unordered.Tag);
            Assert.Equal(2, unordered.Children.Count);
            Assert.Contains("First bullet continued on next line", ExtractPlainText(unordered.Children[0]));
            Assert.Contains("•", ExtractPlainText(unordered.Children[0]));

            var ordered = Assert.IsType<StackPanel>(panel.Children[1]);
            Assert.Equal("ol", ordered.Tag);
            Assert.Equal(2, ordered.Children.Count);
            Assert.Contains("1.", ExtractPlainText(ordered.Children[0]));
            Assert.Contains("Step one", ExtractPlainText(ordered.Children[0]));
            Assert.Contains("2.", ExtractPlainText(ordered.Children[1]));
            Assert.Contains("Step two", ExtractPlainText(ordered.Children[1]));
        });

    [Fact]
    public void Render_TablesAndFencedCode_ProducesTableGridAndCodeBorder() =>
        HeadlessSession.RunAsync(() =>
        {
            const string markdown = """
                | Keyword | Meaning |
                |---|---|
                | `type` | Checks JSON type |
                | `required` | Checks **required** keys |

                ```json
                {
                  "type": "object"
                }
                ```
                """;

            var panel = HelpMarkdownRenderer.Render(markdown);
            Assert.Equal(2, panel.Children.Count);

            var tableBorder = Assert.IsType<Border>(panel.Children[0]);
            Assert.Equal("table", tableBorder.Tag);
            var grid = Assert.IsType<Grid>(tableBorder.Child);
            Assert.Equal(2, grid.ColumnDefinitions.Count);
            Assert.Equal(3, grid.RowDefinitions.Count);
            Assert.Contains("Keyword", ExtractPlainText(tableBorder));
            Assert.Contains("Checks JSON type", ExtractPlainText(tableBorder));

            var codeBorder = Assert.IsType<Border>(panel.Children[1]);
            Assert.Equal("code", codeBorder.Tag);
            var codeText = Assert.IsType<TextBlock>(codeBorder.Child);
            Assert.Contains("\"type\": \"object\"", codeText.Text);
        });

    [Fact]
    public void Render_Links_TopicLinksNavigateAndOtherLinksRenderAsPlainText() =>
        HeadlessSession.RunAsync(() =>
        {
            var navigated = new List<string>();
            var knownTopics = new HashSet<string> { "assertions-and-scripts" };

            const string markdown = """
                Read [Assertions and Scripts](assertions-and-scripts), visit [Example Site](https://example.com),
                email [Support](mailto:support@example.com), or check [Missing Topic](unknown-topic).
                """;

            var panel = HelpMarkdownRenderer.Render(
                markdown,
                onNavigateTopic: topicId => navigated.Add(topicId),
                canResolveTopic: topicId => knownTopics.Contains(topicId));

            var linkButtons = FindLinkButtons(panel);
            var topicButton = Assert.Single(linkButtons);
            Assert.Equal("assertions-and-scripts", topicButton.Tag);
            Assert.Equal("Assertions and Scripts", ExtractPlainText(topicButton));

            topicButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(["assertions-and-scripts"], navigated);

            var fullText = ExtractPlainText(panel);
            Assert.Contains("Example Site", fullText);
            Assert.Contains("Support", fullText);
            Assert.Contains("Missing Topic", fullText);
            Assert.DoesNotContain("https://example.com", fullText);
            Assert.DoesNotContain("mailto:support@example.com", fullText);
        });

    [Fact]
    public void Render_UnsupportedMarkdownConstructs_DegradeGracefullyToPlainText() =>
        HeadlessSession.RunAsync(() =>
        {
            const string markdown = """
                > Blockquote line degrades to text
                <script>alert("hi")</script>
                ![Diagram Alt Text](diagram.png)
                Unclosed **bold and `unclosed code and [broken link](not closed
                Single | pipe | line without delimiter
                #NotAHeadingWithoutSpace
                """;

            var panel = HelpMarkdownRenderer.Render(markdown);
            Assert.Empty(FindLinkButtons(panel));

            var plainText = ExtractPlainText(panel);
            Assert.Contains("Blockquote line degrades to text", plainText);
            Assert.Contains("<script>alert(\"hi\")</script>", plainText);
            Assert.Contains("Diagram Alt Text", plainText);
            Assert.DoesNotContain("diagram.png", plainText);
            Assert.Contains("Unclosed **bold and `unclosed code and [broken link](not closed", plainText);
            Assert.Contains("Single | pipe | line without delimiter", plainText);
            Assert.Contains("#NotAHeadingWithoutSpace", plainText);
        });

    internal static IReadOnlyList<Button> FindLinkButtons(Control root)
    {
        var buttons = new List<Button>();
        CollectButtons(root, buttons);
        return buttons;
    }

    internal static string ExtractPlainText(Control root)
    {
        var builder = new StringBuilder();
        CollectText(root, builder);
        return builder.ToString().Trim();
    }

    private static void CollectButtons(Control control, List<Button> buttons)
    {
        if (control is Button button)
        {
            buttons.Add(button);
            return;
        }

        if (control is TextBlock { Inlines: { Count: > 0 } inlines })
        {
            CollectInlineButtons(inlines, buttons);
            return;
        }

        if (control is Panel panel)
        {
            foreach (var child in panel.Children) CollectButtons(child, buttons);
        }
        else if (control is Decorator { Child: { } decoratorChild })
        {
            CollectButtons(decoratorChild, buttons);
        }
        else if (control is ContentControl { Content: Control contentChild })
        {
            CollectButtons(contentChild, buttons);
        }
    }

    private static void CollectInlineButtons(InlineCollection inlines, List<Button> buttons)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case InlineUIContainer { Child: { } child }:
                    CollectButtons(child, buttons);
                    break;
                case Span span:
                    CollectInlineButtons(span.Inlines, buttons);
                    break;
            }
        }
    }

    private static void CollectText(Control control, StringBuilder builder)
    {
        switch (control)
        {
            case TextBlock textBlock:
                if (!string.IsNullOrEmpty(textBlock.Text))
                {
                    if (builder.Length > 0 && !char.IsWhiteSpace(builder[^1])) builder.Append(' ');
                    builder.Append(textBlock.Text);
                }
                else if (textBlock.Inlines is { Count: > 0 } inlines)
                {
                    CollectInlineText(inlines, builder);
                }

                break;
            case Panel panel:
                foreach (var child in panel.Children) CollectText(child, builder);
                break;
            case Decorator { Child: { } decoratorChild }:
                CollectText(decoratorChild, builder);
                break;
            case ContentControl { Content: Control contentChild }:
                CollectText(contentChild, builder);
                break;
        }
    }

    private static void CollectInlineText(InlineCollection inlines, StringBuilder builder)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run when !string.IsNullOrEmpty(run.Text):
                    builder.Append(run.Text);
                    break;
                case Span span:
                    CollectInlineText(span.Inlines, builder);
                    break;
                case InlineUIContainer { Child: { } child }:
                    CollectText(child, builder);
                    break;
            }
        }
    }
}
