# Hours Tool – User Guide

Hours Tool analyses timesheet exports: how many hours are **invoiced** and how many are **overhead**, by customer, person, project and over time. Nothing is sent anywhere – everything runs locally on your files.

> Quick start: **Load files…** (or drag & drop files onto the window) → check the **Data check** tab → adjust the rules in **Settings** → analyse on Overview, Table and the chart tabs.

## 1. Loading data

- Supported formats: **xlsx, xlsm, xls, csv** (separator `;`, `,` or tab).
- You can load **several files at once**, e.g. one export per year. Files are remembered and loaded automatically on the next start.
- When a new file contains **the same periods** as a file already loaded, the **newer file wins** (typically a fresh export of the current year replaces an older one). The status bar tells you what was replaced.
- The **↻** button (F5) reloads all files from disk – useful after you overwrote an export with a newer one.
- The bin button unloads all data. Single files are removed on the **Data check** tab (select a row → *Remove selected*).

### Different export formats (other years)

The loader tolerates differences between exports:

- the table header is searched for in the first 60 rows (filter texts above the table are fine) and **all sheets** that contain a header are read,
- columns are recognised by a **list of alternative names** (Settings → *Import: column names*). Case, spaces, punctuation and diacritics are ignored (`Posted Time (h)` = `posted_time_h`),
- the period can be one column (`2024/03`, `2024-03`, `03/2024`, `3.2024`, `003.2024`, `202403`, a date) **or** two columns **Year** + **Month** (month as a number or a name such as `Mar` or `březen`),
- numbers may use a decimal comma or dot and thousands separators (`1 234,5`, `1,234.5`),
- rows without a valid period (Total, blank lines) are skipped.

If a file cannot be loaded, the message tells you which column is missing. Add its header name in Settings and press **Apply settings** – files are reloaded automatically. The **Data check** tab shows which columns were recognised (*Recognised columns*).

## 2. Filters (left panel)

Hide/show the panel with the button at the top left or **Ctrl+B**. Active filters are shown as chips above the tabs; the ✕ on a chip clears that filter.

| Filter | Meaning |
|---|---|
| Unit | Hours, or money (sales rate). Money is counted for invoiced rows only. |
| Break down FI | Splits the breakdown customer (default FI) into newbuild / P projects / other. |
| Presets | All, Latest year, Previous year, **YTD vs. prev.** (last two years, only months available in the latest year), Last 12 months. |
| From / To | Continuous period range (e.g. 2024/07 – 2025/06). |
| Years, Months, Q1–Q4 | Year and month selection; Q1–Q4 selects the months of a quarter. *all / none* links (un)check everything. |
| Only common months | Keeps only months present in all selected years – fair comparison with an incomplete year. |
| Cost centres, Customers | `(overhead)` = overhead hours, `??` = customer could not be determined. |
| Type of hours | All / invoiced only / overhead only. |
| Project category | Only newbuild, without newbuild, only P projects, without P, only regular invoiced. |
| Project | Text in ID or description. Separate several terms with `;`, a leading `-` excludes (e.g. `COM; -DD`). |
| People | Temporarily hide specific people (remembered between sessions). |
| Table columns | Which measures the table shows per period and in the Σ totals. |

## 3. Tabs

- **Overview** – cards with the key indicators. ▲/▼ shows the change between the last two years **on the same months** (green = good direction). Below: statistics by year with Δ columns.
- **Table** – pivot table. Rows: person, cost centre, customer, project, year or rule profile. Columns: months, quarters, years or totals only. Sorting, row search and **Copy** (paste into Excel). Double-click a person to open their detail.
- **Over time** – stacked monthly chart: invoiced by customer + overhead.
- **Trends** – a measure over time with moving average, linear trend and forecast. *Ignore last month* drops an incomplete month. R² close to 1 means a reliable trend.
- **Year over year** – years on top of each other (Jan–Dec) and the cumulative curve from January.
- **Customers** – invoiced by customer and year, shares (donut) and development over time.
- **People** – ranking (top 20/40/80/all) by invoiced, total hours, overhead or Inv %.
- **Heatmap** – people (or customers, CCs, projects) × months; value invoiced, overhead, Inv %, all hours or breakdown items.
- **Person** – one person's detail: monthly chart, indicators and project list. ◀ ▶ switch people.
- **Projects** – all projects with value, number of people, period and the profile that classified them. Click a header to sort.
- **Data check** – loaded files (periods, rows, recognised columns) and quality checks: rows outside cost centres, excluded people, unknown customers, missing months, duplicate rows, negative hours, profile coverage, lists of overhead / newbuild / P projects.

## 4. How hours are classified

