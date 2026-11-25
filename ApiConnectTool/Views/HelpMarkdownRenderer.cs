using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using ApiConnectTool.Help;

namespace ApiConnectTool.Views;

public static class HelpMarkdownRenderer
{
    public static StackPanel Render(
        string markdown,
        Action<string>? onNavigateTopic = null,
        Func<string, bool>? canResolveTopic = null) =>
        Render(HelpMarkdownParser.Parse(markdown), onNavigateTopic, canResolveTopic);

    public static StackPanel Render(
        IReadOnlyList<HelpBlock> blocks,
        Action<string>? onNavigateTopic = null,
        Func<string, bool>? canResolveTopic = null)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var root = new StackPanel { Spacing = 12 };

        foreach (var block in blocks)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    root.Children.Add(RenderHeading(heading, onNavigateTopic, canResolveTopic));
                    break;
                case ParagraphBlock paragraph:
                    root.Children.Add(RenderParagraph(paragraph, onNavigateTopic, canResolveTopic));
                    break;
                case ListBlock list:
                    root.Children.Add(RenderList(list, onNavigateTopic, canResolveTopic));
                    break;
                case TableBlock table:
                    root.Children.Add(RenderTable(table, onNavigateTopic, canResolveTopic));
                    break;
                case CodeBlock code:
                    root.Children.Add(RenderCodeBlock(code));
                    break;
            }
        }

        return root;
    }

    private static TextBlock RenderHeading(
        HeadingBlock heading,
        Action<string>? onNavigateTopic,
        Func<string, bool>? canResolveTopic)
    {
        var fontSizeKey = heading.Level switch
        {
            1 => "FontSizeTitle",
            2 => "FontSizeHeading",
            3 => "FontSizeSubheading",
            _ => "FontSizeBody"
        };

        var textBlock = CreateBaseTextBlock(fontSizeKey);
        textBlock.Tag = $"h{heading.Level}";
        textBlock.FontWeight = heading.Level == 1 ? FontWeight.Bold : FontWeight.SemiBold;
        textBlock.Margin = new Thickness(0, heading.Level == 1 ? 0 : 4, 0, 0);
        PopulateInlines(textBlock.Inlines!, heading.Inlines, onNavigateTopic, canResolveTopic);
        return textBlock;
    }

    private static TextBlock RenderParagraph(
        ParagraphBlock paragraph,
        Action<string>? onNavigateTopic,
        Func<string, bool>? canResolveTopic)
    {
        var textBlock = CreateBaseTextBlock("FontSizeBody");
        textBlock.Tag = "p";
        PopulateInlines(textBlock.Inlines!, paragraph.Inlines, onNavigateTopic, canResolveTopic);
        return textBlock;
    }

    private static StackPanel RenderList(
        ListBlock list,
        Action<string>? onNavigateTopic,
        Func<string, bool>? canResolveTopic)
    {
        var panel = new StackPanel { Spacing = 4, Tag = list.IsOrdered ? "ol" : "ul" };

        for (var i = 0; i < list.Items.Count; i++)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var marker = CreateBaseTextBlock("FontSizeBody");
            marker.Text = list.IsOrdered ? $"{i + 1}." : "•";
            marker.Margin = new Thickness(4, 0, 8, 0);
            Grid.SetColumn(marker, 0);

            var content = CreateBaseTextBlock("FontSizeBody");
            PopulateInlines(content.Inlines!, list.Items[i], onNavigateTopic, canResolveTopic);
            Grid.SetColumn(content, 1);

            row.Children.Add(marker);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Border RenderTable(
        TableBlock table,
        Action<string>? onNavigateTopic,
        Func<string, bool>? canResolveTopic)
    {
        var colCount = Math.Max(1, table.Headers.Count);
        var rowCount = table.Rows.Count + 1;
        var grid = new Grid();
        for (var c = 0; c < colCount; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var r = 0; r < rowCount; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var c = 0; c < colCount; c++)
        {
            var headerText = CreateBaseTextBlock("FontSizeBody");
            headerText.FontWeight = FontWeight.SemiBold;
            PopulateInlines(headerText.Inlines!, table.Headers[c], onNavigateTopic, canResolveTopic);
            AddTableCell(grid, 0, c, headerText, "ApiConnectTool.Brush.Chrome", "ApiConnectTool.Brush.Border",
                c < colCount - 1 ? 1 : 0, table.Rows.Count > 0 ? 1 : 0);
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (var c = 0; c < colCount; c++)
            {
                var cellText = CreateBaseTextBlock("FontSizeBody");
                if (c < row.Count) PopulateInlines(cellText.Inlines!, row[c], onNavigateTopic, canResolveTopic);
                AddTableCell(grid, r + 1, c, cellText, "ApiConnectTool.Brush.Surface", "ApiConnectTool.Brush.BorderSubtle",
                    c < colCount - 1 ? 1 : 0, r < table.Rows.Count - 1 ? 1 : 0);
            }
        }

        var outer = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = grid,
            Tag = "table"
        };
        outer.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("ApiConnectTool.Brush.Border"));
        outer.Bind(Border.BackgroundProperty, new DynamicResourceExtension("ApiConnectTool.Brush.Surface"));
        return outer;
    }

    private static void AddTableCell(
        Grid grid,
        int row,
        int col,
        Control child,
        string backgroundKey,
        string borderKey,
        double rightBorder,
        double bottomBorder)
    {
        var cellBorder = new Border
        {
            Padding = new Thickness(8, 4),
            BorderThickness = new Thickness(0, 0, rightBorder, bottomBorder),
            Child = child
        };
        cellBorder.Bind(Border.BackgroundProperty, new DynamicResourceExtension(backgroundKey));
        cellBorder.Bind(Border.BorderBrushProperty, new DynamicResourceExtension(borderKey));
        Grid.SetRow(cellBorder, row);
        Grid.SetColumn(cellBorder, col);
        grid.Children.Add(cellBorder);
    }

    private static Border RenderCodeBlock(CodeBlock codeBlock)
    {
        var textBlock = CreateBaseTextBlock("FontSizeBody");
        textBlock.Text = codeBlock.Code;
        textBlock.Bind(TextBlock.FontFamilyProperty, new DynamicResourceExtension("ApiConnectTool.FontFamily.Monospace"));

        var border = new Border
        {
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Child = textBlock,
            Tag = "code"
        };
        border.Bind(Border.BackgroundProperty, new DynamicResourceExtension("ApiConnectTool.Brush.Chrome"));
        border.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("ApiConnectTool.Brush.BorderSubtle"));
        return border;
    }

    private static TextBlock CreateBaseTextBlock(string fontSizeResourceKey)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Inlines = new InlineCollection()
        };
        textBlock.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension(fontSizeResourceKey));
        textBlock.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("ApiConnectTool.Brush.Text"));
        return textBlock;
    }

    private static void PopulateInlines(
        InlineCollection target,
        IReadOnlyList<HelpInline> inlines,
        Action<string>? onNavigateTopic,
        Func<string, bool>? canResolveTopic)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextInline text:
                    target.Add(new Run(text.Text));
                    break;
                case BoldInline bold:
                    var boldSpan = new Bold();
                    PopulateInlines(boldSpan.Inlines, bold.Inlines, onNavigateTopic, canResolveTopic);
                    target.Add(boldSpan);
                    break;
                case ItalicInline italic:
                    var italicSpan = new Italic();
                    PopulateInlines(italicSpan.Inlines, italic.Inlines, onNavigateTopic, canResolveTopic);
                    target.Add(italicSpan);
                    break;
                case CodeInline code:
                    var codeRun = new Run(code.Code);
                    codeRun.Bind(TextElement.FontFamilyProperty, new DynamicResourceExtension("ApiConnectTool.FontFamily.Monospace"));
                    codeRun.Bind(TextElement.BackgroundProperty, new DynamicResourceExtension("ApiConnectTool.Brush.Chrome"));
                    codeRun.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("ApiConnectTool.Brush.Text"));
                    target.Add(codeRun);
                    break;
                case LinkInline link:
                    if (link.TopicId is { } topicId && (canResolveTopic?.Invoke(topicId) ?? true))
                    {
                        target.Add(new InlineUIContainer(CreateTopicLinkButton(link.Label, topicId, onNavigateTopic)));
                    }
                    else
                    {
                        target.Add(new Run(link.Label));
                    }

                    break;
            }
        }
    }

    private static Button CreateTopicLinkButton(string label, string topicId, Action<string>? onNavigateTopic)
    {
        var labelText = new TextBlock
        {
            Text = label,
            TextDecorations = TextDecorations.Underline
        };
        labelText.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("FontSizeBody"));
        labelText.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("ApiConnectTool.Brush.AccentText"));

        var button = new Button
        {
            Classes = { "help-link" },
            Tag = topicId,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            MinHeight = 0,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = labelText
        };
        button.Bind(Button.BackgroundProperty, new DynamicResourceExtension("ApiConnectTool.Brush.Surface"));
        button.Bind(Button.ForegroundProperty, new DynamicResourceExtension("ApiConnectTool.Brush.AccentText"));
        button.Click += (_, _) => onNavigateTopic?.Invoke(topicId);
        return button;
    }
}
