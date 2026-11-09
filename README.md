# Hours Tool

Desktop application for analysing timesheet exports: invoiced vs. overhead hours, customers,
people, projects, trends and year-over-year comparisons. Built with .NET 8 and Avalonia, runs on
Windows (single-file exe), and also on Linux/macOS.

- **User guide:** [docs/USER_GUIDE.en.md](docs/USER_GUIDE.en.md) · [docs/USER_GUIDE.cs.md](docs/USER_GUIDE.cs.md)
  (the same guide is built into the app – *Help* tab or **F1**)
- **Languages:** English / Czech, switchable at runtime (top right)

## Features

- Load several exports at once (xlsx, xlsm, xls, csv) – e.g. one per year; files are remembered.
  Newer files replace overlapping periods of older ones.
- Tolerant loader: header row anywhere in the first 60 rows, all sheets, configurable column
  aliases, period as one column (many formats) or separate Year + Month, decimal comma/dot.
- **Rule profiles** valid for period ranges, so each year can have its own cost centres, people
  lists, overhead/newbuild/P-project rules and customer detection.
- Live **rule tester** and validation in Settings; settings import/export (JSON).
- Filters: presets (latest/previous year, YTD vs. previous, last 12 months), From/To range,
  years, months, quarters, cost centres, customers, hour type, project category, project text,
  hidden people. Active filters are shown as removable chips.
- Views: KPI overview with YoY deltas, yearly statistics, pivot table (person/CC/customer/project/
  year/profile × months/quarters/years), time series, trends with forecast, YoY and cumulative
  charts, customers, people ranking, heatmap, person detail, projects, data-quality check.
- Export to Excel (report, statistics, months, projects, filtered data, filters and rules) and
  all charts to PNG. Copy any table to the clipboard.
- Light/dark theme, keyboard shortcuts, drag & drop.

## Build

Requirements: .NET 8 SDK.

```bash
dotnet build
```

Windows single-file executable (same as `build.bat`):

```bash
dotnet publish HoursTool.csproj -c Release -r win-x64 -o publish
```

Framework-dependent variant (same as `build-fd.bat`) – smaller exe (~35 MB instead of ~47 MB),
but the target PC needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64):

```bash
dotnet publish HoursTool.csproj -c Release -r win-x64 -p:SelfContained=false -o publish-fd
```

Most of the size are the bundled libraries (Avalonia, SkiaSharp native rendering, ScottPlot,
ClosedXML/OpenXML), not .NET itself; the self-contained build is compressed, the framework-dependent
one cannot be.

Other platforms: replace `win-x64` with `linux-x64` or `osx-arm64`.

Tests (core logic: loader, periods, classification, profiles, config migration, translations):

```bash
dotnet test tests/HoursTool.Tests
```

## Project structure

```
Program.cs, App.axaml(.cs)   entry point, global styles, colours and icons
Core/                        UI-independent logic (also compiled into the tests)
  Models.cs                  Raw / Row records, report rows, data issues, loaded file info
  Config.cs                  Config (JSON in %APPDATA%/HoursTool), RuleProfile, ColumnMap, v1 migration
  Loader.cs                  tolerant reader for xlsx/xls/csv exports + LoadReport diagnostics
  Periods.cs                 period parsing (YYYY/MM keys), range bounds, Stats (regression, moving avg.)
  Classifier.cs              applies profile rules per period, customer detection, measures (Metric)
  Analytics.cs               measure evaluation, formatting, YoY on common months, project aggregation
  Loc.cs, Strings.cs         localisation service and the EN/CS translation table
UI/
  MainWindow.axaml           layout of the main window
  MainWindow.axaml.cs        state, file loading, language/theme, shortcuts, refresh pipeline
  MainWindow.Filters.cs      side panel, presets, filtering, active-filter chips
  MainWindow.Views.cs        KPI cards, statistics, pivot table, projects, data check
  MainWindow.Plots.cs        chart tabs
  MainWindow.Settings.cs     settings editor, rule profiles, rule tester, column aliases
  MainWindow.Export.cs       Excel and PNG export
  MainWindow.Help.cs         built-in user guide
  Charts.cs                  ScottPlot helpers (styling, chart types)
  MarkdownView.cs            minimal Markdown renderer for the guide
  LocExtension.cs            {l:T key} XAML markup extension
Assets/                      application icon (svg source, ico, png)
docs/                        user guides (embedded into the app)
tests/HoursTool.Tests/       xUnit tests
```

## How it works

```
files ──Loader──▶ Raw rows ──Classifier (profile per period)──▶ Row (customer, overhead, NB, P)
                                │  cost-centre + relevant-people inclusion
                                ▼
                          included rows ──side-panel filters──▶ filtered rows ──▶ views / export
```

- **Period keys** are always `YYYY/MM`, so ordinal string comparison sorts chronologically.
- **Profiles** are evaluated top to bottom; the first whose range covers the period wins, otherwise
  the first profile without a range (fallback). Compiled regexes are cached per profile and period.
- **Measures** (`Metric`) have stable ids (`inv`, `oh`, `cust:FI`, `cust:other`, `split:nb`,
  `split:p`, `split:rest`) plus derived indicators (`total`, `pct`, `perp`, `heads`) in `Analytics`.
- **Lazy views:** a filter change marks all tabs dirty and rebuilds only the visible one; other tabs
  are rebuilt when shown. Typing in text filters is debounced.
- **Localisation:** XAML uses `{l:T key}` (binding to `L.I[key]`, updates live); code uses
  `L.T("key", args)`. Add new texts to `Core/Strings.cs` – a unit test checks that every key used
  in the sources exists in both languages.

## Configuration

Stored in `%APPDATA%\HoursTool\config.json` (Linux: `~/.config/HoursTool/config.json`).
Version 1 files (flat rule fields) are migrated automatically into a single profile; a corrupt file
is backed up as `config.json.bak` and defaults are used.
