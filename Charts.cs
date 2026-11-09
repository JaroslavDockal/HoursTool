using ScottPlot;
using AvaPlot = ScottPlot.Avalonia.AvaPlot;
namespace HoursTool;

public static class Charts
{
    public static readonly string[] Palette =
        { "#6764f6", "#FF000F", "#93a1ff", "#ff957e", "#3fb68b", "#e0a800", "#00a3e0", "#b565a7" };
    public static Color Col(int i) => Color.FromHex(Palette[((i % Palette.Length) + Palette.Length) % Palette.Length]);
    public static readonly Color Grey = Color.FromHex("#9AA0A6");

    static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Color((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    static void Style(Plot p, bool dark, string title, string ylabel)
    {
        var fig = Color.FromHex(dark ? "#26272E" : "#FFFFFF");
        var data = Color.FromHex(dark ? "#1E1F25" : "#FAFAFD");
        var fg = Color.FromHex(dark ? "#D8D9E0" : "#2A2B33");
        p.FigureBackground.Color = fig;
        p.DataBackground.Color = data;
        p.Axes.Color(fg);
        p.Grid.MajorLineColor = fg.WithOpacity(dark ? 0.12 : 0.10);
        p.Legend.BackgroundColor = fig;
        p.Legend.FontColor = fg;
        p.Legend.OutlineColor = fg.WithOpacity(0.25);
        p.Legend.FontSize = 12;
        p.Title(title);
        p.Axes.Title.Label.FontSize = 15;
        p.Axes.Title.Label.Bold = true;
        p.Axes.Title.Label.ForeColor = fg;
        p.YLabel(ylabel);
        foreach (IAxis ax in new IAxis[] { p.Axes.Left, p.Axes.Bottom })
        {
            ax.TickLabelStyle.FontSize = 12;
            ax.Label.FontSize = 13;
        }
    }

    // oddelovac tisicu na ciselne ose
    static void NumFmt(IAxis ax)
    {
        ax.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
        {
            LabelFormatter = v => v.ToString("#,0.##")
        };
    }

    static void XTicks(Plot p, double[] pos, string[] labels)
    {
        var ticks = pos.Select((x, i) => new Tick(x, labels[i])).ToArray();
        p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);
        p.Axes.Bottom.TickLabelStyle.Rotation = labels.Length > 12 ? -45 : 0;
    }

    static double[] Idx(int n) => Enumerable.Range(0, n).Select(i => (double)i).ToArray();

    // ---------- skladane sloupce po obdobich ----------
    public static void Stacked(AvaPlot ava, bool dark, string title, string[] labels,
                               List<(string name, double[] vals, Color col)> series, string ylabel = "Hodiny")
    {
        var p = ava.Plot; p.Clear();
        if (labels.Length == 0) { Empty(ava, dark, title); return; }
        var b = new double[labels.Length];
        foreach (var (name, vals, col) in series)
        {
            var bars = new List<Bar>();
            for (int i = 0; i < labels.Length; i++)
            {
                if (vals[i] <= 0) continue;
                bars.Add(new Bar { Position = i, ValueBase = b[i], Value = b[i] + vals[i], FillColor = col, Size = 0.7 });
                b[i] += vals[i];
            }
            if (bars.Count == 0) continue;
            var bp = p.Add.Bars(bars);
            bp.LegendText = name;
        }
        XTicks(p, Idx(labels.Length), labels);
        NumFmt(p.Axes.Left);
        Style(p, dark, title, ylabel);
        p.ShowLegend(Alignment.UpperLeft);
        p.Axes.Margins(bottom: 0, top: 0.15);
        ava.Refresh();
    }

    // ---------- cary: osa X = mesice 1..12, serie = roky ----------
    public static void Lines(AvaPlot ava, bool dark, string title, string ylabel,
                             List<(string name, double[] xs, double[] ys, Color col)> series)
    {
        var p = ava.Plot; p.Clear();
        foreach (var (name, xs, ys, col) in series)
        {
            if (xs.Length == 0) continue;
            var s = p.Add.Scatter(xs, ys);
            s.LegendText = name; s.Color = col; s.LineWidth = 3f; s.MarkerSize = 8;
        }
        var mn = new[] { "led", "úno", "bře", "dub", "kvě", "čvn", "čvc", "srp", "zář", "říj", "lis", "pro" };
        XTicks(p, Enumerable.Range(1, 12).Select(i => (double)i).ToArray(), mn);
        NumFmt(p.Axes.Left);
        Style(p, dark, title, ylabel);
        p.ShowLegend(Alignment.UpperLeft);
        p.Axes.Margins(bottom: 0, top: 0.15);
        ava.Refresh();
    }

    // ---------- cary: osa X = obdobi, vice serii ----------
    public static void Series(AvaPlot ava, bool dark, string title, string ylabel, string[] labels,
                              List<(string name, double[] vals, Color col)> series)
    {
        var p = ava.Plot; p.Clear();
        if (labels.Length == 0) { Empty(ava, dark, title); return; }
        var xs = Idx(labels.Length);
        foreach (var (name, vals, col) in series)
        {
            var s = p.Add.Scatter(xs, vals);
            s.LegendText = name; s.Color = col; s.LineWidth = 3f; s.MarkerSize = 7;
        }
        XTicks(p, xs, labels);
        NumFmt(p.Axes.Left);
        Style(p, dark, title, ylabel);
        p.ShowLegend(Alignment.UpperLeft);
        p.Axes.Margins(bottom: 0, top: 0.15);
        ava.Refresh();
    }

    // ---------- trend + klouzavy prumer + prognoza ----------
    public static void Trend(AvaPlot ava, bool dark, string title, string ylabel, string[] labels, double[] y,
                             bool ma, bool trend, int forecast, Color col)
    {
        var p = ava.Plot; p.Clear();
        int n = y.Length;
        if (n == 0) { Empty(ava, dark, title); return; }
        var xs = Idx(n);
        var s = p.Add.Scatter(xs, y);
        s.LegendText = "Skutečnost"; s.Color = col; s.LineWidth = 3f; s.MarkerSize = 9;

        if (ma && n >= 3)
        {
            var s2 = p.Add.Scatter(xs, Calc.Moving(y, 3));
            s2.LegendText = "Klouzavý průměr (3 měs.)"; s2.Color = Color.FromHex("#e0a800");
            s2.LineWidth = 2.5f; s2.MarkerSize = 0;
        }

        var all = labels.ToList();
        if (trend && n >= 3)
        {
            var (a, b, _) = Calc.LinReg(y);
            int last = n - 1 + forecast;
            var s3 = p.Add.Scatter(new[] { 0.0, (double)last }, new[] { b, b + a * last });
            s3.LegendText = forecast > 0 ? $"Lineární trend (+{forecast} měs. prognóza)" : "Lineární trend";
            s3.Color = Grey; s3.LineWidth = 2.5f; s3.MarkerSize = 0; s3.LinePattern = LinePattern.Dashed;
            string lp = labels[^1];
            for (int i = 0; i < forecast; i++) { lp = Calc.NextPeriod(lp); all.Add(lp); }
        }
        XTicks(p, Idx(all.Count), all.ToArray());
        NumFmt(p.Axes.Left);
        Style(p, dark, title, ylabel);
        p.ShowLegend(Alignment.UpperLeft);
        p.Axes.Margins(bottom: 0, top: 0.15);
        ava.Refresh();
    }

    // ---------- skupinove sloupce: kategorie x serie (roky) ----------
    public static void Grouped(AvaPlot ava, bool dark, string title, string[] cats,
                               List<(string name, double[] vals, Color col)> series, string ylabel)
    {
        var p = ava.Plot; p.Clear();
        int n = Math.Max(series.Count, 1);
        double w = 0.8 / n;
        for (int s = 0; s < series.Count; s++)
        {
            var bars = new List<Bar>();
            double off = (s - (n - 1) / 2.0) * w;
            for (int i = 0; i < cats.Length; i++)
                bars.Add(new Bar { Position = i + off, Value = series[s].vals[i], FillColor = series[s].col, Size = w * 0.92 });
            var bp = p.Add.Bars(bars);
            bp.LegendText = series[s].name;
        }
        XTicks(p, Idx(cats.Length), cats);
        NumFmt(p.Axes.Left);
        Style(p, dark, title, ylabel);
        p.ShowLegend(Alignment.UpperRight);
        p.Axes.Margins(bottom: 0, top: 0.15);
        ava.Refresh();
    }

    // ---------- vodorovne skladane sloupce (zebricek) ----------
    public static void HStacked(AvaPlot ava, bool dark, string title, string[] labels,
                                List<(string name, double[] vals, Color col)> series, string xlabel)
    {
        var p = ava.Plot; p.Clear();
        if (labels.Length == 0) { Empty(ava, dark, title); return; }
        var b = new double[labels.Length];
        foreach (var (name, vals, col) in series)
        {
            var bars = new List<Bar>();
            for (int i = 0; i < labels.Length; i++)
            {
                if (vals[i] <= 0) continue;
                bars.Add(new Bar { Position = i, ValueBase = b[i], Value = b[i] + vals[i], FillColor = col, Size = 0.7 });
                b[i] += vals[i];
            }
            if (bars.Count == 0) continue;
            var bp = p.Add.Bars(bars);
            bp.Horizontal = true;
            bp.LegendText = name;
        }
        var ticks = Idx(labels.Length).Select((x, i) => new Tick(x, labels[i])).ToArray();
        p.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);
        NumFmt(p.Axes.Bottom);
        Style(p, dark, title, "");
        p.XLabel(xlabel);
        p.ShowLegend(Alignment.LowerRight);
        p.Axes.Margins(left: 0, right: 0.1);
        ava.Refresh();
    }

