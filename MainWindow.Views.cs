using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using SColor = ScottPlot.Color;
namespace HoursTool;

public partial class MainWindow
{
    // ---------- vypocty ----------
    List<string> SeriesNames() =>
        _metrics.Select(m => m.Name).Concat(new[] { PCT, PERP, HEADS }).ToList();

    string UnitOf(string name) =>
        name == PCT ? "%" : name == HEADS ? "osoby" : name == TOTAL ? "Hodiny" :
        name == PERP ? (Czk ? "CZK / os. / měs." : "h / os. / měs.") : (Czk ? "CZK" : "Hodiny");

    double Eval(string name, List<Row> l)
    {
        if (l.Count == 0) return 0;
        switch (name)
        {
            case TOTAL: return l.Sum(H);
            case PCT:
                {
                    double all = l.Sum(H);
                    return all > 0 ? l.Where(r => !r.Overhead).Sum(H) / all * 100 : 0;
                }
            case PERP:
                {
                    int pm = l.Select(r => (r.Person, r.Period)).Distinct().Count();
                    return pm > 0 ? l.Where(r => !r.Overhead).Sum(V) / pm : 0;
                }
            case HEADS:
                {
                    int pm = l.Select(r => (r.Person, r.Period)).Distinct().Count();
                    int per = l.Select(r => r.Period).Distinct().Count();
                    return per > 0 ? (double)pm / per : 0;
                }
            default:
                {
                    var m = _metrics.FirstOrDefault(x => x.Name == name);
                    return m == null ? 0 : l.Where(m.Pred).Sum(V);
                }
        }
    }

    static string Fmt(string name, double v) =>
        name == PCT ? $"{v:0.0} %" : (name == HEADS || name == PERP) ? $"{v:#,0.0}" : $"{v:#,0}";

    static string Delta(string name, double a, double b) =>
        name == PCT ? $"{b - a:+0.0;-0.0;0.0} p.b."
        : a > 0 ? $"{(b - a) / a * 100:+0.0;-0.0;0.0} %" : "n/a";

    static (List<Row>, List<Row>) CommonMonths(List<Row> a, List<Row> b)
    {
        var common = a.Select(r => r.Month).Distinct().Intersect(b.Select(r => r.Month).Distinct()).ToHashSet();
        return (a.Where(r => common.Contains(r.Month)).ToList(), b.Where(r => common.Contains(r.Month)).ToList());
    }

    string Yoy(List<Row> rows, string name)
    {
        var years = rows.Select(r => r.Year).Distinct().OrderBy(x => x).ToList();
        if (years.Count < 2) return "";
        int a = years[^2], b = years[^1];
        var (ra, rb) = CommonMonths(rows.Where(r => r.Year == a).ToList(), rows.Where(r => r.Year == b).ToList());
        if (ra.Count == 0) return "";
        return $"{Delta(name, Eval(name, ra), Eval(name, rb))} vs {a} (stejné měs.)";
    }

    SColor CustCol(string name)
    {
        int idx = _metrics.Where(m => m.Group == "cust").Select(m => m.Name).ToList().IndexOf(name);
        return Charts.Col(idx < 0 ? 7 : idx);
    }

    string AccentOf(Metric m)
    {
        if (m.Group == "nb")
            return m.Short switch { "FI NB" => "#6764f6", "FI P" => "#b3bcff", _ => "#2f2da8" };
        int idx = _metrics.Where(x => x.Group == "cust").ToList().IndexOf(m);
        return Charts.Palette[(idx < 0 ? 7 : idx) % Charts.Palette.Length];
    }

    // ---------- KPI ----------
    static readonly IBrush GoodBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#2fa57a"));
    static readonly IBrush BadBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#e5252f"));

