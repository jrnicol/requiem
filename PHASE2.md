# Phase 2 — Player characters and campaigns

Status: **agreed plan** (2026-10-24). All open decisions are settled; see §10.

Phase 1 is a single-GM tool: every API needs the `GameMaster` role and every character belongs to that GM. Phase 2 lets anyone run **campaigns** (chronicles) with **players**, who build their own characters in the app and play them at the table alongside the Storyteller's NPCs.

**No account types** (decided 2026-10-24, decision 12): every account is alike. Anyone can sign up, start a campaign and tell it, and play in someone else's. Storyteller tools belong to each campaign. The only site-wide role is **Admin**, which edits the shared rules catalog and nothing else.

---

## 1. Goals

1. A GM creates a campaign and invites players.
2. Players sign up through the invite, build a character with a **guided creation flow** that follows the books, and submit it for the GM's approval.
3. During play, players track their own character (wounds, Willpower, Vitae, conditions), and the GM sees changes as they happen.
4. Advancement follows the rules: the GM awards XP; players spend it within the rules, and the GM can review and undo any spend.
5. Players never see the GM's NPC stats or GM notes. The GM chooses which NPCs players "know", and what they know about them.

**Out of scope for Phase 2:** player-to-player trading, dice rolling in the app (still physical), offline editing, and public sharing of the powers catalog.

---

## 2. Prerequisites

These have to exist before players can use the app:

| Item | Why | Notes |
| --- | --- | --- |
| **Hosting on Azure** (Phase 1 step 8) | Players need to reach the app | Azure Container Apps + Azure SQL + Key Vault (PLAN.md §6, DEPLOY.md). GitHub Actions deploys; the app migrates on start. |
| **Email sending** | Password reset, and optionally emailing invite links | **Azure Communication Services Email** (decided 2026-10-24), using the app's managed identity. Needs a verified sending domain, or Azure's free subdomain to start. |
| **Per-campaign authorization** | Today one role gates everything | See §4. This is the riskiest change, so it goes first. |

---

## 3. Data model

New tables:

| Entity | Fields | Notes |
| --- | --- | --- |
| `Campaign` | Id, Name, Description, ChronicleTenet, CoterieScript, AllowedTemplates, CreatedAt, ArchivedAt | The Chronicle Tenet is the fourth Script, set by the Storyteller for everyone (VtR p.136). The Coterie Script is the third, agreed by the players and stored once (decided 2026-10-24). AllowedTemplates is which of mortal / ghoul / vampire players may build. |
| `CharacterLink` | Id, CharacterId, Kind (`Camaraderie` / `Support` / `CedeSpotlight`), WithCharacterIds, Note, At | Links for mortal and ghoul PCs (WoD Core p.75); see §6. |
| `CampaignMember` | CampaignId, UserId, Role (`Storyteller` / `Player`), JoinedAt | A user can be a Storyteller in one campaign and a player in another. |
| `CampaignInvite` | Id, CampaignId, TokenHash, Role, ExpiresAt, MaxUses, UsesLeft, CreatedByUserId | Only a hash of the token is stored. The link is `/join/{token}`. |
| `AuditEntry` | Id, CampaignId, CharacterId?, UserId, Action, Summary, Undo data, At | Who changed what. Since XP spends need no approval, this is how the Storyteller reviews and reverses them (§6). |

XP spends are written straight to the existing XP ledger, extended with the trait and the dots before and after, so a spend can be shown and undone.

Changes to existing tables:

- **`Character`:** already has `OwnerUserId` and `CampaignId`. Add:
  - `Status` (`Draft` / `Submitted` / `Active` / `Retired` / `Dead`);
  - `IsPlayerCharacter`;
  - `PublicDescription`: what other players see on the character's public card;
  - for NPCs: `Visibility` (`Hidden` / `Known`);
  - for ghouls: `DomitorId` (a campaign vampire, NPC or PC) or `DomitorName` (free text).
