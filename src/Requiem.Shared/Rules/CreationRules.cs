using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;

namespace Requiem.Shared.Rules;

/// <summary>The creation wizard's steps, in the book's order (WoD Core p.55, then VtR p.136).</summary>
public enum CreationStep
{
    Template, Concept, VirtueAndVice, Relationships, Profile, Scripts, Milestones, Attributes, Skills, Merits, Blood, Review,
}

public enum CreationIssueLevel
{
    /// <summary>Something the character still needs; it can't be submitted until it's there.</summary>
    Missing,
    /// <summary>Outside the creation rules. Allowed, but the Storyteller sees it when approving.</summary>
    Warning,
    /// <summary>Worth knowing, e.g. dots left unspent.</summary>
    Note,
}

public sealed record CreationIssue(CreationStep Step, CreationIssueLevel Level, string Message);

/// <param name="Allowed">Null for a pool with no fixed allowance, shown for information.</param>
public sealed record BudgetLine(CreationStep Step, string Name, int Spent, int? Allowed, string? Detail = null)
{
    public bool IsOver => Spent > Allowed;
}

/// <summary>One thing bought with Merit points at creation.</summary>
public sealed record MeritPointCost(string Item, int Points);

/// <summary>
/// The budget panel: every creation pool, spent against its allowance, and what still needs doing. The wizard
/// and the Storyteller's approval screen show the same report, so both see identical numbers (PHASE2.md §5).
/// </summary>
public sealed record CreationReport(
    IReadOnlyList<BudgetLine> Budget,
    IReadOnlyList<CreationIssue> Issues,
    IReadOnlyList<MeritPointCost> MeritPoints,
    IReadOnlyDictionary<TraitCategory, int> AttributePriorities,
    IReadOnlyDictionary<TraitCategory, int> SkillPriorities)
{
    /// <summary>Nothing is missing, so the character can be submitted. Warnings don't block it.</summary>
    public bool IsComplete => Issues.All(i => i.Level != CreationIssueLevel.Missing);

    public bool IsWithinBudget => Budget.All(b => !b.IsOver);

    public int MeritPointsSpent => MeritPoints.Sum(m => m.Points);

    public IEnumerable<CreationIssue> For(CreationStep step) => Issues.Where(i => i.Step == step);
}

/// <summary>The catalog entries the creation and advancement rules need to know about.</summary>
/// <param name="PowerGroupOfPower">Catalog power id to its power group id (for rituals).</param>
/// <param name="PowerLevels">Catalog power id to its dot level (null for rituals), for pick-per-dot Disciplines.</param>
public sealed record CreationCatalog(
    IReadOnlyList<BloodlineOption> Bloodlines,
    IReadOnlyList<DisciplineOption> PowerGroups,
    IReadOnlyDictionary<int, int> PowerGroupOfPower,
    IReadOnlyList<MeritOption> Merits,
    IReadOnlyDictionary<int, int?>? PowerLevels = null);

/// <summary>
/// Starting dots for a player character (WoD Core p.74, VtR p.136). Pure: a draft sheet in, a budget report out.
/// <list type="bullet">
/// <item>Attributes: 5/4/3 extra dots across the three groups (every attribute starts at 1); Skills 11/7/5;
/// 3 Specialties. The groups' priorities are whichever order costs fewest Merit points, so the player never
/// has to say which group came first.</item>
/// <item>10 Merit points: Merits at their dots; 1 per extra Skill dot or Specialty; 3 per extra Attribute dot.
/// Vampires also spend 3 per Blood Potency dot, Discipline dot, Ritual or Generation below 12.</item>
/// <item>Up to 7 points of Flaws. Morality 7 (Humanity 6 for vampires), Willpower Wits + Resolve.</item>
/// </list>
/// Over-budget items are warnings the Storyteller can accept; only missing pieces block a submission.
/// </summary>
public static class CreationRules
{
    public static readonly IReadOnlyList<int> AttributeBudgets = [5, 4, 3];
    public static readonly IReadOnlyList<int> SkillBudgets = [11, 7, 5];
    public const int FreeSpecialties = 3;
    public const int MeritPoints = 10;
    public const int MaxFlawPoints = 7;
    public const int MaxStartingSkill = 3;
    /// <summary>A 4th dot needs the Storyteller's approval (WoD Core p.74), so it's flagged, not refused.</summary>
    public const int MaxFlaggedSkill = 4;
    public const int MeritPointsPerAttributeDot = 3;
    public const int MeritPointsPerSkillDot = 1;
    public const int MeritPointsPerSpecialty = 1;
    /// <summary>Blood Potency, a Discipline dot, a Ritual, or one Generation lower (VtR p.136).</summary>
    public const int MeritPointsPerVampireDot = 3;

