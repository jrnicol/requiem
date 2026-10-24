# Phase 2 — Player characters and campaigns

Status: **agreed plan** (2026-10-24). All open decisions are settled; see §10.

Phase 1 is a single-GM tool: every API needs the `GameMaster` role and every character belongs to that GM. Phase 2 lets the GM run **campaigns** (chronicles) with **players**, who build their own characters in the app and play them at the table alongside the GM's NPCs.

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
| **Hosting on Azure** (Phase 1 step 8) | Players need to reach the app | App Service + Azure SQL + Key Vault, per PLAN.md §6. GitHub Actions deploy with migrations. |
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
- **Existing data:** current NPCs and scenes stay in the GM's personal space (`CampaignId` null). They can be moved into a campaign later. The existing GM becomes their owner.

---

## 4. Access rules

Authorization moves from one global role to **campaign membership**. It is checked on the server for every request, not just hidden in the UI.

| Who | Can |
| --- | --- |
| **Site GM** (today's `GameMaster` role) | Create campaigns; keep a personal NPC roster outside any campaign; manage the powers catalog. |
| **Storyteller** of a campaign | Everything in that campaign: all characters, scenes, invites, approvals and XP awards. |
| **Player** in a campaign | Their own characters (create, edit drafts, track play, spend XP); the campaign's known NPCs as public cards; the other PCs as public cards only (name, concept, bloodline, portrait, the public description their player wrote), never their full sheets (decided 2026-10-24). Any member can edit the shared Coterie Script. |
| **Anyone signed in** | Their account and the campaigns they belong to. |

Server-side shaping: the API returns different shapes per audience. A player's view of an NPC is a `PublicCharacterCard` (name, concept, bloodline, portrait, public description). It never contains the full sheet or GM notes, so nothing can leak through the browser's developer tools.

The **powers catalog** stays readable by every signed-in member, because players need it to build characters. Only the site GM can edit it.

---

## 5. Player character creation

A **step-by-step wizard** on tablet and phone, following the core book's order (WoD Core p.55) and then the vampire template (VtR p.136). Every step can be revisited. Progress saves as a **Draft**, so a player can stop and come back.

| Step | What the player does | Rules applied |
| --- | --- | --- |
| 1. Template | Mortal, Ghoul or Vampire | Only the templates the campaign allows (decided 2026-10-24). |
| 2. Concept | Pick from the book's Concepts or write one | Each Concept has a Willpower-regain trigger. **Needs seeding:** Concepts aren't in the catalog yet. |
| 3. Virtue and Vice | Pick, plus a detail line | Already modelled. |
| 4. Relationships | Three relationships | Required for PCs (WoD Core p.65). Uses the Phase 1 relationships model (step 7, not built yet). |
| 5. Scripts | Humanity and Beast; the Coterie Script and Chronicle Tenet are filled in from the campaign | The Coterie Script is one shared text the group agrees; any member can edit it, and edits are audited. Each player marks it on their own sheet. |
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
- **Invites:** create a link (single or multi-use, with an expiry), copy or email it, revoke it. Players joining through a link create an account or sign in. Invite links are the only way in; the Storyteller doesn't create player accounts (decided 2026-10-24).
- **NPC visibility:** each NPC has a "Known to players" switch and a public description. Players see known NPCs as cards. Revealing an NPC never reveals the sheet.
- **Scenes with PCs:** scenes can include PCs; the Storyteller sees PCs' trackers next to NPCs'.
- **Moving characters:** move personal NPCs into a campaign, or out again.

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

Each step ships on its own, with tests, and is checked at tablet and phone sizes. **Security tests** (a player can't read another campaign, GM notes or a hidden NPC) are written in 2.0 and extended at every step.

---

## 10. Decisions

**Decided (2026-10-24):**
1. **Joining:** invite links only.
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

A pattern runs through these: **players act, the Storyteller oversees.** Player actions take effect at once within the rules, and the audit log with undo is the check. That makes the audit log part of 2.3, not an extra.