- **`Scene`:** gains `CampaignId`, so a campaign's scenes are shared with its Storyteller(s), and PCs can be added to scenes.
- **Existing data:** every character and scene belongs to a campaign (decision 12). At startup, `AccountsUpgrade` moves any outside one (the old personal roster) into a new campaign per owner, "Imported NPCs", which the owner tells. Unowned ones, made before Phase 2, go to the site admin.

---

## 4. Access rules

Authorization moves from one global role to **campaign membership**. It is checked on the server for every request, not just hidden in the UI.

| Who | Can |
| --- | --- |
| **Site admin** (the `Admin` role, the first account) | Edit the powers catalog. No say in anyone's campaign. |
| **Storyteller** of a campaign | Everything in that campaign: all characters, scenes, invites, approvals and XP awards. Nothing in other campaigns, including ones they play in. |
| **Player** in a campaign | Their own characters (create, edit drafts, track play, spend XP); the campaign's known NPCs as public cards; the other PCs as public cards only (name, concept, bloodline, portrait, the public description their player wrote), never their full sheets (decided 2026-10-24). Any member can edit the shared Coterie Script. |
| **Anyone signed in** | Their account and the campaigns they belong to. Start a new campaign, becoming its Storyteller. |
| **Anyone** | Sign up (capped at 30 new accounts an hour across the site, `Auth:SignUpsPerHour`). |

Server-side shaping: the API returns different shapes per audience. A player's view of an NPC is a `PublicCharacterCard` (name, concept, bloodline, portrait, public description). It never contains the full sheet or GM notes, so nothing can leak through the browser's developer tools.

The **powers catalog** stays readable by every signed-in member, because players need it to build characters. Only the site admin can edit it.

---

## 5. Player character creation

A **step-by-step wizard** on tablet and phone, following the core book's order (WoD Core p.55) and then the vampire template (VtR p.136). Every step can be revisited. Progress saves as a **Draft**, so a player can stop and come back.

| Step | What the player does | Rules applied |
| --- | --- | --- |
| 1. Template | Mortal, Ghoul or Vampire | Only the templates the campaign allows (decided 2026-10-24). |
| 2. Concept | Pick from the book's Concepts or write one | Each Concept has a Willpower-regain trigger. **Needs seeding:** Concepts aren't in the catalog yet. |
| 3. Virtue and Vice | Pick, plus a detail line | Already modelled. |
| 4. Relationships | Three relationships | Required for PCs (WoD Core p.65): someone who knows you, someone who complicates your life, someone who ties you to the chronicle. Stored as relationships with a role. |
| 5. Scripts | Vampires: Humanity and Beast. Mortals and ghouls: a Quirk and a Personal Script (decision 14). The Coterie Script and Chronicle Tenet are filled in from the campaign | The Coterie Script is one shared text the group agrees; any member can edit it, and edits are audited. Each player marks it on their own sheet. |
| 6. Milestones | Write the first Milestones | Already modelled. |
| 7. Attributes | Rank Physical / Social / Mental, then spend **5 / 4 / 3** | Every attribute starts at 1 dot, maximum 5. A warning notes that attributes left at 1 can't be raised with XP later. |
| 8. Skills | Rank, then spend **11 / 7 / 5**; add **3 Specialties** | Maximum 3 dots at creation. A 4th dot is allowed but flagged: a warning in the budget panel and highlighted in the approval queue (decided 2026-10-24). Specialties only on skills with at least 1 dot. |
| 9. Merits and Flaws | Spend **10 Merit points**; take up to **7 Flaw points** | Merit points can also buy a skill dot (1), an attribute dot (3) or a specialty (1). Flaws give XP later, not points. |
| 10. Vampire template | Bloodline, Predator type, Disciplines | Humanity = Morality 7 − 1 = 6, Blood Potency 1, Generation 12. Disciplines: 1 Caitiff dot + 1 Bloodline dot; Magi Houses choose 1 Ritual; no Bloodline means 2 Caitiff dots. Merit points (3 each) can buy +1 Blood Potency, +1 dot in an owned Discipline, +1 Ritual, or 1 lower Generation (Merit points only). No Discipline above Blood Potency. The **player picks** the starting Caitiff Discipline, and the Storyteller can change it on approval (decided 2026-10-24; the book says to roll it, and NPCs stay GM-picked). |
| 11. Ghoul template | Domitor, Vitae | Starts with Vitae per `GhoulRules`. The domitor is picked from the campaign's vampires (NPC or PC) or typed as a name; the Storyteller can link a typed name later (decided 2026-10-24). |
| 12. Review and submit | Summary with a **budget panel**: every pool spent or left over | Over-budget items are warnings, not blocks. Submitting sends the character to the Storyteller. |

