using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace HoursTool;

/// <summary>
/// A named set of business rules that applies to a range of periods.
/// <para>
/// Timesheet exports from different years may follow slightly different conventions
/// (renamed cost centres, new project-ID schemes, different overhead codes...).
/// Every row is classified by the first profile (top to bottom) whose period range contains
/// the row's period. See <see cref="Config.ProfileFor"/>.
/// </para>
/// </summary>
public class RuleProfile
{
    /// <summary>Display name of the profile.</summary>
    public string Name { get; set; } = "Default";

    /// <summary>First period the profile applies to: <c>YYYY</c>, <c>YYYY/MM</c> or empty (= open start).</summary>
    public string From { get; set; } = "";

    /// <summary>Last period the profile applies to: <c>YYYY</c>, <c>YYYY/MM</c> or empty (= open end).</summary>
    public string To { get; set; } = "";

    /// <summary>Cost centres whose rows are included in the analysis. Rows of other cost centres are ignored.</summary>
    public List<string> CostCenters { get; set; } = new() { "C23106", "C23107", "C23211" };

    /// <summary>Cost centres where only people listed in <see cref="RelevantPeople"/> are counted.</summary>
    public List<string> RestrictedCostCenters { get; set; } = new() { "C23107" };

    /// <summary>People counted in <see cref="RestrictedCostCenters"/> (exact names as in the export, case-insensitive).</summary>
    public List<string> RelevantPeople { get; set; } = new();

    /// <summary>Regexes matched against the SAP project ID; a match marks the row as overhead (non-invoiced).</summary>
    public List<string> OverheadIdPatterns { get; set; } = new() { "INTR" };

    /// <summary>Regexes matched against the project description; a match marks the row as overhead.</summary>
    public List<string> OverheadDescPatterns { get; set; } = new();

    /// <summary>Regex matched against the SAP project ID; a match marks a newbuild project of the split customer.</summary>
    public string NewbuildIdRegex { get; set; } = @"^FKMMPFI\d{2}-I\d{3}$";

    /// <summary>Project descriptions (exact, case-insensitive) that are never treated as newbuild.</summary>
    public List<string> NewbuildExcludeDescriptions { get; set; } = new() { "ES_FI_RCS_ENG", "ES_FI_RCS_FAT_S" };

    /// <summary>Regex matched against the SAP project ID; a match marks a per-request ("P") project.</summary>
    public string PerRequestIdRegex { get; set; } = @"^FKMMP[A-Z]{2}\d{2}P";

    /// <summary>How many leading characters of the ABACUS code form the customer code (0 = do not use ABACUS).</summary>
    public int AbacusPrefixLength { get; set; } = 2;

    /// <summary>Customer codes searched for as <c>_XX_</c> tokens in the project description (fallback when ABACUS is empty).</summary>
    public List<string> DescCustomerCodes { get; set; } = new() { "FI", "US", "NO", "IT", "CH", "CN", "SG", "NL", "CZ", "GB", "HK", "DE", "SE", "DK" };

    /// <summary>Regex over the SAP project ID whose first capture group is the customer code (last fallback).</summary>
    public string IdCustomerRegex { get; set; } = @"^FKMMP([A-Z]{2})";

    /// <summary>
    /// Manual customer overrides, one per line, in the form <c>regex =&gt; CODE</c>.
    /// The regex is matched against <c>"SAPID DESCRIPTION"</c>; the first match wins and beats all automatic detection.
    /// </summary>
    public List<string> CustomerOverrides { get; set; } = new();

    /// <summary>Returns a deep copy of the profile.</summary>
    public RuleProfile Clone() =>
        JsonSerializer.Deserialize<RuleProfile>(JsonSerializer.Serialize(this))!;

    /// <summary>Lower bound as a comparable period key (YYYY/MM) or <c>null</c> when open.</summary>
    [JsonIgnore] public string? FromKey => Periods.ParseBound(From, false);

    /// <summary>Upper bound as a comparable period key (YYYY/MM) or <c>null</c> when open.</summary>
    [JsonIgnore] public string? ToKey => Periods.ParseBound(To, true);

    /// <summary>True when the profile has no period restriction at all.</summary>
    [JsonIgnore] public bool IsOpen => string.IsNullOrWhiteSpace(From) && string.IsNullOrWhiteSpace(To);

