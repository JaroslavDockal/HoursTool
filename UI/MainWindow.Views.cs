using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using SColor = ScottPlot.Color;
namespace HoursTool;

public partial class MainWindow
{
    // data kept for the Excel export
    List<string> _lastHeaders = new();
    List<ReportRow> _lastReport = new();
    List<string> _lastFormats = new();
    string _lastLabelHdr = "", _lastSubHdr = "";
    List<string> _statHeaders = new();
    List<TableRow> _lastStats = new();
    List<ProjRow> _lastProj = new();

    const string HF = "{0:#,0.#;-#,0.#;–}";

    void WireViewEvents()
    {
        foreach (var cb in new[] { CmbGroup, CmbCols, CmbSort })
            cb.SelectionChanged += (_, _) => RebuildIfVisible("table");
        TxtTableFind.TextChanged += (_, _) => RebuildIfVisible("table");
        BtnCopyTable.Click += async (_, _) => await CopyTable();
        GridTable.LoadingRow += (_, e) => e.Row.Classes.Set("total", (e.Row.DataContext as ReportRow)?.IsTotal == true);
        GridTable.DoubleTapped += (_, _) =>
        {
            if (CmbGroup.SelectedIndex == 0 && GridTable.SelectedItem is ReportRow { IsTotal: false } r) ShowPerson(r.Label);
        };
        TxtProjFind.TextChanged += (_, _) => RebuildIfVisible("projects");
        GridIssues.LoadingRow += (_, e) =>
        {
            var lvl = (e.Row.DataContext as DataIssue)?.Level;
            e.Row.Classes.Set("warn", lvl == IssueLevel.Warning);
            e.Row.Classes.Set("error", lvl == IssueLevel.Error);
        };
        CmbIssueLevel.SelectionChanged += (_, _) => RebuildIfVisible("data");

        foreach (var cb in new[] { CmbTrend, CmbFc }) cb.SelectionChanged += (_, _) => RebuildIfVisible("trend");
        foreach (var ck in new[] { ChkMa, ChkTrend, ChkSkipLast }) ck.IsCheckedChanged += (_, _) => RebuildIfVisible("trend");
        CmbYoy.SelectionChanged += (_, _) => RebuildIfVisible("yoy");
        foreach (var cb in new[] { CmbRankN, CmbRankBy }) cb.SelectionChanged += (_, _) => RebuildIfVisible("people");
        foreach (var cb in new[] { CmbHeat, CmbHeatRows }) cb.SelectionChanged += (_, _) => RebuildIfVisible("heat");
        CmbPerson.SelectionChanged += (_, _) => RebuildIfVisible("person");
        BtnPrevPerson.Click += (_, _) => StepPerson(-1);
        BtnNextPerson.Click += (_, _) => StepPerson(+1);
    }

    /// <summary>Marks a single tab dirty and rebuilds it when it is visible.</summary>
    void RebuildIfVisible(string key)
    {
        if (_busy) return;
        _dirty.Add(key);
        if (CurrentTab() == key) BuildTab(key);
    }

    void ShowPerson(string person)
    {
        if (CmbPerson.Items.OfType<string>().Contains(person)) CmbPerson.SelectedItem = person;
        _dirty.Add("person");
        SelectTab("person");
        if (CmbPerson.Items.OfType<string>().Contains(person)) CmbPerson.SelectedItem = person;
    }

    void StepPerson(int d)
    {
        if (CmbPerson.ItemCount == 0) return;
        CmbPerson.SelectedIndex = (Math.Max(0, CmbPerson.SelectedIndex) + d + CmbPerson.ItemCount) % CmbPerson.ItemCount;
    }

    // =====================================================================
    // grid column definitions (rebuilt on language change)
    // =====================================================================