Every export row goes through these steps (rules are in **Settings** and come from the profile of the row's period):

1. **Profile** – the first profile (top to bottom) whose *Valid from – Valid to* range contains the period. If none matches, the profile without a range is used.
2. **Cost centre** – the row counts only if its CC is in *Cost centres to include* (empty list = all).
3. **Manual people selection** – in cost centres with manual selection only people from *Relevant people* count.
4. **Overhead** – if the SAP ID matches an *Overhead (ID)* regex or the description an *Overhead (description)* regex, the row is overhead (non-invoiced). Otherwise it is **invoiced**.
5. **Customer** (invoiced only) – manual override → first N characters of ABACUS → code token in the description (`_FI_`) → group 1 of the ID regex → `??`.
6. **P project** – SAP ID matches the *P projects* regex.
7. **Newbuild** – SAP ID matches the *Newbuild* regex, it is not a P project and the description is not excluded.

Customer breakdown (default FI) = newbuild + P projects + other; together they make the customer's whole invoiced amount.

### Measures

| Measure | Calculation |
|---|---|
| Total posted | sum of all hours (always hours) |
| Invoiced | hours (or money) on non-overhead projects |
| Overhead | hours on overhead projects |
| Inv % | invoiced hours / all hours × 100; changes in percentage points (pp) |
| Invoiced per person-month | invoiced / number of (person, month) pairs with any posting |
| Avg. active people | number of (person, month) pairs / number of months |

## 5. Settings and rule profiles

Changes are made in the editor and take effect after **Apply settings**. Errors (e.g. an invalid regex) are listed at the bottom and Apply stays disabled until they are fixed. *Discard changes* returns to the last saved state.

### Profiles for different years

When the rules differ between years (other cost centres, new overhead codes, a new project-ID scheme…):

1. In **Rule profiles** pick an existing profile and click **Duplicate** (copy icon).
2. Name the new profile (e.g. `2026+`) and set **Valid from** to `2026` (leave Valid to empty = open).
3. Adjust the rules – they apply to 2026 onwards only.
4. The old profile (no range, or a range `… – 2025`) still applies to older data.
5. Change the order with the arrows; when ranges overlap, the upper profile wins.

Below the range you can see how many loaded periods the profile classifies. The **Table** tab with rows by *Rule profile* and the *Profile* column on the Projects tab show where each profile was used.

Range format: `2024` (whole year), `2024/07` (from/to July 2024), empty = open.

### Rule tester

Enter a SAP ID, description and optionally ABACUS, period, CC and name – you immediately see how the **current (even unsaved)** rules classify such a row: profile, whether it counts, overhead/invoiced, customer and how it was found, P/newbuild.

### Regex cheat sheet

| Pattern | Meaning |
|---|---|
| `^` / `$` | start / end of text |
| `\d` , `\d{2}` | a digit, two digits |
| `[A-Z]{2}` | two letters |
| `A\|B` | A or B |
| `.*` | anything |

Examples: `^FKMMPFI\d{2}-I\d{3}$` (FI newbuild), `^FKMMP[A-Z]{2}\d{2}P` (P projects), `INTR` (contains INTR). Matching is case-insensitive.

### Other settings

- **Customers shown separately** – all others are grouped as *Other customers*.
- **Customer with breakdown** – which customer is split into newbuild / P / other (empty = no breakdown).
- **Money unit label** – label of the money unit (CZK).
- **Manual customer overrides** – lines `regex => CODE`, e.g. `^FKMMPXX12 => FI`.
- **Export / Import settings** – share the rules with colleagues (JSON). Import loads the settings into the editor; then press Apply.
- The configuration is stored in `%APPDATA%\HoursTool\config.json`. An older (version 1) configuration is converted to a single profile automatically.

## 6. Export

- **Export to Excel** (Ctrl+E) creates a workbook with sheets: *Report* (the table as you see it), *Statistics*, *Months* (all measures by month), *Projects*, *Data* (filtered and classified rows with an auto-filter – for your own pivot tables) and *Filters and rules* (what was applied).
- **Charts (PNG)** saves all charts into a chosen folder.
- Charts are interactive: mouse wheel = zoom, left drag = pan, middle click = reset view, right click = menu (incl. saving the image).

## 7. Keyboard shortcuts

| Shortcut | Action |
|---|---|
| Ctrl+O | Load files |
| Ctrl+E | Export to Excel |
| F5 | Reload files from disk |
| Ctrl+B | Hide/show filters |
| Ctrl+1 … Ctrl+9 | Switch tab |
| F1 | Help |

## 8. FAQ

**Why does the total differ from the export?** Look at *Data check*: rows outside the configured cost centres and people excluded in manual-selection cost centres are not counted. Also check the active filters (chips above the tabs).

**The customer is `??`.** The project has no ABACUS and no customer code was found in the description or ID. Add a manual override or the code to *Customer codes searched in the description*.

**An export from a new year has different column names.** Add the new name in Settings → *Import: column names* and press Apply.

**A year-over-year change looks odd.** Changes are computed on the months both years have; the last month is often incomplete (Trends can ignore it).
