using Avalonia.Data;
using ApiConnectTool.Converters;
using ApiConnectTool.Models;
using Xunit;

namespace ApiConnectTool.Tests.Converters;

public class EnumIndexConverterTests
{
    private readonly EnumIndexConverter _converter = new();

    [Fact]
    public void ConvertBack_ValidIndex_ReturnsEnumValue()
    {
        var result = _converter.ConvertBack(1, typeof(ProductionWarningMode), null, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(ProductionWarningMode.WarnOncePerSession, result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void ConvertBack_OutOfRangeIndex_DoesNotProduceUndefinedEnumValue(int index)
    {
        var result = _converter.ConvertBack(index, typeof(ProductionWarningMode), null, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(BindingOperations.DoNothing, result);
    }

    [Fact]
    public void Convert_NullValue_ReturnsZero()
    {
        var result = _converter.Convert(null, typeof(int), null, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(0, result);
    }
}
