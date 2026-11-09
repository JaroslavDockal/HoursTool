using System.Globalization;
using System.Text.RegularExpressions;
namespace HoursTool;

/// <summary>
/// Helpers for period keys. A period key is always <c>YYYY/MM</c> (zero padded) so that plain
/// ordinal string comparison sorts periods chronologically.
/// </summary>
public static class Periods
{
    static readonly Regex YearMonth = new(@"^\s*(\d{4})\s*[/\-.\s_]\s*(\d{1,2})\s*$");
    static readonly Regex MonthYear = new(@"^\s*0*(\d{1,2})\s*[/\-.\s_]\s*(\d{4})\s*$");   // 03/2024, 3.2024, SAP 003.2024
    static readonly Regex Compact = new(@"^\s*(\d{4})(\d{2})\s*$");
    static readonly Regex YearOnly = new(@"^\s*(\d{4})\s*$");

    /// <summary>Builds a period key from year and month.</summary>
    public static string Key(int year, int month) => $"{year:0000}/{month:00}";

    /// <summary>
    /// Tries to interpret a cell value as a period. Supported:
    /// <c>2024/03</c>, <c>2024-3</c>, <c>2024.03</c>, <c>03/2024</c>, <c>3.2024</c>, <c>003.2024</c>, <c>202403</c>,
    /// date strings (<c>2024-03-01</c>, <c>1.3.2024</c>), <see cref="DateTime"/> values and Excel serial dates.
    /// </summary>
    public static bool TryParse(object? value, out int year, out int month)
    {
        year = 0; month = 0;
        switch (value)
        {
            case null: return false;
            case DateTime dt: year = dt.Year; month = dt.Month; return true;
            case double d when d > 20000 && d < 80000:          // Excel serial date (1954–2119)
                var dd = DateTime.FromOADate(d); year = dd.Year; month = dd.Month; return true;
            case double d2 when d2 >= 190001 && d2 <= 299912 && d2 == Math.Floor(d2):
                return TryParse(((int)d2).ToString(CultureInfo.InvariantCulture), out year, out month);
        }
        var s = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        if (s == "") return false;
        Match m;
        if ((m = YearMonth.Match(s)).Success) { year = int.Parse(m.Groups[1].Value); month = int.Parse(m.Groups[2].Value); }
        else if ((m = MonthYear.Match(s)).Success) { month = int.Parse(m.Groups[1].Value); year = int.Parse(m.Groups[2].Value); }
        else if ((m = Compact.Match(s)).Success) { year = int.Parse(m.Groups[1].Value); month = int.Parse(m.Groups[2].Value); }
        else if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1) ||
                 DateTime.TryParse(s, CultureInfo.GetCultureInfo("cs-CZ"), DateTimeStyles.None, out d1))
        { year = d1.Year; month = d1.Month; }
        else return false;
        return year >= 1990 && year <= 2200 && month >= 1 && month <= 12;
    }

    /// <summary>Parses a single year value (number or text).</summary>
    public static bool TryYear(object? v, out int year)
    {
        year = 0;
        if (v is DateTime dt) { year = dt.Year; return true; }
        var s = Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? "";
        if (s.EndsWith(".0")) s = s[..^2];
        return int.TryParse(s, out year) && year >= 1990 && year <= 2200;
    }

    static readonly string[][] MonthWords =
    {
        new[] { "jan", "led" }, new[] { "feb", "úno", "uno" }, new[] { "mar", "bře", "bre" }, new[] { "apr", "dub" },
        new[] { "may", "kvě", "kve", "máj" }, new[] { "jun", "čer", "cer", "čvn" }, new[] { "jul", "čvc", "cvc", "červenec" },
        new[] { "aug", "srp" }, new[] { "sep", "zář", "zar" }, new[] { "oct", "říj", "rij" }, new[] { "nov", "lis" }, new[] { "dec", "pro" }
    };

    /// <summary>Parses a month value: number 1–12, a date, or an English/Czech month name or abbreviation.</summary>
    public static bool TryMonth(object? v, out int month)
    {
        month = 0;
        if (v is DateTime dt) { month = dt.Month; return true; }
        var s = (Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? "").ToLowerInvariant();
        if (s.EndsWith(".0")) s = s[..^2];
        if (int.TryParse(s, out month)) return month >= 1 && month <= 12;
        // "červenec" must be checked before "červen" – handled by order of the abbreviations above
        if (s.StartsWith("červenec") || s.StartsWith("cervenec")) { month = 7; return true; }
        for (int i = 0; i < 12; i++)
            if (MonthWords[i].Any(w => s.StartsWith(w))) { month = i + 1; return true; }
        return false;
    }

    /// <summary>
    /// Parses a profile range bound. <c>YYYY</c> means January (lower bound) or December (upper bound);
    /// <c>YYYY/MM</c> is taken as is. Empty input returns <c>null</c> (open bound); invalid input returns <c>null</c> too.
    /// </summary>
    public static string? ParseBound(string? s, bool upper)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = YearOnly.Match(s);
        if (m.Success) return Key(int.Parse(m.Groups[1].Value), upper ? 12 : 1);
        return TryParse(s, out var y, out var mo) ? Key(y, mo) : null;
    }

    /// <summary>Returns the period following <paramref name="p"/> (YYYY/MM).</summary>
    public static string Next(string p)
    {
        var s = p.Split('/');
        int y = int.Parse(s[0]), m = int.Parse(s[1]) + 1;
        if (m > 12) { m = 1; y++; }
        return Key(y, m);
    }

    /// <summary>Returns the period preceding <paramref name="p"/> (YYYY/MM).</summary>
    public static string Prev(string p)
    {
        var s = p.Split('/');
        int y = int.Parse(s[0]), m = int.Parse(s[1]) - 1;
        if (m < 1) { m = 12; y--; }
        return Key(y, m);
    }

    /// <summary>All periods between the bounds, inclusive.</summary>
    public static IEnumerable<string> Range(string from, string to)
    {
        for (var p = from; string.CompareOrdinal(p, to) <= 0; p = Next(p)) yield return p;
    }
}

/// <summary>Small statistics helpers used by the trend chart.</summary>
public static class Stats
{
    /// <summary>
    /// Ordinary least squares fit of <paramref name="y"/> against x = 0..n-1.
    /// Returns slope, intercept and coefficient of determination R².
    /// </summary>
    public static (double slope, double icpt, double r2) LinReg(double[] y)
    {
        int n = y.Length;
        if (n < 2) return (0, n == 1 ? y[0] : 0, 0);
        double mx = (n - 1) / 2.0, my = y.Average(), sxx = 0, sxy = 0, syy = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = i - mx, dy = y[i] - my;
            sxx += dx * dx; sxy += dx * dy; syy += dy * dy;
        }
        double slope = sxy / sxx;
        double r2 = syy > 0 ? (sxy * sxy) / (sxx * syy) : 0;
        return (slope, my - slope * mx, r2);
    }

    /// <summary>Trailing moving average with window <paramref name="w"/> (shorter window at the start).</summary>
    public static double[] Moving(double[] y, int w)
    {
        var r = new double[y.Length];
        double sum = 0;
        for (int i = 0; i < y.Length; i++)
        {
            sum += y[i];
            if (i >= w) sum -= y[i - w];
            r[i] = sum / Math.Min(i + 1, w);
        }
        return r;
    }
}
