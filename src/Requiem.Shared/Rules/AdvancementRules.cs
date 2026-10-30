using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;

namespace Requiem.Shared.Rules;

/// <summary>
/// One thing a player buys with XP. Raising a rated trait buys one dot at a time; a new Merit is bought at the
/// rating chosen (its new dots × 5, WoD Core p.75).
/// </summary>
public sealed class XpPurchase
{
    public PurchaseKind Kind { get; set; }
    public CharacterAttribute? Attribute { get; set; }
    /// <summary>The Skill raised, or the Skill a new Specialty is on.</summary>
    public Skill? Skill { get; set; }
    public string? SpecialtyName { get; set; }
    /// <summary>A catalog Merit (null for one the player names).</summary>
    public int? MeritDefinitionId { get; set; }
    public string? MeritName { get; set; }
    public string? MeritQualifier { get; set; }
    /// <summary>The rating a new Merit is bought at; ignored when raising one the character has.</summary>
    public int? MeritDots { get; set; }
    /// <summary>The Discipline raised.</summary>
    public int? PowerGroupId { get; set; }
    /// <summary>The Ritual learned, or the power an Investiture-style dot buys.</summary>
    public int? PowerId { get; set; }
}

/// <param name="Trait">What it buys, e.g. "Strength" or "Allies (Harbor Police)".</param>
public sealed record XpPurchaseQuote(string Trait, int FromDots, int ToDots, int Cost, IReadOnlyList<RuleViolation> Violations)
{
    public bool IsAllowed => Violations.Count == 0;

    /// <summary>"Strength 2 → 3", or just the name of a Ritual or Specialty: those are had or not.</summary>
    public static string Label(PurchaseKind kind, string trait, int fromDots, int toDots) =>
        HasNoDots(kind) ? trait : $"{trait} {fromDots} → {toDots}";

    /// <summary>Rituals and Specialties are bought 0 → 1 at a flat price, with no rating.</summary>
    public static bool HasNoDots(PurchaseKind kind) => kind is PurchaseKind.Ritual or PurchaseKind.Specialty;
}

/// <summary>
/// Spending XP on a character in play (WoD Core p.75, VtR p.137, PHASE2.md §6). A spend applies at once when
/// the rules allow it; the Storyteller can undo it from the audit log.
/// <list type="bullet">
/// <item>Spending is locked until the character has 4 Corruption (vampires, from 4 different vampiric acts)
/// or 4 Links (mortals and ghouls). It stays open for a spending session; when the player is done, the
/// count resets (house ruling 2026-10-24).</item>
/// <item>Prices: Attribute new dots × 5, Skill × 3, Specialty 5, Merit × 5; Blood Potency × 5, own Bloodline
/// Discipline × 5, Caitiff or diablerized Discipline × 6, Ritual 10.</item>
/// <item>Not for sale: Attributes at 1 (a plot event raises those), Willpower, Morality and Humanity (Milestones),
/// new Disciplines (diablerie) and Sabbat Ritae (taught by elders). The Storyteller grants those.</item>
/// </list>
/// </summary>
public static class AdvancementRules
{
    public const int MaxMeritDots = 5;

    /// <summary>The book's suggested vampiric acts for Corruption (VtR p.137).</summary>
    public static readonly IReadOnlyList<string> SuggestedCorruptionActs =
    [
        "Drank someone's blood until they died",
        "Defeated another vampire in combat or socially",
        "Committed diablerie",
        "Took sunlight damage",
        "Lost Dominion to the Beast",
        "Suffered debilitating distress at the sight of blood",
        "Lost track of a plot that mattered; its clock advanced",
        "Caused a mortal great terror or panic",
        "Created a new ghoul",
        "Used a 5-dot Discipline",
        "Took harm that would have killed a mortal",
    ];

    /// <summary>XP for completing 4 Links (WoD Core p.75). Corruption only unlocks spending.</summary>
    public const int LinksCompletionXp = 1;

    public static bool IsUnlocked(int progress) => ExperienceRules.IsXpUnlocked(progress);