    Border Card(string title, string value, string sub, string delta = "", bool goodUp = true, string accent = "#6764f6")
    {
        var sp = new StackPanel { Spacing = 3, Margin = new Thickness(14, 10, 16, 10) };
        sp.Children.Add(new TextBlock { Text = title, FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeight.Bold });
        if (sub != "")
            sp.Children.Add(new TextBlock { Text = sub, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxWidth = 230 });
        if (delta != "")
        {
            bool up = delta.StartsWith("+"), down = delta.StartsWith("-");
            var tb = new TextBlock { Text = delta, FontSize = 12, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 230 };
            if (up || down) tb.Foreground = (up == goodUp) ? GoodBrush : BadBrush;
            else tb.Opacity = 0.6;
            sp.Children.Add(tb);
        }

        var bar = new Border
        {
            Width = 5, Background = new SolidColorBrush(Avalonia.Media.Color.Parse(accent))
        };
        DockPanel.SetDock(bar, Dock.Left);
        var dock = new DockPanel();
        dock.Children.Add(bar);
        dock.Children.Add(sp);

        var card = new Border
        {
            Child = dock, Margin = new Thickness(0, 0, 10, 10), MinWidth = 210,
            ClipToBounds = true
        };
        card.Classes.Add("card");
        return card;
    }

    void BuildKpi(List<Row> rows)
    {
        PnlKpi.Children.Clear();
        if (rows.Count == 0)
        {
            PnlKpi.Children.Add(Card("Žádná data", "–", "Načtěte soubor nebo upravte filtry.", "", true, "#9AA0A6"));
            return;
        }
        string u = Czk ? " CZK" : " h";
        var periods = rows.Select(r => r.Period).Distinct().OrderBy(x => x).ToList();
        int people = rows.Select(r => r.Person).Distinct().Count();
        PnlKpi.Children.Add(Card("Období", $"{periods[0]} – {periods[^1]}", $"{periods.Count} měs., {people} lidí", "", true, "#9AA0A6"));
        PnlKpi.Children.Add(Card("Celkem zapsáno", $"{rows.Sum(H):#,0} h", "", Yoy(rows, TOTAL), true, "#93a1ff"));

        double inv = Eval(INV, rows);
        PnlKpi.Children.Add(Card("Invoiced", $"{inv:#,0}{u}", "", Yoy(rows, INV), true, "#6764f6"));
        double oh = rows.Where(r => r.Overhead).Sum(H);
        PnlKpi.Children.Add(Card("Overhead (neinvoiced)", $"{oh:#,0} h", "", Czk ? "" : Yoy(rows, OH), false, "#9AA0A6"));
        PnlKpi.Children.Add(Card("Inv % (podíl invoiced)", Fmt(PCT, Eval(PCT, rows)), "", Yoy(rows, PCT), true, "#3fb68b"));
        PnlKpi.Children.Add(Card("Invoiced na osobu a měsíc", Fmt(PERP, Eval(PERP, rows)), Czk ? "CZK" : "hodin", Yoy(rows, PERP), true, "#e0a800"));
        PnlKpi.Children.Add(Card("Průměr aktivních lidí / měs.", Fmt(HEADS, Eval(HEADS, rows)), "", Yoy(rows, HEADS), true, "#00a3e0"));
        PnlKpi.Children.Add(Card("Invoiced projektů", rows.Where(r => !r.Overhead).Select(r => r.SapId).Distinct().Count().ToString(), "", "", true, "#b565a7"));

        foreach (var g in new[] { "cust", "nb" })
            foreach (var m in _metrics.Where(m => m.Group == g))
            {
                double v = Eval(m.Name, rows);
                if (v <= 0) continue;
                PnlKpi.Children.Add(Card(m.Name, $"{v:#,0}{u}", $"{(inv > 0 ? v / inv * 100 : 0):0.0} % z invoiced",
                                         Yoy(rows, m.Name), true, AccentOf(m)));
            }
    }

    // ---------- statistiky po letech ----------
    void BuildStats(List<Row> rows)
    {
        var years = rows.Select(r => r.Year).Distinct().OrderBy(x => x).ToList();
        var byYear = years.ToDictionary(y => y, y => rows.Where(r => r.Year == y).ToList());
        var names = new List<string> { TOTAL };
        names.AddRange(_metrics.Select(m => m.Name));
        names.AddRange(new[] { PCT, PERP, HEADS });

        var headers = new List<string>();
        foreach (var y in years) headers.Add(y.ToString());
        for (int i = 1; i < years.Count; i++) headers.Add($"Δ {years[i]} vs {years[i - 1]}");

        var list = new List<TableRow>();
        var months = new List<string>();
        foreach (var y in years) months.Add(byYear[y].Select(r => r.Month).Distinct().Count().ToString());
        for (int i = 1; i < years.Count; i++)
        {
            var (a, _) = CommonMonths(byYear[years[i - 1]], byYear[years[i]]);
            months.Add(a.Select(r => r.Month).Distinct().Count() + " spol.");
        }
        list.Add(new TableRow { Label = "Počet měsíců v datech", Cells = months.ToArray() });

        foreach (var n in names)
        {
            var cells = new List<string>();
            foreach (var y in years) cells.Add(Fmt(n, Eval(n, byYear[y])));
            for (int i = 1; i < years.Count; i++)
            {
                var (a, b) = CommonMonths(byYear[years[i - 1]], byYear[years[i]]);
                cells.Add(a.Count == 0 ? "" : Delta(n, Eval(n, a), Eval(n, b)));
            }
            list.Add(new TableRow { Label = n == TOTAL ? TOTAL + " (h)" : n, Cells = cells.ToArray() });
        }

        Grid_SetColumns(GridStats, "Ukazatel", 280, headers, 130);
        GridStats.ItemsSource = list;
        _statHeaders = headers; _lastStats = list;
    }

