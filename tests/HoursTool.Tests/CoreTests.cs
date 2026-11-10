using Xunit;
namespace HoursTool.Tests;

public class PeriodsTests
{
    [Theory]
    [InlineData("2024/03", 2024, 3)]
    [InlineData("2024-3", 2024, 3)]
    [InlineData("2024.03", 2024, 3)]
    [InlineData("03/2024", 2024, 3)]
    [InlineData("3.2024", 2024, 3)]
    [InlineData("003.2024", 2024, 3)]
    [InlineData("202403", 2024, 3)]
    [InlineData("2024-03-15", 2024, 3)]
    public void Parses_period_formats(string s, int y, int m)
    {
        Assert.True(Periods.TryParse(s, out var yy, out var mm));
        Assert.Equal((y, m), (yy, mm));
    }

    [Theory]
    [InlineData("Total")]
    [InlineData("")]
    [InlineData("2024/13")]
    public void Rejects_non_periods(string s) => Assert.False(Periods.TryParse(s, out _, out _));

    [Fact]
    public void Parses_datetime_and_excel_serial()
    {
        Assert.True(Periods.TryParse(new DateTime(2025, 7, 1), out var y, out var m));
        Assert.Equal((2025, 7), (y, m));
        Assert.True(Periods.TryParse(new DateTime(2025, 7, 1).ToOADate(), out y, out m));
        Assert.Equal((2025, 7), (y, m));
    }

    [Fact]
    public void Month_names()
    {
        Assert.True(Periods.TryMonth("březen", out var m)); Assert.Equal(3, m);
        Assert.True(Periods.TryMonth("červenec", out m)); Assert.Equal(7, m);
        Assert.True(Periods.TryMonth("červen", out m)); Assert.Equal(6, m);
        Assert.True(Periods.TryMonth("Dec", out m)); Assert.Equal(12, m);
    }

    [Fact]
    public void Bounds_and_navigation()
    {
        Assert.Equal("2024/01", Periods.ParseBound("2024", false));
        Assert.Equal("2024/12", Periods.ParseBound("2024", true));
        Assert.Equal("2024/07", Periods.ParseBound("2024/7", true));
        Assert.Null(Periods.ParseBound("", false));
        Assert.Equal("2025/01", Periods.Next("2024/12"));
        Assert.Equal("2024/12", Periods.Prev("2025/01"));
        Assert.Equal(14, Periods.Range("2024/11", "2025/12").Count());
    }
}

public class LoaderTests
{
    [Theory]
    [InlineData("1 234,5", 1234.5)]
    [InlineData("1,234.5", 1234.5)]
    [InlineData("1.234,5", 1234.5)]
    [InlineData("12,5", 12.5)]
    [InlineData("-3", -3)]
    public void Parses_numbers(string s, double expected)
    {
        Assert.True(Loader.TryNumber(s, out var d));
        Assert.Equal(expected, d, 6);
    }

    [Fact]
    public void Header_mapping_ignores_case_diacritics_and_punctuation()
    {
        var m = Loader.MapHeader(new[] { "YEAR / MONTH", "resource_name", "Posted Time (h)", "Posted time (CZK sales rate)", "SAP project id" }, new ColumnMap());
        Assert.Equal(0, m["period"]);
        Assert.Equal(1, m["person"]);
        Assert.Equal(2, m["hours"]);
        Assert.Equal(3, m["value"]);
        Assert.Equal(4, m["projectId"]);
    }

    [Fact]
    public void Loads_classic_format()
    {
        var res = Loader.Load(Fixtures.Classic(Fixtures.TempDir()), new ColumnMap());
        Assert.Equal(6, res.Rows.Count);                  // Total row and filter texts skipped
        Assert.Equal("2024/01", res.Rows[0].Period);
        Assert.Equal("C23107", res.Rows[2].Cc);           // 23107 normalised
        Assert.Equal("C23107", res.Rows[3].Cc);           // CC23107 normalised
        Assert.Equal(120000, res.Rows[0].Value);
        Assert.Single(res.Report.Sheets);
    }

    [Fact]
    public void Loads_variant_format_with_year_and_month_columns()
    {
        var res = Loader.Load(Fixtures.Variant(Fixtures.TempDir()), new ColumnMap());
        Assert.Equal(2, res.Rows.Count);
        Assert.All(res.Rows, r => Assert.Equal("2025/03", r.Period));
        Assert.Equal(1234.5, res.Rows[0].Hours, 6);
        Assert.Equal(1000000.5, res.Rows[0].Value, 6);
        Assert.Equal("FI0001", res.Rows[0].Abacus);
        Assert.Equal(0, res.Report.BadNumbers);
    }

    [Fact]
    public void Loads_csv()
    {
        var res = Loader.Load(Fixtures.Csv(Fixtures.TempDir()), new ColumnMap());
        Assert.Equal(2, res.Rows.Count);
        Assert.All(res.Rows, r => Assert.Equal("2026/03", r.Period));
        Assert.Equal(7.5, res.Rows[0].Hours, 6);
        Assert.Equal("Novák Jan", res.Rows[0].Person);
    }

