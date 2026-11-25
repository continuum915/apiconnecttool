using Avalonia.Controls;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public sealed class TokenActionsHeadlessTests
{
    [Fact]
    public Task TokenButtonsFollowTheGrantType() => HeadlessSession.RunAsync(() =>
    {
        var auth = new RequestAuth { Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials };
        var view = new AuthEditorControl { DataContext = new AuthEditorViewModel(auth) };
        var window = new Window { Content = view };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.True(IsShown(view, "AuthorizeButton"));
            Assert.True(IsShown(view, "GetNewTokenButton"));
            Assert.True(IsShown(view, "ClearTokenButton"));

            auth.GrantType = OAuthGrantType.AuthorizationCodePkce;
            window.UpdateLayout();
            Assert.True(IsShown(view, "AuthorizeButton"));
            Assert.False(IsShown(view, "GetNewTokenButton"));
            Assert.True(IsShown(view, "ClearTokenButton"));

            auth.Type = AuthType.Bearer;
            window.UpdateLayout();
            Assert.False(IsShown(view, "ClearTokenButton"));
        }
        finally { window.Close(); }
    });

    private static bool IsShown(Control root, string name)
    {
        var button = root.GetVisualDescendants().OfType<Button>().Single(candidate => candidate.Name == name);
        return button.IsEffectivelyVisible;
    }
}
