namespace HoursTool;

/// <summary>
/// Ready-made rule snippets shown in the "Examples" window of the advanced rule editor.
/// Titles and descriptions are localised (<c>ex.&lt;id&gt;.t</c>, <c>ex.&lt;id&gt;.d</c>); the code is language neutral.
/// Every snippet must compile on its own (checked by a unit test).
/// </summary>
public static class ScriptExamples
{
    /// <summary>One example.</summary>
    public record Example(string Id, string Group, string Code)
    {
        /// <summary>Localised title.</summary>
        public string Title => L.T($"ex.{Id}.t");
        /// <summary>Localised description.</summary>
        public string Description => L.T($"ex.{Id}.d");
    }

    /// <summary>All examples, grouped by <see cref="Example.Group"/> (inclusion, overhead, customer, category, misc).</summary>
    public static readonly Example[] All =
    {
        // ---------------- inclusion ----------------
        new("ccList", "include", """
            list myCentres = C23106; C23107; C23211

            rule Only my cost centres
              when not In(Cc, @myCentres)
              set Include = false
              stop
            """),
        new("people", "include", """
            list team =
                Novák Jan
                Svoboda Petr

            rule Only my team in C23107
              when Cc = "C23107" and not In(Person, @team)
              set Include = false
              stop
            """),
        new("joinDate", "include", """
            rule Count Novák only from July 2025
              when Person = "Novák Jan" and Year * 100 + Month < 202507
              set Include = false
              stop
            """),
        new("negative", "include", """
            rule Ignore corrections (negative hours)
              when Hours < 0
              set Include = false
              stop
            """),

        // ---------------- overhead ----------------
        new("ohPrefix", "overhead", """
            rule Overhead – internal projects
              when StartsWith(Id, "INTR") or Match(Id, "^OH-\d{4}-")
              set Overhead = true
              stop
            """),
        new("ohDesc", "overhead", """
            rule Overhead – by description
              when Match(Desc, "training|meeting|vacation|školení")
              set Overhead = true
              stop
            """),
        new("ohException", "overhead", """
            # must be placed ABOVE the overhead rule
            rule INTR0099 is billed to the customer
              when Id = "INTR0099"
              set Customer = "FI"
              stop
            """),

        // ---------------- customer ----------------
        new("custAbacus", "customer", """
            rule Customer from ABACUS (first 2 characters)
              when Customer = "??" and Abacus.Trim().Length >= 2
              set Customer = Left(Abacus.Trim(), 2)
            """),
        new("custIdPos", "customer", """
            rule Customer = characters 6–7 of the project ID
              when Customer = "??" and Id.Length >= 7
              set Customer = Id.Substring(5, 2)
            """),
        new("custRegex", "customer", """
            rule Customer from the ID by regex group
              when Customer = "??" and Group(Id, "^FKMMP([A-Z]{2})", 1) != ""
              set Customer = Group(Id, "^FKMMP([A-Z]{2})", 1)
            """),
        new("custToken", "customer", """
            rule Customer code in the description (ES_FI_..., US_...)
              when Customer = "??" and Token(Desc, "FI; US; NO; IT; CH") != ""
              set Customer = Token(Desc, "FI; US; NO; IT; CH")
            """),
        new("custMap", "customer", """
            list fiProjects = FKMMPXX12-A001; FKMMPXX12-A002

            rule Projects billed to FI (manual list)
              when In(Id, @fiProjects)
              set Customer = "FI"
            """),
        new("custRename", "customer", """
            rule Merge NL into Other (rename a customer code)
              when Customer = "NL"
              set Customer = "XX"
            """),

        // ---------------- category ----------------
        new("catNb", "category", """
            rule Newbuild (FI)
              when Match(Id, "^FKMMPFI\d{2}-I\d{3}$") and not In(Desc, "ES_FI_RCS_ENG; ES_FI_RCS_FAT_S")
              set Category = "NB"
            """),
        new("catP", "category", """
            rule Per-request (P) projects
              when Match(Id, "^FKMMP[A-Z]{2}\d{2}P")
              set Category = "P"
            """),
        new("catService", "category", """
            rule Service / retrofit / spare parts
              when Category = "" and Match(Desc, "_SERVICE|RETROFIT|SPARE")
              set Category = "Service"
            """),
        new("catTernary", "category", """
            rule Category by ID suffix letter (A = Engineering, S = Service, other = Other)
              when Category = ""
              set Category = Group(Id, "-([A-Z])\d{3}$", 1) == "A" ? "Engineering"
                  : Group(Id, "-([A-Z])\d{3}$", 1) == "S" ? "Service" : "Other"
            """),
        new("catPhase", "category", """
            rule Phase from the ID (…-I001 → Phase I)
              when Category = "" and Group(Id, "-([A-Z])\d{3}$", 1) != ""
              set Category = "Phase " + Group(Id, "-([A-Z])\d{3}$", 1)
            """),

        // ---------------- misc ----------------
        new("yearSwitch", "misc", """
            rule From 2026 the overhead code is OH-
              when Year >= 2026 and StartsWith(Id, "OH-")
              set Overhead = true
              stop
            """),
        new("combined", "misc", """
            rule US service hours in C23211 are overhead
              when Customer = "US" and Cc = "C23211" and Contains(Desc, "WARRANTY")
              set Overhead = true
              stop
            """),
        new("default", "misc", """
            rule Everything else without a category
              when not Overhead and Category = ""
              set Category = "Other"
            """),
    };

    /// <summary>Localised group caption.</summary>
    public static string GroupTitle(string group) => L.T("ex.group." + group);
}
