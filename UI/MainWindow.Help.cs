using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
namespace HoursTool;

public partial class MainWindow
{
    /// <summary>Language of the rendered guide ("" = not rendered yet).</summary>
    string _helpLang = "";
    List<MarkdownView.Heading> _helpHeads = new();

    /// <summary>
    /// Renders the user guide (embedded <c>docs/USER_GUIDE.&lt;lang&gt;.md</c>) when the Help tab is first shown
    /// or the language changed, and fills the table of contents.
    /// </summary>
    void EnsureHelp()
    {
        if (_helpLang == L.Lang) return;
        _helpLang = L.Lang;
        string md;
        try
        {
            using var s = AssetLoader.Open(new Uri($"avares://HoursTool/docs/USER_GUIDE.{L.Lang}.md"));
            using var rd = new StreamReader(s);
            md = rd.ReadToEnd();
        }
        catch (Exception ex) { md = "# Help\n\n" + ex.Message; }

        _helpHeads = MarkdownView.Render(md, PnlHelp);
        LstHelpToc.SelectionChanged -= OnHelpToc;
        LstHelpToc.Items.Clear();
        foreach (var h in _helpHeads.Where(h => h.Level <= 2))
            LstHelpToc.Items.Add(new ListBoxItem
            {
                Content = h.Title, Tag = h,
                FontWeight = h.Level == 1 ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal,
                Padding = new Thickness(h.Level == 1 ? 8 : 16, 5, 8, 5)
            });
        LstHelpToc.SelectionChanged += OnHelpToc;
        HelpScroll.Offset = new Vector(0, 0);
    }

    void OnHelpToc(object? sender, SelectionChangedEventArgs e)
    {
        if ((LstHelpToc.SelectedItem as ListBoxItem)?.Tag is not MarkdownView.Heading h) return;
        var p = h.Control.TranslatePoint(new Point(0, 0), PnlHelp);
        if (p != null) HelpScroll.Offset = new Vector(0, Math.Max(0, p.Value.Y - 8));
    }
}
