# Requiem Character Manager — Plan

Ruleset: house *Vampire: The Requiem* (v2026.09.08) on a house *World of Darkness Core* (v2026.09.12).
Page refs below are PDF pages. OCR text of both books lives in `rules-text/`.

---

## 1. Stack

| Layer | Choice |
|---|---|
| Backend | ASP.NET Core (.NET 10 LTS), Minimal APIs |
| Data | EF Core. **SQLite for local dev** (decided 2026-10-23); **Azure SQL Database** in prod. `Database:Provider` in config picks the provider. |
| Frontend | **Blazor Web App, Interactive WebAssembly**, **MudBlazor**, installable as a **PWA** |
| Search | Powers-catalog search behind one interface: SQLite FTS5 in dev, SQL Server Full-Text Search on Azure SQL |
| Real-time (Phase 2) | SignalR (Azure SignalR Service when hosted) |
| Auth | ASP.NET Core Identity now; external logins (Google/Discord/Microsoft) in Phase 2 |

Solution (`Requiem.slnx`):
- `src/Requiem.Web`: Blazor Web App host, which also serves the API.
- `src/Requiem.Web.Client`: the WebAssembly UI.
- `src/Requiem.Shared`: DTOs, validators and the **rules engine**.
- `src/Requiem.Data`: EF Core model, `RequiemDbContext`, provider selection (`AddRequiemData`).
- `src/Requiem.Data.Sqlite`, `src/Requiem.Data.SqlServer`: each provider's migrations. Migrations are provider-specific, so **a model change needs a migration in both**. Tests fail if either one is missing.
- `tests/Requiem.Shared.Tests`: rules engine tests.
- `tests/Requiem.Data.Tests`: data tests, run on in-memory SQLite through the real migrations.
- `tools/CatalogImport` (later).

`Requiem.Shared/Rules` is a pure C# library (no DB, no UI) holding every formula and constraint below, so client and server compute identical numbers and it is unit-testable.

**Local dev:**
- `dotnet run --project src/Requiem.Web` creates and migrates `src/Requiem.Web/App_Data/requiem.db` on startup (Development only). The file is git-ignored.
- To add a migration:

```bash
dotnet ef migrations add <Name> --project src/Requiem.Data.Sqlite
```

```bash
dotnet ef migrations add <Name> --project src/Requiem.Data.SqlServer
```

**SQLite differences handled:**
- `DateTimeOffset` is stored as sortable integers on SQLite (native `datetimeoffset` on SQL Server).
- SQLite can't produce idempotent migration scripts.

---

## 2. Rules → app mechanics

### Core traits (WoD p.74–76)
- **Attributes (9), 1–5:** Mental = Intelligence, Wits, Perception · Physical = Strength, Dexterity, Stamina · Social = Charisma, Manipulation, Resolve.
  - An attribute left at **1 dot can't be raised with XP** → flag it on the sheet.
- **Skills (15), 0–5**, one single-word **Specialty** each (only on skills with ≥1 dot).
- Creation budgets (5/4/3 attributes, 11/7/5 skills, 3 specialties, 10 merit pts, ≤7 flaw pts) → optional **creation validator**, not enforced for NPCs.

### Derived and stored values
| Value | Rule | Stored or derived |
|---|---|---|
| Size | 5 unless merit/flaw | stored (default 5) |
| Health | Stamina + Size | derived (+ override) |
| Defense | lower of Wits or Dexterity | derived (+ override) |
| Willpower dots | Wits + Resolve **at creation only**; +1 per completed chronicle | **stored** (seeded at creation) |
| Humanity | creation Morality (7) − 1 = 6 | stored |
| Blood Potency | starts 1, max 10 | stored |
| Generation | starts 12 (13 if Thinblood); buy-down with merit points only | stored |
| Max Vitae | 10 + 2 × (BP − 1); **BP 10 = 50**; Thinblood −3 | derived |
| Bloodlust / Hunger dice | starts at Vitae ≤ 5 (or ≤ BP when BP > 5), +1 per Vitae lower | derived, live |
| Masquerade cost | 3 + (7 − Humanity) | derived |
| Daily rise cost | 1 + max(0, BP − 5) Vitae | derived |

