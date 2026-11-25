using System;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace ApiConnectTool.Views.Components;

/// <summary>Shows text as saved, with each <c>{{variable}}</c> token in the
/// <c>ApiConnectTool.Brush.Variable</c> color. The token color follows theme switches.</summary>
public sealed partial class TemplateText : TextBlock
{
    public static readonly StyledProperty<string?> TemplateProperty =
        AvaloniaProperty.Register<TemplateText, string?>(nameof(Template));

    static TemplateText()
    {
        TemplateProperty.Changed.AddClassHandler<TemplateText>((control, _) => control.Rebuild());
    }

    public string? Template { get => GetValue(TemplateProperty); set => SetValue(TemplateProperty, value); }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    [GeneratedRegex(@"\{\{[^{}]*\}\}")]
    private static partial Regex Token();

    private void Rebuild()
    {
        var inlines = new InlineCollection();
        var text = Template ?? string.Empty;
        var position = 0;
        foreach (Match match in Token().Matches(text))
        {
            if (match.Index > position) inlines.Add(new Run(text[position..match.Index]));
            var token = new Run(match.Value);
            token.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("ApiConnectTool.Brush.Variable"));
            inlines.Add(token);
            position = match.Index + match.Length;
        }
        if (position < text.Length) inlines.Add(new Run(text[position..]));
        Inlines = inlines;
    }
}
