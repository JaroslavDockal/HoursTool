using Xunit;
namespace HoursTool.Tests;

public class RuleScriptTests
{
    static Raw R(string id, string desc, string abacus = "", string cc = "C23106", string person = "A", string period = "2024/05", double h = 1) =>
        new(period, int.Parse(period[..4]), int.Parse(period[5..]), person, cc, id, desc, abacus, h, 0);

    static RuleScript Ok(string script)
    {
        var s = RuleScript.Compile(script);
        Assert.True(s.Errors.Count == 0, string.Join("\n", s.Errors));
        return s;
    }

    [Fact]
    public void Basic_rules_stop_and_state()
    {
        var s = Ok("""
            # comment
            rule Overhead
              when Match(Id, "^INTR") or Match(Desc, "training")
              set Overhead = true
              stop
            rule Customer from ID
              when Customer = "??" and Group(Id, "^FKMMP([A-Z]{2})", 1) != ""
              set Customer = Group(Id, "^FKMMP([A-Z]{2})", 1)
            rule P
              when Match(Id, "^FKMMP[A-Z]{2}\d{2}P")
              set Category = "P"
            """);
        var oh = s.Run(R("INTR001", "x"));
        Assert.True(oh.Overhead); Assert.Equal("-", oh.Customer);
        Assert.Equal(new[] { "Overhead" }, oh.Fired);
        var p = s.Run(R("FKMMPUS24P001", "x"));
        Assert.False(p.Overhead); Assert.Equal("US", p.Customer); Assert.Equal("P", p.Category);
        Assert.Equal("??", s.Run(R("XYZ", "x")).Customer);
        Assert.True(s.Run(R("X", "Internal TRAINING")).Overhead);   // case-insensitive
    }

