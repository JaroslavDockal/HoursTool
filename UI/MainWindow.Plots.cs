using System.Text;
using SColor = ScottPlot.Color;
namespace HoursTool;

public partial class MainWindow
{
    static SColor SplitCol(string id) => id switch
    {
        "split:nb" => SColor.FromHex("#6764f6"),
        "split:p" => SColor.FromHex("#b3bcff"),
        _ => SColor.FromHex("#2f2da8")
    };

    /// <summary>
    /// Builds stacked series: invoiced per customer (optionally with the split customer broken down)
    /// plus overhead hours (only in hours mode). <paramref name="keyOf"/> maps a row to its category on the X axis.
    /// </summary>
    List<(string name, double[] vals, SColor col)> StackSeries(List<Row> rows, string[] keys, Func<Row, string> keyOf, bool withOverhead = true)
    {
        var idx = keys.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);
        bool split = ChkSplit.IsChecked == true;
        var splitCode = "cust:" + _cfg.SplitCustomer.Trim().ToUpperInvariant();
        var res = new List<(string name, double[] vals, SColor col)>();

        void Add(Metric m, SColor col)
        {
            var v = new double[keys.Length];
            foreach (var r in rows) if (m.Pred(r) && idx.TryGetValue(keyOf(r), out var i)) v[i] += V(r);
            res.Add((m.Name, v, col));
        }

