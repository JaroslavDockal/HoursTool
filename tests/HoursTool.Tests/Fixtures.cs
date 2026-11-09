using System.Text;
using ClosedXML.Excel;
namespace HoursTool.Tests;

/// <summary>Creates small timesheet exports in the different formats seen across years.</summary>
public static class Fixtures
{
    static Fixtures() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "hourstool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Classic format: filter texts above the header, combined Year/Month column, a Total row.</summary>
    public static string Classic(string dir, string name = "classic.xlsx")
    {
        var path = Path.Combine(dir, name);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Export");
        ws.Cell(1, 1).Value = "Filter: Cost Center = C23106, C23107";
        ws.Cell(2, 1).Value = "Created 2025-01-05";
        string[] h = { "Year/Month", "Resource Name", "Resource Cost Center", "SAP Project ID", "Project Description", "ABACUS", "Posted Time (h)", "Posted Time (CZK sales rate)" };
        for (int i = 0; i < h.Length; i++) ws.Cell(4, i + 1).Value = h[i];
        object[][] rows =
        {
            new object[] { "2024/01", "Novák Jan", "C23106", "FKMMPFI23-I001", "ES_FI_NB_SHIP1", "FI0001", 100.0, 120000.0 },
            new object[] { "2024/01", "Novák Jan", "C23106", "INTR0001", "Internal meeting", "", 20.0, 0.0 },
            new object[] { "2024/01", "Svoboda Petr", "23107", "FKMMPUS24P123", "US_SERVICE", "US0002", 50.0, 60000.0 },
            new object[] { "2024/02", "Svoboda Petr", "CC23107", "FKMMPFI23-I001", "ES_FI_RCS_ENG", "", 10.0, 12000.0 },
            new object[] { "2024/02", "Dvořák Karel", "C23107", "FKMMPIT22-X010", "SOMETHING_IT_X", "", 30.0, 30000.0 },
            new object[] { "2024/02", "Cizí Člověk", "C99999", "FKMMPFI23-I001", "x", "FI", 5.0, 1.0 },
        };
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
                ws.Cell(5 + r, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
        ws.Cell(5 + rows.Length, 1).Value = "Total";
        ws.Cell(5 + rows.Length, 7).Value = 215.0;
        wb.SaveAs(path);
        return path;
    }

    /// <summary>"Other year" format: separate Year and Month columns, different header names, numbers as text with decimal comma.</summary>
    public static string Variant(string dir, string name = "variant.xlsx")
    {
        var path = Path.Combine(dir, name);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Data 2025");
        string[] h = { "Rok", "Měsíc", "Employee", "Cost Centre", "WBS Element", "Description", "Customer Code", "Hours", "Amount" };
        for (int i = 0; i < h.Length; i++) ws.Cell(1, i + 1).Value = h[i];
        object[][] rows =
        {
            new object[] { 2025, "březen", "Novák Jan", "C23106", "FKMMPFI23-I001", "ES_FI_NB_SHIP1", "FI0001", "1 234,5", "1 000 000,50" },
            new object[] { 2025, 3, "Novák Jan", "C23106", "OH-2025-ADMIN", "ADMIN", "", "10,5", "0" },
        };
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
                ws.Cell(2 + r, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
        wb.SaveAs(path);
        return path;
    }

    /// <summary>CSV export with semicolons and a period like 03.2026.</summary>
    public static string Csv(string dir, string name = "export.csv")
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path,
            "Period;Resource Name;Cost Center;Project ID;Project Description;ABACUS;Posted Time (h);CZK\n" +
            "03.2026;Novák Jan;C23106;FKMMPNO25-A001;NO_SHIP;NO0001;7,5;9000\n" +
            "003.2026;Svoboda Petr;C23107;INTR0002;Training;;2;0\n", Encoding.UTF8);
        return path;
    }
}