**Rules engine:** add `CreationRules` to `Requiem.Shared/Rules`, a pure function from a draft sheet to a budget report (spent / allowed per pool, plus warnings). The wizard and the approval screen show the same report, so player and Storyteller see identical numbers. This needs thorough tests, since it is the core of the feature.

**Approval:** the Storyteller sees submitted characters in a queue, with the budget report and any warnings. They can **approve** (the character becomes Active), **return with a note** (back to Draft), or **edit and approve**. Once Active, stats only change through XP (§6); story fields stay editable.

---

## 6. Play and advancement for players

- **Player play view:** reuse the Phase 1 play view for the player's own character: wounds, Willpower and Vitae boxes, conditions, compels and dice pools. The player can change their own trackers; the Storyteller can too.
- **XP awards:** the Storyteller awards XP to one character or to everyone present, with a reason. Awards go into the existing XP ledger.
- **XP spends:** the player picks a trait and the target dots, and the app prices it:
  - attribute ×5, skill ×3, specialty 5, merit ×5 (WoD Core p.75);
  - Blood Potency ×5, Bloodline Discipline ×5, Caitiff Discipline ×6, Ritual 10 (VtR p.137).

  **No approval needed** (decided 2026-10-24): the spend applies at once if the rules allow it (enough XP, unlocked, within maximums, no attribute raised from 1). Every spend goes into the audit log, where the Storyteller sees it and can undo it.
- **Spending locks:**
  - **Vampires:** spending stays locked until **4 Corruption** points from 4 different vampiric acts (VtR p.137). Already tracked.
  - **Mortals and ghouls:** spending stays locked until **4 Links** (WoD Core p.75). A player starts a scene with other PCs and chooses Camaraderie, Support or Cede Spotlight; afterwards that player gains 1 Link. Completing 4 Links unlocks XP and is worth +1 XP; spending XP resets Links to 0. **The player logs each Link** (kind, which PCs), and it counts at once, recorded in the audit log (decided 2026-10-24).
- **Scripts:** **the player marks their own Scripts** (decided 2026-10-24). Marking all 4 adds **+4 XP** for vampires or +1 for mortals and ghouls, then clears the marks (house ruling 6). Marks and the XP go into the audit log, where the Storyteller can undo them.
- **Other XP awards** the Storyteller gives per session (WoD Core p.75): suffering a Flaw (its point value), going along with a Social Compel, a botched roll (+1), and completing a Milestone.
- **Milestones:** Opportunity → Risk → Choice → Benefit. The Storyteller confirms each stage. A completed 10-XP Milestone can buy 1 Humanity.

---

## 7. Storyteller tools

- **Campaign dashboard:** members, outstanding invites, PCs (with the approval queue for new characters and recent XP spends), the campaign's NPCs and scenes.
- **Invites:** create a link (single or multi-use, with an expiry), copy or email it, revoke it. Players joining through a link create an account or sign in. Invite links are the only way into a campaign; the Storyteller doesn't create player accounts (decided 2026-10-24).
- **NPC visibility:** each NPC has a "Known to players" switch and a public description. Players see known NPCs as cards. Revealing an NPC never reveals the sheet.
- **Scenes with PCs:** scenes can include PCs; the Storyteller sees PCs' trackers next to NPCs'.
- **Moving characters:** move an NPC from one campaign you tell into another. Sire and domitor links don't cross campaigns, so the move cuts them.

