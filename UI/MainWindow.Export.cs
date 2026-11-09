using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClosedXML.Excel;
namespace HoursTool;

public partial class MainWindow
{
    static void Header(IXLWorksheet ws)
    {
        var r = ws.Row(1);
        r.Style.Font.Bold = true;
        r.Style.Fill.BackgroundColor = XLColor.FromHtml("#ECEBFF");
        ws.SheetView.FreezeRows(1);
    }

    /// <summary>
    /// Exports the current state to an Excel workbook: pivot table, yearly statistics, monthly measures,
    /// projects, the filtered data rows (for own pivots), active filters and the rule profiles used.
    /// </summary>
    async Task ExportXlsx()
    {
        if (_lastRows.Count == 0) { Status(L.T("st.nothingToExport")); return; }
        EnsureBuilt("overview", "table", "projects");
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = $"hours_report_{DateTime.Now:yyyy-MM-dd}.xlsx", DefaultExtension = "xlsx",
            FileTypeChoices = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } }
        });
        var path = f?.TryGetLocalPath(); if (path == null) return;
        try
        {
            WriteWorkbook(path);
            Status(L.T("st.exported", path));
        }
        catch (IOException ex) { Status(L.T("st.exportError", ex.Message)); }
    }

    /// <summary>Writes the Excel report for the current filters to <paramref name="path"/>.</summary>
    void WriteWorkbook(string path)
    {
        EnsureBuilt("overview", "table", "projects");
        {
            using var wb = new XLWorkbook();

            // 1) pivot table as shown
            var ws = wb.AddWorksheet(L.T("x.report"));
            ws.Cell(1, 1).Value = _lastLabelHdr; ws.Cell(1, 2).Value = _lastSubHdr;
            for (int i = 0; i < _lastHeaders.Count; i++) ws.Cell(1, i + 3).Value = _lastHeaders[i].Replace("\n", " ");
            for (int r = 0; r < _lastReport.Count; r++)
            {
                ws.Cell(r + 2, 1).Value = _lastReport[r].Label; ws.Cell(r + 2, 2).Value = _lastReport[r].Sub;
                for (int i = 0; i < _lastReport[r].Cells.Length; i++)
                {
                    var c = ws.Cell(r + 2, i + 3); c.Value = Math.Round(_lastReport[r].Cells[i], 2);
                    c.Style.NumberFormat.Format = _lastFormats[i] == HF ? "#,##0.0;-#,##0.0;\"–\"" : "0.0";
                }
            }
            Header(ws); ws.Row(2).Style.Font.Bold = true;
            ws.SheetView.FreezeColumns(2);
            ws.Columns().AdjustToContents();

            // 2) statistics by year
            var ws2 = wb.AddWorksheet(L.T("x.stats"));
            ws2.Cell(1, 1).Value = L.T("ov.indicator");
            for (int i = 0; i < _statHeaders.Count; i++) ws2.Cell(1, i + 2).Value = _statHeaders[i];
            for (int r = 0; r < _lastStats.Count; r++)
            {
                ws2.Cell(r + 2, 1).Value = _lastStats[r].Label;
                for (int i = 0; i < _lastStats[r].Cells.Length; i++) ws2.Cell(r + 2, i + 2).Value = _lastStats[r].Cells[i];
            }
            Header(ws2);
            ws2.Columns().AdjustToContents();

            // 3) months x measures
            var ws3 = wb.AddWorksheet(L.T("x.months"));
            var series = _an.Series();
            ws3.Cell(1, 1).Value = L.T("x.period");
            ws3.Cell(1, 2).Value = L.T("m.total");
            for (int i = 0; i < series.Count; i++) ws3.Cell(1, i + 3).Value = series[i].name;
            var byP = _lastRows.GroupBy(r => r.Period).OrderBy(g => g.Key).ToList();
            for (int p = 0; p < byP.Count; p++)
            {
                var sub = byP[p].ToList();
                ws3.Cell(p + 2, 1).Value = byP[p].Key;
                ws3.Cell(p + 2, 2).Value = Math.Round(_an.Eval(Analytics.TOTAL, sub), 2);
                for (int i = 0; i < series.Count; i++)
                {
                    var c = ws3.Cell(p + 2, i + 3); c.Value = Math.Round(_an.Eval(series[i].id, sub), 2);
                    c.Style.NumberFormat.Format = "#,##0.0";
                }
            }
            Header(ws3);
            ws3.Columns().AdjustToContents();

            // 4) projects
            var ws4 = wb.AddWorksheet(L.T("x.projects"));
            string[] ph = { "SAP ID", L.T("col.desc"), L.T("pr.customer"), L.T("pr.type"), L.T("pr.cat"), _an.UnitLabel,
                            L.T("pr.people"), L.T("pr.first"), L.T("pr.last"), L.T("pr.profile") };
            for (int i = 0; i < ph.Length; i++) ws4.Cell(1, i + 1).Value = ph[i];
            for (int r = 0; r < _lastProj.Count; r++)
            {
                var p = _lastProj[r];
                ws4.Cell(r + 2, 1).Value = p.SapId; ws4.Cell(r + 2, 2).Value = p.Description;
                ws4.Cell(r + 2, 3).Value = p.Customer; ws4.Cell(r + 2, 4).Value = p.Type;
                ws4.Cell(r + 2, 5).Value = p.Category; ws4.Cell(r + 2, 6).Value = p.Value;
                ws4.Cell(r + 2, 7).Value = p.People; ws4.Cell(r + 2, 8).Value = p.First; ws4.Cell(r + 2, 9).Value = p.Last;
                ws4.Cell(r + 2, 10).Value = p.Profile;
            }
            Header(ws4);
            ws4.Columns().AdjustToContents();

            // 5) filtered, classified rows – ready for own pivot tables
            var ws5 = wb.AddWorksheet(L.T("x.data"));
            string[] dh = { L.T("x.period"), L.T("g.year"), L.T("x.month"), L.T("col.person"), "CC", "SAP ID", L.T("col.desc"),
                            L.T("pr.customer"), L.T("pr.type"), L.T("cat.nb"), L.T("cat.p"), L.T("col.hours"), L.T("col.value"), L.T("pr.profile") };
            for (int i = 0; i < dh.Length; i++) ws5.Cell(1, i + 1).Value = dh[i];
            int rr = 2;
            foreach (var r in _lastRows.OrderBy(r => r.Period).ThenBy(r => r.Person))
            {
                ws5.Cell(rr, 1).Value = r.Period; ws5.Cell(rr, 2).Value = r.Year; ws5.Cell(rr, 3).Value = r.Month;
                ws5.Cell(rr, 4).Value = r.Person; ws5.Cell(rr, 5).Value = r.Cc; ws5.Cell(rr, 6).Value = r.SapId;
                ws5.Cell(rr, 7).Value = r.Desc; ws5.Cell(rr, 8).Value = r.Customer;
                ws5.Cell(rr, 9).Value = r.Overhead ? L.T("type.overhead") : L.T("type.invoiced");
                ws5.Cell(rr, 10).Value = r.Newbuild ? 1 : 0; ws5.Cell(rr, 11).Value = r.PerRequest ? 1 : 0;
                ws5.Cell(rr, 12).Value = r.Hours; ws5.Cell(rr, 13).Value = r.Value; ws5.Cell(rr, 14).Value = r.Profile;
                rr++;
            }
            Header(ws5);
            if (rr > 2) ws5.Range(1, 1, rr - 1, dh.Length).SetAutoFilter();
            ws5.Columns().AdjustToContents(1, 200);

            // 6) filters and rules used (traceability of the numbers)
            var ws6 = wb.AddWorksheet(L.T("x.info"));
            int ir = 1;
            void Line(string a, string b) { ws6.Cell(ir, 1).Value = a; ws6.Cell(ir, 2).Value = b; ir++; }
            Line(L.T("x.created"), DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            Line(L.T("x.files"), string.Join("; ", _files.Select(x => $"{x.Name} ({x.PeriodText})")));
            Line(L.T("f.unit"), Money ? _cfg.ValueUnit : L.T("unit.hours"));
            foreach (var chip in PnlChips.Children.OfType<Border>())
                if (chip.Child is StackPanel sp && sp.Children.OfType<TextBlock>().FirstOrDefault() is { } tb)
                    Line(L.T("x.filter"), tb.Text ?? "");
            ir++;
            foreach (var p in _cfg.Profiles)
            {
                ws6.Cell(ir, 1).Value = $"{L.T("prof.name")}: {p.Name}"; ws6.Cell(ir, 1).Style.Font.Bold = true; ir++;
                Line(L.T("x.range"), p.RangeText);
                Line(L.T("set.cc"), string.Join(", ", p.CostCenters));
                Line(L.T("set.restricted"), string.Join(", ", p.RestrictedCostCenters));
                Line(L.T("x.relCount"), p.RelevantPeople.Count.ToString());
                Line(L.T("set.ohId"), string.Join(" | ", p.OverheadIdPatterns));
                Line(L.T("set.ohDesc"), string.Join(" | ", p.OverheadDescPatterns));
                Line(L.T("set.nbRegex"), p.NewbuildIdRegex);
                Line(L.T("set.nbEx"), string.Join(" | ", p.NewbuildExcludeDescriptions));
                Line(L.T("set.prRegex"), p.PerRequestIdRegex);
                Line(L.T("set.overrides"), string.Join(" | ", p.CustomerOverrides));
                ir++;
            }
            ws6.Column(1).Style.Font.Bold = true;
            ws6.Columns().AdjustToContents(1, 120);

            wb.SaveAs(path);
        }
    }

    /// <summary>Saves every chart as PNG into a chosen folder (all chart tabs are rebuilt first).</summary>
    async Task ExportCharts()
    {
        if (_lastRows.Count == 0) { Status(L.T("st.nothingToExport")); return; }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.T("dlg.chartFolder") });
        if (folders.Count == 0) return;
        var dir = folders[0].TryGetLocalPath(); if (dir == null) return;
        EnsureBuilt("time", "trend", "yoy", "cust", "people", "heat", "person");

        var plots = new (string name, ScottPlot.Avalonia.AvaPlot plot)[]
        {
            ("01_" + L.T("png.time"), PlotTime), ("02_" + L.T("png.trend"), PlotTrend), ("03_" + L.T("png.yoy"), PlotYoy),
            ("04_" + L.T("png.cum"), PlotCum), ("05_" + L.T("png.custYear"), PlotCust), ("06_" + L.T("png.share"), PlotPie),
            ("07_" + L.T("png.custTime"), PlotCustTime), ("08_" + L.T("png.rank"), PlotRank), ("09_" + L.T("png.heat"), PlotHeat),
            ("10_" + L.T("png.person"), PlotPerson)
        };
        int ok = 0;
        foreach (var (name, plot) in plots)
        {
            try { plot.Plot.SavePng(Path.Combine(dir, name + ".png"), 1600, 900); ok++; }
            catch (Exception ex) { Status($"{name}: {ex.Message}"); }
        }
        Status(L.T("st.chartsSaved", ok, dir));
    }
}
