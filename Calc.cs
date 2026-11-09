using System.Text.RegularExpressions;
namespace HoursTool;

public record Metric(string Name, string Short, Func<Row, bool> Pred, string Group);

public class Calc
{
    readonly Config _cfg;
    readonly List<Regex> _oh;
    readonly Regex _nb;
    readonly Regex _pr;
    readonly HashSet<string> _nbEx;
    static readonly Regex DescCust = new(@"(?:^|_)(FI|US|NO|IT|CH|CN|SG|NL|CZ|GB|HK|DE|SE|DK)(?:_|$)");
    static readonly Regex IdCust = new(@"^FKMMP([A-Z]{2})");

    public Calc(Config cfg)
    {
        _cfg = cfg;
        _oh = cfg.OverheadIdPatterns.Where(p => p.Trim() != "")
                 .Select(p => new Regex(p.Trim(), RegexOptions.IgnoreCase | RegexOptions.Compiled)).ToList();
        _nb = new Regex(cfg.NewbuildIdRegex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        _pr = new Regex(string.IsNullOrWhiteSpace(cfg.PerRequestIdRegex) ? "(?!)" : cfg.PerRequestIdRegex,
                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
        _nbEx = new HashSet<string>(cfg.NewbuildExcludeDescriptions.Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
    }

    public bool IsOverhead(string sapId) => _oh.Any(x => x.IsMatch(sapId));

    public static string CustomerOf(Raw r)
    {
        var ab = r.Abacus.Trim();
        if (ab.Length >= 2) return ab.Substring(0, 2).ToUpperInvariant();      // zakaznik dle ABACUS
        var m = DescCust.Match(r.Desc);
        if (m.Success) return m.Groups[1].Value;
        m = IdCust.Match(r.SapId);
        if (m.Success) return m.Groups[1].Value;
        return "??";
    }

    public Row Classify(Raw r)
    {
        bool oh = IsOverhead(r.SapId);
        bool pr = !oh && _pr.IsMatch(r.SapId);
        bool nb = !oh && !pr && _nb.IsMatch(r.SapId) && !_nbEx.Contains(r.Desc.Trim());
        return new Row(r.Period, r.Year, r.Month, r.Person, r.Cc, r.SapId, r.Desc,
                       oh ? "-" : CustomerOf(r), oh, nb, pr, r.Hours, r.Czk);
    }

    public List<Metric> Metrics()
    {
        var l = new List<Metric>
        {
            new("Invoiced", "Inv", r => !r.Overhead, "core"),
            new("Neinvoiced (overhead)", "Neinv", r => r.Overhead, "core"),
        };
        foreach (var c in _cfg.Customers)
        {
            var cc = c.Trim().ToUpperInvariant();
            l.Add(new Metric(cc, cc, r => !r.Overhead && r.Customer == cc, "cust"));
        }
        var known = _cfg.Customers.Select(c => c.Trim().ToUpperInvariant()).ToHashSet();
        l.Add(new("Ostatní zákazníci", "Ost", r => !r.Overhead && !known.Contains(r.Customer), "cust"));
        // rozklad FI (tri skupiny davaji dohromady FI invoiced)
        l.Add(new("FI newbuild", "FI NB", r => r.Newbuild, "nb"));
        l.Add(new("FI P projekty", "FI P", r => !r.Overhead && r.Customer == "FI" && r.PerRequest, "nb"));
        l.Add(new("FI ostatní (S, RCS...)", "FI jiné",
                  r => !r.Overhead && r.Customer == "FI" && !r.Newbuild && !r.PerRequest, "nb"));
        return l;
    }

    // ---------- statistika ----------
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

    public static double[] Moving(double[] y, int w)
    {
        var r = new double[y.Length];
        for (int i = 0; i < y.Length; i++)
        {
            int from = Math.Max(0, i - w + 1);
            r[i] = y.Skip(from).Take(i - from + 1).Average();
        }
        return r;
    }

    public static string NextPeriod(string p)
    {
        var s = p.Split('/');
        int y = int.Parse(s[0]), m = int.Parse(s[1]) + 1;
        if (m > 12) { m = 1; y++; }
        return $"{y}/{m:00}";
    }
}
