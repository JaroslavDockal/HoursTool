using System.Globalization;
using System.Text;
using ExcelDataReader;
namespace HoursTool;

/// <summary>Diagnostics produced while loading one file (shown on the Data tab).</summary>
public class LoadReport
{
    /// <summary>Detected file format (xlsx, xls, csv).</summary>
    public string Format { get; set; } = "";
    /// <summary>Sheets that contained a recognised header, with their header row (1-based).</summary>
    public List<string> Sheets { get; set; } = new();
    /// <summary>Logical field → header text actually used.</summary>
    public Dictionary<string, string> Mapping { get; set; } = new();
    /// <summary>Header cells that were not mapped to any field.</summary>
    public List<string> Unmapped { get; set; } = new();
    /// <summary>Number of data rows accepted.</summary>
    public int Accepted { get; set; }
    /// <summary>Rows skipped because no valid period could be read (totals, filter texts, blank lines...).</summary>
    public int SkippedNoPeriod { get; set; }
    /// <summary>Rows skipped because the person was empty.</summary>
    public int SkippedNoPerson { get; set; }
    /// <summary>Cells whose numeric value could not be parsed (treated as 0).</summary>
    public int BadNumbers { get; set; }
    /// <summary>Non-fatal warnings.</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Sheets as one text.</summary>
    public string SheetsText => string.Join(", ", Sheets);

    /// <summary>One-line summary of the column mapping.</summary>
    public string MappingText => string.Join(", ", Mapping.Select(kv => $"{L.T("col." + kv.Key)} ← \"{kv.Value}\""));
}

/// <summary>Result of <see cref="Loader.Load"/>.</summary>
public record LoadResult(List<Raw> Rows, LoadReport Report);

/// <summary>
/// Reads timesheet exports (xlsx, xlsm, xls, csv). The loader is tolerant to format differences between years:
/// <list type="bullet">
/// <item>the header row is searched for in the first 60 rows of every sheet (filter texts above the table are skipped),</item>
/// <item>columns are recognised by configurable aliases (<see cref="ColumnMap"/>), exact match first, then "contains",</item>
/// <item>the period may be one column (many formats, see <see cref="Periods.TryParse"/>) or separate Year and Month columns,</item>
/// <item>all sheets with a recognisable header are read (e.g. one sheet per year),</item>
/// <item>numbers may use decimal comma or dot and thousands separators.</item>
/// </list>
/// </summary>
public static class Loader
{
    /// <summary>File extensions supported by the loader.</summary>
    public static readonly string[] Extensions = { ".xlsx", ".xlsm", ".xls", ".csv" };

    const int HeaderSearchRows = 60;