    /// <summary>True when <paramref name="period"/> (YYYY/MM) falls inside the profile's range.</summary>
    public bool Covers(string period)
    {
        var f = FromKey; var t = ToKey;
        return (f == null || string.CompareOrdinal(period, f) >= 0) && (t == null || string.CompareOrdinal(period, t) <= 0);
    }

    /// <summary>Human readable range, e.g. <c>2024/01 – 2024/12</c>.</summary>
    [JsonIgnore]
    public string RangeText => IsOpen ? L.T("prof.anyPeriod")
        : $"{(string.IsNullOrWhiteSpace(From) ? "…" : From.Trim())} – {(string.IsNullOrWhiteSpace(To) ? "…" : To.Trim())}";

    /// <summary>
    /// Validates all regexes and bounds. Returns a list of human readable problems (empty = valid).
    /// </summary>
    public List<string> Validate()
    {
        var errs = new List<string>();
        void Rx(string what, string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            try { _ = new Regex(p); }
            catch (ArgumentException ex) { errs.Add($"{Name}: {what}: {ex.Message}"); }
        }
        foreach (var p in OverheadIdPatterns) Rx(L.T("set.ohId"), p);
        foreach (var p in OverheadDescPatterns) Rx(L.T("set.ohDesc"), p);
        Rx(L.T("set.nbRegex"), NewbuildIdRegex);
        Rx(L.T("set.prRegex"), PerRequestIdRegex);
        Rx(L.T("set.idCust"), IdCustomerRegex);
        foreach (var o in CustomerOverrides)
        {
            var parts = o.Split("=>", 2);
            if (parts.Length != 2 || parts[1].Trim() == "") errs.Add($"{Name}: {L.T("set.overrides")}: '{o}' – {L.T("err.overrideFormat")}");
            else Rx(L.T("set.overrides"), parts[0].Trim());
        }
        if (!string.IsNullOrWhiteSpace(From) && FromKey == null) errs.Add($"{Name}: {L.T("prof.from")}: '{From}' – {L.T("err.period")}");
        if (!string.IsNullOrWhiteSpace(To) && ToKey == null) errs.Add($"{Name}: {L.T("prof.to")}: '{To}' – {L.T("err.period")}");
        if (FromKey != null && ToKey != null && string.CompareOrdinal(FromKey, ToKey) > 0) errs.Add($"{Name}: {L.T("err.range")}");
        if (AbacusPrefixLength < 0 || AbacusPrefixLength > 10) errs.Add($"{Name}: {L.T("set.abacusLen")}: 0–10");
        return errs;
    }
}

/// <summary>
/// Header aliases for every logical column of the timesheet export.
/// Header cells are compared after normalisation (lower case, no diacritics, letters and digits only),
/// so <c>"Posted Time (h)"</c>, <c>"posted_time_h"</c> and <c>"POSTED TIME H"</c> are all equal to <c>postedtimeh</c>.
/// </summary>
public class ColumnMap
{
    /// <summary>Combined period column (e.g. <c>Year/Month</c> with values like <c>2024/03</c>, <c>03.2024</c> or a date).</summary>
    public List<string> Period { get; set; } = new() { "Year/Month", "YearMonth", "Period", "Fiscal Period", "Období", "Month/Year", "Datum", "Date" };
    /// <summary>Separate year column (used when no period column exists).</summary>
    public List<string> Year { get; set; } = new() { "Year", "Rok", "Fiscal Year" };
    /// <summary>Separate month column (used when no period column exists).</summary>
    public List<string> Month { get; set; } = new() { "Month", "Měsíc", "Fiscal Month" };
    /// <summary>Person (resource) name.</summary>
    public List<string> Person { get; set; } = new() { "Resource Name", "Resource", "Employee", "Employee Name", "Name", "Jméno", "Zaměstnanec", "Pracovník" };
    /// <summary>Cost centre of the person.</summary>
    public List<string> CostCenter { get; set; } = new() { "Resource Cost Center", "Resource Cost Centre", "Cost Center", "Cost Centre", "CC", "Nákladové středisko", "Středisko" };
    /// <summary>SAP project / WBS ID.</summary>
    public List<string> ProjectId { get; set; } = new() { "SAP Project ID", "Project ID", "SAP ID", "WBS", "WBS Element", "Projekt ID", "Zakázka" };
    /// <summary>Project description.</summary>
    public List<string> Description { get; set; } = new() { "Project Description", "Description", "Project Name", "Popis", "Název projektu" };
    /// <summary>ABACUS / customer code.</summary>
    public List<string> Abacus { get; set; } = new() { "ABACUS", "Abacus Code", "Customer Code", "Zákazník" };
    /// <summary>Posted hours.</summary>
    public List<string> Hours { get; set; } = new() { "Posted Time (h)", "Posted Time", "Hours", "Hodiny", "Time (h)", "Čas (h)" };
    /// <summary>Posted value (money).</summary>
    public List<string> Value { get; set; } = new() { "Posted Time (CZK sales rate)", "Posted Time CZK Sales Rate", "CZK", "Value", "Amount", "Částka", "Hodnota" };

