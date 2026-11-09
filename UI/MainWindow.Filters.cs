using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
namespace HoursTool;

public partial class MainWindow
{
    // =====================================================================
    // building the side panel
    // =====================================================================

    /// <summary>Creates a check box that triggers <paramref name="onChange"/> unless the window is busy.</summary>
    CheckBox Chk(string text, string tag, bool on, Action onChange)
    {
        var c = new CheckBox { Content = text, Tag = tag, IsChecked = on };
        c.IsCheckedChanged += (_, _) => { if (!_busy) onChange(); };
        return c;
    }

    static string TagOf(Control c) => (c.Tag ?? (c as ContentControl)?.Content)?.ToString() ?? "";

    static HashSet<string> CheckedTags(Panel pnl) =>
        pnl.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(TagOf).ToHashSet();

    static bool AllChecked(Panel pnl) => pnl.Children.OfType<CheckBox>().All(c => c.IsChecked == true);

    /// <summary>
    /// Fills a panel with check boxes, keeping the state of items that existed before.
    /// New items are checked by default, so newly loaded years/customers are visible immediately.
    /// </summary>
    void FillChecks(Panel pnl, IEnumerable<(string tag, string text)> items)
    {
        var oldAll = pnl.Children.OfType<CheckBox>().Select(TagOf).ToHashSet();
        var oldOn = CheckedTags(pnl);
        pnl.Children.Clear();
        foreach (var (tag, text) in items)
            pnl.Children.Add(Chk(text, tag, !oldAll.Contains(tag) || oldOn.Contains(tag), Refresh));
    }

    void SetAll(Panel pnl, bool on)
    {
        _busy = true;
        foreach (var c in pnl.Children.OfType<CheckBox>()) c.IsChecked = on;
        _busy = false;
        Refresh();
    }

    /// <summary>Builds language-dependent panels that do not depend on data (months, quarters, heatmap modes).</summary>
    void BuildStaticPanels()
    {
        bool prev = _busy; _busy = true;
        var on = PnlMonths.Children.Count == 0 ? Enumerable.Range(1, 12).Select(i => i.ToString()).ToHashSet() : CheckedTags(PnlMonths);
        PnlMonths.Children.Clear();
        for (int i = 1; i <= 12; i++)
            PnlMonths.Children.Add(Chk(L.MonthsShort[i - 1], i.ToString(), on.Contains(i.ToString()), Refresh));

        PnlQuarters.Children.Clear();
        for (int q = 1; q <= 4; q++)
        {
            int qq = q;
            var b = new Button { Content = $"Q{q}", Classes = { "preset" } };
            ToolTip.SetTip(b, L.T("f.quarter.tip", q));
            b.Click += (_, _) => SetMonths(Enumerable.Range((qq - 1) * 3 + 1, 3));
            PnlQuarters.Children.Add(b);
        }

        var heatSel = Math.Max(0, CmbHeat.SelectedIndex);
        CmbHeat.Items.Clear();
        foreach (var k in new[] { "he.inv", "he.oh", "he.pct", "he.total", "he.nb", "he.p", "he.rest" })
            CmbHeat.Items.Add(new ComboBoxItem { Content = L.T(k, _cfg.SplitCustomer.ToUpperInvariant()) });
        CmbHeat.SelectedIndex = heatSel;
        _busy = prev;
    }

