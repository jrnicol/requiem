using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Shared.Tests.Rules;

/// <summary>Starting dots for player characters (WoD Core p.74, VtR p.136).</summary>
public class CreationRulesTests
{
    // A tiny catalog: one Bloodline with its Discipline, a Magi House with its sorcery, Caitiff, and a merit.
    private const int Ventrue = 1, Tremere = 2, Caitiff = 3;
    private const int Auspex = 10, Furor = 11, Dominate = 12, Majesty = 13, Hekate = 14, Auctoritas = 15;
    private const int Ritual1 = 100, Ritual2 = 101, Rite = 102;
    private const int Allies = 50, Library = 51;

    private static readonly CreationCatalog Catalog = new(
        [
            new(Ventrue, "Ventrue", "Bloodline", null, null, null, null, null),
            new(Tremere, "Tremere", "MagiHouse", null, null, null, null, null),
            new(Caitiff, "Caitiff", "Caitiff", null, null, null, null, null),
        ],
        [
            new(Auspex, "Auspex", PowerGroupKind.CaitiffDiscipline, null, null, null, null),
            new(Furor, "Furor", PowerGroupKind.CaitiffDiscipline, null, null, null, null),
            new(Dominate, "Dominate", PowerGroupKind.BloodlineDiscipline, Ventrue, "Ventrue", null, null),
            new(Majesty, "Majesty", PowerGroupKind.BloodlineDiscipline, 99, "Toreador", null, null),
            new(Hekate, "Hekate", PowerGroupKind.BloodSorcery, Tremere, "Tremere", null, null),
            new(Auctoritas, "Auctoritas", PowerGroupKind.SabbatRitae, null, null, null, null),
        ],
        new Dictionary<int, int> { [Ritual1] = Hekate, [Ritual2] = Hekate, [Rite] = Auctoritas },
        [
            new(Allies, "Allies", MeritKind.Merit, RuleSource.WodCore, "1–3", 1, 3, null, null, 96),
            new(Library, "Library", MeritKind.Merit, RuleSource.WodCore, "Awarded through plot only", null, null, null, null, 98),
        ]);

    // ---------- A finished mortal ----------

    [Fact]
    public void A_mortal_built_to_the_book_is_complete_and_within_budget()
    {
        var report = Evaluate(Mortal());

        Assert.True(report.IsComplete, string.Join("; ", report.Issues.Where(i => i.Level == CreationIssueLevel.Missing).Select(i => i.Message)));
        Assert.True(report.IsWithinBudget);
        Assert.DoesNotContain(report.Issues, i => i.Level == CreationIssueLevel.Warning);
        Assert.Equal(10, report.MeritPointsSpent);
    }

    [Fact]
    public void A_blank_draft_lists_what_is_missing_step_by_step()
    {
        var report = Evaluate(new FullSheetForm { Kind = CharacterKind.Mortal, Morality = 7, Scripts = FullSheetForm.DefaultScripts(CharacterKind.Mortal) });

        Assert.False(report.IsComplete);
        foreach (var step in new[] { CreationStep.Concept, CreationStep.VirtueAndVice, CreationStep.Relationships, CreationStep.Scripts, CreationStep.Milestones })
            Assert.Contains(report.Issues, i => i.Step == step && i.Level == CreationIssueLevel.Missing);
        // Nothing is spent yet, so nothing is over budget.
        Assert.True(report.IsWithinBudget);
    }

    // ---------- Attributes and Skills ----------

    [Fact]
    public void Attribute_priorities_are_inferred_from_the_dots()
    {
        var sheet = Mortal();
        // Social gets the most dots this time: 5 Social, 4 Mental, 3 Physical.
        SetAttributes(sheet, physical: [2, 2, 2], social: [3, 2, 3], mental: [2, 3, 2]);

        var report = Evaluate(sheet);

        Assert.Equal(5, report.AttributePriorities[TraitCategory.Social]);
        Assert.Equal(4, report.AttributePriorities[TraitCategory.Mental]);
        Assert.Equal(3, report.AttributePriorities[TraitCategory.Physical]);
        Assert.DoesNotContain(report.MeritPoints, m => m.Item.Contains("attribute"));
    }

    [Fact]
    public void Extra_attribute_dots_cost_three_merit_points_each()
    {
        var sheet = Mortal();
        SetAttributes(sheet, physical: [3, 3, 2], social: [2, 2, 3], mental: [2, 2, 2]); // 5 / 4 / 3 + one extra Physical
        sheet.Attributes[CharacterAttribute.Strength] = 4;
        sheet.Merits.Clear();

        var report = Evaluate(sheet);

        var cost = Assert.Single(report.MeritPoints, m => m.Item.Contains("Physical attribute"));
        Assert.Equal(3, cost.Points);
        Assert.Contains(report.Budget, b => b.Name == "Physical attributes" && b.Spent == 6 && b.Allowed == 5 && b.IsOver);
    }