    /// <summary>Enumerates (field key, aliases) pairs; the key is used for localisation (<c>col.&lt;key&gt;</c>).</summary>
    public IEnumerable<(string key, List<string> aliases)> All() => new[]
    {
        ("period", Period), ("year", Year), ("month", Month), ("person", Person), ("cc", CostCenter),
        ("projectId", ProjectId), ("desc", Description), ("abacus", Abacus), ("hours", Hours), ("value", Value)
    };

    /// <summary>Replaces the alias list of a field identified by its key.</summary>
    public void Set(string key, List<string> aliases)
    {
        switch (key)
        {
            case "period": Period = aliases; break;
            case "year": Year = aliases; break;
            case "month": Month = aliases; break;
            case "person": Person = aliases; break;
            case "cc": CostCenter = aliases; break;
            case "projectId": ProjectId = aliases; break;
            case "desc": Description = aliases; break;
            case "abacus": Abacus = aliases; break;
            case "hours": Hours = aliases; break;
            case "value": Value = aliases; break;
        }
    }
}

/// <summary>
/// Persistent application settings, stored as JSON in <c>%APPDATA%/HoursTool/config.json</c>.
/// </summary>
public class Config
{
    /// <summary>Config schema version; used for migrations of older files.</summary>
    public int Version { get; set; } = 2;

    /// <summary>UI language: <c>en</c> or <c>cs</c>.</summary>
    public string Language { get; set; } = "";

    /// <summary>Theme: <c>System</c>, <c>Light</c> or <c>Dark</c>.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Rule profiles in priority order (first matching profile wins).</summary>
    public List<RuleProfile> Profiles { get; set; } = new();

    /// <summary>Customer codes shown as separate columns/series; all others are grouped as "Other customers".</summary>
    public List<string> Customers { get; set; } = new() { "FI", "US", "IT", "NO" };

    /// <summary>Customer whose invoiced work is split into newbuild / P projects / other.</summary>
    public string SplitCustomer { get; set; } = "FI";

    /// <summary>Label of the money unit of the value column (e.g. CZK).</summary>
    public string ValueUnit { get; set; } = "CZK";

    /// <summary>Header aliases used by the loader.</summary>
    public ColumnMap Columns { get; set; } = new();

    /// <summary>People temporarily hidden in the side panel.</summary>
    public List<string> DeselectedPeople { get; set; } = new();

    /// <summary>Files loaded in the last session (re-loaded on start).</summary>
    public List<string> LastFiles { get; set; } = new();

    /// <summary>True when the filter side panel is shown.</summary>
    public bool SidePanelVisible { get; set; } = true;

    // ---- legacy (v1) fields: read only for migration, never written ----
    /// <summary>Legacy v1 field.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? CostCenters { get; set; }
    /// <summary>Legacy v1 field.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? RestrictedCostCenters { get; set; }
    /// <summary>Legacy v1 field.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? RelevantPeople { get; set; }
    /// <summary>Legacy v1 field.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? OverheadIdPatterns { get; set; }
    /// <summary>Legacy v1 field.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? NewbuildIdRegex { get; set; }
    /// <summary>Legacy v1 field.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? NewbuildExcludeDescriptions { get; set; }
    /// <summary>Legacy v1 field.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PerRequestIdRegex { get; set; }

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>Directory of the config file (created on demand).</summary>
    public static string Dir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoursTool");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Full path of the config file.</summary>
    public static string FilePath => Path.Combine(Dir, "config.json");

