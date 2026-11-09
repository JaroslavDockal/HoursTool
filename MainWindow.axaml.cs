using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using System.Text.RegularExpressions;
namespace HoursTool;

public partial class MainWindow : Window
{
    const string INV = "Invoiced";
    const string OH = "Neinvoiced (overhead)";
    const string NBN = "FI newbuild";
    const string PCT = "Inv % (podíl invoiced)";
    const string PERP = "Invoiced na osobu a měsíc";
    const string HEADS = "Průměr aktivních lidí / měs.";
    const string TOTAL = "Celkem zapsáno";
    static readonly string[] MonthNames = { "led", "úno", "bře", "dub", "kvě", "čvn", "čvc", "srp", "zář", "říj", "lis", "pro" };

    Config _cfg = Config.Load();
    Calc _calc = null!;
    List<Metric> _metrics = new();
    readonly List<Raw> _raw = new();
    readonly Dictionary<string, string> _fileOfPeriod = new();
    List<Row> _rows = new();
    List<Row> _lastRows = new();
    Dictionary<string, string> _personCc = new();
    bool _busy;

    // pro export
    List<string> _lastHeaders = new();
    List<ReportRow> _lastReport = new();
    List<string> _lastFormats = new();
    string _lastLabelHdr = "Osoba", _lastSubHdr = "CC";
    List<string> _statHeaders = new();
    List<TableRow> _lastStats = new();
    List<ProjRow> _lastProj = new();

    bool Dark => ActualThemeVariant == ThemeVariant.Dark;
    bool Czk => CmbUnit.SelectedIndex == 1;
    double V(Row r) => Czk ? (r.Overhead ? 0 : r.Czk) : r.Hours;
    static double H(Row r) => r.Hours;

    public MainWindow()
    {
        InitializeComponent();
        NewCalc();
        TxtCfgPath.Text = "Konfigurace: " + Config.FilePath;
        CmbTheme.SelectedIndex = _cfg.Theme == "Light" ? 1 : _cfg.Theme == "Dark" ? 2 : 0;
        ApplyTheme();
        FillSettingsBoxes();
        BuildMonthsPanel();
        BuildMetricPanels();

        BtnOpen.Click += async (_, _) => await OpenFiles();
        BtnClear.Click += (_, _) =>
        {
            _raw.Clear(); _fileOfPeriod.Clear(); _cfg.LastFiles.Clear(); _cfg.Save();
            Reclassify(); Status("Data vymazána.");
        };
        BtnExport.Click += async (_, _) => await ExportXlsx();
        BtnExportCharts.Click += async (_, _) => await ExportCharts();
        BtnApply.Click += (_, _) => ApplySettings();
        BtnFillPeople.Click += (_, _) => FillPeopleFromData();
        BtnReset.Click += (_, _) => ResetFilters();
        BtnAll.Click += (_, _) => SetVisiblePeople(true);
        BtnNone.Click += (_, _) => SetVisiblePeople(false);
        CmbTheme.SelectionChanged += (_, _) =>
        {
            _cfg.Theme = CmbTheme.SelectedIndex == 1 ? "Light" : CmbTheme.SelectedIndex == 2 ? "Dark" : "System";
            _cfg.Save(); ApplyTheme();
        };
        foreach (var cb in new[] { CmbUnit, CmbScope, CmbNb, CmbGroup, CmbCols, CmbSort, CmbTrend, CmbFc, CmbHeat, CmbPerson, CmbYoy })
            cb.SelectionChanged += (_, _) => Refresh();
        foreach (var ck in new[] { ChkCommon, ChkMa, ChkTrend, ChkFiSplit })
            ck.IsCheckedChanged += (_, _) => Refresh();
        TxtProject.TextChanged += (_, _) => Refresh();
        TxtPeople.TextChanged += (_, _) => BuildPeoplePanel();
        ActualThemeVariantChanged += (_, _) => Refresh();

        Opened += async (_, _) =>
        {
            var existing = _cfg.LastFiles.Where(File.Exists).ToList();
            if (existing.Count > 0) await LoadPaths(existing);
        };
    }

