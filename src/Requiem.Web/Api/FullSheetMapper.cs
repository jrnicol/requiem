using Requiem.Data.Entities;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Api;

/// <summary>Maps between the full sheet form and the character entity (with all its parts loaded).</summary>
internal static class FullSheetMapper
{
    public static FullSheetForm ToForm(Character c) => new FullSheetForm
    {
        Id = c.Id,
        CampaignId = c.CampaignId,
        Name = c.Name,
        Kind = c.Kind,
        Concept = c.Concept,
        Faction = c.Faction,
        Titles = [.. c.Titles],
        Virtue = c.Virtue,
        VirtueDetail = c.VirtueDetail,
        Vice = c.Vice,
        ViceDetail = c.ViceDetail,
        BeastDescription = c.BeastDescription,
        SireId = c.SireId,
        DomitorId = c.DomitorId,
        DomitorName = c.DomitorName,
        Relationships = c.Relationships.OrderBy(r => r.Role).ThenBy(r => r.Id)
            .Select(r => new RelationshipEntry { Role = r.Role, Name = r.ToName ?? "", Description = r.Description }).ToList(),
        Attributes = Enum.GetValues<CharacterAttribute>().ToDictionary(
            a => a, a => c.Attributes.FirstOrDefault(r => r.Attribute == a)?.Dots ?? Traits.MinAttribute),
        Skills = Enum.GetValues<Skill>().ToDictionary(
            s => s, s => c.Skills.FirstOrDefault(r => r.Skill == s)?.Dots ?? Traits.MinSkill),
        Specialties = c.Specialties.Where(s => s.Skill is not null).OrderBy(s => s.Id)
            .Select(s => new SpecialtyEntry { Skill = s.Skill!.Value, Name = s.Name }).ToList(),
        Size = c.Size,
        WillpowerDots = c.WillpowerDots ?? 0,
        WillpowerCurrent = c.WillpowerCurrent ?? c.WillpowerDots ?? 0,
        Humanity = c.Humanity,
        Morality = c.Morality,
        BloodlineId = c.BloodlineId,
        PredatorTypeId = c.PredatorTypeId,
        Generation = c.Generation,
        BloodPotency = c.BloodPotency,
        VitaeCurrent = c.VitaeCurrent,
        IsThinblood = c.IsThinblood,
        PotenceBoost = c.PotenceBoost,
        CelerityBoost = c.CelerityBoost,
        FortitudeBoost = c.FortitudeBoost,
        Disciplines = c.Disciplines.OrderBy(d => d.PowerGroupId)
            .Select(d => new DisciplineEntry { PowerGroupId = d.PowerGroupId, Dots = d.Dots }).ToList(),
        LearnedPowerIds = c.LearnedPowers.Select(p => p.PowerId).Order().ToList(),
        Merits = c.Merits.OrderBy(m => m.Kind).ThenBy(m => m.Id).Select(m => new MeritEntry
        {
            MeritDefinitionId = m.MeritDefinitionId, Name = m.Name, Kind = m.Kind, Dots = m.Dots,
            Qualifier = m.Qualifier, Notes = m.Notes,
        }).ToList(),
        Wounds = c.Wounds.OrderBy(w => w.Id)
            .Select(w => new WoundEntry { Description = w.Description, Amount = w.Amount, Type = w.Type }).ToList(),
        Stains = c.Stains.OrderBy(s => s.Id).Select(s => s.Text).ToList(),
        Compels = c.Compels.OrderBy(x => x.Id)
            .Select(x => new CompelEntry { Name = x.Name, Rating = x.Rating, EndsWhen = x.EndsWhen }).ToList(),
        Conditions = c.Conditions.OrderBy(x => x.Id).Select(x => x.Name).ToList(),
        Scripts = c.Scripts.Count == 0 && c.IsPlayerCharacter
            ? FullSheetForm.DefaultScripts(c.Kind)
            : c.Scripts.OrderBy(s => s.Slot)
                .Select(s => new ScriptEntry { Slot = s.Slot, Kind = s.Kind, Text = s.Text, IsMarked = s.IsMarked }).ToList(),
        Milestones = c.Milestones.OrderBy(m => m.Id).Select(m => new MilestoneEntry
        {
            Title = m.Title, Opportunity = m.Opportunity, Risk = m.Risk, Choice = m.Choice,
            Benefit = m.Benefit, Notes = m.Notes, Stage = m.Stage,
        }).ToList(),
        CorruptionMarks = c.CorruptionMarks.OrderBy(m => m.Id).Select(m => m.Act).ToList(),
        Equipment = c.Items.Where(i => i.Kind == ItemKind.Equipment).OrderBy(i => i.SortOrder).Select(i => i.Name).ToList(),
        Experience = c.XpLedger.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new XpLedgerEntry { Amount = x.Amount, Reason = x.Reason, CreatedAt = x.CreatedAt }).ToList(),
        Notes = c.Notes,
        GmNotes = c.GmNotes,
        IsPlayerCharacter = c.IsPlayerCharacter,
        Version = c.Version,
    }.WithProfile(c);

    /// <summary>Copies a validated form onto the entity. Lists are only rewritten when they changed.</summary>
    public static void Apply(FullSheetForm form, Character c, DateTimeOffset now)
    {
        form.NormalizeForKind();
        // Scripts, Milestones, Corruption and XP are how player characters grow; NPCs have none.
        if (!c.IsPlayerCharacter)
        {
            form.Scripts = [];
            form.Milestones = [];
            form.CorruptionMarks = [];
            form.Experience = [];
        }
        ProfileMapper.ApplyProfile(form, c);

        c.Tier = CharacterTier.FullSheet;
        c.Name = form.Name.Trim();
        c.Kind = form.Kind;
        c.Concept = Clean(form.Concept);
        c.Faction = Clean(form.Faction);
        c.Titles = ProfileMapper.CleanTitles(form.Titles);
        c.Virtue = Clean(form.Virtue);
        c.VirtueDetail = Clean(form.VirtueDetail);
        c.Vice = Clean(form.Vice);
        c.ViceDetail = Clean(form.ViceDetail);
        c.BeastDescription = Clean(form.BeastDescription);
        c.SireId = form.SireId;
        c.DomitorId = form.DomitorId;
        c.DomitorName = Clean(form.DomitorName);
        c.Size = form.Size;
        c.WillpowerDots = form.WillpowerDots;
        c.WillpowerCurrent = form.WillpowerCurrent;
        c.Humanity = form.Humanity;
        c.Morality = form.Morality;
        c.BloodlineId = form.BloodlineId;
        c.PredatorTypeId = form.PredatorTypeId;
        c.Generation = form.Generation;
        c.BloodPotency = form.BloodPotency;
        c.VitaeCurrent = form.VitaeCurrent;
        c.IsThinblood = form.IsThinblood;
        c.PotenceBoost = form.PotenceBoost;
        c.CelerityBoost = form.CelerityBoost;
        c.FortitudeBoost = form.FortitudeBoost;
        c.Notes = Clean(form.Notes);
        c.GmNotes = Clean(form.GmNotes);

        // Supporting Cast fields don't apply to a full sheet.
        c.Physical = c.Mental = c.Social = null;
        c.Edges = NpcEdge.None;

        foreach (var attribute in Enum.GetValues<CharacterAttribute>())
        {
            var row = c.Attributes.FirstOrDefault(r => r.Attribute == attribute);
            if (row is null) c.Attributes.Add(row = new CharacterAttributeRating { Attribute = attribute });
            row.Dots = form.Attribute(attribute);
        }

        foreach (var skill in Enum.GetValues<Skill>())
        {
            var row = c.Skills.FirstOrDefault(r => r.Skill == skill);
            if (row is null) c.Skills.Add(row = new CharacterSkillRating { Skill = skill });
            row.Dots = form.SkillDots(skill);
        }

        Replace(c.Specialties, s => s.Skill is not null,
            c.Specialties.Where(s => s.Skill is not null).OrderBy(s => s.Id).Select(s => (s.Skill, s.Name)),
            form.Specialties.Select(s => ((Skill?)s.Skill, s.Name.Trim())),
            () => form.Specialties.Select(s => new CharacterSpecialty { Skill = s.Skill, Name = s.Name.Trim() }));

        // Disciplines are keyed by power group: update in place so learned history isn't churned.
        var wanted = form.Disciplines.Where(d => d.Dots > 0).ToDictionary(d => d.PowerGroupId, d => d.Dots);
        c.Disciplines.RemoveAll(d => !wanted.ContainsKey(d.PowerGroupId));
        foreach (var (groupId, dots) in wanted)
        {
            var row = c.Disciplines.FirstOrDefault(d => d.PowerGroupId == groupId);
            if (row is null) c.Disciplines.Add(new CharacterDiscipline { PowerGroupId = groupId, Dots = dots });
            else row.Dots = dots;
        }

        // Learned powers are keyed by power, so per-power notes survive a save.
        var learned = form.LearnedPowerIds.ToHashSet();
        c.LearnedPowers.RemoveAll(p => !learned.Contains(p.PowerId));
        foreach (var powerId in learned.Where(id => c.LearnedPowers.All(p => p.PowerId != id)))
            c.LearnedPowers.Add(new CharacterPower { PowerId = powerId });

        Replace(c.Merits, _ => true,
            c.Merits.OrderBy(m => m.Kind).ThenBy(m => m.Id).Select(m => (m.MeritDefinitionId, m.Name, m.Kind, m.Dots, m.Qualifier, m.Notes)),
            form.Merits.OrderBy(m => m.Kind).Select(m => (m.MeritDefinitionId, m.Name.Trim(), m.Kind, m.Dots, Clean(m.Qualifier), Clean(m.Notes))),
            () => form.Merits.Select(m => new CharacterMerit
            {
                MeritDefinitionId = m.MeritDefinitionId, Name = m.Name.Trim(), Kind = m.Kind, Dots = m.Dots,
                Qualifier = Clean(m.Qualifier), Notes = Clean(m.Notes),
            }));

        // Relationships to people outside the app; links to characters (ToCharacterId) aren't edited here.
        Replace(c.Relationships, r => r.ToCharacterId is null,
            c.Relationships.Where(r => r.ToCharacterId is null).OrderBy(r => r.Role).ThenBy(r => r.Id).Select(r => (r.Role, r.ToName, r.Description)),
            form.Relationships.OrderBy(r => r.Role).Select(r => (r.Role, (string?)r.Name.Trim(), Clean(r.Description))),
            () => form.Relationships.OrderBy(r => r.Role).Select(r => new CharacterRelationship
                { Role = r.Role, Kind = RelationshipKind.Other, ToName = r.Name.Trim(), Description = Clean(r.Description) }));

        Replace(c.Wounds, _ => true,
            c.Wounds.OrderBy(w => w.Id).Select(w => (w.Description, w.Amount, w.Type)),
            form.Wounds.Select(w => (w.Description.Trim(), w.Amount, w.Type)),
            () => form.Wounds.Select(w => new CharacterWound
                { Description = w.Description.Trim(), Amount = w.Amount, Type = w.Type, CreatedAt = now }));

        Replace(c.Stains, _ => true,
            c.Stains.OrderBy(s => s.Id).Select(s => s.Text),
            form.Stains.Select(s => s.Trim()),
            () => form.Stains.Select(s => new CharacterStain { Text = s.Trim(), CreatedAt = now }));

        Replace(c.Compels, _ => true,
            c.Compels.OrderBy(x => x.Id).Select(x => (x.Name, x.Rating, x.EndsWhen)),
            form.Compels.Select(x => (x.Name.Trim(), x.Rating, Clean(x.EndsWhen))),
            () => form.Compels.Select(x => new CharacterCompel
                { Name = x.Name.Trim(), Rating = x.Rating, EndsWhen = Clean(x.EndsWhen), CreatedAt = now }));

        Replace(c.Conditions, _ => true,
            c.Conditions.OrderBy(x => x.Id).Select(x => x.Name),
            form.Conditions.Select(x => x.Trim()),
            () => form.Conditions.Select(x => new CharacterCondition { Name = x.Trim(), CreatedAt = now }));

        Replace(c.Scripts, _ => true,
            c.Scripts.OrderBy(s => s.Slot).Select(s => (s.Slot, s.Kind, s.Text, s.IsMarked)),
            form.Scripts.OrderBy(s => s.Slot).Select(s => (s.Slot, s.Kind, s.Text.Trim(), s.IsMarked)),
            () => form.Scripts.Select(s => new CharacterScript { Slot = s.Slot, Kind = s.Kind, Text = s.Text.Trim(), IsMarked = s.IsMarked }));

        Replace(c.Milestones, _ => true,
            c.Milestones.OrderBy(m => m.Id).Select(m => (m.Title, m.Opportunity, m.Risk, m.Choice, m.Benefit, m.Notes, m.Stage)),
            form.Milestones.Select(m => (m.Title.Trim(), Clean(m.Opportunity), Clean(m.Risk), Clean(m.Choice), Clean(m.Benefit), Clean(m.Notes), m.Stage)),
            () => form.Milestones.Select(m => new CharacterMilestone
            {
                Title = m.Title.Trim(), Opportunity = Clean(m.Opportunity), Risk = Clean(m.Risk), Choice = Clean(m.Choice),
                Benefit = Clean(m.Benefit), Notes = Clean(m.Notes), Stage = m.Stage,
                CompletedAt = m.Stage == MilestoneStage.Completed ? now : null,
            }));

        Replace(c.CorruptionMarks, _ => true,
            c.CorruptionMarks.OrderBy(m => m.Id).Select(m => m.Act),
            form.CorruptionMarks.Select(m => m.Trim()),
            () => form.CorruptionMarks.Select(m => new CorruptionMark { Act = m.Trim(), CreatedAt = now }));

        Replace(c.Items, i => i.Kind == ItemKind.Equipment,
            c.Items.Where(i => i.Kind == ItemKind.Equipment).OrderBy(i => i.SortOrder).Select(i => i.Name),
            form.Equipment.Select(e => e.Trim()),
            () => form.Equipment.Select((e, i) => new CharacterItem { Kind = ItemKind.Equipment, Name = e.Trim(), SortOrder = i }));

        Replace(c.XpLedger, _ => true,
            c.XpLedger.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => (x.Amount, x.Reason)),
            form.Experience.Select(x => (x.Amount, x.Reason.Trim())),
            () => form.Experience.Select(x => new XpEntry { Amount = x.Amount, Reason = x.Reason.Trim(), CreatedAt = x.CreatedAt ?? now }));
    }

    /// <summary>
    /// Turns a Supporting Cast NPC into a full sheet: each attribute takes its category rating,
    /// skills start at 0, and anything without a full-sheet home is kept in the notes.
    /// </summary>
    public static void Promote(Character c)
    {
        var physical = Math.Clamp(c.Physical ?? 1, Traits.MinAttribute, Traits.MaxAttribute);
        var mental = Math.Clamp(c.Mental ?? 1, Traits.MinAttribute, Traits.MaxAttribute);
        var social = Math.Clamp(c.Social ?? 1, Traits.MinAttribute, Traits.MaxAttribute);

        c.Attributes.Clear();
        foreach (var attribute in Enum.GetValues<CharacterAttribute>())
            c.Attributes.Add(new CharacterAttributeRating
            {
                Attribute = attribute,
                Dots = Traits.CategoryOf(attribute) switch
                {
                    TraitCategory.Physical => physical,
                    TraitCategory.Mental => mental,
                    _ => social,
                },
            });
        c.Skills.Clear();
        c.Skills.AddRange(Enum.GetValues<Skill>().Select(s => new CharacterSkillRating { Skill = s, Dots = 0 }));

        // Willpower comes from Wits (Mental) + Resolve (Social) at creation.
        c.WillpowerDots = DerivedStats.StartingWillpower(mental, social);
        c.WillpowerCurrent = c.WillpowerDots;

        if (c.Kind == CharacterKind.Vampire)
        {
            c.BloodPotency ??= BloodRules.MinBloodPotency;
            c.Generation ??= GenerationRules.StartingGeneration;
            c.Humanity = HumanityRules.ClampToGeneration(DerivedStats.StartingHumanity(), c.BloodPotency.Value, c.Generation.Value);
        }
        else
        {
            c.Morality = DerivedStats.StartingMorality;
        }

        var carried = new List<string>();
        var freeSpecialties = c.Specialties.Where(s => s.Skill is null).ToList();
        if (freeSpecialties.Count > 0) carried.Add($"Specialties: {string.Join(", ", freeSpecialties.Select(s => s.Name))}");
        c.Specialties.RemoveAll(s => s.Skill is null);

        var edges = Enum.GetValues<NpcEdge>().Where(e => e != NpcEdge.None && c.Edges.HasFlag(e)).ToList();
        if (edges.Count > 0) carried.Add($"Edges: {string.Join(", ", edges)}");

        foreach (var asset in c.Items.Where(i => i.Kind == ItemKind.Asset)) asset.Kind = ItemKind.Equipment;

        if (carried.Count > 0)
        {
            var note = $"Promoted from Supporting Cast. {string.Join(". ", carried)}.";
            c.Notes = string.IsNullOrWhiteSpace(c.Notes) ? note : $"{c.Notes}\n\n{note}";
        }

        c.Physical = c.Mental = c.Social = null;
        c.Edges = NpcEdge.None;
        c.Tier = CharacterTier.FullSheet;
    }

    private static void Replace<TEntity, TKey>(
        List<TEntity> rows, Predicate<TEntity> belongs, IEnumerable<TKey> current, IEnumerable<TKey> wanted,
        Func<IEnumerable<TEntity>> build)
    {
        if (current.SequenceEqual(wanted)) return;
        rows.RemoveAll(belongs);
        rows.AddRange(build());
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