    /// <summary>
    /// Normalises a cost centre code: trims, upper-cases, drops a duplicated leading C (<c>CC23106</c>)
    /// and adds the leading C to purely numeric codes (<c>23106</c> → <c>C23106</c>).
    /// </summary>
    public static string NormCc(string cc)
    {
        cc = cc.Trim().ToUpperInvariant();
        if (cc.EndsWith(".0")) cc = cc[..^2];               // numeric cell read as 23106.0
        if (cc.StartsWith("CC")) cc = cc.Substring(1);
        if (cc.Length > 0 && char.IsDigit(cc[0])) cc = "C" + cc;
        return cc;
    }

    /// <summary>
    /// Returns the profile that classifies rows of <paramref name="period"/>:
    /// the first profile whose range covers the period; otherwise the first open profile; otherwise the first profile.
    /// </summary>
    public RuleProfile ProfileFor(string period) =>
        Profiles.FirstOrDefault(p => p.Covers(period)) ?? Profiles.FirstOrDefault(p => p.IsOpen) ?? Profiles[0];

    /// <summary>True when an explicit profile range covers the period (no fallback needed).</summary>
    public bool HasProfileFor(string period) => Profiles.Any(p => p.Covers(period));

    /// <summary>Upgrades older config files and fixes missing values. Safe to call repeatedly.</summary>
    public void Migrate()
    {
        if (Profiles.Count == 0)
        {
            var p = new RuleProfile { Name = "Default" };
            if (CostCenters != null) p.CostCenters = CostCenters;
            if (RestrictedCostCenters != null) p.RestrictedCostCenters = RestrictedCostCenters;
            if (RelevantPeople != null) p.RelevantPeople = RelevantPeople;
            if (OverheadIdPatterns != null) p.OverheadIdPatterns = OverheadIdPatterns;
            if (NewbuildIdRegex != null) p.NewbuildIdRegex = NewbuildIdRegex;
            if (NewbuildExcludeDescriptions != null) p.NewbuildExcludeDescriptions = NewbuildExcludeDescriptions;
            if (PerRequestIdRegex != null) p.PerRequestIdRegex = PerRequestIdRegex;
            Profiles.Add(p);
        }
        CostCenters = null; RestrictedCostCenters = null; RelevantPeople = null; OverheadIdPatterns = null;
        NewbuildIdRegex = null; NewbuildExcludeDescriptions = null; PerRequestIdRegex = null;
        Columns ??= new ColumnMap();
        if (string.IsNullOrWhiteSpace(SplitCustomer)) SplitCustomer = "FI";
        if (string.IsNullOrWhiteSpace(ValueUnit)) ValueUnit = "CZK";
        foreach (var p in Profiles)
        {
            p.CostCenters = p.CostCenters.Select(NormCc).Where(x => x != "").Distinct().ToList();
            p.RestrictedCostCenters = p.RestrictedCostCenters.Select(NormCc).Where(x => x != "").Distinct().ToList();
        }
        Version = 2;
    }

    /// <summary>Validates every profile; returns human readable problems (empty = valid).</summary>
    public List<string> Validate()
    {
        var errs = Profiles.SelectMany(p => p.Validate()).ToList();
        if (Profiles.Count == 0) errs.Add(L.T("err.noProfile"));
        foreach (var g in Profiles.GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errs.Add(L.T("err.dupProfile", g.Key));
        return errs;
    }

    /// <summary>Deep copy of the whole configuration.</summary>
    public Config Clone() => JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(this, JsonOpts), JsonOpts)!;

    /// <summary>Serialises the config to indented JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>Parses (and migrates) a config from JSON text.</summary>
    public static Config FromJson(string json)
    {
        var c = JsonSerializer.Deserialize<Config>(json, JsonOpts) ?? new Config();
        c.Migrate();
        return c;
    }

    /// <summary>Loads the config file; returns defaults when the file is missing or corrupt.</summary>
    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath)) return FromJson(File.ReadAllText(FilePath));
        }
        catch
        {
            // corrupt file: keep a backup so the user does not lose it, start with defaults
            try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
        }
        var c = new Config();
        c.Migrate();
        return c;
    }

    /// <summary>Writes the config file. Errors are swallowed (settings are not critical).</summary>
    public void Save()
    {
        try { File.WriteAllText(FilePath, ToJson()); }
        catch { }
    }
}
