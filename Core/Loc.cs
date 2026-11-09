using System.ComponentModel;
using System.Globalization;
namespace HoursTool;

/// <summary>
/// Minimal localisation service (English / Czech).
/// <para>
/// Code uses <c>L.T("key")</c> or <c>L.T("key", args)</c> (composite format). XAML uses the
/// <c>{l:T key}</c> markup extension, which binds to the indexer <c>L.I[key]</c>; changing
/// <see cref="Lang"/> raises <see cref="PropertyChanged"/> for the indexer so all bound texts update live.
/// Code-generated texts are rebuilt by the window on the <see cref="Changed"/> event.
/// </para>
/// Strings live in <see cref="Strings"/>. Missing keys are returned as <c>[key]</c> so they are easy to spot.
/// </summary>
public sealed partial class L : INotifyPropertyChanged
{
    /// <summary>Singleton instance used as binding source.</summary>
    public static L I { get; } = new();

    /// <summary>Supported language codes.</summary>
    public static readonly string[] Languages = { "en", "cs" };

    string _lang = "en";

    /// <summary>Raised for indexer bindings when the language changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language has changed (for code-generated texts).</summary>
    public static event Action? Changed;

    /// <summary>Current language code (<c>en</c> or <c>cs</c>).</summary>
    public static string Lang
    {
        get => I._lang;
        set
        {
            var v = value == "cs" ? "cs" : "en";
            // number/date formatting follows the UI language (1 234,5 vs 1,234.5)
            var culture = CultureInfo.GetCultureInfo(v == "cs" ? "cs-CZ" : "en-GB");
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            if (v == I._lang) return;
            I._lang = v;
            I.PropertyChanged?.Invoke(I, new PropertyChangedEventArgs("Item"));
            I.PropertyChanged?.Invoke(I, new PropertyChangedEventArgs("Item[]"));
            Changed?.Invoke();
        }
    }

    /// <summary>Default language derived from the OS UI culture (Czech/Slovak → cs, otherwise en).</summary>
    public static string SystemDefault =>
        CultureInfo.InstalledUICulture.TwoLetterISOLanguageName is "cs" or "sk" ? "cs" : "en";

    /// <summary>Indexer used by XAML bindings.</summary>
    public string this[string key] => T(key);

    /// <summary>Returns the translation of <paramref name="key"/> in the current language.</summary>
    public static string T(string key)
    {
        if (Strings.Table.TryGetValue(key, out var s)) return Lang == "cs" ? s.cs : s.en;
        return "[" + key + "]";
    }

    /// <summary>Returns the formatted translation of <paramref name="key"/>.</summary>
    public static string T(string key, params object?[] args)
    {
        try { return string.Format(CultureInfo.CurrentCulture, T(key), args); }
        catch (FormatException) { return T(key); }
    }

    /// <summary>True when a translation exists for the key.</summary>
    public static bool Has(string key) => Strings.Table.ContainsKey(key);

    /// <summary>Localised month abbreviations (index 0 = January).</summary>
    public static string[] MonthsShort => Lang == "cs"
        ? new[] { "led", "úno", "bře", "dub", "kvě", "čvn", "čvc", "srp", "zář", "říj", "lis", "pro" }
        : new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
}
