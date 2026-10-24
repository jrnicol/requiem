# Requiem

A character manager for a homebrew **Vampire: The Requiem** ruleset (house VtR v2026.09.08 on house World of Darkness Core v2026.09.12). Anyone can start a campaign and tell it, or join one as a player. Each campaign keeps its NPC roster, full character sheets and scenes, with a searchable Discipline powers catalog shared by all and a table mode for running NPCs during a session.

Built tablet-first and usable on a phone. It's meant to be hosted on Azure eventually; for now it runs locally on SQLite.

## Features

- **Campaigns** (`/campaigns`). Start one and you're its Storyteller; invite players with a link. One person can tell one campaign and play in another. Each campaign's page holds its overview, members, invites, NPCs and scenes.
- **Player characters.** Players build theirs with a step-by-step creation wizard that follows the book (Concept, Virtue and Vice, relationships, Scripts, Milestones, then the dots), with a live budget of every creation pool. The Storyteller approves or returns them; approved characters are tracked at the table by their players.
- **Advancement.** Players log Corruption (vampires) or Links (mortals and ghouls), mark their Scripts, and spend XP at the book's prices once unlocked. Storytellers award XP and can undo any spend, award or logged act from the character's history.
- **NPC roster,** per campaign. Two tiers of NPC:
  - **Supporting Cast:** quick stat blocks (Physical / Mental / Social, Edges, Specialties, Assets).
  - **Full sheets:** the whole paper sheet, covering attributes, skills, Disciplines, merits and flaws, health and wounds, compels, Scripts, Milestones and XP.
  - A Supporting Cast NPC can be promoted to a full sheet.
- **Rules engine.** The house rules are coded and tested:
  - Wounds, with a dice penalty set by the worst wound's type.
  - Blood Potency, Vitae, Bloodlust and Hunger dice.
  - Generation and Humanity, healing and experience costs.
  - Bloodline weaknesses: the Brujah minimum Hunger dice, Tremere Dread Countenance damage, the Gangrel Predation rating and the Nosferatu Disgusted rating.
  - Rules-engine limits show as warnings rather than blocking a save.
- **Catalog.** Seeded from the rulebooks:
  - 13 Bloodlines and Magi Houses, 10 predator types, 115 merits and flaws, compels, NPC Edges and 25 power groups.
  - **174 Discipline powers** imported from the rulebook PDF by OCR.
- **Powers catalog** (`/catalog/powers`). Everyone can search the full rules text. The site admin corrects imported entries against the book, marking each one reviewed.
- **Powers on the sheet:**
  - Each Discipline lists the powers its dots grant, with the full text.
  - Blood Sorcery rituals and Sabbat Ritae are learned one at a time.
  - Investiture records which power each dot bought.
- **Table mode:**
  - **Scenes** (on each campaign's page) group the NPCs you run together. Each NPC gets a card with Health and described wounds, Willpower and Vitae boxes, Hunger dice, compels and conditions.
  - **The play view** (`/play/{id}`) adds dice pools ready to roll physically, with the wound penalty taken off and Hunger dice split out. It also has Fortify Form and a "spend Vitae" button on each power that costs Vitae.
  - Every change saves straight onto the character.
- **Accounts.** ASP.NET Core Identity with a cookie session. Every account is alike; anyone can sign up at `/register`. The first visit runs a setup that creates the first account, the site admin, who looks after the shared catalog.

## Tech stack

- **.NET 10:** ASP.NET Core Minimal APIs, and a Blazor Web App with Interactive WebAssembly.
- **UI:** [MudBlazor](https://mudblazor.com/) 9.
- **Data:** EF Core 10 with **SQLite** for local development and **SQL Server** (Azure SQL) for hosting. Each provider has its own migrations project.
- **Tests:** xUnit. Web tests run the real app via `WebApplicationFactory` against a throwaway SQLite file.

## Getting started

Prerequisite: the [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.402 or a later feature band; see `global.json`).

```bash
dotnet run --project src/Requiem.Web --launch-profile http
```

Then open http://localhost:5053.

1. **Database.** In Development, the app applies migrations and seeds the catalog on startup. The SQLite file lives at `src/Requiem.Web/App_Data/requiem.db`, which is gitignored.
2. **First run.** You'll be sent to `/setup` to create the first account (the site admin). Outside Development, setup is locked until you set `Auth:SetupCode` in the server configuration, then enter that code on the setup page.
3. **Use it.** Start a campaign on the Campaigns page; its NPCs and Scenes tabs hold the Storyteller tools. Powers is in the nav too.

### Configuration

| Setting | Values | Default |
| --- | --- | --- |
| `Database:Provider` | `Sqlite` or `SqlServer` | `Sqlite` in Development |
| `ConnectionStrings:Requiem` | Connection string for the chosen provider | `Data Source=App_Data/requiem.db` in Development |
| `Auth:SetupCode` | One-time code for first-run setup | Not needed in Development |
| `Auth:SignUpsPerHour` | New accounts allowed per hour, site-wide (sign-up and invite joins) | `30` |

## Project layout

```
src/
  Requiem.Shared/          Rules engine (pure C#), enums and API contracts shared by server and browser
  Requiem.Data/            EF Core model, DbContext, seed JSON (Seed/*.json) and the catalog seeder
  Requiem.Data.Sqlite/     SQLite migrations
  Requiem.Data.SqlServer/  SQL Server migrations
  Requiem.Web/             ASP.NET Core host: API endpoints, auth, static assets
  Requiem.Web.Client/      Blazor WebAssembly UI (pages, sheet panels, table mode)
tests/
  Requiem.Shared.Tests/    Rules engine
  Requiem.Data.Tests/      Persistence, migrations and seed data
  Requiem.Web.Tests/       API, auth and integration tests
tools/
  CatalogImport/           Windows-only tool that turns the rulebook PDF into Seed/powers.json
PLAN.md                    Design plan, rules decisions and status
```

## Development

Run all tests:

```bash
dotnet test Requiem.slnx
```

**Model changes need a migration for both providers.** `dotnet-ef` is a local tool (`dotnet tool restore`):

```bash
dotnet ef migrations add <Name> --project src/Requiem.Data.Sqlite
dotnet ef migrations add <Name> --project src/Requiem.Data.SqlServer
```

The test suite fails if either provider has model changes without a migration.

**Catalog seeding** is insert-only, matched by each entry's natural key. Corrections the GM makes in the app survive re-seeding, and a deleted entry comes back on the next run.

### Re-importing the powers catalog

`tools/CatalogImport` reads the VtR rulebook PDF with Windows' built-in PDF rendering and OCR. Two things don't survive plain OCR text: the ● dot levels in the grey heading bands, and the red ritual titles. The tool reads both from the rendered page images:

```bash
dotnet run --project tools/CatalogImport -- layout "<path to VtR rules PDF>" 15 240 rules-text/vtr-layout.jsonl
dotnet run --project tools/CatalogImport -- parse rules-text/vtr-layout.jsonl src/Requiem.Data/Seed/powers.json
```

Imported powers start unreviewed; check them on the Powers review screen. The OCR text and layout cache in `rules-text/` aren't committed.

## Deploying

The Azure infrastructure is in [`infra/`](infra/), and GitHub Actions deploys `main`. [DEPLOY.md](DEPLOY.md) has the first-time setup.

## Status

Phase 1 covers the GM tools listed above. Phase 2 adds campaigns and player access ([PHASE2.md](PHASE2.md)); its data model, permissions and Azure templates are in place. See [PLAN.md](PLAN.md) for the full plan and the house-rules decisions.
