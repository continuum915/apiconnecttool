using System;
using System.Collections.Generic;
using System.Text;

namespace ApiConnectTool.Help;

public static class HelpInlineParser
{
    public static IReadOnlyList<HelpInline> ParseInlines(string text)
    {
        var result = new List<HelpInline>();
        if (string.IsNullOrEmpty(text)) return result;

        var buffer = new StringBuilder();
        var i = 0;

        void FlushBuffer()
        {
            if (buffer.Length > 0)
            {
                result.Add(new TextInline(buffer.ToString()));
                buffer.Clear();
            }
        }

        while (i < text.Length)
        {
            if (text[i] == '`')
            {
                var close = text.IndexOf('`', i + 1);
                if (close > i + 1)
                {
                    FlushBuffer();
                    result.Add(new CodeInline(text[(i + 1)..close]));
                    i = close + 1;
                    continue;
                }
            }

            if (text[i] == '!' && i + 1 < text.Length && text[i + 1] == '[' &&
                TryParseBracketLink(text, i + 1, out var altText, out _, out var nextAfterImage))
            {
                buffer.Append(altText);
                i = nextAfterImage;
                continue;
            }

            if (text[i] == '[' && TryParseBracketLink(text, i, out var label, out var target, out var nextAfterLink))
            {
                FlushBuffer();
                var topicId = TryParseTopicTarget(target, out var resolvedId) ? resolvedId : null;
                result.Add(new LinkInline(label, target, topicId));
                i = nextAfterLink;
                continue;
            }

            if (i + 1 < text.Length && ((text[i] == '*' && text[i + 1] == '*') || (text[i] == '_' && text[i + 1] == '_')))
            {
                var marker = text.Substring(i, 2);
                var close = text.IndexOf(marker, i + 2, StringComparison.Ordinal);
                if (close > i + 2)
                {
                    FlushBuffer();
                    result.Add(new BoldInline(ParseInlines(text[(i + 2)..close])));
                    i = close + 2;
                    continue;
                }
            }

            if ((text[i] == '*' || text[i] == '_') && CanOpenEmphasis(text, i))
            {
                var close = FindClosingEmphasis(text, i + 1, text[i]);
                if (close > i + 1)
                {
                    FlushBuffer();
                    result.Add(new ItalicInline(ParseInlines(text[(i + 1)..close])));
                    i = close + 1;
                    continue;
                }
            }

            buffer.Append(text[i]);
            i++;
        }

        FlushBuffer();
        return result;
    }

    public static bool TryParseTopicTarget(string target, out string topicId)
    {
        topicId = string.Empty;
        if (string.IsNullOrWhiteSpace(target)) return false;

        var candidate = target.Trim();
        if (candidate.Contains("://", StringComparison.Ordinal) ||
            candidate.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (candidate.StartsWith("topic:", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate["topic:".Length..].Trim();
        }

        var hashIndex = candidate.IndexOf('#');
        if (hashIndex >= 0) candidate = candidate[..hashIndex];

        while (candidate.StartsWith("./", StringComparison.Ordinal) || candidate.StartsWith('/'))
        {
            candidate = candidate.StartsWith("./", StringComparison.Ordinal) ? candidate[2..] : candidate[1..];
        }

        if (candidate.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[..^3];
        }

        topicId = candidate.Trim();
        return true;
    }

    public static IReadOnlyList<LinkInline> ExtractLinks(IReadOnlyList<HelpBlock> blocks)
    {
        var links = new List<LinkInline>();
        foreach (var block in blocks)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    CollectLinks(heading.Inlines, links);
                    break;
                case ParagraphBlock paragraph:
                    CollectLinks(paragraph.Inlines, links);
                    break;
                case ListBlock list:
                    foreach (var item in list.Items) CollectLinks(item, links);
                    break;
                case TableBlock table:
                    foreach (var header in table.Headers) CollectLinks(header, links);
                    foreach (var row in table.Rows)
                        foreach (var cell in row) CollectLinks(cell, links);
                    break;
            }
        }

        return links;
    }

    private static bool TryParseBracketLink(string text, int startIndex, out string label, out string target, out int nextIndex)
    {
        label = string.Empty;
        target = string.Empty;
        nextIndex = startIndex;

        var closeBracket = text.IndexOf(']', startIndex + 1);
        if (closeBracket < 0 || closeBracket + 1 >= text.Length || text[closeBracket + 1] != '(') return false;

        var closeParen = text.IndexOf(')', closeBracket + 2);
        if (closeParen < 0) return false;

        label = text[(startIndex + 1)..closeBracket];
        target = text[(closeBracket + 2)..closeParen].Trim();
        if (label.Length == 0 || target.Length == 0) return false;

        nextIndex = closeParen + 1;
        return true;
    }

    private static bool CanOpenEmphasis(string text, int index) =>
        index + 1 < text.Length &&
        !char.IsWhiteSpace(text[index + 1]) &&
        (text[index] != '_' || index == 0 || !char.IsLetterOrDigit(text[index - 1]));

    private static int FindClosingEmphasis(string text, int searchFrom, char marker)
    {
        for (var i = searchFrom; i < text.Length; i++)
        {
            if (text[i] != marker || char.IsWhiteSpace(text[i - 1])) continue;
            if (marker == '_' && i + 1 < text.Length && char.IsLetterOrDigit(text[i + 1])) continue;
            return i;
        }

        return -1;
    }

    private static void CollectLinks(IReadOnlyList<HelpInline> inlines, List<LinkInline> output)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case LinkInline link:
                    output.Add(link);
                    break;
                case BoldInline bold:
                    CollectLinks(bold.Inlines, output);
                    break;
                case ItalicInline italic:
                    CollectLinks(italic.Inlines, output);
                    break;
            }
        }
    }
}