    /// <summary>Rebuilds the measure check boxes (table columns) and the measure combo boxes, keeping selections by id.</summary>
    void BuildMetricPanels()
    {
        bool prev = _busy; _busy = true;
        var monOn = PnlMonthly.Children.Count == 0 ? new HashSet<string> { "inv", "oh" } : CheckedTags(PnlMonthly);
        var sumOn = CheckedTags(PnlSummary);
        var sumAll = PnlSummary.Children.OfType<CheckBox>().Select(TagOf).ToHashSet();
        PnlMonthly.Children.Clear(); PnlSummary.Children.Clear();
        foreach (var m in _metrics)
        {
            PnlMonthly.Children.Add(Chk(m.Name, m.Id, monOn.Contains(m.Id), Refresh));
            PnlSummary.Children.Add(Chk(m.Name, m.Id, !sumAll.Contains(m.Id) || sumOn.Contains(m.Id), Refresh));
        }
        var series = new Analytics(_metrics, Money, _cfg.ValueUnit).Series();
        foreach (var cb in new[] { CmbYoy, CmbTrend })
        {
            var sel = SelectedId(cb) ?? "inv";
            cb.Items.Clear();
            foreach (var (id, name) in series)
                cb.Items.Add(new ComboBoxItem { Content = name, Tag = id });
            cb.SelectedItem = cb.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == sel)
                              ?? cb.Items.OfType<object>().FirstOrDefault();
        }
        ChkSplit.IsVisible = !string.IsNullOrWhiteSpace(_cfg.SplitCustomer);
        _busy = prev;
    }

    /// <summary>Id stored in the Tag of the selected <see cref="ComboBoxItem"/>.</summary>
    static string? SelectedId(ComboBox cb) => (cb.SelectedItem as ComboBoxItem)?.Tag as string;

    /// <summary>Rebuilds the panels whose items come from the data (years, periods, cost centres, customers, people).</summary>
    void FillDataPanels()
    {
        FillChecks(PnlYears, _rows.Select(r => r.Year).Distinct().OrderBy(x => x).Select(y => (y.ToString(), y.ToString())));
        FillChecks(PnlCc, _rows.Select(r => r.Cc).Distinct().OrderBy(x => x, StringComparer.Ordinal).Select(c => (c, c == "" ? L.T("f.noCc") : c)));
        FillChecks(PnlCust, _rows.Select(r => r.Customer).Distinct()
                                 .OrderBy(x => x == "-" ? 2 : x == "??" ? 1 : 0).ThenBy(x => x, StringComparer.Ordinal)
                                 .Select(c => (c, c == "-" ? L.T("f.cust.oh") : c == "??" ? L.T("f.cust.unknown") : c)));

        // period range: keep an explicit selection, otherwise follow the data bounds
        var periods = _rows.Select(r => r.Period).Distinct().OrderBy(x => x).ToList();
        string? from = CmbFrom.SelectedIndex > 0 ? CmbFrom.SelectedItem as string : null;
        string? to = CmbTo.SelectedIndex >= 0 && CmbTo.SelectedIndex < CmbTo.ItemCount - 1 ? CmbTo.SelectedItem as string : null;
        CmbFrom.Items.Clear(); CmbTo.Items.Clear();
        foreach (var p in periods) { CmbFrom.Items.Add(p); CmbTo.Items.Add(p); }
        CmbFrom.SelectedIndex = from != null && periods.Contains(from) ? periods.IndexOf(from) : (periods.Count > 0 ? 0 : -1);
        CmbTo.SelectedIndex = to != null && periods.Contains(to) ? periods.IndexOf(to) : periods.Count - 1;

        BuildPeoplePanel();
    }

    // =====================================================================
    // events
    // =====================================================================

    void WireFilterEvents()
    {
        foreach (var cb in new[] { CmbUnit, CmbScope, CmbCat, CmbFrom, CmbTo })
            cb.SelectionChanged += (_, _) => Refresh();
        foreach (var ck in new[] { ChkCommon, ChkSplit })
            ck.IsCheckedChanged += (_, _) => Refresh();
        TxtProject.TextChanged += (_, _) => RequestRefresh();
        TxtPeople.TextChanged += (_, _) => BuildPeoplePanel();

        BtnYearsAll.Click += (_, _) => SetAll(PnlYears, true);
        BtnYearsNone.Click += (_, _) => SetAll(PnlYears, false);
        BtnMonthsAll.Click += (_, _) => SetAll(PnlMonths, true);
        BtnMonthsNone.Click += (_, _) => SetAll(PnlMonths, false);
        BtnCcAll.Click += (_, _) => SetAll(PnlCc, true);
        BtnCcNone.Click += (_, _) => SetAll(PnlCc, false);
        BtnCustAll.Click += (_, _) => SetAll(PnlCust, true);
        BtnCustNone.Click += (_, _) => SetAll(PnlCust, false);
        BtnAll.Click += (_, _) => SetVisiblePeople(true);
        BtnNone.Click += (_, _) => SetVisiblePeople(false);
        BtnReset.Click += (_, _) => ResetFilters();
        BtnResetBar.Click += (_, _) => ResetFilters();

        BtnPresetAll.Click += (_, _) => ResetPeriod();
        BtnPresetThisYear.Click += (_, _) => PresetYears(1, 0);
        BtnPresetLastYear.Click += (_, _) => PresetYears(1, 1);
        BtnPresetYtd.Click += (_, _) => PresetYtd();
        BtnPreset12.Click += (_, _) => PresetLast12();
    }

    // =====================================================================
    // presets
    // =====================================================================

    List<int> DataYears() => _rows.Select(r => r.Year).Distinct().OrderBy(x => x).ToList();

    void SetChecks(Panel pnl, Func<string, bool> on)
    {
        foreach (var c in pnl.Children.OfType<CheckBox>()) c.IsChecked = on(TagOf(c));
    }

    void SetFullRange()
    {
        CmbFrom.SelectedIndex = CmbFrom.ItemCount > 0 ? 0 : -1;
        CmbTo.SelectedIndex = CmbTo.ItemCount - 1;
    }

    void SetMonths(IEnumerable<int> months)
    {
        var set = months.Select(m => m.ToString()).ToHashSet();
        _busy = true;
        SetChecks(PnlMonths, set.Contains);
        _busy = false;
        Refresh();
    }

    void ResetPeriod()
    {
        _busy = true;
        SetChecks(PnlYears, _ => true);
        SetChecks(PnlMonths, _ => true);
        ChkCommon.IsChecked = false;
        SetFullRange();
        _busy = false;
        Refresh();
    }

    /// <summary>Selects <paramref name="count"/> years ending <paramref name="back"/> years before the latest year in the data.</summary>
    void PresetYears(int count, int back)
    {
        var ys = DataYears();
        if (ys.Count == 0) return;
        int last = ys[^1] - back;
        _busy = true;
        SetChecks(PnlYears, t => int.TryParse(t, out var y) && y <= last && y > last - count);
        SetChecks(PnlMonths, _ => true);
        ChkCommon.IsChecked = false;
        SetFullRange();
        _busy = false;
        Refresh();
    }

    /// <summary>Year-to-date comparison: the last two years, only months present in the latest year.</summary>
    void PresetYtd()
    {
        var ys = DataYears();
        if (ys.Count == 0) return;
        int last = ys[^1];
        var months = _rows.Where(r => r.Year == last).Select(r => r.Month).ToHashSet();
        _busy = true;
        SetChecks(PnlYears, t => int.TryParse(t, out var y) && y >= last - 1);
        SetChecks(PnlMonths, t => months.Contains(int.Parse(t)));
        ChkCommon.IsChecked = true;
        SetFullRange();
        _busy = false;
        Refresh();
    }

    /// <summary>The last 12 periods present in the data (uses the From/To range).</summary>
    void PresetLast12()
    {
        if (CmbTo.ItemCount == 0) return;
        _busy = true;
        SetChecks(PnlYears, _ => true);
        SetChecks(PnlMonths, _ => true);
        ChkCommon.IsChecked = false;
        CmbTo.SelectedIndex = CmbTo.ItemCount - 1;
        var last = (string)CmbTo.SelectedItem!;
        var first = last;
        for (int i = 0; i < 11; i++) first = Periods.Prev(first);
        var items = CmbFrom.Items.OfType<string>().ToList();
        int idx = items.FindIndex(p => string.CompareOrdinal(p, first) >= 0);
        CmbFrom.SelectedIndex = Math.Max(0, idx);
        _busy = false;
        Refresh();
    }

    void ResetFilters()
    {
        _busy = true;
        foreach (var pnl in new Panel[] { PnlYears, PnlMonths, PnlCc, PnlCust })
            SetChecks(pnl, _ => true);
        CmbScope.SelectedIndex = 0; CmbCat.SelectedIndex = 0;
        TxtProject.Text = ""; ChkCommon.IsChecked = false;
        SetFullRange();
        _cfg.DeselectedPeople.Clear(); _cfg.Save();
        BuildPeoplePanel();
        _busy = false;
        Refresh();
        Status(L.T("st.filtersReset"));
    }

    // =====================================================================
    // people
    // =====================================================================

    bool IsSel(string person) => !_cfg.DeselectedPeople.Contains(person);

    void SetPerson(string p, bool on)
    {
        _cfg.DeselectedPeople.Remove(p);
        if (!on) _cfg.DeselectedPeople.Add(p);
    }

    IEnumerable<string> VisiblePeople()
    {
        var txt = TxtPeople.Text?.Trim() ?? "";
        return _personCc.Where(kv => txt == "" || kv.Key.Contains(txt, StringComparison.OrdinalIgnoreCase)
                                               || kv.Value.Contains(txt, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key);
    }

    void BuildPeoplePanel()
    {
        bool prev = _busy; _busy = true;
        PnlPeople.Children.Clear();
        foreach (var p in VisiblePeople())
        {
            var name = p;
            var c = new CheckBox { Content = $"{name}  [{_personCc[name]}]", Tag = name, IsChecked = IsSel(name) };
            c.IsCheckedChanged += (_, _) =>
            {
                if (_busy) return;
                SetPerson(name, c.IsChecked == true); _cfg.Save(); RequestRefresh();
            };
            PnlPeople.Children.Add(c);
        }
        _busy = prev;
    }

    void SetVisiblePeople(bool on)
    {
        foreach (var p in VisiblePeople().ToList()) SetPerson(p, on);
        _cfg.Save(); BuildPeoplePanel(); Refresh();
    }

    // =====================================================================
    // filtering
    // =====================================================================

    /// <summary>Parses the project filter: terms separated by ';', a leading '-' excludes.</summary>
    static (List<string> inc, List<string> exc) ProjectTerms(string? text)
    {
        var terms = (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return (terms.Where(t => !t.StartsWith("-")).ToList(),
                terms.Where(t => t.StartsWith("-") && t.Length > 1).Select(t => t.Substring(1).Trim()).ToList());
    }

    /// <summary>Applies all side-panel filters to <see cref="_rows"/>.</summary>
    List<Row> Filtered()
    {
        var years = CheckedTags(PnlYears).Select(int.Parse).ToHashSet();
        var months = CheckedTags(PnlMonths).Select(int.Parse).ToHashSet();
        var ccs = CheckedTags(PnlCc);
        var custs = CheckedTags(PnlCust);
        int scope = CmbScope.SelectedIndex, cat = CmbCat.SelectedIndex;
        var (inc, exc) = ProjectTerms(TxtProject.Text);
        var deselected = _cfg.DeselectedPeople.ToHashSet();
        var from = CmbFrom.SelectedItem as string;
        var to = CmbTo.SelectedItem as string;
        var split = _cfg.SplitCustomer.Trim().ToUpperInvariant();

        bool ProjOk(Row r)
        {
            if (inc.Count == 0 && exc.Count == 0) return true;
            var hay = r.SapId + " " + r.Desc;
            if (exc.Any(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase))) return false;
            return inc.Count == 0 || inc.Any(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        bool CatOk(Row r) => cat switch
        {
            1 => r.Newbuild && r.Customer == split,
            2 => !(r.Newbuild && r.Customer == split),
            3 => r.PerRequest,
            4 => !r.PerRequest,
            5 => !r.Overhead && !r.Newbuild && !r.PerRequest,
            _ => true
        };

        var rows = _rows.Where(r =>
            years.Contains(r.Year) && months.Contains(r.Month) && ccs.Contains(r.Cc) && custs.Contains(r.Customer) &&
            (from == null || string.CompareOrdinal(r.Period, from) >= 0) &&
            (to == null || string.CompareOrdinal(r.Period, to) <= 0) &&
            !deselected.Contains(r.Person) &&
            (scope == 0 || (scope == 1 && !r.Overhead) || (scope == 2 && r.Overhead)) &&
            CatOk(r) && ProjOk(r)).ToList();

        if (ChkCommon.IsChecked == true)
        {
            var perYear = rows.GroupBy(r => r.Year).Select(g => g.Select(r => r.Month).ToHashSet()).ToList();
            if (perYear.Count > 1)
            {
                var common = perYear[0];
                foreach (var s in perYear.Skip(1)) common.IntersectWith(s);
                rows = rows.Where(r => common.Contains(r.Month)).ToList();
            }
        }
        return rows;
    }

    // =====================================================================
    // active filter chips
    // =====================================================================

    /// <summary>Shows a chip for every active (non-default) filter; clicking the × clears that filter.</summary>
    void UpdateChips()
    {
        PnlChips.Children.Clear();
        void Chip(string text, Action? clear)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            if (clear != null)
            {
                var x = new Button { Content = "✕", Classes = { "link" }, Padding = new Thickness(2, 0), FontSize = 11 };
                ToolTip.SetTip(x, L.T("chip.clear"));
                x.Click += (_, _) => clear();
                sp.Children.Add(x);
            }
            var b = new Border { Child = sp, Classes = { "chip" } };
            PnlChips.Children.Add(b);
        }
        string List(Panel p) => string.Join(", ", p.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => c.Content?.ToString()));

        if (!AllChecked(PnlYears)) Chip(L.T("chip.years", List(PnlYears)), () => SetAll(PnlYears, true));
        if (!AllChecked(PnlMonths)) Chip(L.T("chip.months", List(PnlMonths)), () => SetAll(PnlMonths, true));
        if (CmbFrom.SelectedIndex > 0 || (CmbTo.SelectedIndex >= 0 && CmbTo.SelectedIndex < CmbTo.ItemCount - 1))
            Chip(L.T("chip.range", CmbFrom.SelectedItem, CmbTo.SelectedItem), () => { _busy = true; SetFullRange(); _busy = false; Refresh(); });
        if (ChkCommon.IsChecked == true) Chip(L.T("f.common"), () => ChkCommon.IsChecked = false);
        if (!AllChecked(PnlCc)) Chip(L.T("chip.cc", List(PnlCc)), () => SetAll(PnlCc, true));
        if (!AllChecked(PnlCust)) Chip(L.T("chip.cust", List(PnlCust)), () => SetAll(PnlCust, true));
        if (CmbScope.SelectedIndex > 0) Chip((CmbScope.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "", () => CmbScope.SelectedIndex = 0);
        if (CmbCat.SelectedIndex > 0) Chip((CmbCat.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "", () => CmbCat.SelectedIndex = 0);
        if (!string.IsNullOrWhiteSpace(TxtProject.Text)) Chip(L.T("chip.project", TxtProject.Text), () => TxtProject.Text = "");
        int hidden = _cfg.DeselectedPeople.Count(p => _personCc.ContainsKey(p));
        if (hidden > 0) Chip(L.T("chip.people", hidden), () => { _cfg.DeselectedPeople.Clear(); _cfg.Save(); BuildPeoplePanel(); Refresh(); });
        if (Money) Chip(L.T("chip.money", _cfg.ValueUnit), () => CmbUnit.SelectedIndex = 0);

        if (PnlChips.Children.Count == 0) Chip(L.T("chip.none"), null);
        BtnResetBar.IsVisible = PnlChips.Children.Count > 0 && !(PnlChips.Children.Count == 1 && _lastRows.Count == _rows.Count && !Money);
    }
}
