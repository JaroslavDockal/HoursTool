using System.Text.Json;
namespace HoursTool;

public record Raw(string Period, int Year, int Month, string Person, string Cc, string SapId,
                  string Desc, string Abacus, double Hours, double Czk);

public record Row(string Period, int Year, int Month, string Person, string Cc, string SapId,
                  string Desc, string Customer, bool Overhead, bool Newbuild, bool PerRequest, double Hours, double Czk);

public class ReportRow
{
    public string Label { get; set; } = "";
    public string Sub { get; set; } = "";
    public double[] Cells { get; set; } = Array.Empty<double>();
    public double InvTotal { get; set; }
    public double Pct { get; set; }
}

public class TableRow
{
    public string Label { get; set; } = "";
    public string[] Cells { get; set; } = Array.Empty<string>();
}

public record ProjRow(string SapId, string Description, string Customer, string Type, string Newbuild,
                      double Value, int People, string First, string Last);

public record DataIssue(string Level, string What, string Detail);

public class Config
{
    public List<string> CostCenters { get; set; } = new() { "C23106", "C23107", "C23211" };
    // CC, kde se do dat zapocitaji jen lide ze seznamu RelevantPeople
    public List<string> RestrictedCostCenters { get; set; } = new() { "C23107" };
    public List<string> RelevantPeople { get; set; } = new();
    // jen docasne skryti lide v bocnim panelu
    public List<string> DeselectedPeople { get; set; } = new();
    // regexy nad SAP Project ID; shoda = overhead (neinvoiced)
    public List<string> OverheadIdPatterns { get; set; } = new() { "INTR" };
    // regex nad SAP Project ID; shoda = FI newbuild
    public string NewbuildIdRegex { get; set; } = @"^FKMMPFI\d{2}-I\d{3}$";
    public List<string> NewbuildExcludeDescriptions { get; set; } = new() { "ES_FI_RCS_ENG", "ES_FI_RCS_FAT_S" };
    // regex nad SAP Project ID; shoda = P projekt (per-request)
    public string PerRequestIdRegex { get; set; } = @"^FKMMP[A-Z]{2}\d{2}P";
    public List<string> Customers { get; set; } = new() { "FI", "US", "IT", "NO" };
    public List<string> LastFiles { get; set; } = new();
    public string Theme { get; set; } = "System";

    public static string FilePath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoursTool");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "config.json");
        }
    }

    public static string Norm(string cc)
    {
        cc = cc.Trim().ToUpperInvariant();
        if (cc.StartsWith("CC")) cc = cc.Substring(1);
        if (cc.Length > 0 && char.IsDigit(cc[0])) cc = "C" + cc;
        return cc;
    }

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new Config();
        }
        catch { }
        return new Config();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }
}