The printed sheet shows 20 Vitae boxes, but with BP 10 the pool can reach 50. The app draws the real max.

### Constraints (warnings, GM can override)
- A single Discipline's dots can't exceed Blood Potency.
- Number of Disciplines can't exceed Blood Potency.
- Humanity + BP ≤ Generation Strength. If over, **Humanity** drops (never BP).

**Generation chart (VtR p.15)**
| Generation | Strength | Feeding |
|---|---|---|
| 13 (Flaw: Thinblood) | 9 | Animals, Humans, Vampires, Blood Bags |
| 12 (newly created) | 10 | Animals, Humans, Vampires, Blood Bags |
| 10–11 | 11 | Humans, Vampires, Blood Bags |
| 7–9 | 13 | Humans (4 pints per Vitae, rounded down), Vampires |
| 4–6 | 15 | Humans (must drain to death for 1 Vitae), Vampires (BP 6+ only) |

**Blood Potency Efficiency** (most Vitae spendable per action round)
| BP | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| Efficiency | 1 | 1 | 1 | 2 | 2 | 3 | 5 | 7 | 10 | 15 |

### Health & wounds (WoD p.32)
Wounds are a **list**, not boxes: `{ description, amount, type B/L/A, location? }`. *(Confirmed house rule.)*
- **Taken Out** (Torpor for vampires) when the **total damage amount** of active wounds ≥ Health dots.
- Dice penalty: the **single highest** among active wounds, by type: Bashing −1 · Lethal −2 · Aggravated −5. Penalties don't stack.
- Otherwise, type matters only for how a wound heals.
- A single wound over 2× Health → possible death.
- Healing costs for vampires: Bashing all for 1 Vitae; Lethal 1 Vitae/pt; Aggravated 3 Vitae + 1 WP/pt, capped per round by BP Efficiency. The app gets a "Heal" button that spends correctly.