    [Fact]
    public void Missing_header_gives_clear_error()
    {
        var dir = Fixtures.TempDir();
        var p = Path.Combine(dir, "bad.csv");
        File.WriteAllText(p, "a;b;c\n1;2;3\n");
        Assert.Throws<InvalidDataException>(() => Loader.Load(p, new ColumnMap()));
    }
}

public class ClassifierTests
{
    static Config Cfg()
    {
        var c = new Config(); c.Migrate();
        c.Profiles[0].RelevantPeople = new() { "Svoboda Petr" };
        return c;
    }

    static Raw R(string period, string person, string cc, string id, string desc, string abacus = "", double h = 1) =>
        new(period, int.Parse(period[..4]), int.Parse(period[5..]), person, cc, id, desc, abacus, h, 0);

    [Fact]
    public void Classifies_overhead_newbuild_p_and_customers()
    {
        var cls = new Classifier(Cfg());
        var oh = cls.Classify(R("2024/01", "A", "C23106", "INTR0001", "x"));
        Assert.True(oh.Overhead); Assert.Equal("-", oh.Customer);

        var nb = cls.Classify(R("2024/01", "A", "C23106", "FKMMPFI23-I001", "ES_FI_NB", "FI0001"));
        Assert.True(nb.Newbuild); Assert.Equal("FI", nb.Customer);

        var nbEx = cls.Classify(R("2024/01", "A", "C23106", "FKMMPFI23-I001", "ES_FI_RCS_ENG"));
        Assert.False(nbEx.Newbuild); Assert.Equal("FI", nbEx.Customer);   // customer from ID regex

        var p = cls.Classify(R("2024/01", "A", "C23106", "FKMMPUS24P123", "US_SERVICE"));
        Assert.True(p.PerRequest); Assert.Equal("US", p.Customer);         // from description token

        var unk = cls.Classify(R("2024/01", "A", "C23106", "XYZ", "nothing"));
        Assert.Equal("??", unk.Customer);
    }

    [Fact]
    public void Restricted_cost_centre_counts_only_relevant_people()
    {
        var cls = new Classifier(Cfg());
        Assert.True(cls.IsIncluded(R("2024/01", "Svoboda Petr", "C23107", "X", "x")));
        Assert.True(cls.IsIncluded(R("2024/01", "svoboda petr", "C23107", "X", "x")));   // case-insensitive
        Assert.False(cls.IsIncluded(R("2024/01", "Dvořák Karel", "C23107", "X", "x")));
        Assert.True(cls.IsIncluded(R("2024/01", "Dvořák Karel", "C23106", "X", "x")));
        Assert.False(cls.IsIncluded(R("2024/01", "Dvořák Karel", "C99999", "X", "x")));
    }

    [Fact]
    public void Empty_cost_centre_list_includes_everything()
    {
        var c = Cfg(); c.Profiles[0].CostCenters.Clear(); c.Profiles[0].RestrictedCostCenters.Clear();
        Assert.True(new Classifier(c).IsIncluded(R("2024/01", "X", "C99999", "X", "x")));
    }

    [Fact]
    public void Profiles_apply_by_period_with_fallback()
    {
        var c = Cfg();
        c.Profiles[0].Name = "Default";
        var y2025 = c.Profiles[0].Clone();
        y2025.Name = "2025"; y2025.From = "2025"; y2025.To = "2025";
        y2025.OverheadIdPatterns = new() { "^OH-" };
        c.Profiles.Insert(0, y2025);

        var cls = new Classifier(c);
        Assert.Equal("2025", cls.ProfileOf("2025/06").Name);
        Assert.Equal("Default", cls.ProfileOf("2024/06").Name);   // open profile as fallback
        Assert.True(cls.Classify(R("2025/03", "A", "C23106", "OH-2025-ADMIN", "x")).Overhead);
        Assert.False(cls.Classify(R("2024/03", "A", "C23106", "OH-2025-ADMIN", "x")).Overhead);
        Assert.False(cls.Classify(R("2025/03", "A", "C23106", "INTR0001", "x")).Overhead);
        Assert.Equal("2025", cls.Classify(R("2025/03", "A", "C23106", "X", "x")).Profile);
    }

    [Fact]
    public void Customer_overrides_win_and_desc_overhead_works()
    {
        var c = Cfg();
        c.Profiles[0].CustomerOverrides = new() { "SPECIAL => ch" };
        c.Profiles[0].OverheadDescPatterns = new() { "^Training$" };
        var cls = new Classifier(c);
        Assert.Equal("CH", cls.Classify(R("2024/01", "A", "C23106", "FKMMPFI23-I001", "SPECIAL job", "FI0001")).Customer);
        Assert.True(cls.Classify(R("2024/01", "A", "C23106", "P1", "Training")).Overhead);
    }

