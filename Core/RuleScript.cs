using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DynamicExpresso;
namespace HoursTool;

/// <summary>Condition delegate of a rule script; parameter names are the variables usable in expressions.</summary>
public delegate bool RuleBool(string Id, string Desc, string Abacus, string Cc, string Person, string Period,
                              int Year, int Month, double Hours, double Value,
                              string Customer, string Category, bool Overhead, bool Include);

/// <summary>Text-valued expression delegate of a rule script (same variables as <see cref="RuleBool"/>).</summary>
public delegate string RuleText(string Id, string Desc, string Abacus, string Cc, string Person, string Period,
                                int Year, int Month, double Hours, double Value,
                                string Customer, string Category, bool Overhead, bool Include);

/// <summary>A compile or runtime problem of a rule script, with its 1-based line number.</summary>
public record ScriptError(int Line, string Message)
{
    /// <inheritdoc />
    public override string ToString() => Line > 0 ? L.T("adv.errLine", Line, Message) : Message;
}

/// <summary>Result of running a rule script on one row.</summary>
public record ScriptResult(bool Include, bool Overhead, string Customer, string Category, List<string> Fired, string? ExcludedBy);

/// <summary>
/// Advanced-mode classification: an ordered list of rules written as a small text "program".
/// <para>Syntax (keywords are case-insensitive, one statement per line, indented lines continue the previous one):</para>
/// <code>
/// # comment            (also // comment)
/// list relevant = Novák Jan; Svoboda Petr      (named list, items separated by ; | or new lines)
/// rule Overhead by ID                          (starts a rule; the rest of the line is its name)
///   when Match(Id, "^INTR|^OH-") or Match(Desc, "^Training$")
///   set Overhead = true                        (fields: Include, Overhead, Customer, Category)
///   stop                                       (do not evaluate further rules for this row)
/// </code>
/// <para>
/// Expressions use C# syntax (via DynamicExpresso) plus <c>and</c>/<c>or</c>/<c>not</c> and <c>=</c> as comparison in
/// <c>when</c>. String literals are raw: backslashes are kept (<c>"\d{2}"</c> is a regex), <c>""</c> is a quote.
/// <c>@name</c> inserts a named list as a string literal. Variables: Id, Desc, Abacus, Cc, Person, Period, Year, Month,
/// Hours, Value and the current state Customer, Category, Overhead, Include. Helper functions: see <see cref="Functions"/>.
/// </para>
/// <para>
/// Evaluation of a row starts with Include = true, Overhead = false, Customer = "??", Category = "".
/// Rules run top to bottom; when a condition holds, its assignments are applied in order (each sees the previous ones)
/// and <c>stop</c> ends the evaluation. Finally overhead rows get customer "-" and the customer is upper-cased.
/// </para>
/// </summary>
public sealed class RuleScript
{
    /// <summary>Assignable fields.</summary>
    public enum Field { Include, Overhead, Customer, Category }

    /// <summary>One compiled assignment.</summary>
    public sealed class Assignment
    {
        public Field Target;
        public int Line;
        public string Text = "";
        public RuleBool? Bool;
        public RuleText? Str;
    }

    /// <summary>One compiled rule.</summary>
    public sealed class Rule
    {
        public string Name = "";
        public int Line;
        public string WhenText = "";
        public RuleBool? When;
        public List<Assignment> Sets = new();
        public bool Stop;
        /// <summary>How many rows matched the condition since the script was compiled.</summary>
        public int Hits;
        /// <summary>How many runtime errors occurred (row treated as not matching).</summary>
        public int RuntimeErrors;
        /// <summary>First runtime error message.</summary>
        public string? FirstRuntimeError;
    }

    /// <summary>Compiled rules in evaluation order.</summary>
    public List<Rule> Rules { get; } = new();
    /// <summary>Compile errors (empty = the script is valid).</summary>
    public List<ScriptError> Errors { get; } = new();
    /// <summary>Named lists defined in the script.</summary>
    public Dictionary<string, List<string>> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Helper functions available in expressions (name, signature, description key).</summary>
    public static readonly (string name, string signature, string helpKey)[] Functions =
    {
        ("Match", "Match(text, regex) → bool", "adv.fn.match"),
        ("Group", "Group(text, regex, n) → text", "adv.fn.group"),
        ("In", "In(value, list) → bool", "adv.fn.in"),
        ("Token", "Token(text, codes) → text", "adv.fn.token"),
        ("Contains", "Contains(text, part) → bool", "adv.fn.contains"),
        ("StartsWith", "StartsWith(text, part) → bool", "adv.fn.startsWith"),
        ("EndsWith", "EndsWith(text, part) → bool", "adv.fn.endsWith"),
        ("Left", "Left(text, n) → text", "adv.fn.left"),
        ("Right", "Right(text, n) → text", "adv.fn.right"),
        ("Upper", "Upper(text) → text", "adv.fn.upper"),
        ("IsEmpty", "IsEmpty(text) → bool", "adv.fn.isEmpty"),
    };