---

## 8. Audit log and live sync

- **Audit log** (with 2.3, because XP spends need no approval): every XP award and spend, approval and play action is recorded with who and when. The Storyteller can view it per character or per campaign, and undo an XP spend.
- **SignalR live sync** (Phase 2b; Azure SignalR Service when hosted): when anyone changes a character's trackers, every open view of that character updates. This matters most for scene cards on the Storyteller's tablet while players tap on their phones.
- **Conflict handling:** play actions are already small deltas (Phase 1), so they don't collide. Sheet edits keep the existing version check.

---

## 9. Milestones

| Step | Delivers | Depends on |
| --- | --- | --- |
| **2.0** | Azure hosting; email; per-campaign authorization (with security tests); data model and migrations | — |
| **2.1** | Campaigns, invites and joining; roster and scenes scoped to a campaign; moving NPCs in | 2.0 |
| **2.2** | `CreationRules` engine and tests; seed Concepts; player creation wizard; approval queue | 2.1 |
| **2.3** | Player play view; XP awards; priced XP spends applied at once; Corruption and Links locks; Scripts and Milestones flows; audit log with undo for spends | 2.2 |
| **2.4** | NPC visibility and public cards; PCs in scenes | 2.1 |
| **2.5** | SignalR live sync; undo for other audited changes | 2.3 |

**Progress on 2.0 (2026-10-24):**
- **Data model and migrations: done.** `AddCampaigns` adds campaigns, members, invites, the audit log and Links, plus the new character, scene and XP-ledger fields. Existing characters upgrade as Active, Hidden and unowned (tested).
- **Per-campaign authorization: done for the existing API.** `CampaignAccess` limits every character and scene request to the campaigns the user tells. Anything else reads as "not found". Characters, scenes and sires must all be in the same campaign. Covered by `AccessTests`.
- **Hosting: moved to AWS serverless on 2026-10-25 ([SERVERLESS.md](SERVERLESS.md), [DEPLOY.md](DEPLOY.md)).** The Azure notes below are history; that deployment is being shut down. **Azure hosting: live (2026-10-24)** in resource group `rg-requiem`, West US 3, as container app `ca-requiem-octvib`. Pushes to `main` test and deploy. The first account (now the site admin) is created.
  - `infra/main.bicep`: West US 3, free tier. **Azure Container Apps**, since the subscription allows no App Service VMs. Passwordless SQL, Key Vault and Application Insights.
  - The GitHub Actions workflow publishes the container to ghcr.io and rolls it out.
  - The app migrates and seeds on start when `Database:MigrateOnStartup` is set, keeps its sign-in keys in the database, and has a `/healthz` endpoint.
  - DEPLOY.md has the steps.
- **Email: not started.** Azure Communication Services goes into the template with the password-reset work.

**Progress on 2.1 (2026-10-24): done.**
- **Campaigns.** Anyone signed in starts a campaign and becomes its Storyteller. Each campaign has a page with tabs:
  - Overview: the shared Coterie Script, which any member can edit (audited). Storytellers also get settings (name, description, Chronicle Tenet, allowed templates), archive and delete. Only an empty campaign can be deleted.
  - Members: Storytellers remove players; anyone can leave, except the last Storyteller.
  - Invites, NPCs and Scenes (Storytellers only).
- **Invite links.**
  - Storytellers make Player links. Each lasts 1–90 days and allows 1–50 uses, or any number.
  - The full link is shown once, with a copy button. Only a hash is stored. Links can be revoked.
  - `/join/{token}` previews the campaign. New players make an account and join in one step; signed-in users accept. Expired, used-up and revoked links explain themselves.