    void Grid_SetColumns(DataGrid g, string labelHdr, int labelW, List<string> headers, int w)
    {
        g.Columns.Clear();
        g.Columns.Add(new DataGridTextColumn { Header = labelHdr, Binding = new Binding("Label"), Width = new DataGridLength(labelW) });
        for (int i = 0; i < headers.Count; i++)
        {
            var col = new DataGridTextColumn
            {
                Header = headers[i], Binding = new Binding($"Cells[{i}]"), Width = new DataGridLength(w)
            };
            col.CellStyleClasses.Add("num");
            g.Columns.Add(col);
        }
    }

    // ---------- hlavni tabulka ----------
    const string HF = "{0:#,0.#;-#,0.#;–}";

    void BuildTable(List<Row> rows)
    {
        var monthly = Checked(PnlMonthly);
        var summary = Checked(PnlSummary);
        if (ChkFiSplit.IsChecked == true)
            foreach (var m in _metrics.Where(m => m.Group == "nb" && !summary.Contains(m)))
                summary.Add(m);
        int grp = CmbGroup.SelectedIndex, colMode = CmbCols.SelectedIndex, sort = CmbSort.SelectedIndex;

        string Key(Row r) => grp switch
        {
            1 => r.Cc,
            2 => r.Customer == "-" ? "(overhead)" : r.Customer,
            3 => r.SapId,
            4 => r.Year.ToString(),
            _ => r.Person
        };
        string ColKey(Row r) => colMode == 0 ? r.Period : r.Year.ToString();

        var colKeys = colMode == 2 ? new List<string>() : rows.Select(ColKey).Distinct().OrderBy(x => x).ToList();
        var cIdx = colKeys.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
        int P = colKeys.Count, M = monthly.Count, S = summary.Count;
        int width = P * M + S + 1;

        var headers = new List<string>(); var fmts = new List<string>();
        foreach (var p in colKeys) foreach (var m in monthly) { headers.Add($"{p}\n{m.Short}"); fmts.Add(HF); }
        foreach (var m in summary) { headers.Add($"Σ\n{m.Short}"); fmts.Add(HF); }
        headers.Add("Inv %"); fmts.Add("{0:0.0}");

        var result = new List<ReportRow>();
        foreach (var g in rows.GroupBy(Key))
        {
            var cells = new double[width]; double invV = 0, all = 0, invH = 0;
            foreach (var r in g)
            {
                if (P > 0)
                {
                    int pi = cIdx[ColKey(r)];
                    for (int mi = 0; mi < M; mi++) if (monthly[mi].Pred(r)) cells[pi * M + mi] += V(r);
                }
                for (int si = 0; si < S; si++) if (summary[si].Pred(r)) cells[P * M + si] += V(r);
                all += r.Hours;
                if (!r.Overhead) { invH += r.Hours; invV += V(r); }
            }
            cells[width - 1] = all > 0 ? invH / all * 100 : 0;
            string sub = grp == 0 ? _personCc.GetValueOrDefault(g.Key, "") : grp == 3 ? g.First().Desc : "";
            result.Add(new ReportRow { Label = g.Key, Sub = sub, Cells = cells, InvTotal = invV, Pct = cells[width - 1] });
        }

        result = sort == 1 ? result.OrderByDescending(r => r.InvTotal).ToList()
               : sort == 2 ? result.OrderByDescending(r => r.Pct).ToList()
               : result.OrderBy(r => grp == 0 ? r.Sub : "").ThenBy(r => r.Label).ToList();

        var tot = new double[width];
        foreach (var r in result) for (int i = 0; i < width - 1; i++) tot[i] += r.Cells[i];
        double tAll = rows.Sum(H), tInv = rows.Where(r => !r.Overhead).Sum(H);
        tot[width - 1] = tAll > 0 ? tInv / tAll * 100 : 0;
        result.Insert(0, new ReportRow { Label = "Σ CELKEM", Sub = "", Cells = tot, InvTotal = 0, Pct = tot[width - 1] });

        string labelHdr = grp switch { 1 => "Cost centrum", 2 => "Zákazník", 3 => "Projekt (ID)", 4 => "Rok", _ => "Osoba" };
        string subHdr = grp == 0 ? "CC" : grp == 3 ? "Popis" : "";

        Grid.Columns.Clear();
        Grid.Columns.Add(new DataGridTextColumn { Header = labelHdr, Binding = new Binding("Label"), Width = new DataGridLength(grp == 3 ? 150 : 200) });
        Grid.Columns.Add(new DataGridTextColumn { Header = subHdr, Binding = new Binding("Sub"), Width = new DataGridLength(grp == 3 ? 240 : 70) });
        for (int i = 0; i < width; i++)
        {
            var col = new DataGridTextColumn
            {
                Header = headers[i],
                Binding = new Binding($"Cells[{i}]") { StringFormat = fmts[i] },
                Width = new DataGridLength(i == width - 1 ? 70 : 78)
            };
            col.CellStyleClasses.Add("num");
            Grid.Columns.Add(col);
        }
        Grid.ItemsSource = result;
        _lastHeaders = headers; _lastReport = result; _lastFormats = fmts;
        _lastLabelHdr = labelHdr; _lastSubHdr = subHdr;
    }