    /// <summary>Corruption for vampires; Links for everyone else.</summary>
    public static string ProgressName(CharacterKind kind) => kind == CharacterKind.Vampire ? "Corruption" : "Links";

    /// <summary>Prices a purchase and lists every rule it breaks; an empty list means it can go ahead.</summary>
    /// <param name="progress">The character's Corruption or Links.</param>
    public static XpPurchaseQuote Quote(FullSheetForm sheet, XpPurchase purchase, CreationCatalog catalog, int progress)
    {
        var violations = new List<RuleViolation>();
        if (!IsUnlocked(progress))
            violations.Add(new("xp.locked", sheet.IsVampire
                ? $"Spending unlocks at {ExperienceRules.ProgressPointsToUnlockXp} Corruption from different vampiric acts ({progress} so far)."
                : $"Spending unlocks after {ExperienceRules.ProgressPointsToUnlockXp} Links ({progress} so far)."));

        var (trait, from, to, quote) = Price(sheet, purchase, catalog, violations);
        violations.AddRange(quote.Violations);
        if (quote.Cost > sheet.ExperienceAvailable)
            violations.Add(new("xp.not-enough", $"Costs {quote.Cost} XP; {sheet.ExperienceAvailable} available."));
        return new XpPurchaseQuote(trait, from, to, quote.Cost, violations);
    }

    private static (string Trait, int From, int To, XpQuote Quote) Price(
        FullSheetForm sheet, XpPurchase purchase, CreationCatalog catalog, List<RuleViolation> violations)
    {
        switch (purchase.Kind)
        {
            case PurchaseKind.Attribute when purchase.Attribute is { } attribute:
            {
                var dots = sheet.Attribute(attribute);
                return (attribute.ToString(), dots, dots + 1, ExperienceRules.Quote(PurchaseKind.Attribute, dots, dots + 1));
            }

            case PurchaseKind.Skill when purchase.Skill is { } skill:
            {
                var dots = sheet.SkillDots(skill);
                return (skill.ToString(), dots, dots + 1, ExperienceRules.Quote(PurchaseKind.Skill, dots, dots + 1));
            }

            case PurchaseKind.Specialty when purchase.Skill is { } skill:
            {
                var name = purchase.SpecialtyName?.Trim() ?? "";
                if (name.Length is 0 or > FullSheetForm.MaxTextLength)
                    violations.Add(new("specialty.name", "Name the Specialty in a word or short phrase."));
                if (sheet.SkillDots(skill) < 1)
                    violations.Add(new("specialty.no-skill", $"A Specialty needs at least 1 dot in {skill}."));
                // One Specialty per Skill (WoD Core p.80).
                if (sheet.Specialties.Any(s => s.Skill == skill))
                    violations.Add(new("specialty.one-per-skill", $"{skill} already has a Specialty; each Skill takes one."));
                // Had or not, like a Ritual: 0 → 1 at a flat 5, so a Specialty added later doesn't reprice this one.
                return ($"{skill} ({name})", 0, 1, ExperienceRules.Quote(PurchaseKind.Specialty, 0, 1));
            }

            case PurchaseKind.Merit:
                return PriceMerit(sheet, purchase, catalog, violations);

            case PurchaseKind.BloodPotency when sheet.IsVampire:
            {
                var dots = sheet.BloodPotency ?? BloodRules.MinBloodPotency;
                return ("Blood Potency", dots, dots + 1, ExperienceRules.Quote(PurchaseKind.BloodPotency, dots, dots + 1));
            }

            case PurchaseKind.BloodlineDiscipline or PurchaseKind.CaitiffDiscipline when sheet.IsVampire:
                return PriceDiscipline(sheet, purchase, catalog, violations);

            case PurchaseKind.Ritual when sheet.IsVampire:
                return PriceRitual(sheet, purchase, catalog, violations);

            default:
                var vampireOnly = purchase.Kind is PurchaseKind.BloodPotency or PurchaseKind.BloodlineDiscipline or PurchaseKind.CaitiffDiscipline or PurchaseKind.Ritual;
                violations.Add(new("xp.not-for-sale", vampireOnly && !sheet.IsVampire
                    ? "Only vampires buy Blood Potency, Disciplines and Rituals."
                    : "Choose what to buy."));
                return ("", 0, 0, new XpQuote(0, []));
        }
    }

