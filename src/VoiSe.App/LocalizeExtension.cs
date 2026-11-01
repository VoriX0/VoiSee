using Microsoft.UI.Xaml.Markup;

namespace VoiSe.App;

public sealed class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    protected override object ProvideValue() => UiText.L(Key);
}