    // ---------- projekty ----------
    List<ProjRow> MakeProj(List<Row> rows) =>
        rows.GroupBy(r => (r.SapId, r.Desc))
            .Select(g => new ProjRow(g.Key.SapId, g.Key.Desc, g.First().Customer,
                g.First().Overhead ? "Overhead" : "Invoiced",
                g.First().Newbuild ? "FI NB" : g.First().PerRequest ? "P" : "",
                Math.Round(g.Sum(V), 1), g.Select(r => r.Person).Distinct().Count(),
                g.Min(r => r.Period)!, g.Max(r => r.Period)!))
            .OrderByDescending(p => p.Value).ToList();

    void BuildProjects(List<Row> rows)
    {
        _lastProj = MakeProj(rows);
        GridProj.ItemsSource = _lastProj;
    }

    // ---------- kontrola dat ----------
    void BuildIssues()
    {
        var l = new List<DataIssue>();
        if (_raw.Count == 0)
        {
            l.Add(new DataIssue("Info", "Žádná data", "Načtěte export z timesheetů."));
            GridIssues.ItemsSource = l; return;
        }

        foreach (var g in _fileOfPeriod.GroupBy(kv => kv.Value))
            l.Add(new DataIssue("Info", $"Soubor {g.Key}", "období: " + string.Join(", ", g.Select(x => x.Key).OrderBy(x => x))));
        l.Add(new DataIssue("Info", "Načteno řádků", $"{_raw.Count:#,0} celkem, {_rows.Count:#,0} po filtru CC a relevantních lidí"));

        var ccset = _cfg.CostCenters.ToHashSet();
        var restricted = _cfg.RestrictedCostCenters.ToHashSet();
        var rel = new HashSet<string>(_cfg.RelevantPeople, StringComparer.OrdinalIgnoreCase);

        int outCc = _raw.Count(r => !ccset.Contains(r.Cc));
        if (outCc > 0) l.Add(new DataIssue("Info", "Řádky mimo nastavená CC (ignorovány)", $"{outCc:#,0} řádků"));

        var excluded = _raw.Where(r => ccset.Contains(r.Cc) && restricted.Contains(r.Cc) && !rel.Contains(r.Person))
                           .GroupBy(r => r.Person).ToList();
        if (excluded.Count > 0)
            l.Add(new DataIssue("Info", $"Vyloučeno {excluded.Count} lidí z CC s ručním výběrem",
                $"{excluded.Sum(g => g.Sum(r => r.Hours)):#,0} h; např. " + string.Join(", ", excluded.Take(6).Select(g => g.Key))));
        if (restricted.Count > 0 && rel.Count == 0)
            l.Add(new DataIssue("Varování", "Seznam relevantních lidí je prázdný", "CC s ručním výběrem se nezobrazí. Doplňte jej v Nastavení."));

        var rawPeople = _raw.Select(r => r.Person).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var n in rel.Where(n => !rawPeople.Contains(n)))
            l.Add(new DataIssue("Varování", "Relevantní osoba není v datech", $"{n} (překlep nebo jiné pořadí jména?)"));

