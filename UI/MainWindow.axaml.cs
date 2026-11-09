using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
namespace HoursTool;

/// <summary>
/// Main application window. The class is split into partial files by concern:
/// <list type="bullet">
/// <item><c>MainWindow.axaml.cs</c> – state, start-up, file loading, language/theme, shortcuts, refresh pipeline</item>
/// <item><c>MainWindow.Filters.cs</c> – side panel, filtering, presets, active-filter chips</item>
/// <item><c>MainWindow.Views.cs</c> – KPIs, statistics, pivot table, projects, data check</item>
/// <item><c>MainWindow.Plots.cs</c> – chart tabs</item>
/// <item><c>MainWindow.Settings.cs</c> – settings editor (rule profiles, rule tester, column aliases)</item>
/// <item><c>MainWindow.Export.cs</c> – Excel / PNG export</item>
/// <item><c>MainWindow.Help.cs</c> – built-in user guide</item>
/// </list>
/// <para>
/// Data flow: files → <see cref="Loader"/> → <see cref="_raw"/> → <see cref="Classifier"/> (profile rules, CC/people
/// inclusion) → <see cref="_rows"/> → side-panel filters → <see cref="_lastRows"/> → views.
/// Views are rebuilt lazily: a refresh marks all tabs dirty and only the visible tab is rebuilt immediately.
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    Config _cfg = Config.Load();
    Classifier _cls = null!;
    Analytics _an = null!;
    List<Metric> _metrics = new();

    /// <summary>All raw rows of all loaded files.</summary>
    readonly List<Raw> _raw = new();
    /// <summary>Loaded files in load order.</summary>
    readonly List<LoadedFile> _files = new();
    /// <summary>Classified rows that pass the profile inclusion rules (cost centres, relevant people).</summary>
    List<Row> _rows = new();
    /// <summary>Rows after side-panel filters – the input of every view.</summary>
    List<Row> _lastRows = new();
    /// <summary>Latest cost centre of each person (for labels and sorting).</summary>
    Dictionary<string, string> _personCc = new();

    /// <summary>Suppresses refreshes while controls are being filled programmatically.</summary>
    bool _busy;
    /// <summary>Tabs whose content is stale and must be rebuilt when shown.</summary>
    readonly HashSet<string> _dirty = new();
    /// <summary>Coalesces rapid filter changes (typing) into one refresh.</summary>
    readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    static readonly string[] ViewTabs = { "overview", "table", "time", "trend", "yoy", "cust", "people", "heat", "person", "projects", "data" };

    bool Dark => ActualThemeVariant == ThemeVariant.Dark;
    bool Money => CmbUnit.SelectedIndex == 1;
    double V(Row r) => _an.V(r);

    /// <summary>Creates the window, restores settings and wires all events.</summary>
    public MainWindow()
    {
        InitializeComponent();
        if (string.IsNullOrWhiteSpace(_cfg.Language)) _cfg.Language = L.SystemDefault;
        L.Lang = _cfg.Language;
        _busy = true;
        CmbLang.SelectedIndex = _cfg.Language == "cs" ? 1 : 0;
        CmbTheme.SelectedIndex = _cfg.Theme == "Light" ? 1 : _cfg.Theme == "Dark" ? 2 : 0;
        SidePanel.IsVisible = _cfg.SidePanelVisible;
        _busy = false;
        ApplyTheme();

        _cls = new Classifier(_cfg);
        _metrics = _cls.Metrics();
        _an = new Analytics(_metrics, false, _cfg.ValueUnit);
        BuildStaticPanels();
        ApplyTexts();
        InitSettings();
        WireEvents();
        UpdateWelcome();

        Opened += async (_, _) =>
        {
            var existing = _cfg.LastFiles.Where(File.Exists).ToList();
            if (existing.Count > 0) await LoadPaths(existing, true);   // same order as before = same overlap resolution
            else Reclassify();
        };
    }

    // =====================================================================
    // events
    // =====================================================================

    void WireEvents()
    {
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); Refresh(); };

        BtnOpen.Click += async (_, _) => await OpenFiles();
        BtnWelcomeOpen.Click += async (_, _) => await OpenFiles();
        BtnWelcomeHelp.Click += (_, _) => SelectTab("help");
        BtnWelcomeSettings.Click += (_, _) => SelectTab("settings");
        BtnReload.Click += async (_, _) => await ReloadFiles();
        BtnReloadFiles.Click += async (_, _) => await ReloadFiles();
        BtnRemoveFile.Click += (_, _) => RemoveSelectedFiles();
        BtnClear.Click += (_, _) => ClearData();
        BtnExport.Click += async (_, _) => await ExportXlsx();
        BtnExportCharts.Click += async (_, _) => await ExportCharts();
        BtnHelp.Click += (_, _) => SelectTab("help");
        BtnSidebar.Click += (_, _) => ToggleSidebar();

        CmbTheme.SelectionChanged += (_, _) =>
        {
            if (_busy) return;
            _cfg.Theme = CmbTheme.SelectedIndex == 1 ? "Light" : CmbTheme.SelectedIndex == 2 ? "Dark" : "System";
            _cfg.Save(); ApplyTheme();
        };
        CmbLang.SelectionChanged += (_, _) =>
        {
            if (_busy || CmbLang.SelectedItem is not ComboBoxItem { Tag: string lang }) return;
            SetLanguage(lang);
        };

        Tabs.SelectionChanged += (s, e) =>
        {
            if (e.Source != Tabs) return;          // ignore bubbling events from inner combo boxes
            UpdateWelcome();
            var key = CurrentTab();
            if (key == "help") EnsureHelp();
            BuildTab(key);
        };
        ActualThemeVariantChanged += (_, _) => { MarkAllDirty(); BuildTab(CurrentTab()); };

        WireFilterEvents();
        WireViewEvents();
        WireSettingsEvents();

        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        });
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var paths = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!)
                        .Where(p => Loader.Extensions.Contains(Path.GetExtension(p).ToLowerInvariant())).ToList();
            if (paths is { Count: > 0 }) await LoadPaths(paths, true);
            else Status(L.T("st.dropNothing"));
        });
    }

    /// <summary>Keyboard shortcuts: Ctrl+O open, Ctrl+E export, Ctrl+B side panel, F5 reload, F1 help, Ctrl+1..9 tabs.</summary>
    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        switch (e.Key)
        {
            case Key.O when ctrl: e.Handled = true; await OpenFiles(); break;
            case Key.E when ctrl: e.Handled = true; await ExportXlsx(); break;
            case Key.B when ctrl: e.Handled = true; ToggleSidebar(); break;
            case Key.F5: e.Handled = true; await ReloadFiles(); break;
            case Key.F1: e.Handled = true; SelectTab("help"); break;
            case >= Key.D1 and <= Key.D9 when ctrl:
                int i = e.Key - Key.D1;
                if (i < Tabs.ItemCount) { Tabs.SelectedIndex = i; e.Handled = true; }
                break;
        }
    }

    // =====================================================================
    // theme, language, layout
    // =====================================================================

    void ApplyTheme()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = _cfg.Theme == "Light" ? ThemeVariant.Light
                                      : _cfg.Theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Default;
    }

    void ToggleSidebar()
    {
        _cfg.SidePanelVisible = !_cfg.SidePanelVisible; _cfg.Save();
        UpdateWelcome();
    }

    /// <summary>Switches the UI language and rebuilds every code-generated text.</summary>
    void SetLanguage(string lang)
    {
        _cfg.Language = lang; _cfg.Save();
        L.Lang = lang;
        _metrics = _cls.Metrics();
        BuildStaticPanels();
        ApplyTexts();
        RefreshSettingsTexts();
        _helpLang = "";
        if (CurrentTab() == "help") EnsureHelp();
        RefreshComboSelections();
        Reclassify();
    }

    /// <summary>
    /// A combo box shows a snapshot of the selected item's content, so after a language switch the
    /// selection is re-applied to display the translated text.
    /// </summary>
    void RefreshComboSelections()
    {
        bool prev = _busy; _busy = true;
        foreach (var cb in this.GetLogicalDescendants().OfType<ComboBox>().Where(c => c.Items.OfType<ComboBoxItem>().Any()))
        {
            int i = cb.SelectedIndex;
            cb.SelectedIndex = -1;
            cb.SelectedIndex = i;
        }
        _busy = prev;
    }

    /// <summary>Sets texts that are generated in code (not bound in XAML).</summary>
    void ApplyTexts()
    {
        var split = _cfg.SplitCustomer.Trim().ToUpperInvariant();
        ItemUnitMoney.Content = L.T("f.unit.money", _cfg.ValueUnit);
        ChkSplit.Content = L.T("f.split", split);
        ItemCatNb.Content = L.T("f.cat.nb", split);
        ItemCatNoNb.Content = L.T("f.cat.noNb", split);
        TxtCfgPath.Text = Config.FilePath;
        SetupGridColumns();
        UpdateCounts();
    }

    void Status(string s) => TxtStatus.Text = s;

    void UpdateCounts()
    {
        TxtCounts.Text = _raw.Count == 0 ? L.T("st.noData")
            : L.T("st.counts", _files.Count, _raw.Count, _rows.Count, _lastRows.Count);
    }

    /// <summary>Shows the welcome screen when nothing is loaded (except on the Settings and Help tabs).</summary>
    void UpdateWelcome()
    {
        var t = CurrentTab();
        Welcome.IsVisible = _raw.Count == 0 && t != "settings" && t != "help";
        // filters are irrelevant on the Settings and Help tabs – give the space to the content
        SidePanel.IsVisible = _cfg.SidePanelVisible && _raw.Count > 0 && t != "settings" && t != "help";
        FilterBar.IsVisible = _raw.Count > 0 && t != "settings" && t != "help" && t != "data";
    }

    string CurrentTab() => (Tabs.SelectedItem as TabItem)?.Tag as string ?? "";

    void SelectTab(string key)
    {
        var t = Tabs.Items.OfType<TabItem>().FirstOrDefault(x => (x.Tag as string) == key);
        if (t != null) Tabs.SelectedItem = t;
    }

    // =====================================================================
    // loading files
    // =====================================================================

    async Task OpenFiles()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = L.T("dlg.open"),
            FileTypeFilter = new[]
            {
                new FilePickerFileType(L.T("dlg.timesheets")) { Patterns = Loader.Extensions.Select(e => "*" + e).ToArray() },
                new FilePickerFileType(L.T("dlg.all")) { Patterns = new[] { "*" } }
            }
        });
        if (files.Count == 0) return;
        var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
        await LoadPaths(paths, true);
    }

    /// <summary>
    /// Loads files in the background and merges them. When a new file contains periods that are already loaded,
    /// the newer file wins: those periods are removed from the older file (typical case: a fresh export of the
    /// current year replaces last month's export).
    /// </summary>
    async Task LoadPaths(IEnumerable<string> paths, bool replaceOverlaps)
    {
        var msgs = new List<string>();
        Status(L.T("st.loading"));
        Cursor = new Cursor(StandardCursorType.Wait);
        try
        {
            foreach (var path in paths)
            {
                var name = Path.GetFileName(path);
                try
                {
                    var res = await Task.Run(() => Loader.Load(path, _cfg.Columns));
                    var old = _files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
                    if (old != null) RemoveFile(old);          // re-opening the same file = refresh it

                    var periods = res.Rows.Select(r => r.Period).Distinct().OrderBy(x => x).ToList();
                    var clash = _raw.Where(r => periods.Contains(r.Period)).Select(r => r.SourceFile).Distinct().ToList();
                    if (clash.Count > 0 && !replaceOverlaps)
                    {
                        msgs.Add(L.T("st.skipOverlap", name));
                        continue;
                    }
                    if (clash.Count > 0)
                    {
                        var set = periods.ToHashSet();
                        _raw.RemoveAll(r => set.Contains(r.Period));
                        msgs.Add(L.T("st.replaced", name, string.Join(", ", clash.Select(Path.GetFileName))));
                    }
                    _raw.AddRange(res.Rows);
                    _files.Add(new LoadedFile { Path = path, Report = res.Report });
                    msgs.Add(L.T("st.loaded", name, res.Rows.Count));
                }
                catch (Exception ex)
                {
                    msgs.Add(L.T("st.loadError", name, ex.Message));
                }
            }
            SyncFiles();
        }
        finally { Cursor = Cursor.Default; }
        Reclassify();
        Status(string.Join("  |  ", msgs));
    }

    /// <summary>Recomputes the per-file period/row counts, drops files that no longer contribute and saves the file list.</summary>
    void SyncFiles()
    {
        var by = _raw.GroupBy(r => r.SourceFile, StringComparer.OrdinalIgnoreCase)
                     .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var f in _files)
        {
            var l = by.GetValueOrDefault(f.Path) ?? new List<Raw>();
            f.Rows = l.Count;
            f.Periods = l.Select(r => r.Period).Distinct().OrderBy(x => x).ToList();
        }
        _files.RemoveAll(f => f.Rows == 0 && f.Report.Accepted > 0);
        _cfg.LastFiles = _files.Select(f => f.Path).ToList();
        _cfg.Save();
    }

    void RemoveFile(LoadedFile f)
    {
        _raw.RemoveAll(r => string.Equals(r.SourceFile, f.Path, StringComparison.OrdinalIgnoreCase));
        _files.Remove(f);
    }

    void RemoveSelectedFiles()
    {
        var sel = GridFiles.SelectedItems.OfType<LoadedFile>().ToList();
        if (sel.Count == 0) { Status(L.T("st.selectFile")); return; }
        foreach (var f in sel) RemoveFile(f);
        SyncFiles();
        Reclassify();
        Status(L.T("st.removed", sel.Count));
    }

    /// <summary>Reads all loaded files again (e.g. after the files were updated or column aliases changed).</summary>
    async Task ReloadFiles()
    {
        var paths = _files.Select(f => f.Path).ToList();
        if (paths.Count == 0) { Status(L.T("st.noFiles")); return; }
        _raw.Clear(); _files.Clear();
        await LoadPaths(paths.Where(File.Exists), true);
    }

    void ClearData()
    {
        _raw.Clear(); _files.Clear();
        SyncFiles();
        Reclassify();
        Status(L.T("st.cleared"));
    }

    // =====================================================================
    // refresh pipeline
    // =====================================================================

    /// <summary>
    /// Re-applies the business rules to all raw rows (after loading files or changing settings)
    /// and rebuilds the data-dependent filter panels.
    /// </summary>
    void Reclassify()
    {
        _cls = new Classifier(_cfg);
        _metrics = _cls.Metrics();
        _rows = _raw.Where(_cls.IsIncluded).Select(_cls.Classify).ToList();
        _personCc = _rows.OrderBy(r => r.Period).GroupBy(r => r.Person).ToDictionary(g => g.Key, g => g.Last().Cc);

        _busy = true;
        BuildMetricPanels();
        FillDataPanels();
        _busy = false;
        Refresh();
    }

    /// <summary>Schedules a refresh (debounced); use for high-frequency events such as typing.</summary>
    void RequestRefresh()
    {
        if (_busy) return;
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    /// <summary>Applies the filters and rebuilds the visible tab; other tabs are rebuilt when shown.</summary>
    void Refresh()
    {
        if (_busy || !IsInitialized) return;
        _refreshTimer.Stop();
        _an = new Analytics(_metrics, Money, _cfg.ValueUnit);
        _lastRows = Filtered();
        UpdateChips();
        UpdateCounts();
        UpdateWelcome();
        MarkAllDirty();
        BuildTab(CurrentTab());
    }

    void MarkAllDirty()
    {
        foreach (var t in ViewTabs) _dirty.Add(t);
    }

    /// <summary>Rebuilds a tab if it is dirty.</summary>
    void BuildTab(string key)
    {
        if (!_dirty.Remove(key)) return;
        var rows = _lastRows;
        try
        {
            switch (key)
            {
                case "overview": BuildKpi(rows); BuildStats(rows); break;
                case "table": BuildTable(rows); break;
                case "time": BuildTimeChart(rows); break;
                case "trend": BuildTrend(rows); break;
                case "yoy": BuildYoy(rows); break;
                case "cust": BuildCust(rows); break;
                case "people": BuildRank(rows); break;
                case "heat": BuildHeat(rows); break;
                case "person": BuildPerson(rows); break;
                case "projects": BuildProjects(rows); break;
                case "data": BuildFiles(); BuildIssues(); break;
            }
        }
        catch (Exception ex)
        {
            Status(L.T("st.viewError", key, ex.Message));
        }
    }

    /// <summary>Makes sure all given tabs are up to date (used by export).</summary>
    void EnsureBuilt(params string[] keys)
    {
        foreach (var k in keys) BuildTab(k);
    }
}