    private static readonly TraitCategory[] Categories = [TraitCategory.Physical, TraitCategory.Social, TraitCategory.Mental];

    public static CreationReport Evaluate(FullSheetForm sheet, CreationCatalog catalog, CharacterTemplates allowed = CharacterTemplates.All)
    {
        var budget = new List<BudgetLine>();
        var issues = new List<CreationIssue>();
        var points = new List<MeritPointCost>();

        CheckTemplate(sheet, allowed, issues);
        CheckStory(sheet, issues);

        var attributePriorities = CheckRanked(
            CreationStep.Attributes, "Attribute", AttributeBudgets, MeritPointsPerAttributeDot,
            Enum.GetValues<CharacterAttribute>().ToLookup(Traits.CategoryOf, a => sheet.Attribute(a) - Traits.MinAttribute),
            budget, issues, points);
        var skillPriorities = CheckRanked(
            CreationStep.Skills, "Skill", SkillBudgets, MeritPointsPerSkillDot,
            Enum.GetValues<Skill>().ToLookup(Traits.CategoryOf, s => sheet.SkillDots(s) - Traits.MinSkill),
            budget, issues, points);
        CheckSkillCaps(sheet, issues);
        CheckSpecialties(sheet, budget, issues, points);
        CheckMeritsAndFlaws(sheet, catalog, budget, issues, points);
        CheckDerived(sheet, issues);

        if (sheet.IsVampire) CheckVampire(sheet, catalog, issues, points);
        else if (sheet.IsGhoul) CheckGhoul(sheet, issues);

        var spent = points.Sum(p => p.Points);
        budget.Add(new BudgetLine(CreationStep.Merits, "Merit points", spent, MeritPoints,
            points.Count == 0 ? null : string.Join(", ", points.Select(p => $"{p.Item} {p.Points}"))));
        if (spent > MeritPoints)
            issues.Add(new(CreationStep.Merits, CreationIssueLevel.Warning, $"{spent} Merit points spent; the limit is {MeritPoints}."));
        else if (spent < MeritPoints)
            issues.Add(new(CreationStep.Merits, CreationIssueLevel.Note, $"{MeritPoints - spent} Merit points left to spend."));

        return new CreationReport(budget, issues, points, attributePriorities, skillPriorities);
    }

    // ---------- Template and story ----------

    private static void CheckTemplate(FullSheetForm sheet, CharacterTemplates allowed, List<CreationIssue> issues)
    {
        var template = sheet.Kind switch
        {
            CharacterKind.Vampire => CharacterTemplates.Vampire,
            CharacterKind.Ghoul => CharacterTemplates.Ghoul,
            _ => CharacterTemplates.Mortal,
        };
        if (!allowed.HasFlag(template))
            issues.Add(new(CreationStep.Template, CreationIssueLevel.Missing, $"This campaign doesn't allow {sheet.Kind.ToString().ToLowerInvariant()} characters."));
    }

    private static void CheckStory(FullSheetForm sheet, List<CreationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(sheet.Name))
            issues.Add(new(CreationStep.Concept, CreationIssueLevel.Missing, "Give your character a name."));
        if (string.IsNullOrWhiteSpace(sheet.Concept))
            issues.Add(new(CreationStep.Concept, CreationIssueLevel.Missing, "Choose a Concept, or write your own."));