    [Fact]
    public void Unspent_dots_are_a_note_not_a_warning()
    {
        var sheet = Mortal();
        sheet.Skills[Skill.Academics] = 0;

        var report = Evaluate(sheet);

        Assert.Contains(report.Issues, i => i.Step == CreationStep.Skills && i.Level == CreationIssueLevel.Note && i.Message.StartsWith("3 skill dots left"));
        Assert.True(report.IsComplete);
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(4, 1)] // allowed, but flagged for the Storyteller (decided 2026-10-24)
    [InlineData(5, 1)]
    public void Skills_above_three_are_flagged(int dots, int warnings)
    {
        var sheet = Mortal();
        sheet.Skills[Skill.Brawl] = dots;

        Assert.Equal(warnings, Evaluate(sheet).Issues.Count(i => i.Step == CreationStep.Skills && i.Level == CreationIssueLevel.Warning));
    }

    [Fact]
    public void Extra_specialties_cost_a_merit_point()
    {
        var sheet = Mortal();
        sheet.Specialties.Add(new SpecialtyEntry { Skill = Skill.Athletics, Name = "Climbing" });

        var report = Evaluate(sheet);

        Assert.Equal(1, Assert.Single(report.MeritPoints, m => m.Item.Contains("Specialty")).Points);
        Assert.Contains(report.Budget, b => b.Name == "Specialties" && b.Spent == 4 && b.IsOver);
    }

    // ---------- Merits and Flaws ----------

    [Fact]
    public void Merit_points_over_ten_and_flaws_over_seven_are_warnings()
    {
        var sheet = Mortal();
        // The base sheet already spends 10 Merit points and has 3 points of Flaws.
        sheet.Merits.Add(new MeritEntry { Name = "Fast Reflexes", Kind = MeritKind.Merit, Dots = 2 });
        sheet.Merits.Add(new MeritEntry { Name = "Addiction", Kind = MeritKind.Flaw, Dots = 5 });

        var report = Evaluate(sheet);

        Assert.Contains(report.Budget, b => b.Name == "Merit points" && b.Spent == 12 && b.IsOver);
        Assert.Contains(report.Budget, b => b.Name == "Flaw points" && b.Spent == 8 && b.IsOver);
        Assert.False(report.IsWithinBudget);
        Assert.True(report.IsComplete); // over budget never blocks a submission
    }

    [Fact]
    public void Catalog_merits_are_checked_against_their_ratings()
    {
        var sheet = Mortal();
        sheet.Merits.Clear();
        sheet.Merits.Add(new MeritEntry { MeritDefinitionId = Allies, Name = "Allies", Kind = MeritKind.Merit, Dots = 4 });
        sheet.Merits.Add(new MeritEntry { MeritDefinitionId = Library, Name = "Library", Kind = MeritKind.Merit, Dots = 1 });

        var warnings = Evaluate(sheet).Issues.Where(i => i.Level == CreationIssueLevel.Warning).Select(i => i.Message).ToList();

        Assert.Contains(warnings, w => w.StartsWith("Allies is rated 1–3"));
        Assert.Contains(warnings, w => w.StartsWith("Library is awarded through play"));
    }

    [Fact]
    public void Willpower_and_morality_start_where_the_book_says()
    {
        var sheet = Mortal();
        sheet.WillpowerDots = 9;
        sheet.Morality = 8;

        var warnings = Evaluate(sheet).Issues.Where(i => i.Level == CreationIssueLevel.Warning).Select(i => i.Message).ToList();

        Assert.Contains(warnings, w => w.StartsWith("Willpower starts at Wits + Resolve (5)"));
        Assert.Contains(warnings, w => w.StartsWith("Morality starts at 7"));
    }

    // ---------- Templates ----------

    [Fact]
    public void Only_the_campaigns_templates_can_be_built()
    {
        var report = Evaluate(Mortal(), CharacterTemplates.Vampire);
        Assert.Contains(report.Issues, i => i.Step == CreationStep.Template && i.Level == CreationIssueLevel.Missing);
        Assert.False(report.IsComplete);
    }

