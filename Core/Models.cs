namespace HoursTool;

/// <summary>
/// One raw timesheet line exactly as read from the export file (before any business rules are applied).
/// </summary>
/// <param name="Period">Normalised period key in the form <c>YYYY/MM</c>.</param>
/// <param name="Year">Calendar year of the period.</param>
/// <param name="Month">Month of the period (1–12).</param>
/// <param name="Person">Resource (employee) name as written in the export.</param>
/// <param name="Cc">Normalised cost centre of the person (see <see cref="Config.NormCc"/>).</param>
/// <param name="SapId">SAP project / WBS identifier.</param>
/// <param name="Desc">Project description.</param>
/// <param name="Abacus">ABACUS (customer) code, may be empty.</param>
/// <param name="Hours">Posted hours.</param>
/// <param name="Value">Posted value in the money unit of the export (typically CZK at sales rate).</param>
/// <param name="SourceFile">File name the row was loaded from (for diagnostics).</param>
public record Raw(string Period, int Year, int Month, string Person, string Cc, string SapId,
                  string Desc, string Abacus, double Hours, double Value, string SourceFile = "");

/// <summary>
/// A timesheet line after classification by the active <see cref="RuleProfile"/>.
/// All reports and charts are computed from a filtered list of these rows.
/// </summary>
/// <param name="Period">Period key <c>YYYY/MM</c>.</param>
/// <param name="Year">Calendar year.</param>
/// <param name="Month">Month 1–12.</param>
/// <param name="Person">Resource name.</param>
/// <param name="Cc">Normalised cost centre.</param>
/// <param name="SapId">SAP project ID.</param>
/// <param name="Desc">Project description.</param>
/// <param name="Hours">Posted hours.</param>
/// <param name="Value">Posted value (money).</param>
/// <param name="Customer">Customer code (e.g. <c>FI</c>), <c>"-"</c> for overhead, <c>"??"</c> when unknown.</param>
/// <param name="Overhead">True when the line is non-invoiced overhead.</param>
/// <param name="Newbuild">True when the line belongs to a newbuild project of the split customer.</param>
/// <param name="PerRequest">True when the line belongs to a per-request ("P") project.</param>
/// <param name="Profile">Name of the rule profile that classified the line.</param>
/// <param name="Abacus">ABACUS code from the export.</param>
/// <param name="Category">Project category: <c>NB</c> (newbuild), <c>P</c> (per-request), empty, or any
/// category defined by advanced rules.</param>
public record Row(string Period, int Year, int Month, string Person, string Cc, string SapId,
                  string Desc, string Customer, bool Overhead, bool Newbuild, bool PerRequest,
                  double Hours, double Value, string Profile = "", string Abacus = "", string Category = "");

/// <summary>One line of the main pivot table (Table tab). <see cref="Cells"/> are numeric, formatted by the grid.</summary>
public class ReportRow
{
    /// <summary>Group key (person, cost centre, customer, project or year).</summary>
    public string Label { get; set; } = "";
    /// <summary>Secondary text (cost centre of a person, description of a project).</summary>
    public string Sub { get; set; } = "";
    /// <summary>Numeric cells; the last cell is always Inv %.</summary>
    public double[] Cells { get; set; } = Array.Empty<double>();
    /// <summary>Invoiced total of the group, used for sorting.</summary>
    public double InvTotal { get; set; }
    /// <summary>Total of all hours/value of the group, used for sorting.</summary>
    public double AllTotal { get; set; }
    /// <summary>Invoiced share in percent, used for sorting.</summary>
    public double Pct { get; set; }
    /// <summary>True for the Σ total line.</summary>
    public bool IsTotal { get; set; }
}

/// <summary>Generic text table line (used by the yearly statistics grid).</summary>
public class TableRow
{
    /// <summary>Row caption.</summary>
    public string Label { get; set; } = "";
    /// <summary>Pre-formatted cell texts.</summary>
    public string[] Cells { get; set; } = Array.Empty<string>();
}

/// <summary>Aggregated project line for the Projects tab and the per-person project list.</summary>
public record ProjRow(string SapId, string Description, string Customer, string Type, string Category,
                      double Value, int People, string First, string Last, string Profile);

/// <summary>Severity of a <see cref="DataIssue"/>.</summary>
public enum IssueLevel { Info, Warning, Error }

/// <summary>A finding shown on the Data tab (data quality, rule coverage, loader diagnostics).</summary>
public record DataIssue(IssueLevel Level, string Category, string What, string Detail)
{
    /// <summary>Localised level text for the grid.</summary>
    public string LevelText => Level switch
    {
        IssueLevel.Error => L.T("lvl.error"),
        IssueLevel.Warning => L.T("lvl.warning"),
        _ => L.T("lvl.info")
    };
}

/// <summary>Information about one loaded source file (shown in the Data tab and the side panel).</summary>
public class LoadedFile
{
    /// <summary>Full path of the file.</summary>
    public string Path { get; set; } = "";
    /// <summary>File name only.</summary>
    public string Name => System.IO.Path.GetFileName(Path);
    /// <summary>Periods (YYYY/MM) that this file currently provides.</summary>
    public List<string> Periods { get; set; } = new();
    /// <summary>Number of rows currently provided by the file.</summary>
    public int Rows { get; set; }
    /// <summary>Loader diagnostics (sheet, header row, column mapping...).</summary>
    public LoadReport Report { get; set; } = new();
    /// <summary>Compact period range text for display.</summary>
    public string PeriodText => Periods.Count == 0 ? "–" : Periods.Count == 1 ? Periods[0] : $"{Periods.Min()} – {Periods.Max()} ({Periods.Count})";
}