    [Fact]
    public void Validation_reports_bad_regex_and_ranges()
    {
        var c = Cfg();
        c.Profiles[0].NewbuildIdRegex = "([";
        c.Profiles[0].From = "2025"; c.Profiles[0].To = "2024";
        c.Profiles[0].CustomerOverrides = new() { "no arrow" };
        var errs = c.Validate();
        Assert.True(errs.Count >= 3);
    }

    [Fact]
    public void Metrics_split_adds_up_to_customer_total()
    {
        var c = Cfg();
        var cls = new Classifier(c);
        var rows = new[]
        {
            R("2024/01", "A", "C23106", "FKMMPFI23-I001", "ES_FI_NB", "FI1", 10),
            R("2024/01", "A", "C23106", "FKMMPFI23P001", "FI_P", "FI1", 5),
            R("2024/01", "A", "C23106", "FKMMPFI23-S001", "FI_S", "FI1", 3),
            R("2024/01", "A", "C23106", "INTR1", "oh", "", 2),
        }.Select(cls.Classify).ToList();
        var an = new Analytics(cls.Metrics(), false, "CZK");
        Assert.Equal(18, an.Eval("cust:FI", rows));
        Assert.Equal(18, an.Eval("split:nb", rows) + an.Eval("split:p", rows) + an.Eval("split:rest", rows));
        Assert.Equal(90, an.Eval(Analytics.PCT, rows), 6);
        Assert.Equal(2, an.Eval("oh", rows));
    }
}

public class ConfigTests
{
    [Fact]
    public void Migrates_v1_config_into_a_profile()
    {
        var json = """
        { "CostCenters": ["23106"], "RestrictedCostCenters": ["C23107"], "RelevantPeople": ["A B"],
          "OverheadIdPatterns": ["INTR", "ADMIN"], "NewbuildIdRegex": "^NB", "PerRequestIdRegex": "P$",
          "NewbuildExcludeDescriptions": ["X"], "Customers": ["FI"], "Theme": "Dark" }
        """;
        var c = Config.FromJson(json);
        Assert.Single(c.Profiles);
        var p = c.Profiles[0];
        Assert.Equal(new[] { "C23106" }, p.CostCenters);
        Assert.Equal(new[] { "INTR", "ADMIN" }, p.OverheadIdPatterns);
        Assert.Equal("^NB", p.NewbuildIdRegex);
        Assert.Equal("Dark", c.Theme);
        Assert.DoesNotContain("\"CostCenters\": [\n    \"23106\"", c.ToJson());
        Assert.Null(c.CostCenters);
    }

    [Fact]
    public void NormCc_variants()
    {
        Assert.Equal("C23106", Config.NormCc("23106"));
        Assert.Equal("C23106", Config.NormCc("CC23106"));
        Assert.Equal("C23106", Config.NormCc(" c23106 "));
        Assert.Equal("C23106", Config.NormCc("23106.0"));
        Assert.Equal("", Config.NormCc(""));
    }

    [Fact]
    public void All_translation_keys_have_both_languages()
    {
        foreach (var (k, v) in Strings.Table)
        {
            Assert.False(string.IsNullOrWhiteSpace(v.en), k);
            Assert.False(string.IsNullOrWhiteSpace(v.cs), k);
        }
    }

    [Fact]
    public void All_keys_used_in_sources_exist()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var rx = new System.Text.RegularExpressions.Regex(@"L\.T\(""([^""]+)""|\{l:T ([^}]+)\}");
        var missing = new List<string>();
        foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains("tests") && !f.Contains("/obj/") && !f.Contains("/bin/")))
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(File.ReadAllText(f)))
            {
                var key = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value.Trim();
                if (key != "key" && !key.EndsWith(".") && !Strings.Table.ContainsKey(key)) missing.Add($"{Path.GetFileName(f)}: {key}");
            }
        foreach (var k in new[] { "period", "year", "month", "person", "cc", "projectId", "desc", "abacus", "hours", "value" })
            if (!Strings.Table.ContainsKey("col." + k)) missing.Add("col." + k);
        foreach (var k in new[] { "override", "abacus", "desc", "id", "none" })
            if (!Strings.Table.ContainsKey("src." + k)) missing.Add("src." + k);
        foreach (var (_, k) in RuleScript.Variables) if (!Strings.Table.ContainsKey(k)) missing.Add(k);
        foreach (var (_, _, k) in RuleScript.Functions) if (!Strings.Table.ContainsKey(k)) missing.Add(k);
        foreach (var k in new[] { "data", "files", "rules", "profiles", "overhead", "nb", "p", "script" })
            if (!Strings.Table.ContainsKey("cat." + k)) missing.Add("cat." + k);
        foreach (var k in new[] { "inv", "oh", "pct", "total", "nb", "p", "rest" })
            if (!Strings.Table.ContainsKey("he." + k)) missing.Add("he." + k);
        Assert.Empty(missing);
    }
}