        foreach (var m in _metrics.Where(m => m.Group == "cust"))
        {
            if (split && m.Id == splitCode)
                foreach (var s in _metrics.Where(x => x.Group == "split")) Add(s, SplitCol(s.Id));
            else
                Add(m, CustCol(m.Id));
        }
        if (!Money && withOverhead)
        {
            var o = new double[keys.Length];
            foreach (var r in rows) if (r.Overhead && idx.TryGetValue(keyOf(r), out var i)) o[i] += r.Hours;
            res.Add((L.T("m.oh"), o, Charts.Grey));
        }
        return res;
    }

    static string[] PeriodsOf(List<Row> rows) => rows.Select(r => r.Period).Distinct().OrderBy(x => x).ToArray();

    // ---------- time ----------
    void BuildTimeChart(List<Row> rows)
    {
        var periods = PeriodsOf(rows);
        if (periods.Length == 0) { Charts.Empty(PlotTime, Dark, L.T("ch.noData")); return; }
        Charts.Stacked(PlotTime, Dark, L.T("ch.time"), periods, StackSeries(rows, periods, r => r.Period), _an.UnitLabel);
    }

    // ---------- trend ----------
    void BuildTrend(List<Row> rows)
    {
        var id = SelectedId(CmbTrend);
        if (id == null) return;
        var name = _an.NameOf(id);
        var periods = PeriodsOf(rows);
        if (ChkSkipLast.IsChecked == true && periods.Length > 1) periods = periods[..^1];
        if (periods.Length == 0) { Charts.Empty(PlotTrend, Dark, L.T("ch.noData")); TxtTrend.Text = ""; return; }
        var byP = rows.GroupBy(r => r.Period).ToDictionary(g => g.Key, g => g.ToList());
        var y = periods.Select(p => _an.Eval(id, byP[p])).ToArray();
        int fc = new[] { 0, 1, 3, 6 }[Math.Max(0, CmbFc.SelectedIndex)];
        string unit = _an.UnitOf(id);

        Charts.Trend(PlotTrend, Dark, L.T("ch.trend", name), unit, periods, y,
                     ChkMa.IsChecked == true, ChkTrend.IsChecked == true, fc, Charts.Col(0), id == Analytics.PCT);

        int n = y.Length;
        var sb = new StringBuilder();
        sb.Append(L.T("tr.avg", Analytics.Fmt(id, y.Average()), periods[^1], Analytics.Fmt(id, y[^1])));
        if (n >= 2) sb.Append("  |  ").Append(L.T("tr.vsPrev", Analytics.Delta(id, y[^2], y[^1])));
        sb.Append("  |  ").Append(L.T("tr.minmax", Analytics.Fmt(id, y.Min()), periods[Array.IndexOf(y, y.Min())],
                                                     Analytics.Fmt(id, y.Max()), periods[Array.IndexOf(y, y.Max())]));
        if (n >= 3)
        {
            var (a, b, r2) = Stats.LinReg(y);
            var unitShort = id == Analytics.PCT ? L.T("unit.pp") : unit == L.T("unit.hours") ? "h" : unit;
            sb.Append("  |  ").Append(L.T("tr.slope", a.ToString("+#,0.0;-#,0.0;0.0"), unitShort, r2.ToString("0.00")));
            string lp = periods[^1];
            for (int k = 1; k <= fc; k++)
            {
                lp = Periods.Next(lp);
                double val = Math.Max(0, b + a * (n - 1 + k));
                if (id == Analytics.PCT) val = Math.Min(100, val);
                sb.Append("  |  ").Append(L.T("tr.fcVal", lp, Analytics.Fmt(id, val)));
            }
        }
        sb.Append("\n").Append(L.T("tr.note"));
        TxtTrend.Text = sb.ToString();
    }

    // ---------- year over year ----------
    void BuildYoy(List<Row> rows)
    {
        var id = SelectedId(CmbYoy);
        if (id == null) return;
        var name = _an.NameOf(id);
        var years = rows.Select(r => r.Year).Distinct().OrderBy(x => x).ToList();
        if (years.Count == 0)
        {
            Charts.Empty(PlotYoy, Dark, L.T("ch.noData")); Charts.Empty(PlotCum, Dark, L.T("ch.noData"));
            TxtYoy.Text = ""; return;
        }
        var byYm = rows.GroupBy(r => (r.Year, r.Month)).ToDictionary(g => g.Key, g => g.ToList());
        var lines = new List<(string name, double[] xs, double[] ys, SColor col)>();
        var cum = new List<(string name, double[] xs, double[] ys, SColor col)>();
        bool additive = Analytics.Additive(id);
        var monthVals = new Dictionary<int, Dictionary<int, List<Row>>>();

        for (int yi = 0; yi < years.Count; yi++)
        {
            int y = years[yi];
            var ms = byYm.Keys.Where(k => k.Year == y).Select(k => k.Month).OrderBy(x => x).ToArray();
            var xs = ms.Select(m => (double)m).ToArray();
            var vals = ms.Select(m => _an.Eval(id, byYm[(y, m)])).ToArray();
            lines.Add((y.ToString(), xs, vals, Charts.Col(yi)));
            if (additive)
            {
                double s = 0;
                cum.Add((y.ToString(), xs, vals.Select(v => s += v).ToArray(), Charts.Col(yi)));
            }
            monthVals[y] = ms.ToDictionary(m => m, m => byYm[(y, m)]);
        }
        string unit = _an.UnitOf(id);
        Charts.Lines(PlotYoy, Dark, L.T("ch.yoy", name), unit, lines);
        if (additive) Charts.Lines(PlotCum, Dark, L.T("ch.cum", name), unit, cum);
        else Charts.Empty(PlotCum, Dark, L.T("ch.cumNa"));

        var txt = new List<string>();
        for (int i = 1; i < years.Count; i++)
        {
            int a = years[i - 1], b = years[i];
            var common = monthVals[a].Keys.Intersect(monthVals[b].Keys).OrderBy(x => x).ToList();
            if (common.Count == 0) continue;
            double va = _an.Eval(id, common.SelectMany(m => monthVals[a][m]).ToList());
            double vb = _an.Eval(id, common.SelectMany(m => monthVals[b][m]).ToList());
            txt.Add(L.T("yo.line", b, a, L.MonthsShort[common[0] - 1], L.MonthsShort[common[^1] - 1], common.Count,
                        Analytics.Fmt(id, vb), Analytics.Fmt(id, va), Analytics.Delta(id, va, vb)));
        }
        TxtYoy.Text = txt.Count == 0 ? L.T("yo.single") : string.Join("\n", txt);
    }

    // ---------- customers ----------
    void BuildCust(List<Row> rows)
    {
        var years = rows.Select(r => r.Year).Distinct().OrderBy(x => x).ToList();
        if (years.Count == 0)
        {
            Charts.Empty(PlotCust, Dark, L.T("ch.noData")); Charts.Empty(PlotPie, Dark, "");
            Charts.Empty(PlotCustTime, Dark, L.T("ch.noData")); return;
        }
        string unit = _an.UnitLabel;

        var mets = _metrics.Where(m => m.Group is "cust" or "split").ToList();
        var series = new List<(string name, double[] vals, SColor col)>();
        for (int yi = 0; yi < years.Count; yi++)
        {
            var yr = rows.Where(r => r.Year == years[yi]).ToList();
            series.Add((years[yi].ToString(), mets.Select(m => yr.Where(m.Pred).Sum(V)).ToArray(), Charts.Col(yi)));
        }
        Charts.Grouped(PlotCust, Dark, L.T("ch.custYear", _cfg.SplitCustomer.ToUpperInvariant()),
                       mets.Select(m => m.Short).ToArray(), series, unit);

        var items = new List<(string label, double value, SColor col)>();
        bool split = ChkSplit.IsChecked == true;
        var splitCode = "cust:" + _cfg.SplitCustomer.Trim().ToUpperInvariant();
        foreach (var m in _metrics.Where(m => m.Group == "cust"))
        {
            if (split && m.Id == splitCode)
            {
                foreach (var s in _metrics.Where(x => x.Group == "split"))
                {
                    double sv = rows.Where(s.Pred).Sum(V);
                    if (sv > 0) items.Add((s.Name, sv, SplitCol(s.Id)));
                }
                continue;
            }
            double v = rows.Where(m.Pred).Sum(V);
            if (v > 0) items.Add((m.Name, v, CustCol(m.Id)));
        }
        double tot = items.Sum(i => i.value);
        items = items.Select(i => ($"{i.label} ({(tot > 0 ? i.value / tot * 100 : 0):0.0} %)", i.value, i.col)).ToList();
        Charts.Pie(PlotPie, Dark, items);

        var periods = PeriodsOf(rows);
        var ts = StackSeries(rows, periods, r => r.Period, false);
        Charts.Series(PlotCustTime, Dark, L.T("ch.custTime"), unit, periods, ts);
    }

    // ---------- people ranking ----------
    void BuildRank(List<Row> rows)
    {
        int n = CmbRankN.SelectedIndex switch { 0 => 20, 1 => 40, 2 => 80, _ => int.MaxValue };
        int by = CmbRankBy.SelectedIndex;
        double Score(IGrouping<string, Row> g) => by switch
        {
            1 => g.Sum(r => r.Hours),
            2 => g.Where(r => r.Overhead).Sum(r => r.Hours),
            3 => g.Sum(r => r.Hours) > 0 ? g.Where(r => !r.Overhead).Sum(r => r.Hours) / g.Sum(r => r.Hours) : 0,
            _ => g.Where(r => !r.Overhead).Sum(V)
        };
        var top = rows.GroupBy(r => r.Person).Select(g => (p: g.Key, v: Score(g)))
                      .OrderBy(x => x.v).ToList();
        if (top.Count > n) top = top.TakeLast(n).ToList();
        if (top.Count == 0) { Charts.Empty(PlotRank, Dark, L.T("ch.noData")); return; }
        var names = top.Select(x => x.p).ToArray();
        var set = names.ToHashSet();
        var sub = rows.Where(r => set.Contains(r.Person)).ToList();
        var byText = (CmbRankBy.SelectedItem as Avalonia.Controls.ComboBoxItem)?.Content?.ToString() ?? "";
        Charts.HStacked(PlotRank, Dark, L.T("ch.rank", names.Length, byText),
                        names.Select(p => $"{p} [{_personCc.GetValueOrDefault(p, "")}]").ToArray(),
                        StackSeries(sub, names, r => r.Person), _an.UnitLabel);
    }

    // ---------- heatmap ----------
    void BuildHeat(List<Row> rows)
    {
        int mode = Math.Max(0, CmbHeat.SelectedIndex);
        int dim = Math.Max(0, CmbHeatRows.SelectedIndex);
        var periods = PeriodsOf(rows);
        if (periods.Length == 0) { Charts.Empty(PlotHeat, Dark, L.T("ch.noData")); return; }

        Func<Row, string> keyOf = dim switch
        {
            1 => r => r.Customer == "-" ? L.T("f.cust.oh") : r.Customer,
            2 => r => r.Cc,
            3 => r => r.SapId,
            _ => r => r.Person
        };
        var keys = rows.GroupBy(keyOf).OrderByDescending(g => g.Sum(r => r.Hours)).Take(80).Select(g => g.Key).ToList();
        keys = dim == 0 ? keys.OrderBy(p => _personCc.GetValueOrDefault(p, "")).ThenBy(p => p).ToList() : keys.OrderBy(k => k).ToList();

        var kIdx = keys.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
        var cIdx = periods.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
        var v = new double[keys.Count, periods.Length];
        var all = new double[keys.Count, periods.Length];
        var inv = new double[keys.Count, periods.Length];
        Metric? splitMetric = mode switch
        {
            4 => _metrics.FirstOrDefault(x => x.Id == "split:nb"),
            5 => _metrics.FirstOrDefault(x => x.Id == "split:p"),
            6 => _metrics.FirstOrDefault(x => x.Id == "split:rest"),
            _ => null
        };

        foreach (var r in rows)
        {
            if (!kIdx.TryGetValue(keyOf(r), out var i)) continue;
            int c = cIdx[r.Period];
            all[i, c] += r.Hours;
            if (!r.Overhead) inv[i, c] += r.Hours;
            switch (mode)
            {
                case 0: if (!r.Overhead) v[i, c] += V(r); break;
                case 1: if (r.Overhead) v[i, c] += r.Hours; break;
                case 3: v[i, c] += r.Hours; break;
                case 4: case 5: case 6:
                    if (splitMetric != null && splitMetric.Pred(r)) v[i, c] += V(r);
                    break;
            }
        }
        if (mode == 2)
            for (int i = 0; i < keys.Count; i++)
                for (int c = 0; c < periods.Length; c++)
                    v[i, c] = all[i, c] > 0 ? inv[i, c] / all[i, c] * 100 : 0;

        var title = (CmbHeat.SelectedItem as Avalonia.Controls.ComboBoxItem)?.Content?.ToString() ?? "";
        var dimText = (CmbHeatRows.SelectedItem as Avalonia.Controls.ComboBoxItem)?.Content?.ToString() ?? "";
        var labels = dim == 0 ? keys.Select(p => $"{p} [{_personCc.GetValueOrDefault(p, "")}]").ToArray() : keys.ToArray();
        Charts.Heat(PlotHeat, Dark, L.T("ch.heat", dimText, title), labels, periods, v, mode == 2);
    }

    // ---------- person detail ----------
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
        ProjColumns(GridPersonProj, true);
        PnlPersonKpi.Children.Clear();
        var person = CmbPerson.SelectedItem?.ToString();
        if (person == null)
        {
            Charts.Empty(PlotPerson, Dark, L.T("ch.noData")); GridPersonProj.ItemsSource = null; return;
        }
        var pr = rows.Where(r => r.Person == person).ToList();
        var periods = PeriodsOf(pr);
        Charts.Stacked(PlotPerson, Dark, L.T("ch.person", person), periods, StackSeries(pr, periods, r => r.Period), _an.UnitLabel);

        string u = _an.U;
        void Chip(string text)
        {
            var b = new Avalonia.Controls.Border { Classes = { "chip" }, Child = new Avalonia.Controls.TextBlock { Text = text } };
            PnlPersonKpi.Children.Add(b);
        }
        Chip($"CC {_personCc.GetValueOrDefault(person, "")}");
        Chip($"{L.T("m.inv")}: {_an.Eval("inv", pr):#,0}{u}");
        Chip($"{L.T("m.oh")}: {pr.Where(r => r.Overhead).Sum(r => r.Hours):#,0} h");
        Chip($"{L.T("m.pct.s")}: {_an.Eval(Analytics.PCT, pr):0.0} %");
        Chip($"{L.T("m.perp")}: {Analytics.Fmt(Analytics.PERP, _an.Eval(Analytics.PERP, pr))}");
        foreach (var m in _metrics.Where(m => m.Group == "split"))
        {
            var v = _an.Eval(m.Id, pr);
            if (v > 0) Chip($"{m.Short}: {v:#,0}{u}");
        }
        Chip(L.T("pe.projects", pr.Where(r => !r.Overhead).Select(r => r.SapId).Distinct().Count()));
        GridPersonProj.ItemsSource = _an.Projects(pr);
    }
}
