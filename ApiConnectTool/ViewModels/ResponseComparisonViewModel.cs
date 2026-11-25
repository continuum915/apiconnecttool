using System.Collections.Generic;
using ApiConnectTool.Services;

namespace ApiConnectTool.ViewModels;

/// <summary>Backs the response comparison dialog: two snapshots plus their line-level diffs.</summary>
public sealed class ResponseComparisonViewModel
{
    public ResponseSnapshot Left { get; }
    public ResponseSnapshot Right { get; }
    public IReadOnlyList<DiffLine> HeaderDiff { get; }
    public IReadOnlyList<DiffLine> BodyDiff { get; }

    public ResponseComparisonViewModel(ResponseSnapshot left, ResponseSnapshot right)
    {
        Left = left;
        Right = right;
        HeaderDiff = TextDiffer.Diff(left.HeadersText, right.HeadersText);
        BodyDiff = TextDiffer.Diff(left.BodyText, right.BodyText);
    }
}
