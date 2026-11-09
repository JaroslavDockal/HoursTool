using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
namespace HoursTool;

/// <summary>
/// Very small Markdown renderer for the built-in user guide (no external dependency).
/// Supported: headings (#, ##, ###), paragraphs, bullet and numbered lists (one nesting level),
/// block quotes (rendered as a tip box), fenced code blocks, pipe tables, horizontal rules and inline
/// <c>**bold**</c>, <c>*italic*</c>, <c>`code`</c> and <c>[text](link)</c> (link shown as text).
/// </summary>
public static class MarkdownView
{
    /// <summary>A rendered heading, used to build the table of contents.</summary>
    public record Heading(int Level, string Title, Control Control);

    static readonly Regex Inline = new(@"(\*\*[^*]+\*\*|`[^`]+`|\*[^*\s][^*]*\*|\[[^\]]+\]\([^)]+\))");
    static readonly Regex Numbered = new(@"^(\s*)(\d+)\.\s+(.*)$");
    static readonly Regex Bullet = new(@"^(\s*)[-*]\s+(.*)$");
    static readonly FontFamily Mono = new("Cascadia Mono,Consolas,Menlo,monospace");

    /// <summary>Renders <paramref name="md"/> into <paramref name="target"/> and returns the headings.</summary>
    public static List<Heading> Render(string md, Panel target)
    {
        target.Children.Clear();
        var heads = new List<Heading>();
        var lines = md.Replace("\r\n", "\n").Split('\n');
        var para = new List<string>();

        void FlushPara()
        {
            if (para.Count == 0) return;
            target.Children.Add(Text(string.Join(" ", para.Select(p => p.Trim())), 14, FontWeight.Normal, new Thickness(0, 2, 0, 6)));
            para.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var t = line.Trim();

            if (t.StartsWith("```"))
            {
                FlushPara();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].Trim().StartsWith("```"); i++) code.Add(lines[i]);
                target.Children.Add(new Border
                {
                    Classes = { "chip" }, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8), Margin = new Thickness(0, 2, 0, 8),
                    Child = new SelectableTextBlock { Text = string.Join("\n", code), FontFamily = Mono, FontSize = 12.5, TextWrapping = TextWrapping.Wrap }
                });
                continue;
            }
            if (t == "") { FlushPara(); continue; }
            if (t.StartsWith("#"))
            {
                FlushPara();
                int lvl = t.TakeWhile(c => c == '#').Count();
                var title = t[lvl..].Trim();
                var (size, weight, margin) = lvl switch
                {
                    1 => (26.0, FontWeight.Bold, new Thickness(0, 0, 0, 8)),
                    2 => (20.0, FontWeight.SemiBold, new Thickness(0, 18, 0, 4)),
                    _ => (16.0, FontWeight.SemiBold, new Thickness(0, 10, 0, 2))
                };
                var h = Text(title, size, weight, margin);
                target.Children.Add(h);
                heads.Add(new Heading(lvl, title, h));
                continue;
            }
            if (t is "---" or "***")
            {
                FlushPara();
                target.Children.Add(new Border { Height = 1, Background = Brushes.Gray, Opacity = 0.3, Margin = new Thickness(0, 10) });
                continue;
            }
            if (t.StartsWith(">"))
            {
                FlushPara();
                var q = new List<string>();
                for (; i < lines.Length && lines[i].Trim().StartsWith(">"); i++) q.Add(lines[i].Trim().TrimStart('>').Trim());
                i--;
                target.Children.Add(new Border
                {
                    Classes = { "chip" }, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10), Margin = new Thickness(0, 4, 0, 8),
                    BorderThickness = new Thickness(4, 0, 0, 0), BorderBrush = new SolidColorBrush(Color.Parse("#6764f6")),
                    Child = Text(string.Join(" ", q), 13.5, FontWeight.Normal, new Thickness(0))
                });
                continue;
            }
            if (t.StartsWith("|"))
            {
                FlushPara();
                var rows = new List<string>();
                for (; i < lines.Length && lines[i].Trim().StartsWith("|"); i++) rows.Add(lines[i].Trim());
                i--;
                target.Children.Add(Table(rows));
                continue;
            }
            Match m;
            if ((m = Bullet.Match(line)).Success || (m = Numbered.Match(line)).Success)
            {
                FlushPara();
                bool num = m.Groups.Count == 4;
                int indent = m.Groups[1].Value.Length >= 2 ? 1 : 0;
                string marker = num ? m.Groups[2].Value + "." : indent == 0 ? "•" : "◦";
                string body = num ? m.Groups[3].Value : m.Groups[2].Value;
                // continuation lines (indented text without marker)
                while (i + 1 < lines.Length && lines[i + 1].StartsWith("  ") && lines[i + 1].Trim() != ""
                       && !Bullet.IsMatch(lines[i + 1]) && !Numbered.IsMatch(lines[i + 1]))
                    body += " " + lines[++i].Trim();
                var dock = new DockPanel { Margin = new Thickness(8 + indent * 20, 1, 0, 3) };
                var mk = new TextBlock { Text = marker, Width = num ? 24 : 16, FontSize = 14, Opacity = 0.7 };
                DockPanel.SetDock(mk, Dock.Left);
                dock.Children.Add(mk);
                dock.Children.Add(Text(body, 14, FontWeight.Normal, new Thickness(0)));
                target.Children.Add(dock);
                continue;
            }
            para.Add(line);
        }
        FlushPara();
        return heads;
    }

    /// <summary>Creates a wrapping text block with inline formatting.</summary>
    public static SelectableTextBlock Text(string text, double size, FontWeight weight, Thickness margin)
    {
        var tb = new SelectableTextBlock { FontSize = size, FontWeight = weight, Margin = margin, TextWrapping = TextWrapping.Wrap };
        tb.Inlines = new InlineCollection();
        foreach (var part in Inline.Split(text))
        {
            if (part == "") continue;
            if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                tb.Inlines.Add(new Run(part[2..^2]) { FontWeight = FontWeight.Bold });
            else if (part.StartsWith("`") && part.EndsWith("`") && part.Length > 2)
                tb.Inlines.Add(new Run(part[1..^1]) { FontFamily = Mono, FontSize = size - 1, Foreground = new SolidColorBrush(Color.Parse("#7a5cff")) });
            else if (part.StartsWith("[") && part.Contains("]("))
                tb.Inlines.Add(new Run(part[1..part.IndexOf("](")]) { TextDecorations = TextDecorations.Underline });
            else if (part.StartsWith("*") && part.EndsWith("*") && part.Length > 2)
                tb.Inlines.Add(new Run(part[1..^1]) { FontStyle = FontStyle.Italic });
            else
                tb.Inlines.Add(new Run(part));
        }
        return tb;
    }

    static Control Table(List<string> rows)
    {
        // "\|" is an escaped pipe inside a cell
        static string[] Cells(string r) => r.Replace("\\|", "\u0001").Trim().Trim('|').Split('|')
                                             .Select(c => c.Trim().Replace("\u0001", "|")).ToArray();
        var data = rows.Where(r => !Regex.IsMatch(r, @"^\|[\s:\-|]+\|?$")).Select(Cells).ToList();
        int cols = data.Max(r => r.Length);
        var g = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        for (int c = 0; c < cols; c++) g.ColumnDefinitions.Add(new ColumnDefinition(c == cols - 1 ? GridLength.Star : GridLength.Auto));
        for (int r = 0; r < data.Count; r++)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int c = 0; c < cols; c++)
            {
                var txt = c < data[r].Length ? data[r][c] : "";
                var cell = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.Parse("#808080")) { Opacity = 0.3 }, BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(8, 5),
                    Child = Text(txt, 13, r == 0 ? FontWeight.SemiBold : FontWeight.Normal, new Thickness(0))
                };
                if (r == 0) cell.Classes.Add("chip");
                if (r == 0) cell.CornerRadius = new CornerRadius(0);
                if (r == 0) cell.Margin = new Thickness(0);
                Grid.SetRow(cell, r); Grid.SetColumn(cell, c);
                g.Children.Add(cell);
            }
        }
        return g;
    }
}
