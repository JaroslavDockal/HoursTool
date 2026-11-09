using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
namespace HoursTool;

public static class Loader
{
    static readonly Regex PeriodRx = new(@"^(\d{4})[/-](\d{2})$");

    static string N(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s) if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    public static List<Raw> Load(string path)
    {
        var list = new List<Raw>();
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var rd = ExcelReaderFactory.CreateReader(fs);
        Dictionary<string, int>? col = null;

        while (rd.Read())
        {
            if (col == null)
            {
                var tmp = new Dictionary<string, int>();
                for (int i = 0; i < rd.FieldCount; i++)
                {
                    var v = rd.GetValue(i);
                    if (v != null) tmp.TryAdd(N(Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""), i);
                }
                if (tmp.ContainsKey("yearmonth") && tmp.ContainsKey("resourcename")) col = tmp;
                continue;
            }

            string S(string key) =>
                col.TryGetValue(key, out var i) && i < rd.FieldCount
                    ? (Convert.ToString(rd.GetValue(i), CultureInfo.InvariantCulture) ?? "").Trim() : "";
            double D(string key)
            {
                if (!col.TryGetValue(key, out var i) || i >= rd.FieldCount) return 0;
                var v = rd.GetValue(i);
                if (v is double d) return d;
                var s = (Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").Replace(',', '.');
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0;
            }

            var m = PeriodRx.Match(S("yearmonth"));
            if (!m.Success) continue;               // Total, texty filtru, prazdne radky
            var person = S("resourcename");
            if (person == "") continue;

            list.Add(new Raw(
                $"{m.Groups[1].Value}/{m.Groups[2].Value}",
                int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                person, Config.Norm(S("resourcecostcenter")),
                S("sapprojectid"), S("projectdescription"), S("abacus"),
                D("postedtimeh"), D("postedtimeczksalesrate")));
        }

        if (col == null) throw new Exception("Nenalezena hlavicka (Year/Month, Resource Name ...).");
        foreach (var req in new[] { "resourcecostcenter", "sapprojectid", "postedtimeh" })
            if (!col.ContainsKey(req)) throw new Exception($"Chybi sloupec: {req}");
        return list;
    }
}