    [Fact]
    public void A_bloodline_vampire_gets_a_caitiff_dot_and_a_bloodline_dot_free()
    {
        var report = Evaluate(Vampire(Ventrue, [new() { PowerGroupId = Auspex }, new() { PowerGroupId = Dominate }]));

        Assert.True(report.IsComplete, Missing(report));
        Assert.DoesNotContain(report.MeritPoints, m => m.Item.Contains("Discipline"));
        Assert.DoesNotContain(report.Issues, i => i.Step == CreationStep.Blood && i.Level == CreationIssueLevel.Warning);
    }

    [Fact]
    public void A_bloodline_vampire_must_take_their_bloodlines_discipline()
    {
        var report = Evaluate(Vampire(Ventrue, [new() { PowerGroupId = Auspex }]));
        Assert.Contains(report.Issues, i => i.Level == CreationIssueLevel.Missing && i.Message.Contains("Dominate"));
    }

    [Fact]
    public void A_caitiff_gets_two_dots_in_their_caitiff_discipline_even_at_blood_potency_one()
    {
        var report = Evaluate(Vampire(Caitiff, [new() { PowerGroupId = Furor, Dots = 2 }]));

        Assert.True(report.IsComplete, Missing(report));
        Assert.DoesNotContain(report.MeritPoints, m => m.Item.Contains("Discipline"));
        // The free dots are exempt from the Blood Potency limit (house ruling 2026-10-24).
        Assert.DoesNotContain(report.Issues, i => i.Level == CreationIssueLevel.Warning);
    }

    [Fact]
    public void A_bought_third_caitiff_dot_still_needs_the_blood_potency()
    {
        var report = Evaluate(Vampire(Caitiff, [new() { PowerGroupId = Furor, Dots = 3 }]));
        Assert.Contains(report.Issues, i => i.Level == CreationIssueLevel.Warning && i.Message.StartsWith("Furor has 3 dots but Blood Potency is 1"));
    }

    [Fact]
    public void A_second_caitiff_discipline_or_another_bloodlines_comes_only_through_diablerie()
    {
        var report = Evaluate(Vampire(Ventrue, [new() { PowerGroupId = Auspex }, new() { PowerGroupId = Furor }, new() { PowerGroupId = Dominate }, new() { PowerGroupId = Majesty }]));

        var warnings = report.Issues.Where(i => i.Level == CreationIssueLevel.Warning).Select(i => i.Message).ToList();
        Assert.Contains(warnings, w => w.StartsWith("Only one Caitiff Discipline"));
        Assert.Contains(warnings, w => w.StartsWith("Majesty belongs to another Bloodline"));
        // No warning about how many Disciplines: Blood Potency limits dots only (house ruling 2026-10-24).
        Assert.DoesNotContain(warnings, w => w.Contains("Disciplines but Blood Potency"));
    }

    [Fact]
    public void Extra_vampire_dots_cost_three_merit_points_and_stay_within_blood_potency()
    {
        var sheet = Vampire(Ventrue, [new() { PowerGroupId = Auspex }, new() { PowerGroupId = Dominate, Dots = 2 }]);
        sheet.Generation = 11;

        var report = Evaluate(sheet);

        Assert.Equal(3, Assert.Single(report.MeritPoints, m => m.Item.Contains("Discipline")).Points);
        Assert.Equal(3, Assert.Single(report.MeritPoints, m => m.Item.StartsWith("Generation")).Points);
        Assert.Contains(report.Issues, i => i.Level == CreationIssueLevel.Warning && i.Message.StartsWith("Dominate has 2 dots but Blood Potency is 1"));
    }

    [Fact]
    public void A_magi_house_picks_one_free_ritual_from_its_own_sorcery()
    {
        var withNone = Evaluate(Vampire(Tremere, [new() { PowerGroupId = Auspex }]));
        Assert.Contains(withNone.Issues, i => i.Level == CreationIssueLevel.Missing && i.Message.Contains("Ritual"));

        var sheet = Vampire(Tremere, [new() { PowerGroupId = Auspex }]);
        sheet.LearnedPowerIds = [Ritual1, Ritual2, Rite];
        var report = Evaluate(sheet);

        Assert.True(report.IsComplete, Missing(report));
        Assert.Equal(3, Assert.Single(report.MeritPoints, m => m.Item.Contains("Ritual")).Points);
        Assert.Contains(report.Issues, i => i.Level == CreationIssueLevel.Warning && i.Message.StartsWith("Sabbat Ritae"));
    }

    [Fact]
    public void Humanity_starts_at_six_and_respects_generation_strength()
    {
        var sheet = Vampire(Ventrue, [new() { PowerGroupId = Auspex }, new() { PowerGroupId = Dominate }], bloodPotency: 5);
        sheet.Humanity = 6;

        // Generation 12 is strength 10, so Humanity 6 + Blood Potency 5 must drop to 5.
        Assert.Contains(Evaluate(sheet).Issues, i => i.Message.StartsWith("Humanity starts at 5"));
    }

