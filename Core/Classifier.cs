using System.Text.RegularExpressions;
namespace HoursTool;

/// <summary>
/// A named measure that can be shown as a table column, a chart series or a KPI.
/// </summary>
/// <param name="Id">Stable identifier (used in settings and for lookups), e.g. <c>inv</c>, <c>cust:FI</c>, <c>split:nb</c>.</param>
/// <param name="Name">Localised display name.</param>
/// <param name="Short">Short localised name for column headers.</param>
/// <param name="Pred">Row predicate; the measure is the sum of hours/value of matching rows.</param>
/// <param name="Group"><c>core</c>, <c>cust</c> (customers) or <c>split</c> (split-customer breakdown).</param>
public record Metric(string Id, string Name, string Short, Func<Row, bool> Pred, string Group);

/// <summary>
/// Applies the business rules of a <see cref="RuleProfile"/> to raw rows. One compiled rule set is kept
/// per profile; every raw row is classified by the profile returned by <see cref="Config.ProfileFor"/>.
/// </summary>
public class Classifier
{
    /// <summary>Compiled form of one <see cref="RuleProfile"/>.</summary>
    sealed class Compiled
    {
        public RuleProfile P = null!;
        public List<Regex> OhId = new(), OhDesc = new();
        public Regex Nb = null!, Pr = null!, IdCust = null!;
        public HashSet<string> NbEx = null!, Cc = null!, Restricted = null!, People = null!, DescCodes = null!;
        public Regex DescCust = null!;
        public List<(Regex rx, string code)> Overrides = new();
        /// <summary>Compiled rule script when the profile is in advanced mode, otherwise null.</summary>
        public RuleScript? Script;
    }

    static readonly Regex Never = new("(?!)");
    readonly Config _cfg;
    readonly Dictionary<RuleProfile, Compiled> _compiled = new();
    readonly Dictionary<string, Compiled> _byPeriod = new();

    /// <summary>Creates a classifier for the current configuration. Invalid regexes never match (validate before).</summary>
    public Classifier(Config cfg)
    {
        _cfg = cfg;
        foreach (var p in cfg.Profiles) _compiled[p] = Compile(p);
    }

