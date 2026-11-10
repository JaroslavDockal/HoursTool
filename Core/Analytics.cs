using System.Globalization;
namespace HoursTool;

/// <summary>
/// Evaluates measures over sets of classified rows. Holds the current unit mode (hours vs. money)
/// and the measure list, so that the UI and the export compute numbers the same way.
/// </summary>
public class Analytics
{
    /// <summary>Special measure: all posted hours (always hours, also in money mode).</summary>
    public const string TOTAL = "total";
    /// <summary>Special measure: invoiced share of hours in percent.</summary>
    public const string PCT = "pct";
    /// <summary>Special measure: invoiced hours/value per person-month.</summary>
    public const string PERP = "perp";
    /// <summary>Special measure: average number of active people per month.</summary>
    public const string HEADS = "heads";

    /// <summary>Measures from <see cref="Classifier.Metrics"/>.</summary>
    public List<Metric> Metrics { get; }
    /// <summary>True = values are money (invoiced only), false = hours.</summary>
    public bool Money { get; }
    /// <summary>Money unit label, e.g. CZK.</summary>
    public string MoneyUnit { get; }

    /// <summary>Creates an evaluator.</summary>
    public Analytics(List<Metric> metrics, bool money, string moneyUnit)
    {
        Metrics = metrics; Money = money; MoneyUnit = moneyUnit;
    }

    /// <summary>Value of a row in the current unit (money is counted for invoiced rows only).</summary>
    public double V(Row r) => Money ? (r.Overhead ? 0 : r.Value) : r.Hours;

    /// <summary>Unit suffix for values, e.g. " h" or " CZK".</summary>
    public string U => Money ? " " + MoneyUnit : " h";

    /// <summary>Unit label for axes.</summary>
    public string UnitLabel => Money ? MoneyUnit : L.T("unit.hours");

    /// <summary>All selectable series: measures followed by the derived indicators.</summary>
    public List<(string id, string name)> Series() =>
        Metrics.Select(m => (m.Id, m.Name)).Concat(new[] { (PCT, NameOf(PCT)), (PERP, NameOf(PERP)), (HEADS, NameOf(HEADS)) }).ToList();

    /// <summary>Localised display name of a measure id.</summary>
    public string NameOf(string id) => id switch
    {
        TOTAL => L.T("m.total"),
        PCT => L.T("m.pct"),
        PERP => L.T("m.perp"),
        HEADS => L.T("m.heads"),
        _ => Metrics.FirstOrDefault(m => m.Id == id)?.Name ?? id
    };

    /// <summary>Axis unit of a measure.</summary>
    public string UnitOf(string id) => id switch
    {
        PCT => "%",
        HEADS => L.T("unit.people"),
        TOTAL => L.T("unit.hours"),
        PERP => Money ? L.T("unit.perPersonMoney", MoneyUnit) : L.T("unit.perPersonHours"),
        _ => UnitLabel
    };

    /// <summary>True for measures that can be summed over time (cumulative charts make sense).</summary>
    public static bool Additive(string id) => id != PCT && id != PERP && id != HEADS;

    /// <summary>Evaluates a measure over a set of rows.</summary>
    public double Eval(string id, IReadOnlyCollection<Row> l)
    {
        if (l.Count == 0) return 0;
        switch (id)
        {
            case TOTAL: return l.Sum(r => r.Hours);
            case PCT:
                {
                    double all = l.Sum(r => r.Hours);
                    return all > 0 ? l.Where(r => !r.Overhead).Sum(r => r.Hours) / all * 100 : 0;
                }
            case PERP:
                {
                    int pm = l.Select(r => (r.Person, r.Period)).Distinct().Count();
                    return pm > 0 ? l.Where(r => !r.Overhead).Sum(V) / pm : 0;
                }
            case HEADS:
                {
                    int pm = l.Select(r => (r.Person, r.Period)).Distinct().Count();
                    int per = l.Select(r => r.Period).Distinct().Count();
                    return per > 0 ? (double)pm / per : 0;
                }
            default:
                {
                    var m = Metrics.FirstOrDefault(x => x.Id == id);
                    return m == null ? 0 : l.Where(m.Pred).Sum(V);
                }
        }
    }

    /// <summary>Formats a value of a measure for display.</summary>
    public static string Fmt(string id, double v) =>
        id == PCT ? v.ToString("0.0", CultureInfo.CurrentCulture) + " %"
        : (id == HEADS || id == PERP) ? v.ToString("#,0.0", CultureInfo.CurrentCulture)
        : v.ToString("#,0", CultureInfo.CurrentCulture);

    /// <summary>Formats the change from <paramref name="a"/> to <paramref name="b"/> (percentage points for Inv %).</summary>
    public static string Delta(string id, double a, double b) =>
        id == PCT ? (b - a).ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture) + " " + L.T("unit.pp")
        : a > 0 ? ((b - a) / a * 100).ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture) + " %" : "n/a";

    /// <summary>Restricts two row sets to the months (1–12) present in both.</summary>
    public static (List<Row>, List<Row>) CommonMonths(List<Row> a, List<Row> b)
    {
        var common = a.Select(r => r.Month).Distinct().Intersect(b.Select(r => r.Month).Distinct()).ToHashSet();
        return (a.Where(r => common.Contains(r.Month)).ToList(), b.Where(r => common.Contains(r.Month)).ToList());
    }

    /// <summary>
    /// Year-over-year change of the last two years in <paramref name="rows"/>, computed on common months only
    /// (so an incomplete current year is compared fairly). Empty when there is only one year.
    /// </summary>
    public string Yoy(List<Row> rows, string id)
    {
        var years = rows.Select(r => r.Year).Distinct().OrderBy(x => x).ToList();
        if (years.Count < 2) return "";
        int a = years[^2], b = years[^1];
        var (ra, rb) = CommonMonths(rows.Where(r => r.Year == a).ToList(), rows.Where(r => r.Year == b).ToList());
        if (ra.Count == 0) return "";
        return L.T("kpi.yoy", Delta(id, Eval(id, ra), Eval(id, rb)), a);
    }

    /// <summary>Aggregates rows to project lines (Projects tab, person detail, export).</summary>
    public List<ProjRow> Projects(IEnumerable<Row> rows) =>
        rows.GroupBy(r => (r.SapId, r.Desc))
            .Select(g =>
            {
                var f = g.First();
                return new ProjRow(g.Key.SapId, g.Key.Desc, f.Customer,
                    f.Overhead ? L.T("type.overhead") : L.T("type.invoiced"),
                    f.Category,
                    Math.Round(g.Sum(V), 1), g.Select(r => r.Person).Distinct().Count(),
                    g.Min(r => r.Period)!, g.Max(r => r.Period)!,
                    string.Join(", ", g.Select(r => r.Profile).Distinct()));
            })
            .OrderByDescending(p => p.Value).ToList();
}
