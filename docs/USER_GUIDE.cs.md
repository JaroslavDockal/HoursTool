# Hours Tool – uživatelská příručka

Hours Tool analyzuje exporty z timesheetů: kolik hodin je **invoiced** (fakturovatelných) a kolik **overhead**, podle zákazníků, lidí, projektů a v čase. Aplikace nic neposílá ven – vše běží lokálně nad vašimi soubory.

> Rychlý start: **Načíst soubory…** (nebo přetáhněte soubory do okna) → zkontrolujte záložku **Kontrola dat** → v **Nastavení** upravte pravidla → analyzujte na záložkách Přehled, Tabulka a grafech.

## 1. Načtení dat

- Podporované formáty: **xlsx, xlsm, xls, csv** (oddělovač `;`, `,` nebo tabulátor).
- Lze načíst **více souborů najednou**, např. jeden export za každý rok. Soubory si aplikace pamatuje a při dalším spuštění je načte sama.
- Pokud nový soubor obsahuje **stejná období** jako už načtený soubor, platí **novější soubor** (typicky čerstvý export letošního roku nahradí starší). Ve stavovém řádku se vypíše, co bylo nahrazeno.
- Tlačítko **↻** (F5) načte všechny soubory znovu z disku – hodí se, když jste export přepsali novějším.
- Tlačítko s košem odebere všechna data. Jednotlivé soubory odeberete na záložce **Kontrola dat** (vyberte řádek → *Odebrat vybrané*).

### Různé formáty exportu (jiné roky)

Loader je tolerantní k rozdílům mezi exporty:

- hlavička tabulky se hledá v prvních 60 řádcích (texty filtrů nad tabulkou nevadí), čtou se **všechny listy**, které hlavičku obsahují,
- sloupce se rozpoznávají podle **seznamu alternativních názvů** (Nastavení → *Import: názvy sloupců*). Nezáleží na velikosti písmen, mezerách, interpunkci ani diakritice (`Posted Time (h)` = `posted_time_h`),
- období může být v jednom sloupci (`2024/03`, `2024-03`, `03/2024`, `3.2024`, `003.2024`, `202403`, datum) **nebo** ve dvou sloupcích **Rok** + **Měsíc** (měsíc číslem i názvem, např. `březen`, `Mar`),
- čísla mohou mít desetinnou čárku i tečku a oddělovače tisíců (`1 234,5`, `1,234.5`),
- řádky bez platného období (Total, prázdné řádky) se přeskočí.

Pokud soubor nejde načíst, chybová hláška řekne, který sloupec chybí. Doplňte jeho název v Nastavení a stiskněte **Použít nastavení** – soubory se načtou znovu. Které sloupce byly rozpoznány, uvidíte na záložce **Kontrola dat** (sloupec *Rozpoznané sloupce*).

## 2. Filtry (levý panel)

Panel skryjete/zobrazíte tlačítkem vlevo nahoře nebo **Ctrl+B**. Aktivní filtry se zobrazují jako štítky nad záložkami; křížkem ✕ jednotlivý filtr zrušíte.

| Filtr | Význam |
|---|---|
| Jednotka | Hodiny, nebo peníze (sales rate). Peníze se počítají jen pro invoiced. |
| Rozklad FI | Rozdělí zákazníka s rozkladem (výchozí FI) na newbuild / P projekty / ostatní. |
| Rychlé volby | Vše, Poslední rok, Předchozí rok, **YTD vs. loni** (poslední dva roky, jen měsíce dostupné v posledním roce), Posledních 12 měsíců. |
| Od / Do | Souvislý rozsah období (např. 2024/07 – 2025/06). |
| Roky, Měsíce, Q1–Q4 | Výběr roků a měsíců; Q1–Q4 vybere měsíce čtvrtletí. Odkazy *vše / nic* zaškrtnou/odškrtnou vše. |
| Jen společné měsíce | Ponechá jen měsíce, které mají všechny vybrané roky – férové srovnání rozpracovaného roku. |
| Cost centra, Zákazníci | `(overhead)` = overhead hodiny, `??` = zákazník se nepodařilo určit. |
| Typ hodin | Vše / jen invoiced / jen overhead. |
| Kategorie projektu | Jen newbuild, bez newbuild, jen P projekty, bez P, jen běžné invoiced. |
| Projekt | Text v ID nebo popisu. Více výrazů oddělte `;`, výraz s `-` na začátku vyloučí (např. `COM; -DD`). |
| Lidé | Dočasné skrytí konkrétních lidí (pamatuje se mezi spuštěními). |
| Sloupce tabulky | Které metriky se zobrazí v tabulce za období a v souhrnu Σ. |

