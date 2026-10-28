# Requiem

A character manager for a homebrew **Vampire: The Requiem** ruleset (house VtR v2026.09.08 on house World of Darkness Core v2026.09.12). Anyone can start a campaign and tell it, or join one as a player. Each campaign keeps its NPC roster, full character sheets and scenes, with a searchable Discipline powers catalog shared by all and a table mode for running NPCs during a session.

Built tablet-first and usable on a phone. Its data is in DynamoDB (DynamoDB Local when run locally), and it is moving to serverless AWS hosting ([SERVERLESS.md](SERVERLESS.md)).

## Features

- **Campaigns** (`/campaigns`). Start one and you're its Storyteller; invite players with a link. One person can tell one campaign and play in another. Each campaign's page holds its overview, members, invites, NPCs and scenes.
- **Player characters.** Players build theirs with a step-by-step creation wizard that follows the book (Concept, Virtue and Vice, relationships, Scripts, Milestones, then the dots), with a live budget of every creation pool. The Storyteller approves or returns them; approved characters are tracked at the table by their players.
- **Advancement.** Players log Corruption (vampires) or Links (mortals and ghouls), mark their Scripts, and spend XP at the book's prices once unlocked. Storytellers award XP and can undo any spend, award or logged act from the character's history.
- **NPC roster,** per campaign. It uses the same cards and full profiles as Known NPCs, but shows every detail, with links to each NPC's editor and play view. Two tiers of NPC:
  - **Supporting Cast:** quick stat blocks (Physical / Mental / Social, Edges, Specialties, Assets).
  - **Full sheets:** the whole paper sheet, covering attributes, skills, Disciplines, merits and flaws, health and wounds, and compels. Scripts, Milestones, Corruption and XP are for player characters only.
  - A Supporting Cast NPC can be promoted to a full sheet.
- **Profiles.** Every character (NPC or player character) has a portrait, uploaded and shrunk in the browser, plus appearance, reputation and backstory. A player character's profile page (`/profile/{id}`) shows everything to everyone in the campaign, and the campaign's **Characters** tab shows them as profile cards. Its player and the Storyteller also get links between its profile, sheet and play view; the other players see only the profile, and only once the character is approved.
- **Factions and titles.** Every character's faction is one of Anarch, Camarilla, Carthian Movement, Sabbat or No Known Affiliation. The Storyteller grants titles from the book's lists (`Seed/titles.json`): the Camarilla's Council of Thorns and court positions, its fleeting and negative titles, the Sabbat's church and Inquisition offices, and the Carthians' offices and committees (the Anarchs have none). Each title's benefits show in the play view.
- **Known NPCs.** A Storyteller can share an NPC with the campaign's players, then reveal details one at a time as the players find out: portrait, type (vampire, ghoul or mortal), faction, bloodline, generation, titles, appearance, reputation and backstory. Players see only what's revealed, on the campaign's **Known NPCs** tab: a card for each NPC that opens its profile page (`/profile/{id}`), which has a link of its own. For the Storyteller, an NPC's profile, editor and play view link to each other through icons at the top of each page. The Storyteller sees the same thing with the **Players' View** switch on the NPCs tab, where search and the type filter then match only what the players know.
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
  - Full-sheet characters show their Concept, Virtue and Vice with what restores Willpower from each, and a button for each trigger: Concept +1, Virtue refills, Vice +1 (once per scene, which the table tracks). At 0 Willpower it warns that the Vice applies as a Condition.
  - Every change saves straight onto the character.
- **Accounts.** ASP.NET Core Identity with a cookie session. Every account is alike; anyone can sign up at `/register`. The first visit runs a setup that creates the first account, the site admin, who looks after the shared catalog.

## Tech stack