        if (string.IsNullOrWhiteSpace(sheet.Virtue))
            issues.Add(new(CreationStep.VirtueAndVice, CreationIssueLevel.Missing, "Choose a Virtue."));
        if (string.IsNullOrWhiteSpace(sheet.Vice))
            issues.Add(new(CreationStep.VirtueAndVice, CreationIssueLevel.Missing, "Choose a Vice."));

        foreach (var (role, text) in new[]
                 {
                     (RelationshipRole.Anchor, "Name someone who knows you for who you are."),
                     (RelationshipRole.Complication, "Name someone who complicates your life."),
                     (RelationshipRole.Connection, "Name someone who connects you to the chronicle."),
                 })
            if (!sheet.Relationships.Any(r => r.Role == role && !string.IsNullOrWhiteSpace(r.Name)))
                issues.Add(new(CreationStep.Relationships, CreationIssueLevel.Missing, text));

        foreach (var script in sheet.Scripts.Where(s => s.Kind is not (ScriptKind.Coterie or ScriptKind.ChronicleTenet)))
            if (string.IsNullOrWhiteSpace(script.Text))
                issues.Add(new(CreationStep.Scripts, CreationIssueLevel.Missing, $"Write your {ScriptName(script.Kind)} Script."));
        foreach (var script in sheet.Scripts.Where(s => s.Kind is ScriptKind.Coterie or ScriptKind.ChronicleTenet))
            if (string.IsNullOrWhiteSpace(script.Text))
                issues.Add(new(CreationStep.Scripts, CreationIssueLevel.Note,
                    $"The {ScriptName(script.Kind)} comes from the campaign and isn't set yet."));

