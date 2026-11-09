using SColor = ScottPlot.Color;
namespace HoursTool;

public partial class MainWindow
{
    static SColor SplitCol(string shortName) => shortName switch
    {
        "FI NB" => SColor.FromHex("#6764f6"),
        "FI P" => SColor.FromHex("#b3bcff"),
        _ => SColor.FromHex("#2f2da8")
    };

    List<(string name, double[] vals, SColor col)> StackSeries(List<Row> rows, string[] keys, Func<Row, string> keyOf)
    {
        var idx = keys.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);
        bool split = ChkFiSplit.IsChecked == true;
        var res = new List<(string name, double[] vals, SColor col)>();

        void Add(Metric m, SColor col)
        {
            var v = new double[keys.Length];
            foreach (var r in rows) if (m.Pred(r) && idx.TryGetValue(keyOf(r), out var i)) v[i] += V(r);
            res.Add((m.Name, v, col));
        }

        foreach (var m in _metrics.Where(m => m.Group == "cust"))
        {
            if (split && m.Short == "FI")
                foreach (var s in _metrics.Where(x => x.Group == "nb")) Add(s, SplitCol(s.Short));
            else
                Add(m, CustCol(m.Name));
        }
        if (!Czk)
        {
            var o = new double[keys.Length];
            foreach (var r in rows) if (r.Overhead && idx.TryGetValue(keyOf(r), out var i)) o[i] += r.Hours;
            res.Add(("Overhead (neinvoiced)", o, Charts.Grey));
        }
        return res;
    }

    string[] PeriodsOf(List<Row> rows) => rows.Select(r => r.Period).Distinct().OrderBy(x => x).ToArray();

    // ---------- prubeh v case ----------
    void BuildTimeChart(List<Row> rows)
    {
        var periods = PeriodsOf(rows);
        if (periods.Length == 0) { Charts.Empty(PlotTime, Dark, "Žádná data"); return; }
        Charts.Stacked(PlotTime, Dark, "Po měsících: invoiced podle zákazníka + overhead", periods,
                       StackSeries(rows, periods, r => r.Period), Czk ? "CZK" : "Hodiny");
    }

    // ---------- trendy ----------
    void BuildTrend(List<Row> rows)
    {
        var name = CmbTrend.SelectedItem?.ToString();
        if (name == null) return;
        var periods = PeriodsOf(rows);
        if (periods.Length == 0) { Charts.Empty(PlotTrend, Dark, "Žádná data"); TxtTrend.Text = ""; return; }
        var byP = rows.GroupBy(r => r.Period).ToDictionary(g => g.Key, g => g.ToList());
        var y = periods.Select(p => Eval(name, byP[p])).ToArray();
        int fc = new[] { 0, 1, 3, 6 }[Math.Max(0, CmbFc.SelectedIndex)];
        string unit = UnitOf(name);

        Charts.Trend(PlotTrend, Dark, $"{name} – trend", unit, periods, y,
                     ChkMa.IsChecked == true, ChkTrend.IsChecked == true, fc, Charts.Col(0));

        int n = y.Length;
        var sb = new System.Text.StringBuilder();
        sb.Append($"Průměr: {Fmt(name, y.Average())}  |  Poslední období ({periods[^1]}): {Fmt(name, y[^1])}");
        if (n >= 2) sb.Append($"  |  Změna proti předchozímu: {Delta(name, y[^2], y[^1])}");
        if (n >= 3)
        {
            var (a, b, r2) = Calc.LinReg(y);
            sb.Append($"  |  Trend: {a:+#,0.0;-#,0.0;0.0} {unit} / měs., R² = {r2:0.00}");
            string lp = periods[^1];
            for (int k = 1; k <= fc; k++)
            {
                lp = Calc.NextPeriod(lp);
                double val = Math.Max(0, b + a * (n - 1 + k));
                if (name == PCT) val = Math.Min(100, val);
                sb.Append($"  |  Prognóza {lp}: {Fmt(name, val)}");
            }
        }
        sb.Append("  (poslední měsíc může být rozpracovaný; prognóza je jen lineární extrapolace)");
        TxtTrend.Text = sb.ToString();
    }

    // ---------- mezirocni srovnani ----------
    void BuildYoy(List<Row> rows)
    {
        var name = CmbYoy.SelectedItem?.ToString();
        if (name == null) return;
        var years = rows.Select(r => r.Year).Distinct().OrderBy(x => x).ToList();
        if (years.Count == 0)
        {
            Charts.Empty(PlotYoy, Dark, "Žádná data"); Charts.Empty(PlotCum, Dark, "Žádná data");
            TxtYoy.Text = ""; return;
        }
        var byYm = rows.GroupBy(r => (r.Year, r.Month)).ToDictionary(g => g.Key, g => g.ToList());
        var lines = new List<(string name, double[] xs, double[] ys, SColor col)>();
        var cum = new List<(string name, double[] xs, double[] ys, SColor col)>();
        bool additive = name != PCT && name != PERP && name != HEADS;
        var monthVals = new Dictionary<int, Dictionary<int, List<Row>>>();

        for (int yi = 0; yi < years.Count; yi++)
        {
            int y = years[yi];
            var ms = byYm.Keys.Where(k => k.Year == y).Select(k => k.Month).OrderBy(x => x).ToArray();
            var xs = ms.Select(m => (double)m).ToArray();
            var vals = ms.Select(m => Eval(name, byYm[(y, m)])).ToArray();
            lines.Add((y.ToString(), xs, vals, Charts.Col(yi)));
            if (additive)
            {
                double s = 0;
                cum.Add((y.ToString(), xs, vals.Select(v => s += v).ToArray(), Charts.Col(yi)));
            }
            monthVals[y] = ms.ToDictionary(m => m, m => byYm[(y, m)]);
        }
        string unit = UnitOf(name);
        Charts.Lines(PlotYoy, Dark, $"{name} po měsících", unit, lines);
        if (additive) Charts.Lines(PlotCum, Dark, $"{name} – kumulativně od ledna", unit, cum);
        else Charts.Empty(PlotCum, Dark, "Kumulativní průběh dává smysl jen pro součtové metriky");

        var txt = new List<string>();
        for (int i = 1; i < years.Count; i++)
        {
            int a = years[i - 1], b = years[i];
            var common = monthVals[a].Keys.Intersect(monthVals[b].Keys).ToList();
            if (common.Count == 0) continue;
            double va = Eval(name, common.SelectMany(m => monthVals[a][m]).ToList());
            double vb = Eval(name, common.SelectMany(m => monthVals[b][m]).ToList());
            txt.Add($"{b} vs {a} (stejné měsíce {common.Min()}–{common.Max()}, {common.Count} měs.): {Fmt(name, vb)} vs {Fmt(name, va)} ({Delta(name, va, vb)})");
        }
        TxtYoy.Text = string.Join("   |   ", txt);
    }

    // ---------- zakaznici ----------
    void BuildCust(List<Row> rows)
    {
        var years = rows.Select(r => r.Year).Distinct().OrderBy(x => x).ToList();
        if (years.Count == 0)
        {
            Charts.Empty(PlotCust, Dark, "Žádná data"); Charts.Empty(PlotPie, Dark, "");
            Charts.Empty(PlotCustTime, Dark, "Žádná data"); return;
        }
        string unit = Czk ? "CZK" : "Hodiny";

        var mets = _metrics.Where(m => m.Group is "cust" or "nb").ToList();
        var series = new List<(string name, double[] vals, SColor col)>();
        for (int yi = 0; yi < years.Count; yi++)
        {
            var yr = rows.Where(r => r.Year == years[yi]).ToList();
            series.Add((years[yi].ToString(), mets.Select(m => yr.Where(m.Pred).Sum(V)).ToArray(), Charts.Col(yi)));
        }
        Charts.Grouped(PlotCust, Dark, "Invoiced podle zákazníka a roku (FI newbuild / P / ostatní jsou podmnožiny FI)",
                       mets.Select(m => m.Name).ToArray(), series, unit);

        var items = new List<(string label, double value, SColor col)>();
        bool split = ChkFiSplit.IsChecked == true;
        foreach (var m in _metrics.Where(m => m.Group == "cust"))
        {
            if (split && m.Short == "FI")
            {
                foreach (var s in _metrics.Where(x => x.Group == "nb"))
                {
                    double sv = rows.Where(s.Pred).Sum(V);
                    if (sv > 0) items.Add((s.Name, sv, SplitCol(s.Short)));
                }
                continue;
            }
            double v = rows.Where(m.Pred).Sum(V);
            if (v > 0) items.Add((m.Name, v, CustCol(m.Name)));
        }
        double tot = items.Sum(i => i.value);
        items = items.Select(i => ($"{i.label} ({(tot > 0 ? i.value / tot * 100 : 0):0.0} %)", i.value, i.col)).ToList();
        Charts.Pie(PlotPie, Dark, items);

        var periods = PeriodsOf(rows);
        var ts = StackSeries(rows, periods, r => r.Period).Where(s => s.name != "Overhead (neinvoiced)").ToList();
        Charts.Series(PlotCustTime, Dark, "Invoiced podle zákazníka v čase", unit, periods, ts);
    }

    // ---------- zebricek lidi ----------
    void BuildRank(List<Row> rows)
    {
        var top = rows.GroupBy(r => r.Person)
                      .Select(g => (p: g.Key, v: g.Where(r => !r.Overhead).Sum(V)))
                      .OrderBy(x => x.v).TakeLast(40).ToList();
        if (top.Count == 0) { Charts.Empty(PlotRank, Dark, "Žádná data"); return; }
        var names = top.Select(x => x.p).ToArray();
        var set = names.ToHashSet();
        var sub = rows.Where(r => set.Contains(r.Person)).ToList();
        Charts.HStacked(PlotRank, Dark, "Žebříček lidí (top 40 podle invoiced): invoiced podle zákazníka + overhead",
                        names, StackSeries(sub, names, r => r.Person), Czk ? "CZK" : "Hodiny");
    }

    // ---------- heatmapa ----------
    void BuildHeat(List<Row> rows)
    {
        int mode = CmbHeat.SelectedIndex;
        var periods = PeriodsOf(rows);
        if (periods.Length == 0 || rows.Count == 0) { Charts.Empty(PlotHeat, Dark, "Žádná data"); return; }

        var people = rows.Select(r => r.Person).Distinct().ToList();
        if (people.Count > 80)
            people = rows.GroupBy(r => r.Person).OrderByDescending(g => g.Sum(r => r.Hours)).Take(80).Select(g => g.Key).ToList();
        people = people.OrderBy(p => _personCc.GetValueOrDefault(p, "")).ThenBy(p => p).ToList();

        var pIdx = people.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
        var cIdx = periods.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
        var v = new double[people.Count, periods.Length];
        var all = new double[people.Count, periods.Length];
        var inv = new double[people.Count, periods.Length];

        Metric? fiMetric = mode switch
        {
            3 => _metrics.FirstOrDefault(x => x.Short == "FI NB"),
            4 => _metrics.FirstOrDefault(x => x.Short == "FI P"),
            5 => _metrics.FirstOrDefault(x => x.Short == "FI jiné"),
            _ => null
        };

        foreach (var r in rows)
        {
            if (!pIdx.TryGetValue(r.Person, out var i)) continue;
            int c = cIdx[r.Period];
            all[i, c] += r.Hours;
            if (!r.Overhead) inv[i, c] += r.Hours;
            switch (mode)
            {
                case 0: if (!r.Overhead) v[i, c] += V(r); break;
                case 1: if (r.Overhead) v[i, c] += r.Hours; break;
                case 3:
                case 4:
                case 5:
                    if (fiMetric != null && fiMetric.Pred(r)) v[i, c] += V(r);
                    break;
            }
        }
        if (mode == 2)
            for (int i = 0; i < people.Count; i++)
                for (int c = 0; c < periods.Length; c++)
                    v[i, c] = all[i, c] > 0 ? inv[i, c] / all[i, c] * 100 : 0;

        string[] titles = { "Invoiced", "Overhead (h)", "Inv %", "FI newbuild", "FI P projekty", "FI ostatní" };
        Charts.Heat(PlotHeat, Dark, $"Heatmapa osoba × měsíc: {titles[Math.Clamp(mode, 0, 5)]}",
                    people.Select(p => $"{p} [{_personCc.GetValueOrDefault(p, "")}]").ToArray(), periods, v, mode == 2);
    }

    // ---------- detail osoby ----------
    void BuildPerson(List<Row> rows)
    {
        var people = rows.Select(r => r.Person).Distinct()
                         .OrderBy(p => _personCc.GetValueOrDefault(p, "")).ThenBy(p => p).ToList();
        var cur = CmbPerson.Items.Cast<object>().Select(o => o.ToString()!).ToList();
        var sel = CmbPerson.SelectedItem?.ToString();
        if (!cur.SequenceEqual(people))
        {
            bool prev = _busy; _busy = true;
            CmbPerson.Items.Clear();
            foreach (var p in people) CmbPerson.Items.Add(p);
            CmbPerson.SelectedItem = sel != null && people.Contains(sel) ? sel : people.FirstOrDefault();
            _busy = prev;
        }
        var person = CmbPerson.SelectedItem?.ToString();
        if (person == null)
        {
            Charts.Empty(PlotPerson, Dark, "Žádná data"); TxtPerson.Text = ""; GridPersonProj.ItemsSource = null; return;
        }
        var pr = rows.Where(r => r.Person == person).ToList();
        var periods = PeriodsOf(pr);
        Charts.Stacked(PlotPerson, Dark, $"{person}: hodiny po měsících", periods,
                       StackSeries(pr, periods, r => r.Period), Czk ? "CZK" : "Hodiny");
        string u = Czk ? " CZK" : " h";
        string fi = ChkFiSplit.IsChecked == true
            ? $"  |  FI: NB {Eval(NBN, pr):#,0}, P {Eval("FI P projekty", pr):#,0}, ostatní {Eval("FI ostatní (S, RCS...)", pr):#,0}{u}"
            : $"  |  FI NB {Eval(NBN, pr):#,0}{u}";
        TxtPerson.Text = $"CC {_personCc.GetValueOrDefault(person, "")}  |  Invoiced {Eval(INV, pr):#,0}{u}  |  Overhead {pr.Where(r => r.Overhead).Sum(H):#,0} h  |  " +
                         $"Inv % {Eval(PCT, pr):0.0}{fi}  |  {pr.Where(r => !r.Overhead).Select(r => r.SapId).Distinct().Count()} projektů";
        GridPersonProj.ItemsSource = MakeProj(pr);
    }
}