    /// <summary>
    /// Normalises a header text for comparison: removes diacritics, keeps letters and digits only, lower case.
    /// </summary>
    public static string Norm(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Maps header cells to logical fields. Returns field key → column index.
    /// Exact (normalised) alias matches are assigned first, then headers that contain an alias.
    /// </summary>
    public static Dictionary<string, int> MapHeader(IReadOnlyList<string> headers, ColumnMap map)
    {
        var norm = headers.Select(Norm).ToList();
        var res = new Dictionary<string, int>();
        var used = new HashSet<int>();
        var fields = map.All().ToList();

        // pass 1: exact match, in alias order (first alias has priority)
        foreach (var (key, aliases) in fields)
            foreach (var a in aliases.Select(Norm).Where(a => a != ""))
            {
                int i = norm.FindIndex(h => h == a);
                if (i >= 0 && !used.Contains(i)) { res[key] = i; used.Add(i); break; }
            }
        // pass 2: header contains alias (only for aliases with >= 4 chars to avoid false hits like "cc")
        foreach (var (key, aliases) in fields)
        {
            if (res.ContainsKey(key)) continue;
            foreach (var a in aliases.Select(Norm).Where(a => a.Length >= 4))
            {
                int i = norm.FindIndex(h => h.Contains(a));
                if (i >= 0 && !used.Contains(i)) { res[key] = i; used.Add(i); break; }
            }
        }
        return res;
    }

    /// <summary>True when the mapping contains enough fields to read data rows.</summary>
    static bool Usable(Dictionary<string, int> m) =>
        m.ContainsKey("person") && (m.ContainsKey("period") || (m.ContainsKey("year") && m.ContainsKey("month")));

    /// <summary>
    /// Parses a number from a cell. Accepts doubles, integers and texts like <c>1 234,5</c>, <c>1,234.5</c>, <c>1.234,5</c>.
    /// </summary>
    public static bool TryNumber(object? v, out double d)
    {
        d = 0;
        switch (v)
        {
            case null: return true;
            case double x: d = x; return true;
            case int i: d = i; return true;
            case long l: d = l; return true;
            case decimal m: d = (double)m; return true;
            case bool: return false;
        }
        var s = (Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").Trim()
                .Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace("'", "");
        if (s == "" || s == "-" || s == "–") return true;
        int lc = s.LastIndexOf(','), ld = s.LastIndexOf('.');
        if (lc >= 0 && ld >= 0)
        {
            // the separator that comes last is the decimal one
            if (lc > ld) s = s.Replace(".", "").Replace(',', '.');
            else s = s.Replace(",", "");
        }
        else if (lc >= 0) s = s.Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
    }

    /// <summary>
    /// Loads one file. Throws <see cref="InvalidDataException"/> (with a localised message) when no usable header is found.
    /// </summary>
    public static LoadResult Load(string path, ColumnMap map)
    {
        var rep = new LoadReport();
        var list = new List<Raw>();
        var file = path;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        rep.Format = ext.TrimStart('.');

        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var rd = ext == ".csv"
            ? ExcelReaderFactory.CreateCsvReader(fs, new ExcelReaderConfiguration
            {
                FallbackEncoding = Encoding.GetEncoding(1250),
                AutodetectSeparators = new[] { ';', ',', '\t', '|' }
            })
            : ExcelReaderFactory.CreateReader(fs);

        do
        {
            Dictionary<string, int>? col = null;
            int rowNo = 0;
            while (rd.Read())
            {
                rowNo++;
                if (col == null)
                {
                    if (rowNo > HeaderSearchRows) break;          // sheet without a recognisable table
                    var hdr = new List<string>();
                    for (int i = 0; i < rd.FieldCount; i++)
                        hdr.Add(Convert.ToString(rd.GetValue(i), CultureInfo.InvariantCulture)?.Trim() ?? "");
                    var m = MapHeader(hdr, map);
                    if (!Usable(m)) continue;
                    col = m;
                    rep.Sheets.Add($"{rd.Name} (#{rowNo})");
                    foreach (var kv in m) rep.Mapping.TryAdd(kv.Key, hdr[kv.Value]);
                    var mapped = m.Values.ToHashSet();
                    for (int i = 0; i < hdr.Count; i++)
                        if (hdr[i] != "" && !mapped.Contains(i) && !rep.Unmapped.Contains(hdr[i])) rep.Unmapped.Add(hdr[i]);
                    continue;
                }

                object? Get(string key) =>
                    col.TryGetValue(key, out var i) && i < rd.FieldCount ? rd.GetValue(i) : null;
                string S(string key) => (Convert.ToString(Get(key), CultureInfo.InvariantCulture) ?? "").Trim();
                double D(string key)
                {
                    if (!col.ContainsKey(key)) return 0;
                    if (TryNumber(Get(key), out var d)) return d;
                    rep.BadNumbers++;
                    return 0;
                }

                int year, month;
                bool okPeriod = col.ContainsKey("period")
                    ? Periods.TryParse(Get("period"), out year, out month)
                    : (Periods.TryYear(Get("year"), out year) & Periods.TryMonth(Get("month"), out month));
                if (!okPeriod) { rep.SkippedNoPeriod++; continue; }   // totals, filter texts, empty rows
                var person = S("person");
                if (person == "") { rep.SkippedNoPerson++; continue; }

                list.Add(new Raw(Periods.Key(year, month), year, month, person,
                    Config.NormCc(S("cc")), S("projectId"), S("desc"), S("abacus"), D("hours"), D("value"), file));
            }
        } while (rd.NextResult());

        if (rep.Sheets.Count == 0)
            throw new InvalidDataException(L.T("load.noHeader"));
        foreach (var req in new[] { "projectId", "hours" })
            if (!rep.Mapping.ContainsKey(req))
                throw new InvalidDataException(L.T("load.missingCol", L.T("col." + req)));
        foreach (var opt in new[] { "cc", "desc", "abacus", "value" })
            if (!rep.Mapping.ContainsKey(opt)) rep.Warnings.Add(L.T("load.optMissing", L.T("col." + opt)));
        rep.Accepted = list.Count;
        return new LoadResult(list, rep);
    }
}