- **Scoping.** The NPC tab is the campaign's roster (search, kind filter, archived). New Supporting Cast, full sheets and scenes are created inside a campaign. NPCs move between campaigns the user tells (audited, cutting sire and domitor links); player characters can't be moved.
- **No account types** (decision 12). The GameMaster role, the personal roster and the global NPC and Scene pages are gone:
  - Open sign-up at `/register`, rate-limited. The first account still comes from `/setup` and becomes the site admin.
  - Everyone lands on Campaigns. The drawer shows Campaigns and Powers (read-only unless admin).
  - At startup, `AccountsUpgrade` renames the GameMaster role to Admin and moves loose characters and scenes into an "Imported NPCs" campaign per owner (tested).
- Covered by `CampaignApiTests`, `AccessTests`, `SignUpTests` and `AccountsUpgradeTests`. Checked in the browser at tablet and phone sizes.

**Progress on 2.2 (2026-10-24): done.**
- **`CreationRules`** (Shared): a draft sheet in, a budget report out.
  - Pools: Attributes 5/4/3, Skills 11/7/5, 3 Specialties, 10 Merit points, up to 7 points of Flaws. The groups' priorities are inferred (the cheapest fit), so the player never states them.
  - Merit points also pay for extra Attribute dots (3), Skill dots and Specialties (1), and for vampires Blood Potency, Discipline dots, Rituals and Generation (3 each).
  - Vampire template: a Bloodline vampire gets a Caitiff dot plus their Bloodline's dot; a Magi House a Caitiff dot plus 1 Ritual; Caitiff 2 Caitiff dots. Humanity 6 within Generation's limit. Ghouls need a domitor.
  - Levels: Missing (blocks submitting), Warning (over budget or outside the book; the Storyteller decides), Note. 21 tests.
- **Concepts** seeded from WoD Core p.55–57 (31, each with its Willpower trigger); `GET /api/catalog/concepts`.
- **Wizard** (`/campaigns/{id}/characters/new`, then `/characters/{id}`): Template, Concept, Virtue & Vice, Relationships, Scripts, Milestones, Attributes, Skills, Merits & Flaws, the Blood (vampire or ghoul), Review.
  - Each step shows its own issues; the budget panel sits beside it on tablets and below on phones.
  - Willpower, Humanity, Vitae and Morality are filled in automatically. Picking a Bloodline adds its Discipline.
  - The draft saves when the player changes step (once it has a name) and reopens at its first incomplete step.
- **Player characters API** (`/api/campaigns/{id}/player-characters`, `/api/player-characters/{id}`): create, save, delete a draft, submit, withdraw; the Storyteller approves, returns with a note, retires or marks dead. One character in play per player per campaign. Players can't set links to other characters, XP or the Storyteller's notes, and never see those notes. Once Active, a player edits only its story; stats wait for XP (2.3).
- **Campaign page:** a Characters tab. Players start their character there; Storytellers see every PC, with how many wait for approval. The NPC roster no longer lists PCs.
- **At the table:** a player opens the play view for their own approved character (wounds, Willpower, Vitae and so on); Storytellers keep the full sheet editor for any PC, including linking a ghoul's domitor.
- Covered by `CreationRulesTests` and `PlayerCharacterApiTests`. Checked in the browser at tablet and phone sizes.

**Progress on 2.3 (2026-10-24): Corruption, Links, Scripts and XP done; Milestone flows still to come.**
- **`AdvancementRules`** (Shared): prices a purchase and lists the rules it breaks, applies it, and reverts it for undo.
  - Prices: Attribute new dots × 5, Skill × 3, Specialty 5, Merit × 5 (a new one at the rating chosen); Blood Potency × 5, own Bloodline Discipline × 5, Caitiff or diablerized × 6, Ritual 10.
  - Refused: locked spending, not enough XP, maximums, Attributes at 1, a second Specialty on a Skill, Discipline dots above Blood Potency, new Disciplines, other houses' Rituals and Sabbat Ritae, Flaws and plot-only Merits. Investiture dots name the power they buy. Raising Blood Potency lowers Humanity when Generation demands it. 16 tests.