    static Regex Rx(string? p, RegexOptions extra = RegexOptions.None)
    {
        if (string.IsNullOrWhiteSpace(p)) return Never;
        try { return new Regex(p.Trim(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | extra); }
        catch (ArgumentException) { return Never; }
    }

    static Compiled Compile(RuleProfile p)
    {
        var c = new Compiled
        {
            P = p,
            OhId = p.OverheadIdPatterns.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Rx(x)).ToList(),
            OhDesc = p.OverheadDescPatterns.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Rx(x)).ToList(),
            Nb = Rx(p.NewbuildIdRegex),
            Pr = Rx(p.PerRequestIdRegex),
            IdCust = Rx(p.IdCustomerRegex),
            NbEx = new HashSet<string>(p.NewbuildExcludeDescriptions.Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase),
            Cc = p.CostCenters.Select(Config.NormCc).ToHashSet(),
            Restricted = p.RestrictedCostCenters.Select(Config.NormCc).ToHashSet(),
            People = new HashSet<string>(p.RelevantPeople.Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase),
            DescCodes = p.DescCustomerCodes.Select(x => x.Trim().ToUpperInvariant()).Where(x => x != "").ToHashSet(),
        };
        c.DescCust = c.DescCodes.Count == 0 ? Never
            : new Regex(@"(?:^|[_\s\-])(" + string.Join("|", c.DescCodes.Select(Regex.Escape)) + @")(?:[_\s\-]|$)",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var o in p.CustomerOverrides)
        {
            var parts = o.Split("=>", 2);
            if (parts.Length == 2 && parts[1].Trim() != "") c.Overrides.Add((Rx(parts[0]), parts[1].Trim().ToUpperInvariant()));
        }
        if (p.Advanced) c.Script = RuleScript.Compile(p.Script);
        return c;
    }

    Compiled For(string period)
    {
        if (!_byPeriod.TryGetValue(period, out var c))
            _byPeriod[period] = c = _compiled[_cfg.ProfileFor(period)];
        return c;
    }

    /// <summary>Profile used for the given period.</summary>
    public RuleProfile ProfileOf(string period) => For(period).P;

    /// <summary>
    /// Why a row is not counted: <c>null</c> = included, <c>"cc"</c> = cost centre not configured,
    /// <c>"people"</c> = not a relevant person in a manual-selection cost centre, <c>"rule:NAME"</c> = excluded
    /// by an advanced rule. An empty cost-centre list in a simple profile means "all cost centres".
    /// </summary>
    public string? ExcludeReason(Raw r)
    {
        var c = For(r.Period);
        if (c.Script != null)
        {
            var res = c.Script.Run(r);
            return res.Include ? null : "rule:" + (res.ExcludedBy ?? "?");
        }
        if (c.Cc.Count > 0 && !c.Cc.Contains(r.Cc)) return "cc";
        if (c.Restricted.Contains(r.Cc) && !c.People.Contains(r.Person.Trim())) return "people";
        return null;
    }

    /// <summary>True when the row is counted (see <see cref="ExcludeReason"/>).</summary>
    public bool IsIncluded(Raw r) => ExcludeReason(r) == null;

    /// <summary>
    /// Classifies a row and applies the inclusion rules in one pass (the script runs only once).
    /// Returns the classified row, or <c>null</c> plus the exclusion reason.
    /// </summary>
    public (Row? row, string? reason) Process(Raw r)
    {
        var c = For(r.Period);
        if (c.Script != null)
        {
            var res = c.Script.Run(r);
            if (!res.Include) return (null, "rule:" + (res.ExcludedBy ?? "?"));
            return (FromScript(r, res, c.P.Name), null);
        }
        var reason = ExcludeReason(r);
        return reason == null ? (Classify(r), null) : (null, reason);
    }

    static Row FromScript(Raw r, ScriptResult res, string profile)
    {
        var cat = res.Overhead ? "" : res.Category;
        return new Row(r.Period, r.Year, r.Month, r.Person, r.Cc, r.SapId, r.Desc, res.Customer, res.Overhead,
                       cat.Equals("NB", StringComparison.OrdinalIgnoreCase), cat.Equals("P", StringComparison.OrdinalIgnoreCase),
                       r.Hours, r.Value, profile, r.Abacus, cat);
    }

    /// <summary>All compiled rule scripts (advanced profiles), for rule statistics on the Data tab.</summary>
    public IEnumerable<(RuleProfile profile, RuleScript script)> Scripts =>
        _compiled.Values.Where(c => c.Script != null).Select(c => (c.P, c.Script!));

    /// <summary>True when the project is overhead according to the simple rules of <paramref name="period"/>.</summary>
    public bool IsOverhead(string period, string sapId, string desc)
    {
        var c = For(period);
        return c.OhId.Any(x => x.IsMatch(sapId)) || c.OhDesc.Any(x => x.IsMatch(desc));
    }

    /// <summary>
    /// Detects the customer (simple mode). Order: manual override → ABACUS prefix → <c>_XX_</c> token in the description →
    /// capture group of the ID regex → <c>??</c>.
    /// </summary>
    /// <returns>Customer code and a short description of the rule that decided it.</returns>
    public (string code, string source) CustomerOf(Raw r)
    {
        var c = For(r.Period);
        var hay = r.SapId + " " + r.Desc;
        foreach (var (rx, code) in c.Overrides)
            if (rx.IsMatch(hay)) return (code, "override");
        var ab = r.Abacus.Trim();
        if (c.P.AbacusPrefixLength > 0 && ab.Length >= c.P.AbacusPrefixLength)
            return (ab.Substring(0, c.P.AbacusPrefixLength).ToUpperInvariant(), "abacus");
        var m = c.DescCust.Match(r.Desc);
        if (m.Success) return (m.Groups[1].Value.ToUpperInvariant(), "desc");
        m = c.IdCust.Match(r.SapId);
        if (m.Success && m.Groups.Count > 1 && m.Groups[1].Value != "") return (m.Groups[1].Value.ToUpperInvariant(), "id");
        return ("??", "none");
    }

    /// <summary>
    /// Classifies a raw row (ignores inclusion). Advanced profiles run their script; simple profiles apply:
    /// overhead → per-request (P) → newbuild (unless the description is excluded). Overhead rows get customer <c>"-"</c>.
    /// </summary>
    public Row Classify(Raw r)
    {
        var c = For(r.Period);
        if (c.Script != null) return FromScript(r, c.Script.Run(r), c.P.Name);
        bool oh = IsOverhead(r.Period, r.SapId, r.Desc);
        string cust = oh ? "-" : CustomerOf(r).code;
        bool pr = !oh && c.Pr.IsMatch(r.SapId);
        bool nb = !oh && !pr && c.Nb.IsMatch(r.SapId) && !c.NbEx.Contains(r.Desc.Trim());
        return new Row(r.Period, r.Year, r.Month, r.Person, r.Cc, r.SapId, r.Desc,
                       cust, oh, nb, pr, r.Hours, r.Value, c.P.Name, r.Abacus, pr ? "P" : nb ? "NB" : "");
    }

    /// <summary>
    /// Explains in words how a hypothetical row would be classified (used by the rule tester in Settings).
    /// </summary>
    public string Explain(Raw r)
    {
        var c = For(r.Period);
        var sb = new List<string> { L.T("test.profile", c.P.Name, c.P.RangeText) };
        if (!_cfg.HasProfileFor(r.Period)) sb.Add(L.T("test.fallback"));
        if (c.Script != null)
        {
            var res = c.Script.Run(r);
            sb.Add(L.T("test.fired", res.Fired.Count == 0 ? "–" : string.Join(" → ", res.Fired)));
            if (!res.Include) sb.Add(L.T("test.excludedBy", res.ExcludedBy));
            else if (res.Overhead) sb.Add(L.T("test.overhead"));
            else
            {
                sb.Add(L.T("test.customerAdv", res.Customer));
                sb.Add(res.Category == "" ? L.T("test.noCategory") : L.T("test.category", res.Category));
            }
            return string.Join("\n", sb);
        }
        if (c.Cc.Count > 0 && !c.Cc.Contains(r.Cc)) sb.Add(L.T("test.ccOut", r.Cc));
        else if (c.Restricted.Contains(r.Cc) && !c.People.Contains(r.Person.Trim())) sb.Add(L.T("test.personOut", r.Person, r.Cc));
        var row = Classify(r);
        if (row.Overhead) sb.Add(L.T("test.overhead"));
        else
        {
            var (code, src) = CustomerOf(r);
            sb.Add(L.T("test.customer", code, L.T("src." + src)));
            if (row.PerRequest) sb.Add(L.T("test.p"));
            else if (row.Newbuild) sb.Add(code == _cfg.SplitCustomer.ToUpperInvariant() ? L.T("test.nb") : L.T("test.nbOther", _cfg.SplitCustomer));
            else if (c.Nb.IsMatch(r.SapId) && c.NbEx.Contains(r.Desc.Trim())) sb.Add(L.T("test.nbExcluded"));
            else sb.Add(L.T("test.regular"));
        }
        return string.Join("\n", sb);
    }

    /// <summary>Builds the list of measures for the current configuration (localised names).</summary>
    public List<Metric> Metrics()
    {
        var split = _cfg.SplitCustomer.Trim().ToUpperInvariant();
        var l = new List<Metric>
        {
            new("inv", L.T("m.inv"), L.T("m.inv.s"), r => !r.Overhead, "core"),
            new("oh", L.T("m.oh"), L.T("m.oh.s"), r => r.Overhead, "core"),
        };
        var known = _cfg.Customers.Select(c => c.Trim().ToUpperInvariant()).Where(c => c != "").Distinct().ToList();
        foreach (var cc in known)
            l.Add(new Metric("cust:" + cc, cc, cc, r => !r.Overhead && r.Customer == cc, "cust"));
        var knownSet = known.ToHashSet();
        l.Add(new("cust:other", L.T("m.other"), L.T("m.other.s"), r => !r.Overhead && !knownSet.Contains(r.Customer), "cust"));
        if (split != "")
        {
            // the three split measures add up to the invoiced total of the split customer
            l.Add(new("split:nb", L.T("m.nb", split), L.T("m.nb.s", split), r => !r.Overhead && r.Customer == split && r.Newbuild, "split"));
            l.Add(new("split:p", L.T("m.p", split), L.T("m.p.s", split), r => !r.Overhead && r.Customer == split && r.PerRequest, "split"));
            l.Add(new("split:rest", L.T("m.rest", split), L.T("m.rest.s", split),
                      r => !r.Overhead && r.Customer == split && !r.Newbuild && !r.PerRequest, "split"));
        }
        return l;
    }
}