        if (!sheet.Milestones.Any(m => !string.IsNullOrWhiteSpace(m.Title)))
            issues.Add(new(CreationStep.Milestones, CreationIssueLevel.Missing, "Write at least one Milestone."));
    }

    public static string ScriptName(ScriptKind kind) => kind switch
    {
        ScriptKind.Humanity => "Humanity",
        ScriptKind.Beast => "Beast",
        ScriptKind.Coterie => "Coterie Script",
        ScriptKind.ChronicleTenet => "Chronicle Tenet",
        ScriptKind.Quirk => "Quirk",
        ScriptKind.Personal => "Personal",
        _ => "Player character",
    };

    // ---------- Attributes and Skills ----------

    /// <summary>
    /// Fits the three groups' extra dots to the ranked budgets (5/4/3 or 11/7/5) in the order that leaves the
    /// fewest dots over budget, charging those to Merit points.
    /// </summary>
    private static IReadOnlyDictionary<TraitCategory, int> CheckRanked(
        CreationStep step, string trait, IReadOnlyList<int> budgets, int pointsPerDot, ILookup<TraitCategory, int> extraDots,
        List<BudgetLine> budget, List<CreationIssue> issues, List<MeritPointCost> points)
    {
        var spent = Categories.ToDictionary(c => c, c => extraDots[c].Sum());
        var best = Permutations(budgets)
            .Select(order => Categories.Select((c, i) => (Category: c, Allowed: order[i])).ToDictionary(x => x.Category, x => x.Allowed))
            .OrderBy(assignment => Categories.Sum(c => Math.Max(0, spent[c] - assignment[c])))
            // Ties: keep the larger budgets on the groups with more dots, which reads most naturally.
            .ThenByDescending(assignment => Categories.Sum(c => assignment[c] * spent[c]))
            .First();

        foreach (var category in Categories.OrderByDescending(c => best[c]))
        {
            budget.Add(new BudgetLine(step, $"{category} {trait.ToLowerInvariant()}s", spent[category], best[category]));
            var over = spent[category] - best[category];
            if (over > 0)
                points.Add(new($"{over} {category} {trait.ToLowerInvariant()} dot{(over == 1 ? "" : "s")}", over * pointsPerDot));
        }

        var unspent = Categories.Sum(c => Math.Max(0, best[c] - spent[c]));
        if (unspent > 0)
            issues.Add(new(step, CreationIssueLevel.Note, $"{unspent} {trait.ToLowerInvariant()} dot{(unspent == 1 ? "" : "s")} left to place."));
        return best;
    }

    private static IEnumerable<int[]> Permutations(IReadOnlyList<int> values)
    {
        if (values.Count <= 1)
        {
            yield return values.ToArray();
            yield break;
        }
        for (var i = 0; i < values.Count; i++)
        {
            var rest = values.Where((_, j) => j != i).ToList();
            foreach (var tail in Permutations(rest))
                yield return [values[i], .. tail];
        }
    }

    private static void CheckSkillCaps(FullSheetForm sheet, List<CreationIssue> issues)
    {
        foreach (var skill in Enum.GetValues<Skill>())
        {
            var dots = sheet.SkillDots(skill);
            if (dots > MaxFlaggedSkill)
                issues.Add(new(CreationStep.Skills, CreationIssueLevel.Warning, $"{skill} has {dots} dots; characters start with at most {MaxFlaggedSkill}."));
            else if (dots > MaxStartingSkill)
                issues.Add(new(CreationStep.Skills, CreationIssueLevel.Warning, $"{skill} has {dots} dots: a 4th dot needs the Storyteller's approval."));
        }
    }

    private static void CheckSpecialties(FullSheetForm sheet, List<BudgetLine> budget, List<CreationIssue> issues, List<MeritPointCost> points)
    {
        var count = sheet.Specialties.Count;
        budget.Add(new BudgetLine(CreationStep.Skills, "Specialties", count, FreeSpecialties));
        if (count > FreeSpecialties)
            points.Add(new($"{count - FreeSpecialties} extra Specialt{(count - FreeSpecialties == 1 ? "y" : "ies")}", (count - FreeSpecialties) * MeritPointsPerSpecialty));
        else if (count < FreeSpecialties)
            issues.Add(new(CreationStep.Skills, CreationIssueLevel.Note, $"{FreeSpecialties - count} Specialt{(FreeSpecialties - count == 1 ? "y" : "ies")} left to choose."));
    }

    // ---------- Merits and Flaws ----------

    private static void CheckMeritsAndFlaws(
        FullSheetForm sheet, CreationCatalog catalog, List<BudgetLine> budget, List<CreationIssue> issues, List<MeritPointCost> points)
    {
        foreach (var merit in sheet.Merits.Where(m => m.Kind == MeritKind.Merit && m.Dots > 0))
            points.Add(new(merit.Qualifier is { Length: > 0 } q ? $"{merit.Name} ({q})" : merit.Name, merit.Dots));

        var flawPoints = sheet.Merits.Where(m => m.Kind == MeritKind.Flaw).Sum(m => m.Dots);
        budget.Add(new BudgetLine(CreationStep.Merits, "Flaw points", flawPoints, MaxFlawPoints, "Flaws earn XP in play, not points"));
        if (flawPoints > MaxFlawPoints)
            issues.Add(new(CreationStep.Merits, CreationIssueLevel.Warning, $"{flawPoints} points of Flaws; the most at creation is {MaxFlawPoints}."));

        foreach (var entry in sheet.Merits.Where(m => m.MeritDefinitionId is not null))
        {
            var option = catalog.Merits.FirstOrDefault(o => o.Id == entry.MeritDefinitionId);
            if (option is null) continue;
            if ((option.MinDots is { } min && entry.Dots < min) || (option.MaxDots is { } max && entry.Dots > max))
                issues.Add(new(CreationStep.Merits, CreationIssueLevel.Warning, $"{option.Name} is rated {option.Cost}, not {entry.Dots}."));
            if (option.Cost?.Contains("plot", StringComparison.OrdinalIgnoreCase) == true)
                issues.Add(new(CreationStep.Merits, CreationIssueLevel.Warning, $"{option.Name} is awarded through play, not bought at creation."));
            if (option.Source == RuleSource.Vampire && !sheet.IsVampire)
                issues.Add(new(CreationStep.Merits, CreationIssueLevel.Warning, $"{option.Name} is a vampire {option.Kind.ToString().ToLowerInvariant()}."));
        }
    }

    private static void CheckDerived(FullSheetForm sheet, List<CreationIssue> issues)
    {
        var willpower = DerivedStats.StartingWillpower(sheet.Attribute(CharacterAttribute.Wits), sheet.Attribute(CharacterAttribute.Resolve));
        if (sheet.WillpowerDots != willpower)
            issues.Add(new(CreationStep.Attributes, CreationIssueLevel.Warning, $"Willpower starts at Wits + Resolve ({willpower}), not {sheet.WillpowerDots}."));
        if (!sheet.IsVampire && sheet.Morality != DerivedStats.StartingMorality)
            issues.Add(new(CreationStep.Merits, CreationIssueLevel.Warning, $"Morality starts at {DerivedStats.StartingMorality} unless a Flaw says otherwise."));
    }

    // ---------- Vampire and ghoul templates ----------

    private static void CheckVampire(FullSheetForm sheet, CreationCatalog catalog, List<CreationIssue> issues, List<MeritPointCost> points)
    {
        var bloodline = catalog.Bloodlines.FirstOrDefault(b => b.Id == sheet.BloodlineId);
        if (bloodline is null)
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Missing, "Choose a Bloodline, Magi House or Caitiff."));
        if (sheet.PredatorTypeId is null)
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Missing, "Choose a Predator type."));

        // Blood Potency and Generation.
        var bloodPotency = sheet.BloodPotency ?? BloodRules.MinBloodPotency;
        if (bloodPotency > BloodRules.MinBloodPotency)
            points.Add(new($"Blood Potency {bloodPotency}", (bloodPotency - BloodRules.MinBloodPotency) * MeritPointsPerVampireDot));

        var generation = sheet.Generation ?? GenerationRules.StartingGeneration;
        if (sheet.IsThinblood)
        {
            if (generation != GenerationRules.ThinbloodGeneration)
                issues.Add(new(CreationStep.Blood, CreationIssueLevel.Warning, $"Thin-blooded vampires start at Generation {GenerationRules.ThinbloodGeneration}."));
        }
        else if (generation < GenerationRules.StartingGeneration)
            points.Add(new($"Generation {generation}", (GenerationRules.StartingGeneration - generation) * MeritPointsPerVampireDot));
        else if (generation > GenerationRules.StartingGeneration)
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Warning, $"Vampires start at Generation {GenerationRules.StartingGeneration} (13 only with the Thinblood Flaw)."));

        var humanity = HumanityRules.ClampToGeneration(DerivedStats.StartingHumanity(), bloodPotency, Math.Clamp(generation, 1, GenerationRules.ThinbloodGeneration));
        if (sheet.Humanity != humanity)
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Warning, humanity == DerivedStats.StartingHumanity()
                ? $"Humanity starts at {humanity} (Morality 7, less the dot the Embrace burns)."
                : $"Humanity starts at {humanity}: Humanity + Blood Potency can't exceed Generation {generation}'s strength."));

        CheckDisciplines(sheet, catalog, bloodline, bloodPotency, issues, points);
        CheckRituals(sheet, catalog, bloodline, issues, points);
    }

    private static void CheckDisciplines(
        FullSheetForm sheet, CreationCatalog catalog, BloodlineOption? bloodline, int bloodPotency,
        List<CreationIssue> issues, List<MeritPointCost> points)
    {
        var owned = sheet.Disciplines.Where(d => d.Dots > 0)
            .Select(d => (Entry: d, Group: catalog.PowerGroups.FirstOrDefault(g => g.Id == d.PowerGroupId)))
            .Where(d => d.Group is not null).ToList();
        var caitiff = owned.Where(d => d.Group!.Kind == PowerGroupKind.CaitiffDiscipline).ToList();
        var bloodlineDisciplines = owned.Where(d => d.Group!.Kind == PowerGroupKind.BloodlineDiscipline).ToList();
        var ownDiscipline = bloodline is null ? null
            : catalog.PowerGroups.FirstOrDefault(g => g.Kind == PowerGroupKind.BloodlineDiscipline && g.BloodlineId == bloodline.Id);

        if (caitiff.Count == 0)
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Missing, "Choose your starting Caitiff Discipline."));
        else if (caitiff.Count > 1)
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Warning, "Only one Caitiff Discipline at creation; others come only through diablerie."));

        if (ownDiscipline is not null && bloodlineDisciplines.All(d => d.Group!.Id != ownDiscipline.Id))
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Missing, $"Add your Bloodline's Discipline, {ownDiscipline.Name}."));
        foreach (var other in bloodlineDisciplines.Where(d => d.Group!.Id != ownDiscipline?.Id))
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Warning, $"{other.Group!.Name} belongs to another Bloodline; it comes only through diablerie."));

        // The free dots: a Caitiff dot plus the Bloodline's dot, or 2 Caitiff dots with no Bloodline
        // (a Magi House's free pick is a Ritual instead).
        var free = bloodline?.Kind switch
        {
            "Bloodline" => 2,
            "MagiHouse" => 1,
            _ => 2,
        };
        var total = owned.Sum(d => d.Entry.Dots);
        if (total > free)
            points.Add(new($"{total - free} Discipline dot{(total - free == 1 ? "" : "s")}", (total - free) * MeritPointsPerVampireDot));
        else if (total < free && caitiff.Count > 0)
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Note, $"{free - total} free Discipline dot{(free - total == 1 ? "" : "s")} left to place."));

        foreach (var violation in PowerRules.Validate(bloodPotency, owned.Select(d =>
                     new DisciplineRating(d.Group!.Name, d.Entry.Dots, StartingDots(d.Group, bloodline?.Kind)))))
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Warning, violation.Message));
    }

    /// <summary>
    /// The free dots a Discipline gets at creation, which Blood Potency doesn't limit (house ruling 2026-10-24):
    /// 2 in a Caitiff's Caitiff Discipline, 1 in anyone else's Caitiff or own Bloodline Discipline.
    /// </summary>
    /// <param name="bloodlineKind">"Bloodline", "MagiHouse", "Caitiff" or null for none.</param>
    public static int StartingDots(DisciplineOption discipline, string? bloodlineKind) => discipline.Kind switch
    {
        PowerGroupKind.CaitiffDiscipline => bloodlineKind is null or "Caitiff" ? 2 : 1,
        PowerGroupKind.BloodlineDiscipline => 1,
        _ => 0,
    };

    private static void CheckRituals(
        FullSheetForm sheet, CreationCatalog catalog, BloodlineOption? bloodline, List<CreationIssue> issues, List<MeritPointCost> points)
    {
        var groups = sheet.LearnedPowerIds
            .Select(id => catalog.PowerGroupOfPower.TryGetValue(id, out var groupId) ? catalog.PowerGroups.FirstOrDefault(g => g.Id == groupId) : null)
            .OfType<DisciplineOption>().ToList();
        var rituals = groups.Where(g => g.Kind == PowerGroupKind.BloodSorcery).ToList();
        var ownHouse = rituals.Count(g => g.BloodlineId is not null && g.BloodlineId == bloodline?.Id);
        var isMagi = bloodline?.Kind == "MagiHouse";

        if (rituals.Count > ownHouse)
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Warning, "Rituals from another house's sorcery have to be taught or stolen in play."));
        if (groups.Any(g => g.Kind == PowerGroupKind.SabbatRitae))
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Warning, "Sabbat Ritae are taught by Sabbat elders, not chosen at creation."));

        var free = isMagi ? 1 : 0;
        if (rituals.Count > free)
            points.Add(new($"{rituals.Count - free} Ritual{(rituals.Count - free == 1 ? "" : "s")}", (rituals.Count - free) * MeritPointsPerVampireDot));
        else if (isMagi && rituals.Count == 0)
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Missing, "Choose your house's starting Ritual."));
    }

    private static void CheckGhoul(FullSheetForm sheet, List<CreationIssue> issues)
    {
        if (sheet.DomitorId is null && string.IsNullOrWhiteSpace(sheet.DomitorName))
            issues.Add(new(CreationStep.Blood, CreationIssueLevel.Missing, "Name your domitor, the vampire whose blood you drink."));
    }
}
