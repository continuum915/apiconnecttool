using System.Text;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class HistoryRequestBodyTests
{
    [Fact]
    public void Cap_BodyUnderTheCap_IsKeptWhole()
    {
        var body = new string('a', HistoryRequestBody.MaxBytes - 1);

        var (stored, truncated) = HistoryRequestBody.Cap(body);

        Assert.Equal(body, stored);
        Assert.False(truncated);
    }

    [Fact]
    public void Cap_BodyExactlyAtTheCap_IsKeptWhole()
    {
        var body = new string('a', HistoryRequestBody.MaxBytes);

        var (stored, truncated) = HistoryRequestBody.Cap(body);

        Assert.Equal(body, stored);
        Assert.False(truncated);
    }

    [Fact]
    public void Cap_MultiByteBodyExactlyAtTheCap_IsKeptWhole()
    {
        var body = new string('é', HistoryRequestBody.MaxBytes / 2);

        var (stored, truncated) = HistoryRequestBody.Cap(body);

        Assert.Equal(body, stored);
        Assert.False(truncated);
    }

    [Fact]
    public void Cap_BodyOverTheCap_IsCutToTheCapAndFlagged()
    {
        var body = new string('a', HistoryRequestBody.MaxBytes + 10);

        var (stored, truncated) = HistoryRequestBody.Cap(body);

        Assert.Equal(HistoryRequestBody.MaxBytes, Encoding.UTF8.GetByteCount(stored));
        Assert.StartsWith(stored, body);
        Assert.True(truncated);
    }

    [Fact]
    public void Cap_MultiByteCharacterStraddlingTheCap_IsDroppedWhole()
    {
        // 'é' is 2 UTF-8 bytes; placed so it would end one byte past the cap.
        var body = new string('a', HistoryRequestBody.MaxBytes - 1) + "é" + "tail";

        var (stored, truncated) = HistoryRequestBody.Cap(body);

        Assert.Equal(new string('a', HistoryRequestBody.MaxBytes - 1), stored);
        Assert.True(truncated);
    }

    [Fact]
    public void Cap_SurrogatePairStraddlingTheCap_IsNeverSplit()
    {
        // U+10348 is a surrogate pair in UTF-16 and 4 bytes in UTF-8.
        var body = new string('a', HistoryRequestBody.MaxBytes - 2) + "\U00010348" + "tail";

        var (stored, truncated) = HistoryRequestBody.Cap(body);

        Assert.Equal(new string('a', HistoryRequestBody.MaxBytes - 2), stored);
        Assert.False(char.IsHighSurrogate(stored[^1]));
        Assert.True(truncated);
    }

    [Fact]
    public void Cap_MultiByteCharactersFillingTheCapExactly_AreKeptWhole()
    {
        // 3-byte characters: the cap is not a multiple of 3, so the boundary sits mid-character.
        var body = new string('\u20AC', HistoryRequestBody.MaxBytes / 3 + 1);

        var (stored, truncated) = HistoryRequestBody.Cap(body);

        Assert.Equal(HistoryRequestBody.MaxBytes / 3, stored.Length);
        Assert.True(Encoding.UTF8.GetByteCount(stored) <= HistoryRequestBody.MaxBytes);
        Assert.True(truncated);
    }
}
