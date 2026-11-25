using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class ProductionSendGuardTests
{
    private static readonly ProductionTarget[] ProductionEnvironment = [new(new RequestEnvironment { Name = "Prod", IsProduction = true })];

    [Fact]
    public async Task ConfirmSendAsync_NeverWarn_ProceedsWithoutPrompting()
    {
        var promptCount = 0;
        var guard = new ProductionSendGuard(() => ProductionWarningMode.NeverWarn, _ => { promptCount++; return Task.FromResult(false); });

        var result = await guard.ConfirmSendAsync(ProductionEnvironment);

        Assert.True(result);
        Assert.Equal(0, promptCount);
    }

    [Fact]
    public async Task ConfirmSendAsync_AlwaysWarn_PromptsEveryTime()
    {
        var promptCount = 0;
        var guard = new ProductionSendGuard(() => ProductionWarningMode.AlwaysWarn, _ => { promptCount++; return Task.FromResult(true); });

        await guard.ConfirmSendAsync(ProductionEnvironment);
        await guard.ConfirmSendAsync(ProductionEnvironment);

        Assert.Equal(2, promptCount);
    }

    [Fact]
    public async Task ConfirmSendAsync_AlwaysWarn_DeclinedPromptBlocksSend()
    {
        var guard = new ProductionSendGuard(() => ProductionWarningMode.AlwaysWarn, _ => Task.FromResult(false));

        var result = await guard.ConfirmSendAsync(ProductionEnvironment);

        Assert.False(result);
    }

    [Fact]
    public async Task ConfirmSendAsync_WarnOncePerSession_PromptsOnceThenAutoProceeds()
    {
        var promptCount = 0;
        var guard = new ProductionSendGuard(() => ProductionWarningMode.WarnOncePerSession, _ => { promptCount++; return Task.FromResult(true); });

        var first = await guard.ConfirmSendAsync(ProductionEnvironment);
        var second = await guard.ConfirmSendAsync(ProductionEnvironment);

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(1, promptCount);
    }

    [Fact]
    public async Task ConfirmSendAsync_WarnOncePerSession_DecliningDoesNotCountAsConfirmed()
    {
        var promptCount = 0;
        var guard = new ProductionSendGuard(() => ProductionWarningMode.WarnOncePerSession, _ => { promptCount++; return Task.FromResult(false); });

        await guard.ConfirmSendAsync(ProductionEnvironment);
        await guard.ConfirmSendAsync(ProductionEnvironment);

        Assert.Equal(2, promptCount);
    }
}