- **.NET 10:** ASP.NET Core Minimal APIs (runs on AWS Lambda when hosted), and a standalone Blazor WebAssembly app (static files on S3 when hosted).
- **UI:** [MudBlazor](https://mudblazor.com/) 9.
- **Data:** **DynamoDB**, one table ([SERVERLESS.md](SERVERLESS.md)). Locally and in tests, DynamoDB Local stands in for it. Endpoints use the store interfaces in `Requiem.Data/Stores`; ASP.NET Identity and the sign-in keys are stored in the same table.
- **Tests:** xUnit. Web tests run the real app via `WebApplicationFactory`, each test class against a fresh table in an in-memory DynamoDB Local.

## Getting started

Prerequisites:

- The [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.402 or a later feature band; see `global.json`).
- Java 17 or later for DynamoDB Local, e.g. [Amazon Corretto 21](https://corretto.aws/) (`winget install Amazon.Corretto.21.JDK`).
- [DynamoDB Local](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/DynamoDBLocal.DownloadingAndRunning.html), unzipped to `%LOCALAPPDATA%\DynamoDBLocal`, or anywhere with `DYNAMODB_LOCAL_HOME` pointing at it.

```bash
dotnet run --project src/Requiem.Web --launch-profile http
```

Then open http://localhost:5053.

1. **Database.** In Development, the app starts DynamoDB Local itself. It creates the table, seeds the catalog, and keeps the data in `src/Requiem.Web/App_Data/dynamodb`, which is gitignored. DynamoDB Local stops when the app does.
2. **First run.** You'll be sent to `/setup` to create the first account (the site admin). Outside Development, setup is locked until you set `Auth:SetupCode` in the server configuration, then enter that code on the setup page.
3. **Use it.** Start a campaign on the Campaigns page; its NPCs and Scenes tabs hold the Storyteller tools. Powers is in the nav too.

### Configuration

| Setting | Values | Default |
| --- | --- | --- |
| `DynamoDB:TableName` | The table | `requiem` |
| `DynamoDB:ServiceUrl` | DynamoDB Local's address; unset on AWS | Set by `StartLocal` in Development |
| `DynamoDB:StartLocal` | Start DynamoDB Local with the app | `true` in Development |
| `DynamoDB:CreateTable` | Create the table if it's missing (on AWS, CloudFormation does) | `true` in Development |
| `DynamoDB:Region` | AWS Region | `us-east-2` |
| `Database:SetupOnStartup` | Seed the catalog at startup outside Development | Off |
| `Auth:SetupCode` | One-time code for first-run setup | Not needed in Development |
| `Auth:SignUpsPerHour` | New accounts allowed per hour, site-wide (sign-up and invite joins) | `30` |
| `Hosting:ServeClient` | Also serve the browser app (index.html for app paths), as S3 does when hosted | `true`; `false` on Lambda |

## Project layout

```
src/
  Requiem.Shared/          Rules engine (pure C#), enums and API contracts shared by server and browser
  Requiem.Data/            Entities, store interfaces, the DynamoDB stores, seed JSON (Seed/*.json) and the catalog seeder
  Requiem.Web/             The API (ASP.NET Core; Lambda when hosted). Locally it also serves the browser app
  Requiem.Web.Client/      Standalone Blazor WebAssembly UI (wwwroot/index.html; pages, sheet panels, table mode)
tests/
  Requiem.Shared.Tests/    Rules engine
  Requiem.Data.Tests/      DynamoDB stores and seed data
  Requiem.Web.Tests/       API, auth and integration tests
tools/
  CatalogImport/           Windows-only tool that turns the rulebook PDF into Seed/powers.json
PLAN.md                    Design plan, rules decisions and status
```

## Development

Run all tests (they start their own DynamoDB Local):

```bash
dotnet test Requiem.slnx
```

**There are no migrations.** Each entity is stored as a JSON document, so new fields just appear; renaming or removing one needs code that reads old documents.

**Catalog seeding** is insert-only, matched by each entry's natural key. Corrections the admin makes in the app survive re-seeding. Seeding only compares entries when the seed files have changed since the last run, so a deleted entry comes back with the next change to them.

### Re-importing the powers catalog

`tools/CatalogImport` reads the VtR rulebook PDF with Windows' built-in PDF rendering and OCR. Two things don't survive plain OCR text: the ● dot levels in the grey heading bands, and the red ritual titles. The tool reads both from the rendered page images:

```bash
dotnet run --project tools/CatalogImport -- layout "<path to VtR rules PDF>" 15 240 rules-text/vtr-layout.jsonl
dotnet run --project tools/CatalogImport -- parse rules-text/vtr-layout.jsonl src/Requiem.Data/Seed/powers.json
```

Imported powers start unreviewed; check them on the Powers review screen. The OCR text and layout cache in `rules-text/` aren't committed.

## Deploying

Pushes to `main` deploy through AWS CodeBuild once the pipeline stack exists: S3 + CloudFront for the browser app, API Gateway + Lambda for the API, DynamoDB for data. [DEPLOY.md](DEPLOY.md) has the first-time setup, and [SERVERLESS.md](SERVERLESS.md) the design.

## Status

Phase 1 covers the GM tools listed above. Phase 2 adds campaigns and player access ([PHASE2.md](PHASE2.md)); its data model, permissions and hosting templates are in place. See [PLAN.md](PLAN.md) for the full plan and the house-rules decisions.