## 3. Záložky

- **Přehled** – karty s hlavními ukazateli. Šipka ▲/▼ ukazuje meziroční změnu posledních dvou roků **na stejných měsících** (zelená = dobrý směr). Pod kartami jsou statistiky po letech se sloupci Δ.
- **Tabulka** – kontingenční tabulka. Řádky: osoba, cost centrum, zákazník, projekt, rok nebo profil pravidel. Sloupce: měsíce, čtvrtletí, roky nebo jen souhrn. Řazení, hledání řádku a **Kopírovat** (vloží se do Excelu). Dvojklik na osobu otevře její detail.
- **Průběh v čase** – skládaný graf po měsících: invoiced podle zákazníků + overhead.
- **Trendy** – vybraná metrika v čase s klouzavým průměrem, lineárním trendem a prognózou. *Ignorovat poslední měsíc* vyřadí rozpracovaný měsíc. R² blízko 1 znamená spolehlivý trend.
- **Meziročně** – roky přes sebe (leden–prosinec) a kumulativní průběh od ledna.
- **Zákazníci** – invoiced podle zákazníka a roku, podíly (donut) a vývoj v čase.
- **Lidé** – žebříček lidí (top 20/40/80/vše) podle invoiced, celkových hodin, overheadu nebo Inv %.
- **Heatmapa** – osoby (nebo zákazníci, CC, projekty) × měsíce; hodnota invoiced, overhead, Inv %, všechny hodiny nebo položky rozkladu.
- **Osoba** – detail jednoho člověka: graf po měsících, ukazatele a seznam projektů. Šipky ◀ ▶ přepínají osoby.
- **Projekty** – všechny projekty s hodnotou, počtem lidí, obdobím a profilem, který je klasifikoval. Řazení kliknutím na hlavičku.
- **Kontrola dat** – načtené soubory (období, počet řádků, rozpoznané sloupce) a kontrola kvality: řádky mimo CC, vyloučení lidé, neznámí zákazníci, chybějící měsíce, duplicitní řádky, záporné hodiny, pokrytí profily, seznamy overhead / newbuild / P projektů.

## 4. Jak se hodiny klasifikují

Každý řádek exportu projde těmito kroky (pravidla jsou v **Nastavení** a platí pro profil daného období):

1. **Profil** – vybere se první profil (shora), jehož rozsah *Platí od – Platí do* obsahuje období řádku. Když žádný nesedí, použije se profil bez rozsahu.
2. **Cost centrum** – řádek se započítá, jen pokud je CC v seznamu *Započítaná cost centra* (prázdný seznam = všechna CC).
3. **Ruční výběr lidí** – v CC s ručním výběrem se započítají jen lidé ze seznamu *Relevantní lidé*.
4. **Overhead** – pokud SAP ID odpovídá některému regexu *Overhead (ID)* nebo popis regexu *Overhead (popis)*, jde o overhead (neinvoiced). Jinak je řádek **invoiced**.
5. **Zákazník** (jen invoiced) – ruční přiřazení → prvních N znaků ABACUS → kód v popisu (`_FI_`) → skupina 1 regexu nad ID → `??`.
6. **P projekt** – SAP ID odpovídá regexu *P projekty*.
7. **Newbuild** – SAP ID odpovídá regexu *Newbuild*, nejde o P projekt a popis není mezi vyloučenými.

Rozklad zákazníka (výchozí FI) = newbuild + P projekty + ostatní; tyto tři části dávají dohromady celé invoiced daného zákazníka.

### Metriky

| Metrika | Výpočet |
|---|---|
| Celkem zapsáno | součet všech hodin (vždy hodiny) |
| Invoiced | hodiny (nebo peníze) na ne-overhead projektech |
| Overhead | hodiny na overhead projektech |
| Inv % | invoiced hodiny / všechny hodiny × 100; změna v procentních bodech (p.b.) |
| Invoiced na osobu a měsíc | invoiced / počet dvojic (osoba, měsíc) s nějakým zápisem |
| Průměr aktivních lidí | počet dvojic (osoba, měsíc) / počet měsíců |

## 5. Nastavení a profily pravidel

Změny se dělají v editoru a projeví se až po **Použít nastavení**. Chyby (např. neplatný regex) se ukazují dole a tlačítko Použít je do opravy neaktivní. *Zahodit změny* vrátí poslední uložený stav.

### Profily pro různé roky