    private static (string, int, int, XpQuote) PriceMerit(FullSheetForm sheet, XpPurchase purchase, CreationCatalog catalog, List<RuleViolation> violations)
    {
        var option = catalog.Merits.FirstOrDefault(m => m.Id == purchase.MeritDefinitionId);
        if (purchase.MeritDefinitionId is not null && option is null)
            violations.Add(new("merit.unknown", "Unknown Merit."));
        if (option?.Kind == MeritKind.Flaw)
            violations.Add(new("merit.flaw", "Flaws aren't bought; the Storyteller adds them."));
        if (option?.Cost?.Contains("plot", StringComparison.OrdinalIgnoreCase) == true)
            violations.Add(new("merit.plot-only", $"{option.Name} is awarded through play, not bought."));

        var name = MeritName(purchase, catalog);
        if (name.Length is 0 or > FullSheetForm.MaxTextLength) violations.Add(new("merit.name", "Name the Merit."));
        var qualifier = Qualifier(purchase);
        var label = qualifier is null ? name : $"{name} ({qualifier})";
        var max = option?.MaxDots ?? MaxMeritDots;

        // Raising one the character has buys the next dot; a new one is bought at the rating chosen.
        var existing = FindMerit(sheet, purchase.MeritDefinitionId, name, qualifier);
        var from = existing?.Dots ?? 0;
        var to = existing is not null ? from + 1 : purchase.MeritDots ?? option?.MinDots ?? 1;
        if (existing is null && option is { MinDots: { } min } && to < min) violations.Add(new("merit.min", $"{name} starts at {min} dots."));
        if (to > max) violations.Add(new("merit.max", $"{name} goes up to {max}."));
        return (label, from, to, ExperienceRules.Quote(PurchaseKind.Merit, from, Math.Max(from + 1, to)));
    }

    private static string MeritName(XpPurchase purchase, CreationCatalog catalog) =>
        catalog.Merits.FirstOrDefault(m => m.Id == purchase.MeritDefinitionId)?.Name ?? purchase.MeritName?.Trim() ?? "";

    private static string? Qualifier(XpPurchase purchase) =>
        string.IsNullOrWhiteSpace(purchase.MeritQualifier) ? null : purchase.MeritQualifier.Trim();

    private static (string, int, int, XpQuote) PriceDiscipline(FullSheetForm sheet, XpPurchase purchase, CreationCatalog catalog, List<RuleViolation> violations)
    {
        var group = catalog.PowerGroups.FirstOrDefault(g => g.Id == purchase.PowerGroupId);
        var entry = sheet.Disciplines.FirstOrDefault(d => d.PowerGroupId == purchase.PowerGroupId && d.Dots > 0);
        if (group is null || !group.HasDots)
        {
            violations.Add(new("discipline.unknown", "Choose one of your Disciplines."));
            return ("", 0, 0, new XpQuote(0, []));
        }
        // New Disciplines come only through diablerie; the Storyteller adds those.
        if (entry is null)
        {
            violations.Add(new("discipline.not-owned", $"{group.Name} isn't one of your Disciplines; new ones come only through diablerie."));
            return (group.Name, 0, 1, new XpQuote(0, []));
        }

        // Your own Bloodline's Discipline costs × 5; Caitiff and diablerized ones × 6 (VtR p.137).
        var kind = group.Kind == PowerGroupKind.BloodlineDiscipline && group.BloodlineId == sheet.BloodlineId
            ? PurchaseKind.BloodlineDiscipline
            : PurchaseKind.CaitiffDiscipline;
        var bloodPotency = sheet.BloodPotency ?? BloodRules.MinBloodPotency;
        var quote = ExperienceRules.Quote(kind, entry.Dots, entry.Dots + 1, bloodPotency);

        var picks = catalog.PickPerDotPowers(group.Id);
        if (picks.Count > 0)
        {
            if (purchase.PowerId is not { } powerId || !picks.Contains(powerId))
                violations.Add(new("discipline.pick", $"Each {group.Name} dot buys one of its powers; choose which."));
            else if (sheet.LearnedPowerIds.Contains(powerId))
                violations.Add(new("discipline.pick-owned", "You already have that power."));
        }
        return (group.Name, entry.Dots, entry.Dots + 1, quote);
    }