    /// <summary>Variables available in expressions (name, description key).</summary>
    public static readonly (string name, string helpKey)[] Variables =
    {
        ("Id", "adv.var.id"), ("Desc", "adv.var.desc"), ("Abacus", "adv.var.abacus"), ("Cc", "adv.var.cc"),
        ("Person", "adv.var.person"), ("Period", "adv.var.period"), ("Year", "adv.var.year"), ("Month", "adv.var.month"),
        ("Hours", "adv.var.hours"), ("Value", "adv.var.value"), ("Customer", "adv.var.customer"),
        ("Category", "adv.var.category"), ("Overhead", "adv.var.overhead"), ("Include", "adv.var.include"),
    };

    // ------------------------------------------------------------------ helpers

    static readonly ConcurrentDictionary<string, Regex> RxCache = new();
    static Regex Rx(string p) => RxCache.GetOrAdd(p, x => new Regex(x, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    /// <summary>Splits a list value: items separated by ';', '|' or new lines, trimmed, empty items dropped.</summary>
    public static IEnumerable<string> Items(string? list) =>
        (list ?? "").Split(new[] { ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static bool FnMatch(string t, string p) => Rx(p).IsMatch(t ?? "");
    static string FnGroup(string t, string p, int n)
    {
        var m = Rx(p).Match(t ?? "");
        return m.Success && n < m.Groups.Count ? m.Groups[n].Value : "";
    }
    static bool FnIn(string v, string list) => Items(list).Any(i => string.Equals(i, (v ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
    static string FnToken(string t, string codes)
    {
        var c = Items(codes).ToList();
        if (c.Count == 0) return "";
        var m = Rx(@"(?:^|[_\s\-])(" + string.Join("|", c.Select(Regex.Escape)) + @")(?:[_\s\-]|$)").Match(t ?? "");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
    }

    static Interpreter NewInterpreter()
    {
        var it = new Interpreter();
        it.SetFunction("Match", (Func<string, string, bool>)FnMatch);
        it.SetFunction("Group", (Func<string, string, int, string>)FnGroup);
        it.SetFunction("In", (Func<string, string, bool>)FnIn);
        it.SetFunction("Token", (Func<string, string, string>)FnToken);
        it.SetFunction("Contains", (Func<string, string, bool>)((t, p) => (t ?? "").Contains(p ?? "", StringComparison.OrdinalIgnoreCase)));
        it.SetFunction("StartsWith", (Func<string, string, bool>)((t, p) => (t ?? "").StartsWith(p ?? "", StringComparison.OrdinalIgnoreCase)));
        it.SetFunction("EndsWith", (Func<string, string, bool>)((t, p) => (t ?? "").EndsWith(p ?? "", StringComparison.OrdinalIgnoreCase)));
        it.SetFunction("Left", (Func<string, int, string>)((t, n) => (t ?? "").Length <= n ? t ?? "" : t!.Substring(0, Math.Max(0, n))));
        it.SetFunction("Right", (Func<string, int, string>)((t, n) => (t ?? "").Length <= n ? t ?? "" : t!.Substring(t.Length - Math.Max(0, n))));
        it.SetFunction("Upper", (Func<string, string>)(t => (t ?? "").ToUpperInvariant()));
        it.SetFunction("IsEmpty", (Func<string, bool>)(t => string.IsNullOrWhiteSpace(t)));
        return it;
    }

    // ------------------------------------------------------------------ parsing

    static readonly Regex Keyword = new(@"^(rule|when|set|stop|list)\b\s*(.*)$", RegexOptions.IgnoreCase);
    static readonly Regex SetRx = new(@"^([A-Za-z]+)\s*=(?!=)\s*(.+)$", RegexOptions.Singleline);
    static readonly Regex ListRx = new(@"^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$", RegexOptions.Singleline);

    /// <summary>
    /// Rewrites an expression of the rule language to a DynamicExpresso (C#) expression:
    /// raw string literals, <c>@list</c> references, word operators and (in conditions) <c>=</c> as comparison.
    /// </summary>
    public string Translate(string expr, bool condition, int line)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < expr.Length)
        {
            char ch = expr[i];
            if (ch == '"')
            {
                // raw literal: "" is an escaped quote, backslashes are literal
                var lit = new StringBuilder();
                i++;
                bool closed = false;
                while (i < expr.Length)
                {
                    if (expr[i] == '"')
                    {
                        if (i + 1 < expr.Length && expr[i + 1] == '"') { lit.Append('"'); i += 2; continue; }
                        closed = true; i++; break;
                    }
                    lit.Append(expr[i++]);
                }
                if (!closed) Errors.Add(new ScriptError(line, L.T("adv.err.quote")));
                sb.Append(Literal(lit.ToString()));
                continue;
            }
            if (ch == '@')
            {
                int j = i + 1;
                while (j < expr.Length && (char.IsLetterOrDigit(expr[j]) || expr[j] == '_')) j++;
                var name = expr[(i + 1)..j];
                if (Lists.TryGetValue(name, out var items)) sb.Append(Literal(string.Join(";", items)));
                else { Errors.Add(new ScriptError(line, L.T("adv.err.list", name))); sb.Append("\"\""); }
                i = j;
                continue;
            }
            if (char.IsLetter(ch) && (i == 0 || !char.IsLetterOrDigit(expr[i - 1]) && expr[i - 1] != '_' && expr[i - 1] != '.'))
            {
                int j = i;
                while (j < expr.Length && (char.IsLetterOrDigit(expr[j]) || expr[j] == '_')) j++;
                var word = expr[i..j];
                sb.Append(word.ToLowerInvariant() switch { "and" => "&&", "or" => "||", "not" => "!", _ => word });
                i = j;
                continue;
            }
            if (condition && ch == '=')
            {
                char prev = i > 0 ? expr[i - 1] : ' ';
                char next = i + 1 < expr.Length ? expr[i + 1] : ' ';
                if (next == '=') { sb.Append("=="); i += 2; continue; }
                if (prev is '!' or '<' or '>' or '=') { sb.Append('='); i++; continue; }
                sb.Append("=="); i++;
                continue;
            }
            sb.Append(ch);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>C# string literal for a raw value.</summary>
    static string Literal(string raw) => "\"" + raw.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>Compiles a script. Errors are collected in <see cref="Errors"/>; valid rules are still usable.</summary>
    public static RuleScript Compile(string? text)
    {
        var s = new RuleScript();
        var it = NewInterpreter();
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');

        // 1) group physical lines into statements (indented lines without a keyword continue the previous one)
        var stmts = new List<(int line, string kw, string body)>();
        for (int n = 0; n < lines.Length; n++)
        {
            var raw = lines[n];
            var t = raw.Trim();
            if (t == "" || t.StartsWith("#") || t.StartsWith("//")) continue;
            var m = Keyword.Match(t);
            if (m.Success) stmts.Add((n + 1, m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.Trim()));
            else if (stmts.Count > 0 && char.IsWhiteSpace(raw[0]) && stmts[^1].kw is "when" or "set" or "list")
                stmts[^1] = (stmts[^1].line, stmts[^1].kw, stmts[^1].body + (stmts[^1].kw == "list" ? "\n" : " ") + t);
            else s.Errors.Add(new ScriptError(n + 1, L.T("adv.err.statement", t.Length > 40 ? t[..40] + "…" : t)));
        }

        // 2) lists first, so that rules may reference lists defined anywhere
        foreach (var (line, kw, body) in stmts.Where(x => x.kw == "list"))
        {
            var m = ListRx.Match(body);
            if (!m.Success) { s.Errors.Add(new ScriptError(line, L.T("adv.err.listSyntax"))); continue; }
            s.Lists[m.Groups[1].Value] = Items(m.Groups[2].Value).ToList();
        }

        // 3) rules
        Rule? cur = null;
        foreach (var (line, kw, body) in stmts)
        {
            switch (kw)
            {
                case "list": break;
                case "rule":
                    cur = new Rule { Name = body == "" ? L.T("adv.ruleN", s.Rules.Count + 1) : body.Trim('"'), Line = line };
                    s.Rules.Add(cur);
                    break;
                case "when":
                    if (cur == null) { s.Errors.Add(new ScriptError(line, L.T("adv.err.noRule"))); break; }
                    if (cur.When != null || cur.WhenText != "") { s.Errors.Add(new ScriptError(line, L.T("adv.err.twoWhen"))); break; }
                    cur.WhenText = body;
                    try { cur.When = it.ParseAsDelegate<RuleBool>(s.Translate(body, true, line)); }
                    catch (Exception ex) { s.Errors.Add(new ScriptError(line, Clean(ex.Message))); }
                    break;
                case "set":
                    {
                        if (cur == null) { s.Errors.Add(new ScriptError(line, L.T("adv.err.noRule"))); break; }
                        var m = SetRx.Match(body);
                        if (!m.Success || !Enum.TryParse<Field>(m.Groups[1].Value, true, out var f))
                        { s.Errors.Add(new ScriptError(line, L.T("adv.err.set"))); break; }
                        var a = new Assignment { Target = f, Line = line, Text = m.Groups[2].Value.Trim() };
                        try
                        {
                            var expr = s.Translate(a.Text, false, line);
                            if (f is Field.Include or Field.Overhead) a.Bool = it.ParseAsDelegate<RuleBool>(expr);
                            else a.Str = it.ParseAsDelegate<RuleText>(expr);
                            cur.Sets.Add(a);
                        }
                        catch (Exception ex) { s.Errors.Add(new ScriptError(line, Clean(ex.Message))); }
                        break;
                    }
                case "stop":
                    if (cur == null) s.Errors.Add(new ScriptError(line, L.T("adv.err.noRule")));
                    else cur.Stop = true;
                    break;
            }
        }
        if (s.Rules.Count == 0 && s.Errors.Count == 0) s.Errors.Add(new ScriptError(0, L.T("adv.err.empty")));
        return s;
    }

    static string Clean(string msg) => msg.Replace("\r", " ").Replace("\n", " ");

    // ------------------------------------------------------------------ evaluation

    /// <summary>Runs the rules on one raw row.</summary>
    public ScriptResult Run(Raw r)
    {
        bool include = true, overhead = false;
        string customer = "??", category = "";
        string? excludedBy = null;
        var fired = new List<string>();
        foreach (var rule in Rules)
        {
            bool hit;
            try
            {
                hit = rule.When == null || rule.When(r.SapId, r.Desc, r.Abacus, r.Cc, r.Person, r.Period, r.Year, r.Month,
                                                     r.Hours, r.Value, customer, category, overhead, include);
            }
            catch (Exception ex) { Fail(rule, ex); continue; }
            if (!hit) continue;
            rule.Hits++;
            fired.Add(rule.Name);
            foreach (var a in rule.Sets)
            {
                try
                {
                    switch (a.Target)
                    {
                        case Field.Include:
                            include = a.Bool!(r.SapId, r.Desc, r.Abacus, r.Cc, r.Person, r.Period, r.Year, r.Month, r.Hours, r.Value, customer, category, overhead, include);
                            if (!include) excludedBy ??= rule.Name;
                            break;
                        case Field.Overhead:
                            overhead = a.Bool!(r.SapId, r.Desc, r.Abacus, r.Cc, r.Person, r.Period, r.Year, r.Month, r.Hours, r.Value, customer, category, overhead, include);
                            break;
                        case Field.Customer:
                            customer = a.Str!(r.SapId, r.Desc, r.Abacus, r.Cc, r.Person, r.Period, r.Year, r.Month, r.Hours, r.Value, customer, category, overhead, include) ?? "";
                            break;
                        case Field.Category:
                            category = a.Str!(r.SapId, r.Desc, r.Abacus, r.Cc, r.Person, r.Period, r.Year, r.Month, r.Hours, r.Value, customer, category, overhead, include) ?? "";
                            break;
                    }
                }
                catch (Exception ex) { Fail(rule, ex); }
            }
            if (rule.Stop) break;
        }
        customer = customer.Trim().ToUpperInvariant();
        if (customer == "") customer = "??";
        if (overhead) customer = "-";
        if (include) excludedBy = null;
        return new ScriptResult(include, overhead, customer, category.Trim(), fired, excludedBy);
    }

    static void Fail(Rule rule, Exception ex)
    {
        rule.RuntimeErrors++;
        rule.FirstRuntimeError ??= ex.Message;
    }

    // ------------------------------------------------------------------ generator

    static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// Generates a script that classifies exactly like the simple settings of <paramref name="p"/>
    /// (a starting point for advanced mode).
    /// </summary>
    public static string FromSimple(RuleProfile p)
    {
        var sb = new StringBuilder();
        void W(string s = "") => sb.AppendLine(s);
        W("# " + L.T("adv.gen.header", p.Name));
        W("# " + L.T("adv.gen.hint"));
        W();
        if (p.CostCenters.Count > 0) W("list costCentres = " + string.Join("; ", p.CostCenters));
        if (p.RestrictedCostCenters.Count > 0)
        {
            W("list manualCentres = " + string.Join("; ", p.RestrictedCostCenters));
            W("list relevantPeople =");
            foreach (var x in p.RelevantPeople) W("    " + x);
        }
        if (p.NewbuildExcludeDescriptions.Count > 0) W("list newbuildExcluded = " + string.Join("; ", p.NewbuildExcludeDescriptions));
        if (p.DescCustomerCodes.Count > 0) W("list customerCodes = " + string.Join("; ", p.DescCustomerCodes));
        W();

        if (p.CostCenters.Count > 0)
        {
            W("rule " + L.T("adv.gen.cc"));
            W("  when not In(Cc, @costCentres)");
            W("  set Include = false");
            W("  stop");
            W();
        }
        if (p.RestrictedCostCenters.Count > 0)
        {
            W("rule " + L.T("adv.gen.people"));
            W("  when In(Cc, @manualCentres) and not In(Person, @relevantPeople)");
            W("  set Include = false");
            W("  stop");
            W();
        }
        var oh = p.OverheadIdPatterns.Where(x => x.Trim() != "").Select(x => $"Match(Id, {Q(x.Trim())})")
            .Concat(p.OverheadDescPatterns.Where(x => x.Trim() != "").Select(x => $"Match(Desc, {Q(x.Trim())})")).ToList();
        if (oh.Count > 0)
        {
            W("rule " + L.T("adv.gen.overhead"));
            W("  when " + string.Join("\n    or ", oh));
            W("  set Overhead = true");
            W("  stop");
            W();
        }
        int k = 1;
        foreach (var o in p.CustomerOverrides)
        {
            var parts = o.Split("=>", 2);
            if (parts.Length != 2 || parts[1].Trim() == "") continue;
            W($"rule {L.T("adv.gen.override")} {k++}");
            W($"  when Customer = \"??\" and Match(Id + \" \" + Desc, {Q(parts[0].Trim())})");
            W($"  set Customer = {Q(parts[1].Trim().ToUpperInvariant())}");
            W();
        }
        if (p.AbacusPrefixLength > 0)
        {
            W("rule " + L.T("adv.gen.abacus"));
            W($"  when Customer = \"??\" and Abacus.Trim().Length >= {p.AbacusPrefixLength}");
            W($"  set Customer = Left(Abacus.Trim(), {p.AbacusPrefixLength})");
            W();
        }
        if (p.DescCustomerCodes.Count > 0)
        {
            W("rule " + L.T("adv.gen.desc"));
            W("  when Customer = \"??\" and Token(Desc, @customerCodes) != \"\"");
            W("  set Customer = Token(Desc, @customerCodes)");
            W();
        }
        if (!string.IsNullOrWhiteSpace(p.IdCustomerRegex))
        {
            W("rule " + L.T("adv.gen.id"));
            W($"  when Customer = \"??\" and Group(Id, {Q(p.IdCustomerRegex.Trim())}, 1) != \"\"");
            W($"  set Customer = Group(Id, {Q(p.IdCustomerRegex.Trim())}, 1)");
            W();
        }
        if (!string.IsNullOrWhiteSpace(p.PerRequestIdRegex))
        {
            W("rule " + L.T("adv.gen.p"));
            W($"  when Match(Id, {Q(p.PerRequestIdRegex.Trim())})");
            W("  set Category = \"P\"");
            W("  stop");
            W();
        }
        if (!string.IsNullOrWhiteSpace(p.NewbuildIdRegex))
        {
            W("rule " + L.T("adv.gen.nb"));
            W($"  when Match(Id, {Q(p.NewbuildIdRegex.Trim())})" + (p.NewbuildExcludeDescriptions.Count > 0 ? " and not In(Desc, @newbuildExcluded)" : ""));
            W("  set Category = \"NB\"");
            W();
        }
        W("# " + L.T("adv.gen.custom"));
        W("# rule Service projects");
        W("#   when Category = \"\" and Contains(Desc, \"_SERVICE\")");
        W("#   set Category = \"Service\"");
        return sb.ToString();
    }
}
