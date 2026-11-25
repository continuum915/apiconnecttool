using System;
using System.Collections.Generic;

namespace ApiConnectTool.Services;

public enum DiffLineKind
{
    Unchanged,
    Added,
    Removed
}

public sealed record DiffLine(DiffLineKind Kind, string Text);

/// <summary>
/// Minimal line-based diff (classic LCS dynamic-programming table, O(n*m)) - enough for a
/// side-by-side response comparison view without pulling in an external diff library.
/// Callers can pass unbounded text (the comparison view feeds a tab's full live response, up to
/// the configured response limit), so the common prefix and suffix are trimmed first and the LCS
/// table is only built when the remaining middle fits <see cref="MaxTableCells"/>. Past that the
/// middle is reported as one removed block followed by one added block: still a correct diff,
/// just not a minimal one, instead of allocating hundreds of megabytes on the UI thread.
/// </summary>
public static class TextDiffer
{
    /// <summary>Upper bound on LCS table cells (4 bytes each), about 16 MB.</summary>
    public const long MaxTableCells = 4_000_000;

    public static IReadOnlyList<DiffLine> Diff(string? left, string? right)
    {
        var a = SplitLines(left);
        var b = SplitLines(right);

        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix
               && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
        {
            suffix++;
        }

        var result = new List<DiffLine>(a.Length + b.Length - prefix - suffix);
        for (var i = 0; i < prefix; i++)
        {
            result.Add(new DiffLine(DiffLineKind.Unchanged, a[i]));
        }

        var n = a.Length - prefix - suffix;
        var m = b.Length - prefix - suffix;
        if ((long)(n + 1) * (m + 1) <= MaxTableCells)
        {
            DiffMiddle(a, b, prefix, n, m, result);
        }
        else
        {
            for (var i = 0; i < n; i++) result.Add(new DiffLine(DiffLineKind.Removed, a[prefix + i]));
            for (var j = 0; j < m; j++) result.Add(new DiffLine(DiffLineKind.Added, b[prefix + j]));
        }

        for (var i = a.Length - suffix; i < a.Length; i++)
        {
            result.Add(new DiffLine(DiffLineKind.Unchanged, a[i]));
        }

        return result;
    }

    private static void DiffMiddle(string[] a, string[] b, int offset, int n, int m, List<DiffLine> result)
    {
        var lcsLength = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcsLength[i, j] = a[offset + i] == b[offset + j]
                    ? lcsLength[i + 1, j + 1] + 1
                    : Math.Max(lcsLength[i + 1, j], lcsLength[i, j + 1]);
            }
        }

        var x = 0;
        var y = 0;
        while (x < n && y < m)
        {
            if (a[offset + x] == b[offset + y])
            {
                result.Add(new DiffLine(DiffLineKind.Unchanged, a[offset + x]));
                x++;
                y++;
            }
            else if (lcsLength[x + 1, y] >= lcsLength[x, y + 1])
            {
                result.Add(new DiffLine(DiffLineKind.Removed, a[offset + x]));
                x++;
            }
            else
            {
                result.Add(new DiffLine(DiffLineKind.Added, b[offset + y]));
                y++;
            }
        }

        while (x < n)
        {
            result.Add(new DiffLine(DiffLineKind.Removed, a[offset + x]));
            x++;
        }

        while (y < m)
        {
            result.Add(new DiffLine(DiffLineKind.Added, b[offset + y]));
            y++;
        }
    }

    private static string[] SplitLines(string? text) =>
        string.IsNullOrEmpty(text) ? [] : text.Replace("\r\n", "\n").Split('\n');
}