Když se pravidla mezi roky liší (jiné CC, nové kódy overheadu, jiné schéma ID projektů…):

1. V sekci **Profily pravidel** zvolte existující profil a klikněte na **Duplikovat** (ikona kopírování).
2. Nový profil pojmenujte (např. `2026+`) a nastavte **Platí od** `2026` (Platí do nechte prázdné = bez omezení).
3. Upravte pravidla – ta platí jen pro období 2026 a dál.
4. Starý profil (bez rozsahu, nebo s rozsahem `… – 2025`) dál platí pro starší data.
5. Pořadí profilů měníte šipkami; při překryvu rozsahů platí ten výš.

Pod rozsahem vidíte, kolik načtených období profil klasifikuje. Záložka **Tabulka** s řádky podle *Profilu pravidel* a sloupec *Profil* na záložce Projekty ukazují, který profil se kde použil.

Formát rozsahu: `2024` (= celý rok), `2024/07` (od/do července 2024), prázdné = neomezeno.

### Tester pravidel

Zadejte SAP ID, popis, případně ABACUS, období, CC a jméno – okamžitě uvidíte, jak by takový řádek klasifikovala **aktuální (i neuložená)** pravidla: profil, zda se započítá, overhead/invoiced, zákazníka a podle čeho byl určen, P/newbuild.

### Regexy – stručný tahák

| Zápis | Význam |
|---|---|
| `^` / `$` | začátek / konec textu |
| `\d` , `\d{2}` | číslice, dvě číslice |
| `[A-Z]{2}` | dvě písmena |
| `A\|B` | A nebo B |
| `.*` | cokoli |

Příklady: `^FKMMPFI\d{2}-I\d{3}$` (newbuild FI), `^FKMMP[A-Z]{2}\d{2}P` (P projekty), `INTR` (obsahuje INTR). Velikost písmen se nerozlišuje.

### Další nastavení

- **Zákazníci zobrazovaní samostatně** – ostatní se sloučí do *Ostatní zákazníci*.
- **Zákazník s rozkladem** – kterého zákazníka rozkládat na newbuild / P / ostatní (prázdné = bez rozkladu).
- **Označení měny** – popisek peněžní jednotky (CZK).
- **Ruční přiřazení zákazníka** – řádky `regex => KÓD`, např. `^FKMMPXX12 => FI`.
- **Export / Import nastavení** – sdílení pravidel s kolegy (JSON). Import načte nastavení do editoru, pak stiskněte Použít.
- Konfigurace se ukládá do `%APPDATA%\HoursTool\config.json`. Starší konfigurace (verze 1) se automaticky převede na jeden profil.

## 6. Export

- **Export do Excelu** (Ctrl+E) vytvoří sešit s listy: *Report* (tabulka tak, jak ji vidíte), *Statistiky*, *Měsíce* (všechny metriky po měsících), *Projekty*, *Data* (vyfiltrované a klasifikované řádky s automatickým filtrem – pro vlastní kontingenční tabulky) a *Filtry a pravidla* (co bylo použito).
- **Grafy (PNG)** uloží všechny grafy do vybrané složky.
- Grafy jsou interaktivní: kolečko myši = zoom, tažení levým tlačítkem = posun, kliknutí prostředním tlačítkem = výchozí pohled, pravé tlačítko = menu (mj. uložení obrázku).

## 7. Klávesové zkratky

| Zkratka | Akce |
|---|---|
| Ctrl+O | Načíst soubory |
| Ctrl+E | Export do Excelu |
| F5 | Znovu načíst soubory z disku |
| Ctrl+B | Skrýt/zobrazit filtry |
| Ctrl+1 … Ctrl+9 | Přepnout záložku |
| F1 | Nápověda |

## 8. Časté otázky

**Proč nesedí součet s exportem?** Podívejte se na *Kontrola dat*: řádky mimo nastavená CC a vyloučení lidé z CC s ručním výběrem se nezapočítávají. Zkontrolujte také aktivní filtry (štítky nad záložkami).

**Zákazník je `??`.** Projekt nemá ABACUS a kód zákazníka se nenašel ani v popisu, ani v ID. Přidejte ruční přiřazení nebo kód do *Kódy zákazníků hledané v popisu*.

**Export z nového roku má jiné názvy sloupců.** Přidejte nový název do Nastavení → *Import: názvy sloupců* a stiskněte Použít.

**Meziroční změna vypadá divně.** Změny se počítají jen na měsících, které mají oba roky; poslední měsíc bývá rozpracovaný (v Trendech jej lze ignorovat).