    // ---------- donut ----------
    public static void Pie(AvaPlot ava, bool dark, List<(string label, double value, Color col)> items)
    {
        var p = ava.Plot; p.Clear();
        if (items.Count == 0) { Empty(ava, dark, ""); return; }
        var slices = items.Select(x => new PieSlice { Value = x.value, FillColor = x.col, Label = "", LegendText = x.label }).ToList();
        var pie = p.Add.Pie(slices);
        pie.DonutFraction = 0.55;
        Style(p, dark, "", "");
        p.FigureBackground.Color = Color.FromHex(dark ? "#26272E" : "#FFFFFF");
        p.Axes.Frameless();
        p.HideGrid();
        p.ShowLegend(Alignment.MiddleRight);
        ava.Refresh();
    }

    // ---------- heatmapa osoba x obdobi ----------
    public static void Heat(AvaPlot ava, bool dark, string title, string[] rowsL, string[] cols, double[,] v, bool pct)
    {
        var p = ava.Plot; p.Clear();
        int R = rowsL.Length, C = cols.Length;
        if (R == 0 || C == 0) { Empty(ava, dark, title); return; }
        double max = 0;
        for (int r = 0; r < R; r++) for (int c = 0; c < C; c++) max = Math.Max(max, v[r, c]);
        if (pct) max = 100;
        if (max <= 0) max = 1;

        var fig = Color.FromHex(dark ? "#26272E" : "#FFFFFF");
        var lo = Color.FromHex(dark ? "#2d2b55" : "#e4e7ff");
        var hi = Color.FromHex(dark ? "#a9a6ff" : "#3b38c8");
        var zero = Color.FromHex(dark ? "#2e2f38" : "#f2f2f6");
        var white = Color.FromHex("#FFFFFF"); var black = Color.FromHex("#111111");
        bool labelsOn = R * C <= 700;

        for (int r = 0; r < R; r++)
            for (int c = 0; c < C; c++)
            {
                double val = v[r, c], t = Math.Clamp(val / max, 0, 1);
                double y0 = R - 1 - r;
                var rect = p.Add.Rectangle(c, c + 1, y0, y0 + 1);
                rect.FillColor = val == 0 ? zero : Lerp(lo, hi, t);
                rect.LineColor = fig; rect.LineWidth = 1;
                if (labelsOn && val != 0)
                {
                    var tx = p.Add.Text($"{val:0}", c + 0.5, y0 + 0.5);
                    tx.LabelFontSize = 10;
                    tx.LabelFontColor = dark ? (t > 0.5 ? black : white) : (t > 0.5 ? white : black);
                    tx.LabelAlignment = Alignment.MiddleCenter;
                }
            }

        var yt = Enumerable.Range(0, R).Select(r => new Tick(R - 1 - r + 0.5, rowsL[r])).ToArray();
        p.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(yt);
        XTicks(p, Enumerable.Range(0, C).Select(c => c + 0.5).ToArray(), cols);
        Style(p, dark, title, "");
        p.Axes.SetLimits(0, C, 0, R);
        ava.Refresh();
    }

    public static void Empty(AvaPlot ava, bool dark, string title)
    {
        ava.Plot.Clear(); Style(ava.Plot, dark, title, ""); ava.Refresh();
    }
}
