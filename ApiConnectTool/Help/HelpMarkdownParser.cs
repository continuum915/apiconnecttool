using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ApiConnectTool.Help;

public abstract record HelpBlock;

public sealed record HeadingBlock(int Level, IReadOnlyList<HelpInline> Inlines) : HelpBlock;

public sealed record ParagraphBlock(IReadOnlyList<HelpInline> Inlines) : HelpBlock;

public sealed record ListBlock(bool IsOrdered, IReadOnlyList<IReadOnlyList<HelpInline>> Items) : HelpBlock;

public sealed record TableBlock(
    IReadOnlyList<IReadOnlyList<HelpInline>> Headers,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<HelpInline>>> Rows) : HelpBlock;

public sealed record CodeBlock(string Language, string Code) : HelpBlock;

public abstract record HelpInline;

public sealed record TextInline(string Text) : HelpInline;

public sealed record BoldInline(IReadOnlyList<HelpInline> Inlines) : HelpInline;

public sealed record ItalicInline(IReadOnlyList<HelpInline> Inlines) : HelpInline;

public sealed record CodeInline(string Code) : HelpInline;

public sealed record LinkInline(string Label, string Target, string? TopicId) : HelpInline;

public static class HelpMarkdownParser
{
    public static IReadOnlyList<HelpBlock> Parse(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = new List<HelpBlock>();
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                index++;
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                blocks.Add(ParseCodeBlock(lines, ref index));
                continue;
            }

            if (TryParseHeading(trimmed, out var heading))
            {
                blocks.Add(heading);
                index++;
                continue;
            }

            if (index + 1 < lines.Length && trimmed.Contains('|') && IsTableDelimiterLine(lines[index + 1]))
            {
                blocks.Add(ParseTableBlock(lines, ref index));
                continue;
            }

            if (TryMatchListItem(line, out var isOrdered, out _))
            {
                blocks.Add(ParseListBlock(lines, ref index, isOrdered));
                continue;
            }

            blocks.Add(ParseParagraphBlock(lines, ref index));
        }

        return blocks;
    }

    public static IReadOnlyList<LinkInline> ExtractLinks(string markdown) =>
        HelpInlineParser.ExtractLinks(Parse(markdown));

    private static CodeBlock ParseCodeBlock(string[] lines, ref int index)
    {
        var language = lines[index].Trim()[3..].Trim();
        index++;
        var codeLines = new List<string>();
        while (index < lines.Length)
        {
            if (lines[index].Trim().StartsWith("```", StringComparison.Ordinal))
            {
                index++;
                break;
            }

            codeLines.Add(lines[index]);
            index++;
        }

        return new CodeBlock(language, string.Join("\n", codeLines));
    }

    private static bool TryParseHeading(string trimmed, out HeadingBlock heading)
    {
        var level = 0;
        while (level < trimmed.Length && level < 7 && trimmed[level] == '#') level++;

        if (level is >= 1 and <= 6 && level < trimmed.Length && char.IsWhiteSpace(trimmed[level]))
        {
            heading = new HeadingBlock(level, HelpInlineParser.ParseInlines(trimmed[(level + 1)..].Trim()));
            return true;
        }

        heading = null!;
        return false;
    }

    private static TableBlock ParseTableBlock(string[] lines, ref int index)
    {
        var headers = SplitTableRow(lines[index]).Select(HelpInlineParser.ParseInlines).ToArray();
        var columnCount = headers.Length;
        index += 2;

        var rows = new List<IReadOnlyList<IReadOnlyList<HelpInline>>>();
        while (index < lines.Length)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.Length == 0 || !trimmed.Contains('|') ||
                trimmed.StartsWith("```", StringComparison.Ordinal) || TryParseHeading(trimmed, out _))
            {
                break;
            }

            var rawCells = SplitTableRow(lines[index]);
            var row = new IReadOnlyList<HelpInline>[columnCount];
            for (var col = 0; col < columnCount; col++)
            {
                row[col] = col < rawCells.Count ? HelpInlineParser.ParseInlines(rawCells[col]) : [];
            }

            rows.Add(row);
            index++;
        }

        return new TableBlock(headers, rows);
    }

    private static ListBlock ParseListBlock(string[] lines, ref int index, bool isOrdered)
    {
        var itemTexts = new List<StringBuilder>();
        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (trimmed.Length == 0) break;

            if (TryMatchListItem(line, out var itemOrdered, out var itemContent))
            {
                if (itemOrdered != isOrdered) break;
                itemTexts.Add(new StringBuilder(itemContent));
                index++;
                continue;
            }

            if (itemTexts.Count > 0 && (line.StartsWith("  ", StringComparison.Ordinal) || line.StartsWith('\t')) &&
                !trimmed.StartsWith("```", StringComparison.Ordinal) && !TryParseHeading(trimmed, out _))
            {
                itemTexts[^1].Append(' ').Append(trimmed);
                index++;
                continue;
            }

            break;
        }

        return new ListBlock(isOrdered, itemTexts.Select(b => HelpInlineParser.ParseInlines(b.ToString())).ToArray());
    }

    private static ParagraphBlock ParseParagraphBlock(string[] lines, ref int index)
    {
        var parts = new List<string>();
        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("```", StringComparison.Ordinal) ||
                TryParseHeading(trimmed, out _) || TryMatchListItem(line, out _, out _) ||
                (index + 1 < lines.Length && trimmed.Contains('|') && IsTableDelimiterLine(lines[index + 1])))
            {
                break;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal)) trimmed = trimmed[2..].TrimStart();
            else if (trimmed == ">") trimmed = string.Empty;

            if (trimmed.Length > 0) parts.Add(trimmed);
            index++;
        }

        return new ParagraphBlock(HelpInlineParser.ParseInlines(string.Join(" ", parts)));
    }

    private static bool TryMatchListItem(string line, out bool isOrdered, out string content)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            isOrdered = false;
            content = trimmed[2..].Trim();
            return true;
        }

        var digits = 0;
        while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits])) digits++;
        if (digits > 0 && digits + 1 < trimmed.Length && trimmed[digits] == '.' && char.IsWhiteSpace(trimmed[digits + 1]))
        {
            isOrdered = true;
            content = trimmed[(digits + 2)..].Trim();
            return true;
        }

        isOrdered = false;
        content = string.Empty;
        return false;
    }

    private static bool IsTableDelimiterLine(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.Contains('|')) return false;
        var cells = SplitTableRow(trimmed);
        return cells.Count > 0 && cells.All(cell =>
        {
            var stripped = cell.Trim().Trim(':');
            return stripped.Length > 0 && stripped.All(ch => ch == '-');
        });
    }

    private static IReadOnlyList<string> SplitTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        return trimmed.Split('|').Select(cell => cell.Trim()).ToArray();
    }
}