    [Fact]
    public void Lists_continuations_quotes_and_word_operators()
    {
        var s = Ok(""""
            list team =
                Novák Jan
                Svoboda Petr
            list cc = C23106; C23107
            rule Exclude others
              when not (In(Person, @team) and In(Cc, @cc))
              set Include = false
              stop
            rule Quote
              when Desc = "say ""hi"""
              set Category = "Quoted"
            rule Backslash
              when Match(Id,
                   "^\d{3}$")
              set Category = Category + "Num"
            """");
        Assert.True(s.Run(R("x", "x", person: "novák jan")).Include);
        var ex = s.Run(R("x", "x", person: "Someone"));
        Assert.False(ex.Include); Assert.Equal("Exclude others", ex.ExcludedBy);
        Assert.False(s.Run(R("x", "x", person: "Novák Jan", cc: "C99999")).Include);
        Assert.Equal("Quoted", s.Run(R("x", "say \"hi\"", person: "Novák Jan")).Category);
        Assert.Equal("Num", s.Run(R("123", "x", person: "Novák Jan")).Category);
    }

    [Fact]
    public void Compile_errors_have_line_numbers()
    {
        var s = RuleScript.Compile("""
            rule A
              when Id ==
              set Customer = "X"
            set Overhead = true
            rule B
              set Foo = 1
              when In(Id, @missing)
            garbage line
            """);
        var lines = s.Errors.Select(e => e.Line).ToList();
        Assert.Contains(2, lines);   // bad expression
        Assert.Contains(6, lines);   // unknown field
        Assert.Contains(7, lines);   // unknown list
        Assert.Contains(8, lines);   // unknown statement
        Assert.DoesNotContain(4, lines.Where(l => l == 0));
        Assert.True(s.Errors.Count >= 4);
    }

    [Fact]
    public void Runtime_errors_do_not_crash()
    {
        var s = Ok("""
            rule Risky
              when Id.Substring(5, 2) = "FI"
              set Customer = "FI"
            rule Fallback
              set Category = "ok"
            """);
        var r = s.Run(R("ABC", "x"));          // Substring out of range
        Assert.Equal("??", r.Customer);
        Assert.Equal("ok", r.Category);
        Assert.Equal(1, s.Rules[0].RuntimeErrors);
        Assert.NotNull(s.Rules[0].FirstRuntimeError);
    }

    [Fact]
    public void All_examples_compile()
    {
        foreach (var e in ScriptExamples.All)
        {
            var s = RuleScript.Compile(e.Code);
            Assert.True(s.Errors.Count == 0, e.Id + ": " + string.Join("; ", s.Errors));
            Assert.NotEmpty(s.Rules);
            s.Run(R("FKMMPFI23-I001", "ES_FI_NB_SHIP1", "FI0001"));   // must not throw
            Assert.True(L.Has($"ex.{e.Id}.t") && L.Has($"ex.{e.Id}.d"), e.Id);
            Assert.True(L.Has("ex.group." + e.Group), e.Group);
        }
    }

    [Fact]
    public void Examples_behave_as_described()
    {
        Assert.Equal("FI", RuleScript.Compile(ScriptExamples.All.First(e => e.Id == "custIdPos").Code).Run(R("FKMMPFI23-A1", "x")).Customer);
        Assert.Equal("Phase I", RuleScript.Compile(ScriptExamples.All.First(e => e.Id == "catPhase").Code).Run(R("FKMMPFI23-I001", "x")).Category);
        var t = RuleScript.Compile(ScriptExamples.All.First(e => e.Id == "catTernary").Code);
        Assert.Equal("Engineering", t.Run(R("X-A001", "x")).Category);
        Assert.Equal("Service", t.Run(R("X-S001", "x")).Category);
        Assert.Equal("Other", t.Run(R("X", "x")).Category);
        var j = RuleScript.Compile(ScriptExamples.All.First(e => e.Id == "joinDate").Code);
        Assert.False(j.Run(R("X", "x", person: "Novák Jan", period: "2025/06")).Include);
        Assert.True(j.Run(R("X", "x", person: "Novák Jan", period: "2025/07")).Include);
    }

    /// <summary>The generated script must classify exactly like the simple settings.</summary>
    [Fact]
    public void Generated_script_is_equivalent_to_simple_settings()
    {
        var cfg = new Config(); cfg.Migrate();
        var p = cfg.Profiles[0];
        p.RelevantPeople = new() { "Svoboda Petr", "Dvořák Karel" };
        p.OverheadDescPatterns = new() { "^Training$" };
        p.CustomerOverrides = new() { "SPECIAL => ch", "^FKMMPXX12 => FI" };
        var adv = new Config(); adv.Migrate();
        adv.Profiles[0] = p.Clone();
        adv.Profiles[0].Advanced = true;
        adv.Profiles[0].Script = RuleScript.FromSimple(p);
        Assert.Empty(adv.Validate());

        var simple = new Classifier(cfg);
        var script = new Classifier(adv);
        string[] ids = { "FKMMPFI23-I001", "FKMMPFI23-I002", "FKMMPFI22P010", "FKMMPUS24P123", "FKMMPUS24-A001", "INTR0001", "FKMMPXX12-B1", "XYZ", "FKMMPIT22-X010", "" };
        string[] descs = { "ES_FI_NB_SHIP1", "ES_FI_RCS_ENG", "US_SERVICE", "Training", "SPECIAL job", "SOMETHING_IT_X", "", "no code" };
        string[] abacus = { "", "FI0001", "US2", "X" };
        string[] ccs = { "C23106", "C23107", "C23211", "C99999", "" };
        string[] people = { "Svoboda Petr", "svoboda petr", "Dvořák Karel", "Někdo Jiný" };
        int n = 0;
        foreach (var id in ids) foreach (var d in descs) foreach (var a in abacus) foreach (var cc in ccs) foreach (var pe in people)
        {
            var raw = R(id, d, a, cc, pe);
            var (r1, why1) = simple.Process(raw);
            var (r2, why2) = script.Process(raw);
            Assert.Equal(r1 == null, r2 == null);
            if (r1 == null) continue;
            Assert.Equal((r1.Customer, r1.Overhead, r1.Newbuild, r1.PerRequest, r1.Category),
                         (r2!.Customer, r2.Overhead, r2.Newbuild, r2.PerRequest, r2.Category));
            n++;
        }
        Assert.True(n > 500);
    }

    [Fact]
    public void Advanced_profile_custom_categories_flow_through_classifier()
    {
        var cfg = new Config(); cfg.Migrate();
        cfg.Profiles[0].Advanced = true;
        cfg.Profiles[0].Script = """
            rule Service
              when Contains(Desc, "_SERVICE")
              set Category = "Service"
              set Customer = Left(Desc, 2)
            rule Drop
              when Hours < 0
              set Include = false
            """;
        var c = new Classifier(cfg);
        var (row, _) = c.Process(R("X1", "US_SERVICE"));
        Assert.Equal("Service", row!.Category);
        Assert.Equal("US", row.Customer);
        Assert.False(row.Newbuild || row.PerRequest);
        var (none, why) = c.Process(R("X1", "x", h: -5));
        Assert.Null(none);
        Assert.Equal("rule:Drop", why);
        Assert.Contains("Service", c.Explain(R("X1", "US_SERVICE")));
    }

    [Fact]
    public void Invalid_script_is_reported_by_validation()
    {
        var cfg = new Config(); cfg.Migrate();
        cfg.Profiles[0].Advanced = true;
        cfg.Profiles[0].Script = "rule A\n  when Id ==\n";
        Assert.NotEmpty(cfg.Validate());
    }
}