    [Fact]
    public void A_ghoul_needs_a_domitor()
    {
        var sheet = Mortal();
        sheet.Kind = CharacterKind.Ghoul;
        Assert.Contains(Evaluate(sheet).Issues, i => i.Step == CreationStep.Blood && i.Level == CreationIssueLevel.Missing);

        sheet.DomitorName = "Mister Grey";
        Assert.True(Evaluate(sheet).IsComplete, Missing(Evaluate(sheet)));
    }

    [Fact]
    public void A_campaigns_budgets_replace_the_books()
    {
        // The book's mortal against richer attributes and more Merit points: dots left over, nothing over budget.
        var generous = new CreationSettings { PrimaryAttributes = 6, SecondaryAttributes = 5, TertiaryAttributes = 4, MeritPoints = 15 };
        var report = CreationRules.Evaluate(Mortal(), Catalog, CharacterTemplates.All, generous);
        Assert.True(report.IsWithinBudget);
        Assert.Equal([6, 5, 4], report.Budget.Where(b => b.Step == CreationStep.Attributes).Select(b => b.Allowed));
        Assert.Contains(report.Issues, i => i.Message == "3 attribute dots left to place.");
        Assert.Contains(report.Issues, i => i.Message == "5 Merit points left to spend.");

        // Leaner skills and fewer Flaws: the extra skill dots cost Merit points, and the Flaws are a warning.
        var lean = new CreationSettings { PrimarySkills = 10, SecondarySkills = 6, TertiarySkills = 4, FlawPoints = 2 };
        report = CreationRules.Evaluate(Mortal(), Catalog, CharacterTemplates.All, lean);
        Assert.Equal(3, report.MeritPoints.Where(m => m.Item.Contains("skill dot")).Sum(m => m.Points));
        Assert.Contains(report.Issues, i => i.Level == CreationIssueLevel.Warning && i.Message.EndsWith("the most at creation is 2."));
    }

    [Fact]
    public void A_campaign_can_start_vampires_lower_and_stop_them_buying_generation()
    {
        var sheet = Vampire(Ventrue, [new() { PowerGroupId = Auspex }, new() { PowerGroupId = Dominate }]);
        var tenth = new CreationSettings { StartingGeneration = 10 };

        // Buying down from the campaign's start, not the book's.
        sheet.Generation = 9;
        Assert.Equal(3, Assert.Single(CreationRules.Evaluate(sheet, Catalog, CharacterTemplates.All, tenth).MeritPoints, m => m.Item.StartsWith("Generation")).Points);
        sheet.Generation = 12;
        Assert.Contains(CreationRules.Evaluate(sheet, Catalog, CharacterTemplates.All, tenth).Issues,
            i => i.Level == CreationIssueLevel.Warning && i.Message.StartsWith("This campaign's vampires start at Generation 10"));

        // Without buying, a lower Generation costs nothing but is a warning.
        tenth.CanBuyGeneration = false;
        sheet.Generation = 9;
        var report = CreationRules.Evaluate(sheet, Catalog, CharacterTemplates.All, tenth);
        Assert.DoesNotContain(report.MeritPoints, m => m.Item.StartsWith("Generation"));
        Assert.Contains(report.Issues, i => i.Message == "This campaign's vampires start at Generation 10 and can't buy a lower one.");
        sheet.Generation = 10;
        Assert.DoesNotContain(CreationRules.Evaluate(sheet, Catalog, CharacterTemplates.All, tenth).Issues, i => i.Message.Contains("Generation 10"));
    }

    [Fact]
    public void Creation_settings_keep_priorities_in_order()
    {
        var settings = new CreationSettings { PrimaryAttributes = 3, SecondaryAttributes = 4 };
        Assert.Contains(settings.Validate(new(settings)), r => r.ErrorMessage!.StartsWith("Attributes go from most dots to fewest"));
        Assert.Empty(new CreationSettings().Validate(new(new CreationSettings())));
    }

    // ---------- Helpers ----------

    private static CreationReport Evaluate(FullSheetForm sheet, CharacterTemplates allowed = CharacterTemplates.All) =>
        CreationRules.Evaluate(sheet, Catalog, allowed);

    private static string Missing(CreationReport report) =>
        string.Join("; ", report.Issues.Where(i => i.Level == CreationIssueLevel.Missing).Select(i => i.Message));