- **Advancement tab** on a character in play, for its player and the Storyteller:
  - XP available, earned and spent.
  - Corruption (vampires): the player logs vampiric acts from the book's list or their own; 4 different ones unlock spending. Links (mortals and ghouls): a scene with other PCs in play (Camaraderie, Support, Cede Spotlight); the 4th earns 1 XP and unlocks spending. Players now see the other PCs in play by name for this.
  - Scripts: tick each as it's played out; all four earn 4 XP (vampires) or 1 and clear.
  - Spend XP: pick what to buy, see the price and any rule it breaks, buy. "Done spending" resets Corruption or Links to 0; so does the Storyteller's next award once the player has spent (decision 16).
  - History from the audit log. The Storyteller can undo a spend (the trait and XP go back, unless it's changed since), an award, a Corruption act or a Link.
- **Awards:** the Storyteller awards XP from a character's tab, or to everyone in play from the campaign's Characters tab, with a reason (Flaw, Social Compel, botched roll, Milestone, or their own).
- **Not yet:** Milestone stages confirmed by the Storyteller, and buying Humanity with a completed 10-XP Milestone.
- Covered by `AdvancementRulesTests` and `AdvancementApiTests`. Checked in the browser at tablet and phone sizes.

Each step ships on its own, with tests, and is checked at tablet and phone sizes. **Security tests** (a player can't read another campaign, GM notes or a hidden NPC) are written in 2.0 and extended at every step.

---

## 10. Decisions

**Decided (2026-10-24):**
1. **Joining:** invite links are the only way into a campaign. (Accounts themselves are open sign-up since decision 12.)
2. **Templates:** each campaign sets which templates players may build.
3. **Starting Caitiff Discipline for PCs:** the player picks it; the Storyteller can change it on approval.
4. **XP spends:** no approval. Spends apply at once within the rules, and the audit log lets the Storyteller review and undo them.

5. **Skills at 4 dots at creation:** allowed, but flagged in the budget panel and the approval queue.
6. **Player visibility:** players see other PCs as public cards only, never their full sheets.
7. **Coterie Script:** one shared text on the campaign, editable by any member and audited; each player marks it on their own sheet.
8. **Ghoul PCs:** the domitor is a linked campaign vampire or a typed name the Storyteller can link later.
9. **Links:** players log their own Links (WoD Core p.75); they count at once and are audited.
10. **Email:** Azure Communication Services Email.
11. **Scripts:** players mark their own Scripts and get the XP; audited, and the Storyteller can undo.
12. **No account types:** anyone can sign up and start a campaign; Storyteller or player is a role per campaign, so one person can tell one campaign and play in another. Every character and scene lives in a campaign (the old personal roster moved into one). Only the site admin edits the shared catalog.
13. **Discipline limit:** Blood Potency limits the dots in one Discipline, not how many Disciplines a vampire has. A vampire always has their Caitiff Discipline and, in a Bloodline, its Discipline; others come only through diablerie. The free dots a Discipline gets at creation are exempt from the limit, so a Caitiff's 2 Caitiff dots are fine at Blood Potency 1; a third dot still needs Blood Potency 3.
14. **Mortal and ghoul Scripts:** a Quirk, a Personal Script, the shared Coterie Script and the Chronicle Tenet.
15. **One character in play:** a player has one Draft, Submitted or Active character per campaign at a time; retired and dead ones stay listed.
16. **Corruption:** the player logs vampiric acts (audited; the Storyteller can undo). 4 different acts unlock spending XP and earn no XP themselves. Corruption, like Links, resets after a spending session: when the player says they're done, or at the Storyteller's next award once they've spent.

A pattern runs through these: **players act, the Storyteller oversees.** Player actions take effect at once within the rules, and the audit log with undo is the check. That makes the audit log part of 2.3, not an extra.
