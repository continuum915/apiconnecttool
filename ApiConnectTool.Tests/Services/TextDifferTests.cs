using System.Linq;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class TextDifferTests
{
    [Fact]
    public void Diff_IdenticalText_AllLinesUnchanged()
    {
        var result = TextDiffer.Diff("a\nb\nc", "a\nb\nc");

        Assert.All(result, line => Assert.Equal(DiffLineKind.Unchanged, line.Kind));
        Assert.Equal(["a", "b", "c"], result.Select(l => l.Text));
    }

    [Fact]
    public void Diff_AddedLine_MarksOnlyTheNewLineAsAdded()
    {
        var result = TextDiffer.Diff("a\nb", "a\nb\nc");

        Assert.Equal(3, result.Count);
        Assert.Equal(DiffLineKind.Unchanged, result[0].Kind);
        Assert.Equal(DiffLineKind.Unchanged, result[1].Kind);
        Assert.Equal(DiffLineKind.Added, result[2].Kind);
        Assert.Equal("c", result[2].Text);
    }

    [Fact]
    public void Diff_RemovedLine_MarksOnlyTheMissingLineAsRemoved()
    {
        var result = TextDiffer.Diff("a\nb\nc", "a\nc");

        Assert.Equal(3, result.Count);
        Assert.Equal(DiffLineKind.Unchanged, result[0].Kind);
        Assert.Equal(DiffLineKind.Removed, result[1].Kind);
        Assert.Equal("b", result[1].Text);
        Assert.Equal(DiffLineKind.Unchanged, result[2].Kind);
    }

    [Fact]
    public void Diff_ChangedLine_RepresentedAsRemovedThenAdded()
    {
        var result = TextDiffer.Diff("status: pending", "status: complete");

        Assert.Equal(2, result.Count);
        Assert.Equal(DiffLineKind.Removed, result[0].Kind);
        Assert.Equal("status: pending", result[0].Text);
        Assert.Equal(DiffLineKind.Added, result[1].Kind);
        Assert.Equal("status: complete", result[1].Text);
    }

    [Fact]
    public void Diff_EmptyBothSides_ReturnsEmpty()
    {
        var result = TextDiffer.Diff("", "");

        Assert.Empty(result);
    }

    [Fact]
    public void Diff_NullTreatedAsEmpty()
    {
        var result = TextDiffer.Diff(null, "a");

        var line = Assert.Single(result);
        Assert.Equal(DiffLineKind.Added, line.Kind);
        Assert.Equal("a", line.Text);
    }

    [Fact]
    public void Diff_HandlesCrLfAndLfConsistently()
    {
        var result = TextDiffer.Diff("a\r\nb", "a\nb");

        Assert.All(result, line => Assert.Equal(DiffLineKind.Unchanged, line.Kind));
    }

    [Fact]
    public void Diff_SharedPrefixAndSuffix_OnlyMiddleDiffers()
    {
        var result = TextDiffer.Diff("h\nx\ny\nt", "h\nz\nt");

        Assert.Equal(
            [(DiffLineKind.Unchanged, "h"), (DiffLineKind.Removed, "x"), (DiffLineKind.Removed, "y"),
             (DiffLineKind.Added, "z"), (DiffLineKind.Unchanged, "t")],
            result.Select(l => (l.Kind, l.Text)));
    }

    [Fact]
    public void Diff_MiddleOverTableBudget_FallsBackWithoutLargeAllocation()
    {
        // 16 KB of 2-char lines on one side vs a 5 MB-scale pretty-printed body on the other:
        // the full LCS table would be ~8k x 150k cells (several GB).
        var left = string.Join("\n", Enumerable.Range(0, 8_000).Select(i => $"L{i % 10}"));
        var right = string.Join("\n", Enumerable.Range(0, 150_000).Select(i => $"  \"field{i}\": {i},"));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = TextDiffer.Diff(left, right);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 100L * 1024 * 1024, $"Diff allocated {allocated:N0} bytes");
        AssertReconstructs(left, right, result);
    }

    [Fact]
    public void Diff_LargeInputsWithSmallChange_StaysExactViaTrimming()
    {
        var lines = Enumerable.Range(0, 100_000).Select(i => $"line {i}").ToArray();
        var left = string.Join("\n", lines);
        lines[50_000] = "changed";
        var right = string.Join("\n", lines);

        var result = TextDiffer.Diff(left, right);

        Assert.Equal(2, result.Count(l => l.Kind != DiffLineKind.Unchanged));
        Assert.Contains(result, l => l is { Kind: DiffLineKind.Removed, Text: "line 50000" });
        Assert.Contains(result, l => l is { Kind: DiffLineKind.Added, Text: "changed" });
        AssertReconstructs(left, right, result);
    }

    private static void AssertReconstructs(string left, string right, IReadOnlyList<DiffLine> diff)
    {
        Assert.Equal(left, string.Join("\n", diff.Where(l => l.Kind != DiffLineKind.Added).Select(l => l.Text)));
        Assert.Equal(right, string.Join("\n", diff.Where(l => l.Kind != DiffLineKind.Removed).Select(l => l.Text)));
    }
}