    /// <summary>A complete mortal: 5/4/3 attributes, 11/7/5 skills, 3 specialties, 10 merit points, 3 flaw points.</summary>
    private static FullSheetForm Mortal()
    {
        var sheet = new FullSheetForm
        {
            Name = "Dana Reyes", Kind = CharacterKind.Mortal, Concept = "Survivor", Virtue = "Fortitude", Vice = "Wrath",
            Morality = 7, Humanity = null,
            Relationships =
            [
                new() { Role = RelationshipRole.Anchor, Name = "Marco", Description = "My brother" },
                new() { Role = RelationshipRole.Complication, Name = "Officer Hale" },
                new() { Role = RelationshipRole.Connection, Name = "The Night Market" },
            ],
            Scripts = FullSheetForm.DefaultScripts(CharacterKind.Mortal),
            Milestones = [new() { Title = "Find who burned the shop" }],
        };
        sheet.Scripts[0].Text = "Chain smoker";
        sheet.Scripts[1].Text = "Never leaves a debt unpaid";
        SetAttributes(sheet, physical: [3, 3, 2], social: [2, 2, 3], mental: [2, 2, 2]); // 5 / 4 / 3
        // Mental 11, Physical 7, Social 5.
        foreach (var (skill, dots) in new[]
                 {
                     (Skill.Academics, 3), (Skill.Crafts, 2), (Skill.Intrigue, 2), (Skill.Investigation, 3), (Skill.Technology, 1),
                     (Skill.Athletics, 2), (Skill.Brawl, 3), (Skill.Ballistics, 1), (Skill.Stealth, 1),
                     (Skill.Empathy, 2), (Skill.Streetwise, 3),
                 })
            sheet.Skills[skill] = dots;
        sheet.Specialties = [new() { Skill = Skill.Brawl, Name = "Boxing" }, new() { Skill = Skill.Academics, Name = "History" }, new() { Skill = Skill.Streetwise, Name = "Gangs" }];
        sheet.Merits =
        [
            new() { Name = "Contacts", Kind = MeritKind.Merit, Dots = 3 },
            new() { Name = "Resources", Kind = MeritKind.Merit, Dots = 3 },
            new() { Name = "Fighting Style", Kind = MeritKind.Merit, Dots = 3 },
            new() { Name = "Danger Sense", Kind = MeritKind.Merit, Dots = 1 },
            new() { Name = "Nightmares", Kind = MeritKind.Flaw, Dots = 3 },
        ];
        sheet.WillpowerDots = sheet.WillpowerCurrent = DerivedStats.StartingWillpower(sheet.Attribute(CharacterAttribute.Wits), sheet.Attribute(CharacterAttribute.Resolve));
        return sheet;
    }

    private static FullSheetForm Vampire(int bloodline, List<DisciplineEntry> disciplines, int bloodPotency = 1)
    {
        var sheet = Mortal();
        sheet.Kind = CharacterKind.Vampire;
        sheet.Scripts = FullSheetForm.DefaultScripts();
        sheet.Scripts[0].Text = "Protect Marco";
        sheet.Scripts[1].Text = "Own the Night Market";
        sheet.Morality = null;
        sheet.Humanity = 6;
        sheet.Generation = 12;
        sheet.BloodPotency = bloodPotency;
        sheet.BloodlineId = bloodline;
        sheet.PredatorTypeId = 1;
        sheet.Disciplines = disciplines;
        // Keep Merit points at 10 or under so the Blood checks stand alone.
        sheet.Merits.RemoveAll(m => m.Name == "Fighting Style");
        return sheet;
    }

    /// <summary>Sets the attributes in sheet order: Strength, Dexterity, Stamina / Charisma, Manipulation, Resolve / Intelligence, Wits, Perception.</summary>
    private static void SetAttributes(FullSheetForm sheet, int[] physical, int[] social, int[] mental)
    {
        (sheet.Attributes[CharacterAttribute.Strength], sheet.Attributes[CharacterAttribute.Dexterity], sheet.Attributes[CharacterAttribute.Stamina]) = (physical[0], physical[1], physical[2]);
        (sheet.Attributes[CharacterAttribute.Charisma], sheet.Attributes[CharacterAttribute.Manipulation], sheet.Attributes[CharacterAttribute.Resolve]) = (social[0], social[1], social[2]);
        (sheet.Attributes[CharacterAttribute.Intelligence], sheet.Attributes[CharacterAttribute.Wits], sheet.Attributes[CharacterAttribute.Perception]) = (mental[0], mental[1], mental[2]);
        sheet.WillpowerDots = sheet.WillpowerCurrent = DerivedStats.StartingWillpower(sheet.Attribute(CharacterAttribute.Wits), sheet.Attribute(CharacterAttribute.Resolve));
    }
}