    static DataGridTextColumn Col(string header, string path, double width, string? fmt = null, bool num = false)
    {
        var c = new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path) { StringFormat = fmt },
            Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
        };
        if (num) c.CellStyleClasses.Add("num");
        return c;
    }

    void ProjColumns(DataGrid g, bool compact)
    {
        g.Columns.Clear();
        g.Columns.Add(Col("SAP ID", nameof(ProjRow.SapId), 150));
        g.Columns.Add(Col(L.T("col.desc"), nameof(ProjRow.Description), compact ? 200 : 300));
        g.Columns.Add(Col(L.T("pr.customer"), nameof(ProjRow.Customer), 80));
        if (!compact) g.Columns.Add(Col(L.T("pr.type"), nameof(ProjRow.Type), 90));
        g.Columns.Add(Col(L.T("pr.cat"), nameof(ProjRow.Category), 70));
        g.Columns.Add(Col(_an?.UnitLabel ?? L.T("unit.hours"), nameof(ProjRow.Value), 90, "{0:#,0.#}", true));
        g.Columns.Add(Col(L.T("pr.people"), nameof(ProjRow.People), 60, null, true));
        g.Columns.Add(Col(L.T("pr.first"), nameof(ProjRow.First), 75));
        g.Columns.Add(Col(L.T("pr.last"), nameof(ProjRow.Last), 75));
        if (!compact) g.Columns.Add(Col(L.T("pr.profile"), nameof(ProjRow.Profile), 0));
    }

    void SetupGridColumns()
    {
        ProjColumns(GridProj, false);
        ProjColumns(GridPersonProj, true);

        GridFiles.Columns.Clear();
        GridFiles.Columns.Add(Col(L.T("da.file"), nameof(LoadedFile.Name), 230));
        GridFiles.Columns.Add(Col(L.T("da.periods"), nameof(LoadedFile.PeriodText), 190));
        GridFiles.Columns.Add(Col(L.T("da.rows"), nameof(LoadedFile.Rows), 80, "{0:#,0}", true));
        GridFiles.Columns.Add(Col(L.T("da.sheets"), "Report.SheetsText", 150));
        GridFiles.Columns.Add(Col(L.T("da.mapping"), "Report.MappingText", 0));

        GridIssues.Columns.Clear();
        GridIssues.Columns.Add(Col(L.T("da.level"), nameof(DataIssue.LevelText), 90));
        GridIssues.Columns.Add(Col(L.T("da.category"), nameof(DataIssue.Category), 110));
        GridIssues.Columns.Add(Col(L.T("da.what"), nameof(DataIssue.What), 360));
        GridIssues.Columns.Add(Col(L.T("da.detail"), nameof(DataIssue.Detail), 0));
        MarkAllDirty();
    }

    // =====================================================================
    // KPI cards
    // =====================================================================

    static readonly IBrush GoodBrush = new SolidColorBrush(Color.Parse("#2fa57a"));
    static readonly IBrush BadBrush = new SolidColorBrush(Color.Parse("#e5252f"));

    string AccentOf(Metric m)
    {
        if (m.Group == "split")
            return m.Id switch { "split:nb" => "#6764f6", "split:p" => "#b3bcff", _ => "#2f2da8" };
        int idx = _metrics.Where(x => x.Group == "cust").ToList().IndexOf(m);
        return Charts.Palette[(idx < 0 ? 7 : idx) % Charts.Palette.Length];
    }

    SColor CustCol(string id)
    {
        int idx = _metrics.Where(m => m.Group == "cust").Select(m => m.Id).ToList().IndexOf(id);
        return Charts.Col(idx < 0 ? 7 : idx);
    }

    /// <summary>Creates a KPI card. A delta starting with + or - is coloured (green when the direction is good).</summary>
    Border Card(string title, string value, string sub, string delta = "", bool goodUp = true, string accent = "#6764f6", string tip = "")
    {
        var sp = new StackPanel { Spacing = 3, Margin = new Thickness(14, 10, 16, 10) };
        sp.Children.Add(new TextBlock { Text = title, FontSize = 12, Classes = { "lbl" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 230 });
        sp.Children.Add(new TextBlock { Text = value, FontSize = 25, FontWeight = FontWeight.Bold });
        if (sub != "")
            sp.Children.Add(new TextBlock { Text = sub, FontSize = 12, Classes = { "lbl" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 230 });
        if (delta != "")
        {
            bool up = delta.StartsWith("+"), down = delta.StartsWith("-");
            var tb = new TextBlock { Text = (up ? "▲ " : down ? "▼ " : "") + delta, FontSize = 12, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 230 };
            if (up || down) tb.Foreground = (up == goodUp) ? GoodBrush : BadBrush;
            else tb.Opacity = 0.6;
            sp.Children.Add(tb);
        }
        var bar = new Border { Width = 5, Background = new SolidColorBrush(Color.Parse(accent)) };
        DockPanel.SetDock(bar, Dock.Left);
        var dock = new DockPanel();
        dock.Children.Add(bar);
        dock.Children.Add(sp);
        var card = new Border { Child = dock, Margin = new Thickness(0, 0, 10, 10), MinWidth = 200, ClipToBounds = true, Classes = { "card" } };
        if (tip != "") ToolTip.SetTip(card, tip);
        return card;
    }

    void BuildKpi(List<Row> rows)
    {
        PnlKpi.Children.Clear();
        if (rows.Count == 0)
        {
            PnlKpi.Children.Add(Card(L.T("kpi.noData"), "–", L.T("kpi.noData.sub"), "", true, "#9AA0A6"));
            return;
        }
        string u = _an.U;
        var periods = rows.Select(r => r.Period).Distinct().OrderBy(x => x).ToList();
        int people = rows.Select(r => r.Person).Distinct().Count();
        PnlKpi.Children.Add(Card(L.T("kpi.period"), $"{periods[0]} – {periods[^1]}", L.T("kpi.period.sub", periods.Count, people), "", true, "#9AA0A6"));
        PnlKpi.Children.Add(Card(L.T("m.total"), $"{rows.Sum(r => r.Hours):#,0} h", "", _an.Yoy(rows, Analytics.TOTAL), true, "#93a1ff", L.T("kpi.total.tip")));

        double inv = _an.Eval("inv", rows);
        PnlKpi.Children.Add(Card(L.T("m.inv"), $"{inv:#,0}{u}", "", _an.Yoy(rows, "inv"), true, "#6764f6", L.T("kpi.inv.tip")));
        double oh = rows.Where(r => r.Overhead).Sum(r => r.Hours);
        var ohAn = new Analytics(_metrics, false, _cfg.ValueUnit);
        PnlKpi.Children.Add(Card(L.T("m.oh"), $"{oh:#,0} h", "", ohAn.Yoy(rows, "oh"), false, "#9AA0A6", L.T("kpi.oh.tip")));
        PnlKpi.Children.Add(Card(L.T("m.pct"), Analytics.Fmt(Analytics.PCT, _an.Eval(Analytics.PCT, rows)), "", _an.Yoy(rows, Analytics.PCT), true, "#3fb68b", L.T("kpi.pct.tip")));
        PnlKpi.Children.Add(Card(L.T("m.perp"), Analytics.Fmt(Analytics.PERP, _an.Eval(Analytics.PERP, rows)), _an.UnitOf(Analytics.PERP), _an.Yoy(rows, Analytics.PERP), true, "#e0a800", L.T("kpi.perp.tip")));
        PnlKpi.Children.Add(Card(L.T("m.heads"), Analytics.Fmt(Analytics.HEADS, _an.Eval(Analytics.HEADS, rows)), "", _an.Yoy(rows, Analytics.HEADS), true, "#00a3e0", L.T("kpi.heads.tip")));
        PnlKpi.Children.Add(Card(L.T("kpi.projects"), rows.Where(r => !r.Overhead).Select(r => r.SapId).Distinct().Count().ToString(), "", "", true, "#b565a7"));

        foreach (var g in new[] { "cust", "split" })
            foreach (var m in _metrics.Where(m => m.Group == g))
            {
                double v = _an.Eval(m.Id, rows);
                if (v <= 0) continue;
                PnlKpi.Children.Add(Card(m.Name, $"{v:#,0}{u}", L.T("kpi.shareOfInv", inv > 0 ? v / inv * 100 : 0),
                                         _an.Yoy(rows, m.Id), true, AccentOf(m)));
            }
    }

    // =====================================================================
    // statistics by year
    // =====================================================================

    void BuildStats(List<Row> rows)
    {
        var years = rows.Select(r => r.Year).Distinct().OrderBy(x => x).ToList();
        var byYear = years.ToDictionary(y => y, y => rows.Where(r => r.Year == y).ToList());
        var ids = new List<string> { Analytics.TOTAL };
        ids.AddRange(_metrics.Select(m => m.Id));
        ids.AddRange(new[] { Analytics.PCT, Analytics.PERP, Analytics.HEADS });

        var headers = years.Select(y => y.ToString()).ToList();
        for (int i = 1; i < years.Count; i++) headers.Add($"Δ {years[i]} vs {years[i - 1]}");

        var list = new List<TableRow>();
        var months = years.Select(y => byYear[y].Select(r => r.Month).Distinct().Count().ToString()).ToList();
        for (int i = 1; i < years.Count; i++)
        {
            var (a, _) = Analytics.CommonMonths(byYear[years[i - 1]], byYear[years[i]]);
            months.Add(L.T("ov.common", a.Select(r => r.Month).Distinct().Count()));
        }
        list.Add(new TableRow { Label = L.T("ov.months"), Cells = months.ToArray() });

        foreach (var id in ids)
        {
            var cells = years.Select(y => Analytics.Fmt(id, _an.Eval(id, byYear[y]))).ToList();
            for (int i = 1; i < years.Count; i++)
            {
                var (a, b) = Analytics.CommonMonths(byYear[years[i - 1]], byYear[years[i]]);
                cells.Add(a.Count == 0 ? "" : Analytics.Delta(id, _an.Eval(id, a), _an.Eval(id, b)));
            }
            var label = _an.NameOf(id) + (id == Analytics.TOTAL ? " (h)" : "");
            list.Add(new TableRow { Label = label, Cells = cells.ToArray() });
        }

        TextGridColumns(GridStats, L.T("ov.indicator"), 280, headers, 130);
        GridStats.ItemsSource = list;
        _statHeaders = headers; _lastStats = list;
    }

    static void TextGridColumns(DataGrid g, string labelHdr, int labelW, List<string> headers, int w)
    {
        g.Columns.Clear();
        g.Columns.Add(new DataGridTextColumn { Header = labelHdr, Binding = new Binding("Label"), Width = new DataGridLength(labelW) });
        for (int i = 0; i < headers.Count; i++)
        {
            var col = new DataGridTextColumn { Header = headers[i], Binding = new Binding($"Cells[{i}]"), Width = new DataGridLength(headers[i].StartsWith("Δ") ? w + 40 : w) };
            col.CellStyleClasses.Add("num");
            g.Columns.Add(col);
        }
    }

    // =====================================================================
    // pivot table
    // =====================================================================

    List<Metric> CheckedMetrics(Panel pnl)
    {
        var ids = CheckedTags(pnl);
        return _metrics.Where(m => ids.Contains(m.Id)).ToList();
    }

    void BuildTable(List<Row> rows)
    {
        var monthly = CheckedMetrics(PnlMonthly);
        var summary = CheckedMetrics(PnlSummary);
        if (ChkSplit.IsChecked == true)
            foreach (var m in _metrics.Where(m => m.Group == "split" && !summary.Contains(m)))
                summary.Add(m);
        int grp = CmbGroup.SelectedIndex, colMode = CmbCols.SelectedIndex, sort = CmbSort.SelectedIndex;

        string Key(Row r) => grp switch
        {
            1 => r.Cc,
            2 => r.Customer == "-" ? L.T("f.cust.oh") : r.Customer,
            3 => r.SapId,
            4 => r.Year.ToString(),
            5 => r.Profile,
            _ => r.Person
        };
        string ColKey(Row r) => colMode switch
        {
            1 => $"{r.Year} Q{(r.Month - 1) / 3 + 1}",
            2 => r.Year.ToString(),
            _ => r.Period
        };

        var colKeys = colMode == 3 ? new List<string>() : rows.Select(ColKey).Distinct().OrderBy(x => x).ToList();
        var cIdx = colKeys.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
        int P = colKeys.Count, M = monthly.Count, S = summary.Count;
        int width = P * M + S + 1;

        var headers = new List<string>(); var fmts = new List<string>();
        foreach (var p in colKeys) foreach (var m in monthly) { headers.Add($"{p}\n{m.Short}"); fmts.Add(HF); }
        foreach (var m in summary) { headers.Add($"Σ\n{m.Short}"); fmts.Add(HF); }
        headers.Add(L.T("m.pct.s")); fmts.Add("{0:0.0}");

        var find = TxtTableFind.Text?.Trim() ?? "";
        var result = new List<ReportRow>();
        foreach (var g in rows.GroupBy(Key))
        {
            var cells = new double[width]; double invV = 0, all = 0, invH = 0, allV = 0;
            foreach (var r in g)
            {
                if (P > 0)
                {
                    int pi = cIdx[ColKey(r)];
                    for (int mi = 0; mi < M; mi++) if (monthly[mi].Pred(r)) cells[pi * M + mi] += V(r);
                }
                for (int si = 0; si < S; si++) if (summary[si].Pred(r)) cells[P * M + si] += V(r);
                all += r.Hours; allV += V(r);
                if (!r.Overhead) { invH += r.Hours; invV += V(r); }
            }
            cells[width - 1] = all > 0 ? invH / all * 100 : 0;
            string sub = grp == 0 ? _personCc.GetValueOrDefault(g.Key, "") : grp == 3 ? g.First().Desc : "";
            if (find != "" && !g.Key.Contains(find, StringComparison.OrdinalIgnoreCase) && !sub.Contains(find, StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add(new ReportRow { Label = g.Key, Sub = sub, Cells = cells, InvTotal = invV, AllTotal = allV, Pct = cells[width - 1] });
        }

        result = sort switch
        {
            1 => result.OrderByDescending(r => r.InvTotal).ToList(),
            2 => result.OrderByDescending(r => r.AllTotal).ToList(),
            3 => result.OrderByDescending(r => r.Pct).ToList(),
            4 => result.OrderBy(r => r.Pct).ToList(),
            _ => result.OrderBy(r => grp == 0 ? r.Sub : "").ThenBy(r => r.Label, StringComparer.CurrentCulture).ToList()
        };

        // total line (over the shown groups)
        var tot = new double[width];
        foreach (var r in result) for (int i = 0; i < width - 1; i++) tot[i] += r.Cells[i];
        var shown = result.Select(r => r.Label).ToHashSet();
        var shownRows = find == "" ? rows : rows.Where(r => shown.Contains(Key(r))).ToList();
        double tAll = shownRows.Sum(r => r.Hours), tInv = shownRows.Where(r => !r.Overhead).Sum(r => r.Hours);
        tot[width - 1] = tAll > 0 ? tInv / tAll * 100 : 0;
        result.Insert(0, new ReportRow { Label = find == "" ? L.T("tb.total") : L.T("tb.totalShown"), Cells = tot, Pct = tot[width - 1], IsTotal = true });

        string labelHdr = grp switch { 1 => L.T("g.cc"), 2 => L.T("g.customer"), 3 => L.T("g.project"), 4 => L.T("g.year"), 5 => L.T("g.profile"), _ => L.T("g.person") };
        string subHdr = grp == 0 ? "CC" : grp == 3 ? L.T("col.desc") : "";

        GridTable.Columns.Clear();
        GridTable.Columns.Add(new DataGridTextColumn { Header = labelHdr, Binding = new Binding("Label"), Width = new DataGridLength(grp == 3 ? 150 : 200) });
        GridTable.Columns.Add(new DataGridTextColumn { Header = subHdr, Binding = new Binding("Sub"), Width = new DataGridLength(grp == 3 ? 240 : 70) });
        for (int i = 0; i < width; i++)
        {
            var col = new DataGridTextColumn
            {
                Header = headers[i],
                Binding = new Binding($"Cells[{i}]") { StringFormat = fmts[i] },
                Width = new DataGridLength(i == width - 1 ? 70 : colMode == 1 ? 104 : colMode == 2 ? 92 : 80)
            };
            col.CellStyleClasses.Add("num");
            GridTable.Columns.Add(col);
        }
        GridTable.ItemsSource = result;
        _lastHeaders = headers; _lastReport = result; _lastFormats = fmts;
        _lastLabelHdr = labelHdr; _lastSubHdr = subHdr;
    }

    /// <summary>Copies the pivot table to the clipboard as tab separated text (paste into Excel/e-mail).</summary>
    async Task CopyTable()
    {
        EnsureBuilt("table");
        if (_lastReport.Count == 0 || Clipboard == null) return;
        var sb = new StringBuilder();
        sb.Append(_lastLabelHdr).Append('\t').Append(_lastSubHdr);
        foreach (var h in _lastHeaders) sb.Append('\t').Append(h.Replace("\n", " "));
        sb.AppendLine();
        foreach (var r in _lastReport)
        {
            sb.Append(r.Label).Append('\t').Append(r.Sub);
            foreach (var c in r.Cells) sb.Append('\t').Append(Math.Round(c, 2).ToString(CultureInfo.CurrentCulture));
            sb.AppendLine();
        }
        await Clipboard.SetTextAsync(sb.ToString());
        Status(L.T("st.copied", _lastReport.Count));
    }

    // =====================================================================
    // projects
    // =====================================================================

    void BuildProjects(List<Row> rows)
    {
        ProjColumns(GridProj, false);
        _lastProj = _an.Projects(rows);
        var find = TxtProjFind.Text?.Trim() ?? "";
        var shown = find == "" ? _lastProj
            : _lastProj.Where(p => (p.SapId + " " + p.Description + " " + p.Customer + " " + p.Profile).Contains(find, StringComparison.OrdinalIgnoreCase)).ToList();
        GridProj.ItemsSource = shown;
        TxtProjCount.Text = L.T("pr.count", shown.Count, _lastProj.Count, shown.Sum(p => p.Value).ToString("#,0", CultureInfo.CurrentCulture), _an.UnitLabel);
    }

    // =====================================================================
    // data tab: files and data check
    // =====================================================================

    void BuildFiles()
    {
        GridFiles.ItemsSource = null;
        GridFiles.ItemsSource = _files.ToList();
    }

    void BuildIssues()
    {
        var l = new List<DataIssue>();
        void Add(IssueLevel lvl, string cat, string what, string detail) => l.Add(new DataIssue(lvl, L.T("cat." + cat), what, detail));
        string Top<T>(IEnumerable<T> items, int n, Func<T, string> f) =>
            string.Join("; ", items.Take(n).Select(f)) + (items.Count() > n ? "; …" : "");

        if (_raw.Count == 0)
        {
            Add(IssueLevel.Info, "data", L.T("is.noData"), L.T("is.noData.d"));
            GridIssues.ItemsSource = l; return;
        }

        // --- files ---
        foreach (var f in _files)
        {
            var r = f.Report;
            foreach (var w in r.Warnings) Add(IssueLevel.Warning, "files", f.Name, w);
            if (r.BadNumbers > 0) Add(IssueLevel.Warning, "files", f.Name, L.T("is.badNumbers", r.BadNumbers));
            if (r.SkippedNoPerson > 0) Add(IssueLevel.Info, "files", f.Name, L.T("is.noPerson", r.SkippedNoPerson));
            if (r.Unmapped.Count > 0) Add(IssueLevel.Info, "files", f.Name, L.T("is.unmapped", string.Join(", ", r.Unmapped)));
            if (r.Sheets.Count > 1) Add(IssueLevel.Info, "files", f.Name, L.T("is.sheets", string.Join(", ", r.Sheets)));
        }

        // --- rows and inclusion ---
        Add(IssueLevel.Info, "data", L.T("is.rows"), L.T("is.rows.d", _raw.Count, _rows.Count, _lastRows.Count));
        var outCc = _raw.Where(r => !_cls.InCostCenters(r)).GroupBy(r => r.Cc).OrderByDescending(g => g.Count()).ToList();
        if (outCc.Count > 0)
            Add(IssueLevel.Info, "rules", L.T("is.outCc", outCc.Sum(g => g.Count())),
                Top(outCc, 10, g => $"{(g.Key == "" ? L.T("f.noCc") : g.Key)} ({g.Count():#,0})"));

        var excluded = _raw.Where(r => _cls.InCostCenters(r) && _cls.InRestricted(r) && !_cls.IsIncluded(r))
                           .GroupBy(r => r.Person).ToList();
        if (excluded.Count > 0)
            Add(IssueLevel.Info, "rules", L.T("is.excluded", excluded.Count),
                L.T("is.excluded.d", excluded.Sum(g => g.Sum(r => r.Hours)).ToString("#,0"), Top(excluded, 8, g => g.Key)));

        // --- profiles ---
        var periods = _raw.Select(r => r.Period).Distinct().OrderBy(x => x).ToList();
        var uncovered = periods.Where(p => !_cfg.HasProfileFor(p)).ToList();
        if (uncovered.Count > 0 && _cfg.Profiles.Any(p => !p.IsOpen))
            Add(IssueLevel.Warning, "profiles", L.T("is.uncovered", uncovered.Count),
                L.T("is.uncovered.d", string.Join(", ", uncovered), _cfg.ProfileFor(uncovered[0]).Name));
        foreach (var p in _cfg.Profiles)
        {
            var used = periods.Where(x => _cls.ProfileOf(x) == p).ToList();
            Add(IssueLevel.Info, "profiles", L.T("is.profile", p.Name, p.RangeText),
                used.Count == 0 ? L.T("is.profile.unused") : L.T("is.profile.used", used.Count, used[0], used[^1]));
            if (p.RestrictedCostCenters.Count > 0 && p.RelevantPeople.Count == 0 && used.Count > 0)
                Add(IssueLevel.Warning, "profiles", L.T("is.emptyPeople", p.Name), L.T("is.emptyPeople.d", string.Join(", ", p.RestrictedCostCenters)));
            if (used.Count > 0)
            {
                var names = _raw.Where(r => used.Contains(r.Period)).Select(r => r.Person.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var n in p.RelevantPeople.Where(n => !names.Contains(n.Trim())))
                    Add(IssueLevel.Warning, "profiles", L.T("is.relMissing", p.Name), L.T("is.relMissing.d", n));
            }
        }
        for (int i = 0; i < _cfg.Profiles.Count; i++)
            for (int j = i + 1; j < _cfg.Profiles.Count; j++)
            {
                var a = _cfg.Profiles[i]; var b = _cfg.Profiles[j];
                if (a.IsOpen || b.IsOpen) continue;
                var ov = periods.Where(p => a.Covers(p) && b.Covers(p)).ToList();
                if (ov.Count > 0) Add(IssueLevel.Info, "profiles", L.T("is.overlap", a.Name, b.Name), L.T("is.overlap.d", ov.Count, a.Name));
            }

        // --- data quality ---
        var unk = _rows.Where(r => r.Customer == "??").GroupBy(r => (r.SapId, r.Desc)).OrderByDescending(g => g.Sum(r => r.Hours)).ToList();
        if (unk.Count > 0)
            Add(IssueLevel.Warning, "data", L.T("is.unknownCust", unk.Count),
                Top(unk, 8, g => $"{g.Key.SapId} {g.Key.Desc} ({g.Sum(r => r.Hours):#,0} h)"));

        var known = _cfg.Customers.Select(c => c.Trim().ToUpperInvariant()).ToHashSet();
        var other = _rows.Where(r => !r.Overhead && r.Customer != "??" && !known.Contains(r.Customer))
                         .GroupBy(r => r.Customer).OrderByDescending(g => g.Sum(r => r.Hours)).ToList();
        if (other.Count > 0)
            Add(IssueLevel.Info, "data", L.T("is.otherCust", other.Count), Top(other, 12, g => $"{g.Key} ({g.Sum(r => r.Hours):#,0} h)"));

        var noAb = _raw.Where(r => _cls.IsIncluded(r) && r.Abacus.Trim().Length < 2 && !_cls.IsOverhead(r.Period, r.SapId, r.Desc))
                       .GroupBy(r => (r.SapId, r.Desc)).OrderByDescending(g => g.Sum(r => r.Hours)).ToList();
        if (noAb.Count > 0)
            Add(IssueLevel.Info, "data", L.T("is.noAbacus", noAb.Count),
                Top(noAb, 8, g => $"{g.Key.SapId} {g.Key.Desc} ({g.Sum(r => r.Hours):#,0} h)"));

        var neg = _rows.Where(r => r.Hours < 0).ToList();
        if (neg.Count > 0) Add(IssueLevel.Warning, "data", L.T("is.negative"), L.T("is.negative.d", neg.Count, neg.Sum(r => r.Hours).ToString("#,0.#")));

        var dup = _raw.GroupBy(r => (r.Period, r.Person, r.Cc, r.SapId, r.Desc, r.Hours, r.Value)).Where(g => g.Count() > 1).ToList();
        if (dup.Count > 0)
            Add(IssueLevel.Warning, "data", L.T("is.dup", dup.Count), Top(dup, 6, g => $"{g.Key.Period} {g.Key.Person} {g.Key.SapId} {g.Key.Hours} h ×{g.Count()}"));

        var multi = _rows.GroupBy(r => r.Person).Where(g => g.Select(r => r.Cc).Distinct().Count() > 1).ToList();
        if (multi.Count > 0)
            Add(IssueLevel.Info, "data", L.T("is.multiCc", multi.Count), Top(multi, 6, g => $"{g.Key}: {string.Join("/", g.Select(r => r.Cc).Distinct())}"));

        var rp = _rows.Select(r => r.Period).Distinct().OrderBy(x => x).ToList();
        if (rp.Count > 1)
        {
            var have = rp.ToHashSet();
            var missing = Periods.Range(rp[0], rp[^1]).Where(p => !have.Contains(p)).ToList();
            if (missing.Count > 0) Add(IssueLevel.Warning, "data", L.T("is.missingMonths"), string.Join(", ", missing));
        }

        // --- classification listings ---
        foreach (var g in _rows.Where(r => r.Overhead).GroupBy(r => r.SapId).OrderByDescending(g => g.Sum(r => r.Hours)))
            Add(IssueLevel.Info, "overhead", g.Key, $"{g.First().Desc} | {g.Sum(r => r.Hours):#,0} h");
        var split = _cfg.SplitCustomer.ToUpperInvariant();
        var nbs = _rows.Where(r => r.Newbuild && r.Customer == split).GroupBy(r => r.SapId).OrderByDescending(g => g.Sum(r => r.Hours)).ToList();
        if (nbs.Count > 0)
        {
            Add(IssueLevel.Info, "nb", L.T("is.nb", nbs.Count, split), L.T("is.sumHours", nbs.Sum(g => g.Sum(r => r.Hours)).ToString("#,0")));
            foreach (var g in nbs) Add(IssueLevel.Info, "nb", g.Key, $"{g.First().Desc} | {g.Sum(r => r.Hours):#,0} h");
        }
        var prs = _rows.Where(r => r.PerRequest).GroupBy(r => r.SapId).OrderByDescending(g => g.Sum(r => r.Hours)).ToList();
        if (prs.Count > 0)
        {
            Add(IssueLevel.Info, "p", L.T("is.p", prs.Count), L.T("is.sumHours", prs.Sum(g => g.Sum(r => r.Hours)).ToString("#,0")));
            foreach (var g in prs) Add(IssueLevel.Info, "p", g.Key, $"{g.First().Desc} | {g.First().Customer} | {g.Sum(r => r.Hours):#,0} h");
        }

        var lists = new[] { L.T("cat.overhead"), L.T("cat.nb"), L.T("cat.p") };
        l = CmbIssueLevel.SelectedIndex switch
        {
            1 => l.Where(i => i.Level != IssueLevel.Info).ToList(),
            2 => l.Where(i => lists.Contains(i.Category)).ToList(),
            _ => l
        };
        GridIssues.ItemsSource = l;
    }
}