### Vampire tracks & state
- **Fortify Form:** spend Vitae for temporary Potence/Celerity/Fortitude (the sheet's boxes), up to the linked attribute; lasts the scene.
- **Corruption 0–4:** XP spending is **locked until 4**, from 4 *different* vampiric acts (list on VtR p.137). Track which act earned each point, then reset when XP is spent.
- **Scripts (4):** Humanity, Beast, Coterie tie, Chronicle Tenet. Check each off; all 4 checked → **+4 XP** for vampires (+1 for mortals and ghouls, per the core book) and reset.
- **Milestones:** Opportunity → Risk → Choice → Benefit, with stage progress; Benefit is auto-applied or logged.
- **Stains:** list of "No longer can…" entries. Stains roll pool comes from the Humanity chart (Humanity 10→5 dice … 2–3→2 dice).
- **Compels:** named + rated (e.g., *Sorrow 3*, *Hysteria 3*), with clear condition ("until fed", "until rests in a coffin"). Each Bloodline has its own Compel. **Base Compels** is a d10 table → a "Roll Compel" button.
- **Dominion** (Beast in control): state + successes; d10 Beast's Designs table.
- **Passions** (from feeding): temporary buff with temperament (fleeting/intense/acute) and expiry.
- Flags: Torpor, Staked, Masquerading, Awake by day.

### Relationships between characters (big for an NPC tool)
- **Sire / Childer** (sire's Beast imprint), **Regnant / Thrall** blood bond stages 1–3.
- **Boons:** a ledger of who owes whom, how many, and why.
- **Relationships** (WoD p.65): 3 required for PCs; a free-form graph for NPCs.
- **Proxies** (Human / Ghoul / Childer).

### XP costs (WoD p.75, VtR p.137)
Attribute ×5 · Skill ×3 · Specialty 5 · Merit ×5 · Blood Potency ×5 · Bloodline Discipline ×5 · Caitiff/Diablerized Discipline ×6 · Ritual 10 · Humanity via 10-XP Milestone only · Willpower not purchasable.
→ An **XP ledger** (earned/spent entries). Spending auto-prices from these rules and is blocked until Corruption = 4.

### Catalog data to seed
**Seeded (2026-10-23)** from `src/Requiem.Data/Seed/*.json`, embedded and loaded by `CatalogSeeder` on dev startup:
- 13 Bloodlines: 7 Bloodlines, 5 Magi Houses, and Caitiff. Each has its weakness and compel.
- 10 Predator types.
- 115 Merits and Flaws, each tagged with its source book (`WodCore` or `Vampire`). Child and Damned exist in both books; the Vampire version applies to vampires.
- 18 Compels: the 10 d10 Base Compels plus 8 fixed "rule" Compels (Sorrow 3, Hysteria 3, Destroyer 6, Disgusted, Spiteful 3, Predation, Madness, Bloodlust).
- 5 NPC Edges.

**How seeding works:**
- Seeding is insert-only, matched by natural key. GM edits are never overwritten, and a deleted entry comes back on the next run.
- To change seed text after it's in a database, edit the row in the app (or delete it so it re-seeds).
- Descriptions are rules summaries with the PDF `sourcePage` for the full text.
- Sub-options (Ailing types, Madness types, Fighting Styles, Demesne features) live in the description; record the chosen one as the character merit's `Qualifier`.

**Not yet seeded:** Concepts (with their WP-regain trigger) · Virtues / Vices · Beast's Designs · Passions · Malkavian/Ravnos d10/d5 tables as rollable rows · Bloodline → Discipline links.

**Powers (seeded 2026-10-23):** `Seed/powers.json`, 174 powers across all 25 groups, generated by `tools/CatalogImport` (Windows only): `layout` OCRs PDF pages and caches each line's grey band / ● count / red-title flags in `rules-text/vtr-layout.jsonl`; `parse` turns that cache into the seed file. Seeded insert-only and unreviewed; the GM checks each one at `/catalog/powers`. Ghoul powers (prose in the book) were transcribed by hand. Names kept as printed, including "23", "Lords Gift", "Ride the Wild Sprit" and "The Banks of the ???". Investiture's powers are all level 1 and each dot buys one (`PowerRules.IsPickPerDot`); the picks are stored with learned rituals (`LearnedPowerIds`), at most one per dot.

**Bloodline rules in the engine** (`BloodlineRules`, added 2026-10-23):
- `BrujahMinimumHungerDice`, applied through `BloodRules.HungerDice(vitae, bp, minimum)`.
- `TremereDreadCountenanceDamage`.
- `GangrelPredationRating`.
- `NosferatuDisgustedRating`.
- The fixed ratings Destroyer 6 and Spiteful 3.

The UI picks which rule applies from the character's Bloodline.

---

## 3. Two NPC tiers

The WoD core (p.114–117) defines a light **Supporting Cast** NPC. The app supports both tiers:

| | Supporting Cast | Full Sheet |
|---|---|---|
| Stats | Physical / Mental / Social 1–5 | full sheet |
| Extras | Ambition, Specialties (+1 die), **Edges** (Tough, Unshakable, Quick Witted, Well Connected, Assassin, + vampire edges), **Assets** (+2 dice) | everything in §2 |
| Health | = Physical (Tough edge: +5) | Stamina + Size |
| Willpower | = Mental, not spent | tracked |
| Vitae | tracked, **auto-full at scene start** | tracked |

- **Promote** turns a Supporting Cast NPC into a Full Sheet NPC (prefilled from the stat block).
- Creating one should take ~30 seconds on a tablet.

---

## 4. Discipline power catalog

The OCR shows about **143 powers** in a regular format, plus Blood Sorcery rituals and Sabbat Ritae in different formats.

**Discipline power format (example, Auspex "The Spirit's Touch", p.145):**
`Name · flavor text · Vitae Cost: 2 · Dice Pool: Crafts + Auspex [− target trait] · Action: … · Roll Results (Failure / Success) · Bonus Successes: +N → effect`

**Model**
- `PowerGroup`: name, kind (Base Power, Caitiff Discipline, Bloodline Discipline, Blood Sorcery, Sabbat Ritae, Ghoul Power), bloodline/house link, description, source page.
- `Power`: group, level (dot), name, flavor, `VitaeCost`, `WillpowerCost`, `CostText`, `DicePoolText` + parsed `{attribute, skill, discipline, opposedBy}`, `ActionText`, `Duration`, `CastingDuration` (rituals), `HowToLearn` (ritae), outcomes `{Failure, Success, DramaticFailure}`, source page, tags.
- `PowerBonusOption`: cost (+N successes), effect text.
- `CharacterPower`: powers learned individually, plus notes.

**How a character gets powers** *(confirmed)*
- **Disciplines** (Caitiff and Bloodline), Base Powers and Ghoul powers are **cumulative**: N dots grants every power at levels 1 through N. These are derived from dots, not stored.
- **Blood Sorcery rituals** and **Sabbat Ritae** are **learned one at a time** and stored in `CharacterPower`.
- The starting Caitiff Discipline is **picked by the GM** from a dropdown. The app doesn't roll it.

**Search UX:** full-text search over name, flavor and effects. Filters: kind, discipline, bloodline, level, cost, dice pool skill/attribute. A power card shows the **computed dice pool for the selected character** (e.g., "Crafts 2 + Auspex 3 = 5, −1 Hunger"). Powers can be pinned to a character for table use.

**Import pipeline:**
1. OCR text → `tools/CatalogImport` parser.
2. Parser writes `catalog.seed.json`.
3. A GM review screen in the app (side by side with the source page) fixes OCR errors.
4. Seed the database.

Each power heading prints its level as dots (e.g. "●● The Spirit's Touch"). OCR drops those glyphs, so the importer renders each heading from the page image and counts the dots (or falls back to power order). Levels get confirmed in review.

The catalog is homebrew book content: the hosted app requires login and doesn't expose the catalog publicly.

---

## 5. Tablet-first / phone-functional UI

- **Tablet, landscape (primary):** two-pane sheet. Left: attributes, skills, disciplines. Right: trackers (Health/wounds, WP, Vitae, Humanity, BP, Hunger) pinned and always visible.
- **Phone:** one column with tabs (Traits · Powers · Trackers · Story), plus a sticky tracker bar.
- **Touch:** dot and box hit targets ≥ 44px. Tap sets a value; lowering asks for an undo-toast confirmation. No hover-only UI.
- **Table Mode (built 2026-10-23):** `/scenes` holds saved scenes (named NPC groups). A scene shows a card per NPC: Health as a fixed number, never marked down, with the described wounds listed and totalled against it; wound entry requires a description; Willpower and Vitae as tappable boxes like the paper sheet (the full sheet uses the same boxes); Hunger dice, compel and condition chips, and a quick condition box. Tapping a name opens `/play/{id}`, which adds a wound list with healing, Fortify Form (1 Vitae per point, capped by the attribute), compels, conditions, ready-made dice pools and the NPC's powers with "Use: spend N Vitae". Every tap is a small `PlayAction` saved straight onto the character (`POST /api/characters/{id}/play`); this bumps the character's version, so an editor left open elsewhere gets a conflict instead of overwriting. "End scene" lets Fortify Form lapse. Not done yet: rolling a Base Compel, and refilling Supporting Cast Vitae.
- **Dice pools, rolled physically (GM's choice 2026-10-23):** the app shows each pool minus the wound penalty, with the Hunger dice split out. Full sheets get a pool per granted power that has a pool, plus an "any roll" attribute + skill builder. Supporting Cast get Physical/Mental/Social, with Quick-Witted adding to Social. Fortify Form points aren't added into pools, because the book doesn't say how they apply.
- **PWA:** installable, full-screen on a tablet; catalog cached for offline reading (offline editing is later).

---

## 6. Azure hosting

| Piece | Service |
|---|---|
| App (API + Blazor client in one) | Azure App Service (Linux), Basic tier to start |
| Database | Azure SQL Database, serverless (auto-pause when idle) |
| Portraits / handouts | Azure Blob Storage |
| Secrets | Key Vault + managed identity (no connection strings in config) |
| Real-time (Phase 2) | Azure SignalR Service (free tier to start) |
| Infra / CI | Bicep templates + GitHub Actions (build, test, EF migrations, deploy) |

Built Azure-ready from day one: managed-identity SQL auth, health checks, Application Insights.

---

## 7. Phases

**Phase 1 — GM NPC manager**
1. Scaffold the solution, Identity, EF migrations, rules engine + tests. *(Done except Identity.)*
2. Seed the ruleset (traits, bloodlines, predators, merits/flaws, compels, edges). *(Done.)*
3. Supporting Cast and Full Sheet editors (responsive), with Promote. *(Done 2026-10-23. Full sheet at `/sheets/{id}` with Traits, Powers, Condition and Story tabs. Promote is in the Supporting Cast editor's menu.)*
4. Roster: search/filter by bloodline, faction, tag, tier; archive; duplicate; templates. *(Search, kind filter and archive done.)*

**UI as built (2026-10-23):**
- Blazor WebAssembly with prerendering off, MudBlazor 9 and a dark theme.
- `/` is the roster: cards, search, kind filter, archived toggle.
- `/npcs/new` and `/npcs/{id}` are the Supporting Cast editor, with an "At the table" panel computed by the rules engine.
- The API is under `/api` (`Requiem.Web/Api`), with JSON shared via `RequiemJson`.

**Sign-in (added 2026-10-23):**
- ASP.NET Core Identity, with users and roles in the main database (`AspNet*` tables).
- **Cookie:** `requiem.auth`, HttpOnly and SameSite=Strict, plus HTTPS-only outside Development. It lasts 14 days, renewed while you use the app.
- **Who can use what:**
  - Everything under `/api` needs the `GameMaster` role and answers 401/403 rather than redirecting.
  - `/api/account/*` (me, login, logout, setup) is open to anyone.
  - The page shell is served to anyone; the browser app decides what to show.
- **First run:** `/setup` creates the first GM, once only. Outside Development it also needs `Auth:SetupCode` from the server configuration (in Azure, the app setting `Auth__SetupCode`). Remove the code after setup.
- **Passwords:** at least 12 characters, with no composition rules. 5 failed sign-ins lock the account for 15 minutes.
- **Signed-out handling:** the browser app checks `/api/account/me`. Any 401 from the API signs it out and sends you to `/login?returnUrl=…`, and only app-relative return URLs are followed.
- **Not yet:**
  - Password reset (no email sender yet).
  - Changing your password in the app.
  - Player accounts and invites (Phase 2).
5. Discipline catalog: import tool, review screen, search UI, link powers to characters.
6. Table Mode + dice roller.
7. Relationships: sire tree, blood bonds, boons ledger, free-form links.
8. Print view / JSON export-import. Deploy to Azure.

**Phase 2 — Campaigns & players**
Campaign (Chronicle) with GM/Player membership and invites · players own PCs · GM-only notes and per-NPC visibility · XP awards and GM-approved spends · Corruption/Scripts/Milestone flows for PCs · SignalR live sync · audit log.
Detailed plan, with open decisions: [PHASE2.md](PHASE2.md).

---

## 8. Rules decisions (resolved 2026-10-23)
1. **Wounds:** Taken Out when total wound damage ≥ Health. The highest single wound gives a dice penalty by type (B −1 / L −2 / A −5). Otherwise type only affects healing.
2. **Generation Strength chart:** read from the page image (§2).
3. **Blood Potency Efficiency:** read from the page image (§2).
4. **Supporting Cast Health** = Physical.
5. **Powers:** Disciplines are cumulative by dots. Blood Sorcery rituals and Sabbat Ritae are learned individually. The GM picks the starting Caitiff Discipline.
6. **Scripts:** +4 XP for vampires.
7. **Masquerade cost** = 3 + (7 − Humanity), minimum 0. So Humanity 4 costs 6; the book's worked example of 8 is an arithmetic error.
8. **Ghouls (VtR p.62, `GhoulRules`):**
   - **Vitae:** a ghoul holds at most **8 Vitae**, reading "can drink up to 8 in one night" as the size of their track. The server trims anything over 8 when a sheet is saved.
   - **Daily loss:** the 1 Vitae lost each day is **marked by hand**. The app shows the reminder, and an "Aging" warning at 0.
   - **Healing:** with Vitae at a vampire's cost (`HealingRules`).
   - **Fortify Form:** 1 Vitae for a temporary dot of Potence, Celerity or Fortitude, **at most 1 in each**, lasting as long as a vampire's.
   - **No Hunger dice.**