    void NewCalc() { _calc = new Calc(_cfg); _metrics = _calc.Metrics(); }

    void ApplyTheme()
    {
        if (Avalonia.Application.Current is { } app)
            app.RequestedThemeVariant = _cfg.Theme == "Light" ? ThemeVariant.Light
                                      : _cfg.Theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Default;
    }

    void Status(string s) => TxtStatus.Text = s;

    // ---------- nacitani ----------
    async Task OpenFiles()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Vyber export(y) z timesheetů",
            FileTypeFilter = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } }
        });
        if (files.Count == 0) return;
        var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
        var ok = await LoadPaths(paths);
        foreach (var p in ok) if (!_cfg.LastFiles.Contains(p)) _cfg.LastFiles.Add(p);
        _cfg.Save();
    }

    async Task<List<string>> LoadPaths(IEnumerable<string> paths)
    {
        var msgs = new List<string>(); var ok = new List<string>();
        Status("Načítám...");
        foreach (var path in paths)
        {
            try
            {
                var rows = await Task.Run(() => Loader.Load(path));
                var periods = rows.Select(r => r.Period).Distinct().ToList();
                var clash = periods.FirstOrDefault(p => _fileOfPeriod.ContainsKey(p));
                if (clash != null)
                {
                    msgs.Add($"{Path.GetFileName(path)}: PŘESKOČENO, období {clash} už je načteno ze souboru {_fileOfPeriod[clash]}");
                    continue;
                }
                foreach (var p in periods) _fileOfPeriod[p] = Path.GetFileName(path);
                _raw.AddRange(rows);
                ok.Add(path);
                msgs.Add($"{Path.GetFileName(path)}: {rows.Count} řádků");
            }
            catch (Exception ex) { msgs.Add($"{Path.GetFileName(path)}: CHYBA {ex.Message}"); }
        }
        Reclassify();
        Status(string.Join(" | ", msgs));
        return ok;
    }

    void Reclassify()
    {
        NewCalc();
        var ccset = _cfg.CostCenters.ToHashSet();
        var restricted = _cfg.RestrictedCostCenters.ToHashSet();
        var rel = new HashSet<string>(_cfg.RelevantPeople, StringComparer.OrdinalIgnoreCase);
        _rows = _raw.Select(_calc.Classify)
                    .Where(r => ccset.Contains(r.Cc) && (!restricted.Contains(r.Cc) || rel.Contains(r.Person)))
                    .ToList();
        _personCc = _rows.OrderBy(r => r.Period).GroupBy(r => r.Person).ToDictionary(g => g.Key, g => g.Last().Cc);

        _busy = true;
        FillChecks(PnlYears, _rows.Select(r => r.Year).Distinct().OrderBy(x => x).Select(y => (y.ToString(), y.ToString())));
        FillChecks(PnlCc, _cfg.CostCenters.Select(c => (c, c)));
        FillChecks(PnlCust, _rows.Select(r => r.Customer).Distinct()
                                 .OrderBy(x => x == "-" ? 1 : 0).ThenBy(x => x)
                                 .Select(c => (c, c == "-" ? "(overhead)" : c)));
        BuildPeoplePanel();
        _busy = false;
        BuildIssues();
        Refresh();
    }

    // ---------- panely ----------
    CheckBox Chk(string text, bool on, Action onChange)
    {
        var c = new CheckBox { Content = text, IsChecked = on, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        c.IsCheckedChanged += (_, _) => { if (!_busy) onChange(); };
        return c;
    }

    static string TagOf(CheckBox c) => (c.Tag ?? c.Content)!.ToString()!;

    static HashSet<string> CheckedTags(Panel pnl) =>
        pnl.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(TagOf).ToHashSet();

    void FillChecks(Panel pnl, IEnumerable<(string tag, string text)> items)
    {
        var oldAll = pnl.Children.OfType<CheckBox>().Select(TagOf).ToHashSet();
        var oldOn = CheckedTags(pnl);
        pnl.Children.Clear();
        foreach (var (tag, text) in items)
        {
            bool on = !oldAll.Contains(tag) || oldOn.Contains(tag);   // nove polozky jsou defaultne zaskrtnute
            var c = Chk(text, on, Refresh); c.Tag = tag;
            pnl.Children.Add(c);
        }
    }

    void BuildMonthsPanel()
    {
        PnlMonths.Children.Clear();
        for (int i = 1; i <= 12; i++)
        {
            var c = Chk(MonthNames[i - 1], true, Refresh); c.Tag = i.ToString();
            PnlMonths.Children.Add(c);
        }
    }

    void BuildMetricPanels()
    {
        _busy = true;
        PnlMonthly.Children.Clear(); PnlSummary.Children.Clear();
        foreach (var m in _metrics)
        {
            PnlMonthly.Children.Add(Chk(m.Name, m.Short is "Inv" or "Neinv", Refresh));
            PnlSummary.Children.Add(Chk(m.Name, true, Refresh));
        }
        foreach (var cb in new[] { CmbYoy, CmbTrend })
        {
            cb.Items.Clear();
            foreach (var n in SeriesNames()) cb.Items.Add(n);
            cb.SelectedIndex = 0;
        }
        _busy = false;
    }

    bool IsSel(string person) => !_cfg.DeselectedPeople.Contains(person);

    void SetPerson(string p, bool on)
    {
        _cfg.DeselectedPeople.Remove(p);
        if (!on) _cfg.DeselectedPeople.Add(p);
    }

    IEnumerable<string> VisiblePeople()
    {
        var txt = TxtPeople.Text?.Trim() ?? "";
        return _personCc.Where(kv => txt == "" || kv.Key.Contains(txt, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key);
    }

    void BuildPeoplePanel()
    {
        bool prev = _busy; _busy = true;
        PnlPeople.Children.Clear();
        foreach (var p in VisiblePeople())
        {
            var name = p;
            var c = Chk($"{name}  [{_personCc[name]}]", IsSel(name), () => { });
            c.Tag = name;
            c.IsCheckedChanged += (_, _) =>
            {
                if (_busy) return;
                SetPerson(name, c.IsChecked == true); _cfg.Save(); Refresh();
            };
            PnlPeople.Children.Add(c);
        }
        _busy = prev;
        if (!prev) Refresh();
    }

    void SetVisiblePeople(bool on)
    {
        foreach (var p in VisiblePeople().ToList()) SetPerson(p, on);
        _cfg.Save(); BuildPeoplePanel();
    }

    void ResetFilters()
    {
        _busy = true;
        foreach (var pnl in new Panel[] { PnlYears, PnlMonths, PnlCc, PnlCust })
            foreach (var c in pnl.Children.OfType<CheckBox>()) c.IsChecked = true;
        CmbScope.SelectedIndex = 0; CmbNb.SelectedIndex = 0;
        TxtProject.Text = ""; ChkCommon.IsChecked = false;
        _cfg.DeselectedPeople.Clear(); _cfg.Save();
        BuildPeoplePanel();
        _busy = false;
        Refresh();
    }

    // ---------- nastaveni ----------
    void FillSettingsBoxes()
    {
        TxtSetCc.Text = string.Join(", ", _cfg.CostCenters);
        TxtSetRestricted.Text = string.Join(", ", _cfg.RestrictedCostCenters);
        TxtSetPeople.Text = string.Join("\n", _cfg.RelevantPeople);
        TxtSetOh.Text = string.Join("\n", _cfg.OverheadIdPatterns);
        TxtSetNb.Text = _cfg.NewbuildIdRegex;
        TxtSetPr.Text = _cfg.PerRequestIdRegex;
        TxtSetNbEx.Text = string.Join("\n", _cfg.NewbuildExcludeDescriptions);
        TxtSetCust.Text = string.Join(", ", _cfg.Customers);
    }

    static List<string> Split(string? s, params char[] seps) =>
        (s ?? "").Split(seps, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    void FillPeopleFromData()
    {
        var cur = Split(TxtSetRestricted.Text, ',', ';').Select(Config.Norm).ToHashSet();
        var names = _raw.Where(r => cur.Contains(r.Cc)).Select(r => r.Person).Distinct()
                        .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        TxtSetPeople.Text = string.Join("\n", names);
        Status(names.Count == 0 ? "Nejdřív načtěte data." : $"Vloženo {names.Count} lidí. Smažte nerelevantní a dejte Použít nastavení.");
    }

    void ApplySettings()
    {
        try
        {
            var oh = Split(TxtSetOh.Text, '\n', '\r');
            foreach (var p in oh) _ = new Regex(p);
            _ = new Regex(TxtSetNb.Text ?? "");
            _ = new Regex(TxtSetPr.Text ?? "");
            _cfg.CostCenters = Split(TxtSetCc.Text, ',', ';').Select(Config.Norm).ToList();
            _cfg.RestrictedCostCenters = Split(TxtSetRestricted.Text, ',', ';').Select(Config.Norm).ToList();
            _cfg.RelevantPeople = Split(TxtSetPeople.Text, '\n', '\r');
            _cfg.OverheadIdPatterns = oh;
            _cfg.NewbuildIdRegex = TxtSetNb.Text ?? "";
            _cfg.PerRequestIdRegex = TxtSetPr.Text ?? "";
            _cfg.NewbuildExcludeDescriptions = Split(TxtSetNbEx.Text, '\n', '\r');
            _cfg.Customers = Split(TxtSetCust.Text, ',', ';').Select(x => x.ToUpperInvariant()).ToList();
            _cfg.Save();
            NewCalc();
            BuildMetricPanels();
            Reclassify();
            Status("Nastavení použito.");
        }
        catch (Exception ex) { Status("Neplatné nastavení: " + ex.Message); }
    }

    // ---------- filtr ----------
    List<Row> Filtered()
    {
        var years = CheckedTags(PnlYears).Select(int.Parse).ToHashSet();
        var months = CheckedTags(PnlMonths).Select(int.Parse).ToHashSet();
        var ccs = CheckedTags(PnlCc);
        var custs = CheckedTags(PnlCust);
        int scope = CmbScope.SelectedIndex, nb = CmbNb.SelectedIndex;
        var terms = (TxtProject.Text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var inc = terms.Where(t => !t.StartsWith("-")).ToList();
        var exc = terms.Where(t => t.StartsWith("-") && t.Length > 1).Select(t => t.Substring(1).Trim()).ToList();
        var deselected = _cfg.DeselectedPeople.ToHashSet();

        bool ProjOk(Row r)
        {
            if (inc.Count == 0 && exc.Count == 0) return true;
            var hay = r.SapId + " " + r.Desc;
            if (exc.Any(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase))) return false;
            return inc.Count == 0 || inc.Any(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        var rows = _rows.Where(r =>
            years.Contains(r.Year) && months.Contains(r.Month) && ccs.Contains(r.Cc) && custs.Contains(r.Customer) &&
            !deselected.Contains(r.Person) &&
            (scope == 0 || (scope == 1 && !r.Overhead) || (scope == 2 && r.Overhead)) &&
            (nb == 0 || (nb == 1 && r.Newbuild) || (nb == 2 && !r.Newbuild) || (nb == 3 && r.PerRequest)) &&
            ProjOk(r)).ToList();

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

    void Refresh()
    {
        if (_busy || !IsInitialized) return;
        var rows = Filtered();
        _lastRows = rows;
        BuildKpi(rows);
        BuildStats(rows);
        BuildTable(rows);
        BuildProjects(rows);
        BuildTimeChart(rows);
        BuildTrend(rows);
        BuildYoy(rows);
        BuildCust(rows);
        BuildRank(rows);
        BuildHeat(rows);
        BuildPerson(rows);
    }

    List<Metric> Checked(Panel pnl)
    {
        var names = pnl.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => c.Content!.ToString()!).ToHashSet();
        return _metrics.Where(m => names.Contains(m.Name)).ToList();
    }
}