    private static (string, int, int, XpQuote) PriceRitual(FullSheetForm sheet, XpPurchase purchase, CreationCatalog catalog, List<RuleViolation> violations)
    {
        var groupId = purchase.PowerId is { } powerId && catalog.PowerGroupOfPower.TryGetValue(powerId, out var g) ? g : (int?)null;
        var group = catalog.PowerGroups.FirstOrDefault(x => x.Id == groupId);

        if (group is null || group.Kind is not (PowerGroupKind.BloodSorcery or PowerGroupKind.SabbatRitae))
            violations.Add(new("ritual.unknown", "Choose a Ritual."));
        else if (group.Kind == PowerGroupKind.SabbatRitae)
            violations.Add(new("ritual.ritae", "Sabbat Ritae are taught by Sabbat elders; the Storyteller adds them."));
        else if (!PowerRules.CanLearnRitual(group.BloodlineId, sheet.BloodlineId))
            violations.Add(new("ritual.other-house", PowerRules.RitualProblem("That Ritual", group.Name, group.BloodlineName)));
        if (purchase.PowerId is { } id && sheet.LearnedPowerIds.Contains(id))
            violations.Add(new("ritual.owned", "You already know that Ritual."));

        // Each Ritual is known or not, so it's bought 0 → 1 at a flat 10 whatever else is known (VtR p.137). Pricing
        // it as a count would let a Ritual picked at creation afterwards break one already bought with starting XP.
        var name = purchase.PowerId is { } ritual && catalog.PowerNames?.GetValueOrDefault(ritual) is { } known ? known : "Ritual";
        return (group is null ? name : $"{name} ({group.Name})", 0, 1, ExperienceRules.Quote(PurchaseKind.Ritual, 0, 1));
    }

    /// <summary>
    /// Applies an allowed purchase to the sheet: the new dots, Specialty, Merit or power. Raising Blood Potency
    /// lowers Humanity if Generation's limit demands it (VtR p.15). The caller records the XP spent.
    /// </summary>
    public static void Apply(FullSheetForm sheet, XpPurchase purchase, XpPurchaseQuote quote, CreationCatalog catalog)
    {
        switch (purchase.Kind)
        {
            case PurchaseKind.Attribute:
                sheet.Attributes[purchase.Attribute!.Value] = quote.ToDots;
                break;
            case PurchaseKind.Skill:
                sheet.Skills[purchase.Skill!.Value] = quote.ToDots;
                break;
            case PurchaseKind.Specialty:
                sheet.Specialties.Add(new SpecialtyEntry { Skill = purchase.Skill!.Value, Name = purchase.SpecialtyName!.Trim() });
                break;
            case PurchaseKind.Merit:
            {
                var name = MeritName(purchase, catalog);
                var qualifier = Qualifier(purchase);
                var existing = FindMerit(sheet, purchase.MeritDefinitionId, name, qualifier);
                if (existing is not null) existing.Dots = quote.ToDots;
                else sheet.Merits.Add(new MeritEntry
                {
                    MeritDefinitionId = purchase.MeritDefinitionId, Name = name, Kind = MeritKind.Merit, Dots = quote.ToDots, Qualifier = qualifier,
                });
                break;
            }
            case PurchaseKind.BloodPotency:
                sheet.BloodPotency = quote.ToDots;
                if (sheet.Humanity is { } humanity)
                    sheet.Humanity = HumanityRules.ClampToGeneration(humanity, quote.ToDots, sheet.Generation ?? GenerationRules.StartingGeneration);
                break;
            case PurchaseKind.BloodlineDiscipline or PurchaseKind.CaitiffDiscipline:
                sheet.Disciplines.First(d => d.PowerGroupId == purchase.PowerGroupId).Dots = quote.ToDots;
                if (purchase.PowerId is { } pick) sheet.LearnedPowerIds.Add(pick);
                break;
            case PurchaseKind.Ritual:
                sheet.LearnedPowerIds.Add(purchase.PowerId!.Value);
                break;
        }
    }

