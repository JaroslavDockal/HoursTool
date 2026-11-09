using Avalonia.Data;
using Avalonia.Markup.Xaml;
namespace HoursTool;

/// <summary>
/// XAML markup extension for localised texts: <c>Text="{l:T menu.open}"</c>.
/// Produces a one-way binding to <see cref="L.I"/>'s indexer, so texts update live when the language changes.
/// </summary>
public class T : MarkupExtension
{
    /// <summary>Creates the extension for a translation key.</summary>
    public T(string key) { Key = key; }

    /// <summary>Translation key (see <see cref="Strings"/>).</summary>
    public string Key { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = L.I, Mode = BindingMode.OneWay };
}
