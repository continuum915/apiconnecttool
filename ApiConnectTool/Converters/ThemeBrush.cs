using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace ApiConnectTool.Converters;

/// <summary>Looks up a <c>ApiConnectTool.Brush.*</c> token for the active theme variant, so converters and
/// code-behind map semantic state to colors without carrying their own palette.</summary>
public static class ThemeBrush
{
    public static IBrush? Find(string key, IResourceHost? scope = null)
    {
        var app = Application.Current;
        if (app is null) return null;
        var variant = (scope as IThemeVariantHost)?.ActualThemeVariant ?? app.ActualThemeVariant;
        if (scope is not null && scope.TryFindResource(key, variant, out var scoped) && scoped is IBrush scopedBrush)
            return scopedBrush;
        return app.TryFindResource(key, variant, out var resource) ? resource as IBrush : null;
    }
}
