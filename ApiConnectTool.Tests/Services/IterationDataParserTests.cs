using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class IterationDataParserTests
{
    [Fact]
    public void Csv_HandlesQuotedCommasQuotesAndNewlines()
    {
        var rows = IterationDataParser.Parse("csv", "name,value\r\n\"a,b\",\"say \"\"hello\"\"\"\r\n\"two\r\nlines\",next\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal("a,b", rows[0]["name"]);
        Assert.Equal("say \"hello\"", rows[0]["value"]);
        Assert.Equal("two\r\nlines", rows[1]["name"]);
    }

    [Fact]
    public void Json_AcceptsFlatScalarObjects()
    {
        var rows = IterationDataParser.Parse("json", "[{\"s\":\"x\",\"n\":12.5,\"b\":true}]");
        Assert.Equal("x", rows[0]["s"]);
        Assert.Equal("12.5", rows[0]["n"]);
        Assert.Equal("true", rows[0]["b"]);
    }

    [Theory]
    [InlineData("csv", "a,b\n1", "line 2")]
    [InlineData("csv", "a,b\n\"unterminated", "line 2")]
    [InlineData("json", "[{\"x\":null}]", "element 1")]
    [InlineData("json", "[{\"x\":[]}]", "element 1")]
    [InlineData("json", "[1]", "element 1")]
    public void MalformedData_IdentifiesLocation(string format, string input, string location)
    {
        var error = Assert.Throws<FormatException>(() => IterationDataParser.Parse(format, input));
        Assert.Contains(location, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
