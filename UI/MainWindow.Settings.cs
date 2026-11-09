using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
namespace HoursTool;

/// <summary>
/// Settings editor. Edits are made on a working copy (<see cref="_edit"/>); nothing changes until
/// "Apply" is pressed. Every edit is validated live and the rule tester shows its effect immediately.
/// </summary>
public partial class MainWindow
{
    /// <summary>Working copy of the configuration being edited.</summary>
    Config _edit = null!;
    /// <summary>Index of the profile shown in the editor.</summary>
    int _profIdx;
    /// <summary>Alias text boxes of the column mapping, by field key.</summary>
    readonly Dictionary<string, TextBox> _colBoxes = new();
    readonly DispatcherTimer _settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    bool _settingsLoading;

    static List<string> Split(string? s, params char[] seps) =>
        (s ?? "").Split(seps, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    static List<string> Lines(string? s) => Split(s, '\n', '\r');

    void InitSettings()
    {
        _edit = _cfg.Clone();
        _profIdx = 0;
        BuildColumnEditor();
        FillSettings();
    }

    void WireSettingsEvents()
    {
        _settingsTimer.Tick += (_, _) => { _settingsTimer.Stop(); SettingsChanged(); };
        BtnApply.Click += (_, _) => ApplySettings();
        BtnRevert.Click += (_, _) => { _edit = _cfg.Clone(); _profIdx = Math.Min(_profIdx, _edit.Profiles.Count - 1); FillSettings(); Status(L.T("st.reverted")); };
        BtnFillPeople.Click += (_, _) => FillPeopleFromData();
        BtnColumnsDefault.Click += (_, _) => { SyncSettings(); _edit.Columns = new ColumnMap(); FillSettings(); };
        BtnCfgExport.Click += async (_, _) => await ExportConfig();
        BtnCfgImport.Click += async (_, _) => await ImportConfig();
        BtnCfgDefaults.Click += (_, _) =>
        {
            var d = new Config(); d.Migrate();
            KeepRuntime(d, _cfg);
            _edit = d; _profIdx = 0; FillSettings();
            Status(L.T("st.defaults"));
        };

        CmbProfile.SelectionChanged += (_, _) =>
        {
            if (_settingsLoading || CmbProfile.SelectedIndex < 0) return;
            SyncSettings();
            _profIdx = CmbProfile.SelectedIndex;
            FillSettings();
        };
        BtnProfAdd.Click += (_, _) => { SyncSettings(); AddProfile(new RuleProfile { Name = UniqueName(L.T("prof.new")) }); };
        BtnProfDup.Click += (_, _) =>
        {
            SyncSettings();
            var c = _edit.Profiles[_profIdx].Clone();
            c.Name = UniqueName(c.Name + " (2)");
            AddProfile(c);
        };
        BtnProfDel.Click += (_, _) =>
        {
            if (_edit.Profiles.Count <= 1) { Status(L.T("st.lastProfile")); return; }
            _edit.Profiles.RemoveAt(_profIdx);
            _profIdx = Math.Max(0, _profIdx - 1);
            FillSettings(); SettingsChanged();
        };
        BtnProfUp.Click += (_, _) => MoveProfile(-1);
        BtnProfDown.Click += (_, _) => MoveProfile(+1);

        foreach (var tb in new[] { TxtProfName, TxtProfFrom, TxtProfTo, TxtSetCc, TxtSetRestricted, TxtSetPeople, TxtSetOh, TxtSetOhDesc,
                                   TxtSetNb, TxtSetNbEx, TxtSetPr, TxtSetIdCust, TxtSetDescCodes, TxtSetOverrides,
                                   TxtSetCust, TxtSetSplit, TxtSetUnit, TstId, TstDesc, TstAbacus, TstPeriod, TstCc, TstPerson })
            tb.TextChanged += (_, _) => { if (!_settingsLoading) { _settingsTimer.Stop(); _settingsTimer.Start(); } };
        NumAbacus.ValueChanged += (_, _) => { if (!_settingsLoading) SettingsChanged(); };
    }

    string UniqueName(string baseName)
    {
        var name = baseName; int i = 2;
        while (_edit.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) name = $"{baseName} {i++}";
        return name;
    }

    void AddProfile(RuleProfile p)
    {
        _edit.Profiles.Insert(_profIdx + 1, p);
        _profIdx++;
        FillSettings(); SettingsChanged();
    }

    void MoveProfile(int d)
    {
        SyncSettings();
        int j = _profIdx + d;
        if (j < 0 || j >= _edit.Profiles.Count) return;
        (_edit.Profiles[_profIdx], _edit.Profiles[j]) = (_edit.Profiles[j], _edit.Profiles[_profIdx]);
        _profIdx = j;
        FillSettings(); SettingsChanged();
    }

    /// <summary>Copies runtime (non-rule) fields such as language, theme and file list from <paramref name="src"/>.</summary>
    static void KeepRuntime(Config dst, Config src)
    {
        dst.Language = src.Language; dst.Theme = src.Theme; dst.LastFiles = src.LastFiles.ToList();
        dst.DeselectedPeople = src.DeselectedPeople.ToList(); dst.SidePanelVisible = src.SidePanelVisible;
    }

    // ---------- column alias editor ----------

    void BuildColumnEditor()
    {
        GridColumns.Children.Clear(); GridColumns.RowDefinitions.Clear(); _colBoxes.Clear();
        int row = 0;
        foreach (var (key, _) in _edit.Columns.All())
        {
            GridColumns.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var lbl = new TextBlock { Text = L.T("col." + key), Classes = { "lbl" }, Margin = new Avalonia.Thickness(0, 0, 8, 4) };
            var tb = new TextBox { Margin = new Avalonia.Thickness(0, 0, 0, 4), FontSize = 12 };
            tb.TextChanged += (_, _) => { if (!_settingsLoading) { _settingsTimer.Stop(); _settingsTimer.Start(); } };
            Grid.SetRow(lbl, row); Grid.SetRow(tb, row); Grid.SetColumn(tb, 1);
            GridColumns.Children.Add(lbl); GridColumns.Children.Add(tb);
            _colBoxes[key] = tb;
            row++;
        }
    }

    /// <summary>Re-creates language-dependent labels of the settings editor.</summary>
    void RefreshSettingsTexts()
    {
        SyncSettings();
        BuildColumnEditor();
        FillSettings();
    }

    // ---------- boxes <-> working copy ----------

    /// <summary>Fills all settings boxes from the working copy.</summary>
    void FillSettings()
    {
        _settingsLoading = true;
        _profIdx = Math.Clamp(_profIdx, 0, _edit.Profiles.Count - 1);
        CmbProfile.Items.Clear();
        foreach (var p in _edit.Profiles) CmbProfile.Items.Add($"{p.Name}   ({p.RangeText})");
        CmbProfile.SelectedIndex = _profIdx;

        var pr = _edit.Profiles[_profIdx];
        TxtProfName.Text = pr.Name;
        TxtProfFrom.Text = pr.From;
        TxtProfTo.Text = pr.To;
        TxtSetCc.Text = string.Join(", ", pr.CostCenters);
        TxtSetRestricted.Text = string.Join(", ", pr.RestrictedCostCenters);
        TxtSetPeople.Text = string.Join("\n", pr.RelevantPeople);
        TxtSetOh.Text = string.Join("\n", pr.OverheadIdPatterns);
        TxtSetOhDesc.Text = string.Join("\n", pr.OverheadDescPatterns);
        TxtSetNb.Text = pr.NewbuildIdRegex;
        TxtSetNbEx.Text = string.Join("\n", pr.NewbuildExcludeDescriptions);
        TxtSetPr.Text = pr.PerRequestIdRegex;
        NumAbacus.Value = pr.AbacusPrefixLength;
        TxtSetIdCust.Text = pr.IdCustomerRegex;
        TxtSetDescCodes.Text = string.Join(", ", pr.DescCustomerCodes);
        TxtSetOverrides.Text = string.Join("\n", pr.CustomerOverrides);

        TxtSetCust.Text = string.Join(", ", _edit.Customers);
        TxtSetSplit.Text = _edit.SplitCustomer;
        TxtSetUnit.Text = _edit.ValueUnit;
        foreach (var (key, aliases) in _edit.Columns.All())
            if (_colBoxes.TryGetValue(key, out var tb)) tb.Text = string.Join("; ", aliases);
        BtnProfDel.IsEnabled = _edit.Profiles.Count > 1;
        _settingsLoading = false;
        SettingsChanged();
    }

    /// <summary>Copies the boxes into the working copy.</summary>
    void SyncSettings()
    {
        if (_edit == null || _edit.Profiles.Count == 0) return;
        var pr = _edit.Profiles[Math.Clamp(_profIdx, 0, _edit.Profiles.Count - 1)];
        pr.Name = (TxtProfName.Text ?? "").Trim() == "" ? pr.Name : TxtProfName.Text!.Trim();
        pr.From = (TxtProfFrom.Text ?? "").Trim();
        pr.To = (TxtProfTo.Text ?? "").Trim();
        pr.CostCenters = Split(TxtSetCc.Text, ',', ';').Select(Config.NormCc).Distinct().ToList();
        pr.RestrictedCostCenters = Split(TxtSetRestricted.Text, ',', ';').Select(Config.NormCc).Distinct().ToList();
        pr.RelevantPeople = Lines(TxtSetPeople.Text);
        pr.OverheadIdPatterns = Lines(TxtSetOh.Text);
        pr.OverheadDescPatterns = Lines(TxtSetOhDesc.Text);
        pr.NewbuildIdRegex = (TxtSetNb.Text ?? "").Trim();
        pr.NewbuildExcludeDescriptions = Lines(TxtSetNbEx.Text);
        pr.PerRequestIdRegex = (TxtSetPr.Text ?? "").Trim();
        pr.AbacusPrefixLength = (int)(NumAbacus.Value ?? 2);
        pr.IdCustomerRegex = (TxtSetIdCust.Text ?? "").Trim();
        pr.DescCustomerCodes = Split(TxtSetDescCodes.Text, ',', ';', ' ').Select(x => x.ToUpperInvariant()).Distinct().ToList();
        pr.CustomerOverrides = Lines(TxtSetOverrides.Text);

        _edit.Customers = Split(TxtSetCust.Text, ',', ';').Select(x => x.ToUpperInvariant()).Distinct().ToList();
        _edit.SplitCustomer = (TxtSetSplit.Text ?? "").Trim().ToUpperInvariant();
        _edit.ValueUnit = string.IsNullOrWhiteSpace(TxtSetUnit.Text) ? "CZK" : TxtSetUnit.Text.Trim();
        foreach (var (key, tb) in _colBoxes) _edit.Columns.Set(key, Split(tb.Text, ';'));
    }

    /// <summary>Validates the working copy, updates the coverage text and the rule tester.</summary>
    void SettingsChanged()
    {
        if (_settingsLoading || _edit == null) return;
        SyncSettings();
        var errs = _edit.Validate();
        bool dirty = _edit.ToJson() != WithRuntime(_cfg).ToJson();
        TxtSetErrors.Text = errs.Count > 0 ? "⚠ " + string.Join("   ", errs) : dirty ? L.T("set.unsaved") : "";
        TxtSetErrors.Classes.Set("err", errs.Count > 0);
        TxtSetErrors.Classes.Set("hint", errs.Count == 0);
        BtnApply.IsEnabled = errs.Count == 0;

        // keep the profile picker text in sync with name/range edits
        if (CmbProfile.SelectedIndex == _profIdx && _profIdx < CmbProfile.ItemCount)
        {
            var p = _edit.Profiles[_profIdx];
            var txt = $"{p.Name}   ({p.RangeText})";
            if ((CmbProfile.Items[_profIdx] as string) != txt)
            {
                _settingsLoading = true;
                CmbProfile.Items[_profIdx] = txt;
                CmbProfile.SelectedIndex = _profIdx;
                _settingsLoading = false;
            }
        }

        // which loaded periods would this profile classify?
        var periods = _raw.Select(r => r.Period).Distinct().OrderBy(x => x).ToList();
        if (periods.Count == 0) TxtProfCoverage.Text = L.T("prof.cov.noData");
        else if (errs.Count == 0)
        {
            var me = _edit.Profiles[_profIdx];
            var mine = periods.Where(x => _edit.ProfileFor(x) == me).ToList();
            var none = periods.Where(x => !_edit.HasProfileFor(x)).ToList();
            TxtProfCoverage.Text = (mine.Count == 0 ? L.T("prof.cov.none") : L.T("prof.cov", mine.Count, mine[0], mine[^1]))
                + (none.Count > 0 && _edit.Profiles.Any(p => !p.IsOpen) ? "  " + L.T("prof.cov.fallback", none.Count) : "");
        }
        UpdateTester(errs.Count == 0);
    }

    /// <summary>The applied config with the same runtime fields as the working copy (for change detection).</summary>
    Config WithRuntime(Config c)
    {
        var x = c.Clone();
        KeepRuntime(x, _edit);
        return x;
    }

    void UpdateTester(bool valid)
    {
        var id = (TstId.Text ?? "").Trim();
        var desc = (TstDesc.Text ?? "").Trim();
        if (id == "" && desc == "") { TxtTest.Text = L.T("set.tester.empty"); return; }
        if (!valid) { TxtTest.Text = L.T("set.tester.invalid"); return; }
        var per = (TstPeriod.Text ?? "").Trim();
        string period = Periods.TryParse(per, out var y, out var m) ? Periods.Key(y, m)
            : _raw.Count > 0 ? _raw.Max(r => r.Period)! : Periods.Key(DateTime.Today.Year, DateTime.Today.Month);
        var cc = Config.NormCc(TstCc.Text ?? "");
        var pr = _edit.ProfileFor(period);
        if (cc == "") cc = pr.CostCenters.FirstOrDefault() ?? "";
        var raw = new Raw(period, int.Parse(period[..4]), int.Parse(period[5..]), (TstPerson.Text ?? "").Trim(), cc,
                          id, desc, (TstAbacus.Text ?? "").Trim(), 1, 0);
        TxtTest.Text = $"{L.T("set.tester.period")}: {period}, CC: {cc}\n" + new Classifier(_edit).Explain(raw);
    }

    void FillPeopleFromData()
    {
        SyncSettings();
        var pr = _edit.Profiles[_profIdx];
        var cur = pr.RestrictedCostCenters.ToHashSet();
        var names = _raw.Where(r => cur.Contains(r.Cc) && pr.Covers(r.Period)).Select(r => r.Person).Distinct()
                        .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        TxtSetPeople.Text = string.Join("\n", names);
        Status(names.Count == 0 ? L.T("st.fillNone") : L.T("st.fillDone", names.Count));
    }

    /// <summary>Validates and activates the working copy, then re-classifies all data.</summary>
    void ApplySettings()
    {
        SyncSettings();
        var errs = _edit.Validate();
        if (errs.Count > 0) { TxtSetErrors.Text = "⚠ " + string.Join("   ", errs); Status(L.T("st.invalid")); return; }
        bool columnsChanged = System.Text.Json.JsonSerializer.Serialize(_edit.Columns) != System.Text.Json.JsonSerializer.Serialize(_cfg.Columns);
        KeepRuntime(_edit, _cfg);
        _cfg = _edit.Clone();
        _cfg.Migrate();
        _cfg.Save();
        _edit = _cfg.Clone();
        BuildStaticPanels();
        ApplyTexts();
        FillSettings();
        Reclassify();
        Status(L.T("st.applied"));
        if (columnsChanged && _files.Count > 0) _ = ReloadFiles();
    }

    async Task ExportConfig()
    {
        SyncSettings();
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "hourstool-settings.json", DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });
        var path = f?.TryGetLocalPath(); if (path == null) return;
        var c = _edit.Clone();
        c.LastFiles.Clear(); c.DeselectedPeople.Clear();      // personal state is not shared
        await File.WriteAllTextAsync(path, c.ToJson());
        Status(L.T("st.cfgExported", path));
    }

    async Task ImportConfig()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("set.cfgImport"),
            FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath(); if (path == null) return;
        try
        {
            var c = Config.FromJson(await File.ReadAllTextAsync(path));
            KeepRuntime(c, _cfg);
            _edit = c; _profIdx = 0; FillSettings();
            Status(L.T("st.cfgImported"));
        }
        catch (Exception ex) { Status(L.T("st.cfgImportError", ex.Message)); }
    }
}
