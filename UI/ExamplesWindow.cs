using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace HoursTool;

/// <summary>
/// Non-modal window with ready-made rule snippets (<see cref="ScriptExamples"/>) for the advanced rule editor.
/// A snippet can be inserted into the script at the caret or copied to the clipboard.
/// </summary>
public class ExamplesWindow : Window
{
    readonly Action<string> _insert;
    readonly ListBox _list = new() { Background = Brushes.Transparent };
    readonly TextBox _search = new();
    readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _desc = new() { TextWrapping = TextWrapping.Wrap, Classes = { "lbl" } };
    readonly SelectableTextBlock _code = new()
    {
        FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"), FontSize = 13, TextWrapping = TextWrapping.NoWrap
    };
    ScriptExamples.Example? _current;

    /// <summary>Creates the window; <paramref name="insert"/> receives the snippet to insert into the script.</summary>
    public ExamplesWindow(Action<string> insert)
    {
        _insert = insert;
        Title = L.T("ex.window");
        Width = 980; Height = 640; MinWidth = 700; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _search.Watermark = L.T("f.search");
        _search.TextChanged += (_, _) => Fill();
        _list.SelectionChanged += (_, _) => Show((_list.SelectedItem as ListBoxItem)?.Tag as ScriptExamples.Example);

        var left = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(_search, Dock.Top);
        _search.Margin = new Thickness(0, 0, 0, 6);
        left.Children.Add(_search);
        left.Children.Add(_list);
        var leftBorder = new Border { Child = left, Classes = { "side" }, Width = 330 };

        var insertBtn = new Button { Content = L.T("ex.insert"), Classes = { "accent" } };
        insertBtn.Click += (_, _) => { if (_current != null) _insert(_current.Code); };
        var copyBtn = new Button { Content = L.T("tb.copy") };
        copyBtn.Click += async (_, _) => { if (_current != null && Clipboard != null) await Clipboard.SetTextAsync(_current.Code); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(insertBtn); buttons.Children.Add(copyBtn);

        var codeBox = new Border
        {
            Classes = { "chip" }, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10), Margin = new Thickness(0),
            Child = new ScrollViewer { Content = _code, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }
        };
        var right = new StackPanel { Margin = new Thickness(20, 16), Spacing = 10 };
        right.Children.Add(_title);
        right.Children.Add(_desc);
        right.Children.Add(codeBox);
        right.Children.Add(buttons);
        right.Children.Add(new TextBlock { Text = L.T("ex.note"), Classes = { "hint" }, Margin = new Thickness(0, 8, 0, 0) });

        var root = new DockPanel();
        DockPanel.SetDock(leftBorder, Dock.Left);
        root.Children.Add(leftBorder);
        root.Children.Add(new ScrollViewer { Content = right });
        Content = root;
        Fill();
    }

    void Fill()
    {
        var q = _search.Text?.Trim() ?? "";
        _list.Items.Clear();
        foreach (var g in ScriptExamples.All.GroupBy(e => e.Group))
        {
            var items = g.Where(e => q == "" || (e.Title + " " + e.Description + " " + e.Code).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (items.Count == 0) continue;
            _list.Items.Add(new ListBoxItem
            {
                Content = ScriptExamples.GroupTitle(g.Key), IsEnabled = false, FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(6, 8, 6, 2)
            });
            foreach (var e in items)
                _list.Items.Add(new ListBoxItem { Content = e.Title, Tag = e, Padding = new Thickness(16, 5, 6, 5) });
        }
        var first = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag != null);
        _list.SelectedItem = first;
        if (first == null) Show(null);
    }

    void Show(ScriptExamples.Example? e)
    {
        _current = e;
        _title.Text = e?.Title ?? "";
        _desc.Text = e?.Description ?? "";
        _code.Text = e?.Code ?? "";
    }
}