        var unk = _rows.Where(r => r.Customer == "??").GroupBy(r => (r.SapId, r.Desc)).ToList();
        if (unk.Count > 0)
            l.Add(new DataIssue("Varování", $"Neznámý zákazník u {unk.Count} projektů",
                string.Join("; ", unk.Take(8).Select(g => $"{g.Key.SapId} {g.Key.Desc} ({g.Sum(r => r.Hours):#,0} h)"))));

        var noAb = _raw.Where(r => ccset.Contains(r.Cc) && r.Abacus.Trim().Length < 2 && !_calc.IsOverhead(r.SapId))
                       .GroupBy(r => (r.SapId, r.Desc)).OrderByDescending(g => g.Sum(r => r.Hours)).ToList();
        if (noAb.Count > 0)
            l.Add(new DataIssue("Info", $"Invoiced projekty bez ABACUS ({noAb.Count}), zákazník odhadnut z popisu/ID",
                string.Join("; ", noAb.Take(8).Select(g => $"{g.Key.SapId} {g.Key.Desc} ({g.Sum(r => r.Hours):#,0} h)"))));

        int neg = _rows.Count(r => r.Hours < 0);
        if (neg > 0) l.Add(new DataIssue("Varování", "Záporné hodiny", $"{neg} řádků, součet {_rows.Where(r => r.Hours < 0).Sum(r => r.Hours):#,0.#} h"));

        var multi = _rows.GroupBy(r => r.Person).Where(g => g.Select(r => r.Cc).Distinct().Count() > 1).ToList();
        if (multi.Count > 0)
            l.Add(new DataIssue("Info", $"{multi.Count} lidí je ve více CC",
                string.Join("; ", multi.Take(6).Select(g => $"{g.Key}: {string.Join("/", g.Select(r => r.Cc).Distinct())}"))));

        var periods = _rows.Select(r => r.Period).Distinct().OrderBy(x => x).ToList();
        if (periods.Count > 1)
        {
            var have = periods.ToHashSet(); var missing = new List<string>();
            for (var p = periods[0]; string.CompareOrdinal(p, periods[^1]) < 0; p = Calc.NextPeriod(p))
                if (!have.Contains(p)) missing.Add(p);
            if (missing.Count > 0) l.Add(new DataIssue("Varování", "Chybějící měsíce v datech", string.Join(", ", missing)));
        }

        foreach (var g in _rows.Where(r => r.Overhead).GroupBy(r => r.SapId).OrderByDescending(g => g.Sum(r => r.Hours)))
            l.Add(new DataIssue("Overhead", g.Key, $"{g.First().Desc} | {g.Sum(r => r.Hours):#,0} h"));

        var nbs = _rows.Where(r => r.Newbuild).GroupBy(r => r.SapId).OrderByDescending(g => g.Sum(r => r.Hours)).ToList();
        if (nbs.Count > 0)
        {
            l.Add(new DataIssue("FI NB", $"{nbs.Count} projektů FI newbuild", $"{nbs.Sum(g => g.Sum(r => r.Hours)):#,0} h celkem"));
            foreach (var g in nbs) l.Add(new DataIssue("FI NB", g.Key, $"{g.First().Desc} | {g.Sum(r => r.Hours):#,0} h"));
        }
        var prs = _rows.Where(r => r.PerRequest).GroupBy(r => r.SapId).OrderByDescending(g => g.Sum(r => r.Hours)).ToList();
        if (prs.Count > 0)
            l.Add(new DataIssue("P", $"{prs.Count} P projektů (per-request)", $"{prs.Sum(g => g.Sum(r => r.Hours)):#,0} h celkem"));
        GridIssues.ItemsSource = l;
    }
}