    /// <summary>
    /// Takes a purchase back off the sheet for the Storyteller's undo. Blood Potency's lost Humanity is the
    /// caller's to restore.
    /// </summary>
    /// <returns>False when the trait has changed since (e.g. bought again), so undoing would lose that.</returns>
    public static bool Revert(FullSheetForm sheet, XpPurchase purchase, XpPurchaseQuote quote, CreationCatalog catalog)
    {
        switch (purchase.Kind)
        {
            case PurchaseKind.Attribute when sheet.Attribute(purchase.Attribute!.Value) == quote.ToDots:
                sheet.Attributes[purchase.Attribute.Value] = quote.FromDots;
                return true;
            case PurchaseKind.Skill when sheet.SkillDots(purchase.Skill!.Value) == quote.ToDots:
                sheet.Skills[purchase.Skill.Value] = quote.FromDots;
                return true;
            case PurchaseKind.Specialty:
                return sheet.Specialties.RemoveAll(s => s.Skill == purchase.Skill
                    && string.Equals(s.Name, purchase.SpecialtyName?.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
            case PurchaseKind.Merit:
            {
                var merit = FindMerit(sheet, purchase.MeritDefinitionId, MeritName(purchase, catalog), Qualifier(purchase));
                if (merit is null || merit.Dots != quote.ToDots) return false;
                if (quote.FromDots == 0) sheet.Merits.Remove(merit);
                else merit.Dots = quote.FromDots;
                return true;
            }
            case PurchaseKind.BloodPotency when sheet.BloodPotency == quote.ToDots:
                sheet.BloodPotency = quote.FromDots;
                return true;
            case PurchaseKind.BloodlineDiscipline or PurchaseKind.CaitiffDiscipline:
            {
                var entry = sheet.Disciplines.FirstOrDefault(d => d.PowerGroupId == purchase.PowerGroupId);
                if (entry is null || entry.Dots != quote.ToDots) return false;
                entry.Dots = quote.FromDots;
                if (purchase.PowerId is { } pick) sheet.LearnedPowerIds.Remove(pick);
                return true;
            }
            case PurchaseKind.Ritual:
                return purchase.PowerId is { } ritual && sheet.LearnedPowerIds.Remove(ritual);
            default:
                return false;
        }
    }

    private static MeritEntry? FindMerit(FullSheetForm sheet, int? definitionId, string name, string? qualifier) =>
        sheet.Merits.FirstOrDefault(m => m.Kind == MeritKind.Merit
            && (definitionId is not null ? m.MeritDefinitionId == definitionId : string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            && string.Equals(m.Qualifier ?? "", qualifier ?? "", StringComparison.OrdinalIgnoreCase));

    /// <summary>The powers a pick-per-dot Discipline (Investiture) offers, or none for an ordinary one.</summary>
    public static IReadOnlyList<int> PickPerDotPowers(this CreationCatalog catalog, int groupId)
    {
        var group = catalog.PowerGroups.FirstOrDefault(g => g.Id == groupId);
        if (group is null || catalog.PowerLevels is null) return [];
        var powers = catalog.PowerGroupOfPower.Where(p => p.Value == groupId).Select(p => p.Key).ToList();
        var levels = powers.Select(id => catalog.PowerLevels.GetValueOrDefault(id)).ToList();
        return PowerRules.IsPickPerDot(group.Kind, levels) ? powers : [];
    }

    /// <summary>
    /// All four Scripts marked: they earn <see cref="ExperienceRules.ScriptsCompletionXp"/> (4 for vampires, 1 for
    /// mortals and ghouls, house ruling 6) and the marks clear.
    /// </summary>
    public static bool ScriptsComplete(IEnumerable<ScriptEntry> scripts)
    {
        var list = scripts.ToList();
        return list.Count == 4 && list.All(s => s.IsMarked);
    }
}
