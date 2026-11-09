using Avalonia.Platform.Storage;
using ClosedXML.Excel;
namespace HoursTool;

public partial class MainWindow
{
    async Task ExportXlsx()
    {
        if (_lastReport.Count == 0) { Status("Není co exportovat."); return; }
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "hodiny_report.xlsx", DefaultExtension = "xlsx",
            FileTypeChoices = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } }
        });
        var path = f?.TryGetLocalPath(); if (path == null) return;

        using var wb = new XLWorkbook();

        // 1) Report (aktualni tabulka)
        var ws = wb.AddWorksheet("Report");
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
        ws.Row(1).Style.Font.Bold = true; ws.Row(2).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1); ws.SheetView.FreezeColumns(2);
        ws.Columns().AdjustToContents();

        // 2) Statistiky po letech
        var ws2 = wb.AddWorksheet("Statistiky");
        ws2.Cell(1, 1).Value = "Ukazatel";
        for (int i = 0; i < _statHeaders.Count; i++) ws2.Cell(1, i + 2).Value = _statHeaders[i];
        for (int r = 0; r < _lastStats.Count; r++)
        {
            ws2.Cell(r + 2, 1).Value = _lastStats[r].Label;
            for (int i = 0; i < _lastStats[r].Cells.Length; i++) ws2.Cell(r + 2, i + 2).Value = _lastStats[r].Cells[i];
        }
        ws2.Row(1).Style.Font.Bold = true;
        ws2.Columns().AdjustToContents();

        // 3) Mesice x metriky
        var ws3 = wb.AddWorksheet("Měsíce");
        var names = SeriesNames();
        ws3.Cell(1, 1).Value = "Období";
        for (int i = 0; i < names.Count; i++) ws3.Cell(1, i + 2).Value = names[i];
        var periods = _lastRows.Select(r => r.Period).Distinct().OrderBy(x => x).ToList();
        for (int p = 0; p < periods.Count; p++)
        {
            var sub = _lastRows.Where(r => r.Period == periods[p]).ToList();
            ws3.Cell(p + 2, 1).Value = periods[p];
            for (int i = 0; i < names.Count; i++)
            {
                var c = ws3.Cell(p + 2, i + 2); c.Value = Math.Round(Eval(names[i], sub), 2);
                c.Style.NumberFormat.Format = "#,##0.0";
            }
        }
        ws3.Row(1).Style.Font.Bold = true;
        ws3.SheetView.FreezeRows(1);
        ws3.Columns().AdjustToContents();

        // 4) Projekty
        var ws4 = wb.AddWorksheet("Projekty");
        string[] ph = { "SAP ID", "Popis", "Zákazník", "Typ", "FI NB", Czk ? "CZK" : "Hodiny", "Lidí", "Od", "Do" };
        for (int i = 0; i < ph.Length; i++) ws4.Cell(1, i + 1).Value = ph[i];
        for (int r = 0; r < _lastProj.Count; r++)
        {
            var p = _lastProj[r];
            ws4.Cell(r + 2, 1).Value = p.SapId; ws4.Cell(r + 2, 2).Value = p.Description;
            ws4.Cell(r + 2, 3).Value = p.Customer; ws4.Cell(r + 2, 4).Value = p.Type;
            ws4.Cell(r + 2, 5).Value = p.Newbuild; ws4.Cell(r + 2, 6).Value = p.Value;
            ws4.Cell(r + 2, 7).Value = p.People; ws4.Cell(r + 2, 8).Value = p.First; ws4.Cell(r + 2, 9).Value = p.Last;
        }
        ws4.Row(1).Style.Font.Bold = true;
        ws4.SheetView.FreezeRows(1);
        ws4.Columns().AdjustToContents();

        wb.SaveAs(path);
        Status("Exportováno: " + path);
    }

    async Task ExportCharts()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Složka pro grafy (PNG)" });
        if (folders.Count == 0) return;
        var dir = folders[0].TryGetLocalPath(); if (dir == null) return;

        var plots = new (string name, ScottPlot.Avalonia.AvaPlot plot)[]
        {
            ("01_prubeh", PlotTime), ("02_trend", PlotTrend), ("03_mezirocni", PlotYoy), ("04_kumulativni", PlotCum),
            ("05_zakaznici_roky", PlotCust), ("06_podily", PlotPie), ("07_zakaznici_cas", PlotCustTime),
            ("08_zebricek_lidi", PlotRank), ("09_heatmapa", PlotHeat), ("10_osoba", PlotPerson)
        };
        int ok = 0;
        foreach (var (name, plot) in plots)
        {
            try { plot.Plot.SavePng(Path.Combine(dir, name + ".png"), 1500, 850); ok++; }
            catch (Exception ex) { Status($"Graf {name}: {ex.Message}"); }
        }
        Status($"Uloženo {ok} grafů do {dir}");
    }
}
