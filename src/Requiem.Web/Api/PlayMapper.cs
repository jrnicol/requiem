using Requiem.Data.Entities;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Api;

/// <summary>Builds the table view of a character, full sheet or Supporting Cast.</summary>
internal static class PlayMapper
{
    /// <param name="catalog">Every catalog power (light-weight: no bonus options needed).</param>
    public static PlayCharacter ToPlay(Character c, IReadOnlyList<Power> catalog)
    {
        var isFull = c.Tier == CharacterTier.FullSheet;
        var attributes = isFull
            ? Enum.GetValues<CharacterAttribute>().ToDictionary(a => a, a => c.Attributes.FirstOrDefault(r => r.Attribute == a)?.Dots ?? Traits.MinAttribute)
            : [];
        var skills = isFull
            ? Enum.GetValues<Skill>().ToDictionary(s => s, s => c.Skills.FirstOrDefault(r => r.Skill == s)?.Dots ?? Traits.MinSkill)
            : [];
        int Attr(CharacterAttribute a) => attributes.GetValueOrDefault(a, Traits.MinAttribute);

        var physical = c.Physical ?? SupportingCastRules.MinRating;
        var health = isFull ? DerivedStats.Health(Attr(CharacterAttribute.Stamina), c.Size) : SupportingCastRules.Health(physical, c.Edges);
        var willpowerDots = isFull ? c.WillpowerDots ?? 0 : SupportingCastRules.Willpower(c.Mental ?? SupportingCastRules.MinRating, c.Edges);

        var isVampire = c.Kind == CharacterKind.Vampire;
        var bloodPotency = isVampire ? c.BloodPotency ?? BloodRules.MinBloodPotency : (int?)null;
        var minimumHunger = isVampire && c.Bloodline?.Name == "Brujah" ? BloodlineRules.BrujahMinimumHungerDice(bloodPotency!.Value) : 0;

        var powerIds = GrantedPowerIds(c, catalog);
        var pools = isFull ? PowerPools(c, catalog, powerIds, attributes, skills) : RatingPools(c);

        return new PlayCharacter(
            c.Id, c.Name, c.Tier, c.Kind, c.Concept, c.Bloodline?.Name,
            health,
            c.Wounds.OrderBy(w => w.Id).Select(w => new PlayWound(w.Id, w.Description, w.Amount, w.Type)).ToList(),
            willpowerDots,
            isFull ? Math.Clamp(c.WillpowerCurrent ?? willpowerDots, 0, willpowerDots) : willpowerDots,
            CanSpendWillpower: isFull,
            bloodPotency,
            GhoulRules.HoldsVitae(c.Kind) ? c.VitaeCurrent ?? 0 : null,
            GhoulRules.VitaeCapacity(c.Kind, bloodPotency, c.IsThinblood),
            minimumHunger,
            c.Humanity, c.Morality,
            isFull ? DerivedStats.Defense(Attr(CharacterAttribute.Wits), Attr(CharacterAttribute.Dexterity)) : null,
            new Dictionary<FortifyDiscipline, int>
            {
                [FortifyDiscipline.Potence] = c.PotenceBoost,
                [FortifyDiscipline.Celerity] = c.CelerityBoost,
                [FortifyDiscipline.Fortitude] = c.FortitudeBoost,
            },
            BoostCaps(c, attributes),
            c.Compels.OrderBy(x => x.Id).Select(x => new PlayCompel(x.Id, x.Name, x.Rating, x.EndsWhen)).ToList(),
            c.Conditions.OrderBy(x => x.Id).Select(x => new PlayCondition(x.Id, x.Name)).ToList(),
            pools,
            powerIds,
            attributes,
            skills,
            c.Specialties.OrderBy(s => s.Id).Select(s => new PlaySpecialty(s.Skill, s.Name)).ToList(),
            c.Items.Where(i => i.Kind == ItemKind.Asset).OrderBy(i => i.SortOrder).Select(i => i.Name).ToList(),
            c.Notes,
            c.GmNotes,
            c.IsPlayerCharacter);
    }

    /// <summary>
    /// Fortify Form is capped by Strength / Dexterity / Stamina (Physical for Supporting Cast);
    /// ghouls gain at most 1 in each, and mortals none.
    /// </summary>
    public static IReadOnlyDictionary<FortifyDiscipline, int> BoostCaps(Character c, IReadOnlyDictionary<CharacterAttribute, int> attributes) =>
        Enum.GetValues<FortifyDiscipline>().ToDictionary(d => d, d => GhoulRules.FortifyCap(c.Kind, c.Tier == CharacterTier.FullSheet
            ? attributes.GetValueOrDefault(Traits.FortifyCap(d), Traits.MinAttribute)
            : c.Physical ?? SupportingCastRules.MinRating));

    /// <summary>Discipline powers the dots grant (or the Investiture picks), plus rituals and ritae learned.</summary>
    private static List<int> GrantedPowerIds(Character c, IReadOnlyList<Power> catalog)
    {
        var learned = c.LearnedPowers.Select(p => p.PowerId).ToHashSet();
        var ids = new List<int>();
        foreach (var discipline in c.Disciplines)
        {
            var group = catalog.Where(p => p.PowerGroupId == discipline.PowerGroupId).ToList();
            var kind = discipline.PowerGroup?.Kind ?? PowerGroupKind.CaitiffDiscipline;
            ids.AddRange(PowerRules.IsPickPerDot(kind, group.Select(p => p.Level).ToList())
                ? group.Where(p => learned.Contains(p.Id)).Select(p => p.Id)
                : group.Where(p => p.Level <= discipline.Dots).OrderBy(p => p.Level).Select(p => p.Id));
        }
        ids.AddRange(learned.Where(id => !ids.Contains(id)));
        return ids;
    }

    private static List<PlayPool> PowerPools(
        Character c, IReadOnlyList<Power> catalog, IEnumerable<int> powerIds,
        IReadOnlyDictionary<CharacterAttribute, int> attributes, IReadOnlyDictionary<Skill, int> skills)
    {
        var pools = new List<PlayPool>();
        foreach (var power in powerIds.Select(id => catalog.FirstOrDefault(p => p.Id == id)).OfType<Power>())
        {
            if (power.DicePoolAttribute is null && power.DicePoolSkill is null) continue;

            var parts = new List<(string Name, int Dots)>();
            if (power.DicePoolAttribute is { } attribute) parts.Add((attribute.ToString(), attributes.GetValueOrDefault(attribute)));
            if (power.DicePoolSkill is { } skill) parts.Add((skill.ToString(), skills.GetValueOrDefault(skill)));
            if (power.DicePoolPowerGroupId is { } groupId)
            {
                var discipline = c.Disciplines.FirstOrDefault(d => d.PowerGroupId == groupId);
                var name = discipline?.PowerGroup?.Name ?? catalog.FirstOrDefault(p => p.PowerGroupId == groupId)?.PowerGroup?.Name ?? "Discipline";
                parts.Add((name, discipline?.Dots ?? 0));
            }

            var detail = string.Join(" + ", parts.Select(p => $"{p.Name} {p.Dots}"));
            if (!string.IsNullOrWhiteSpace(power.OpposedBy)) detail += $" vs {power.OpposedBy}";
            pools.Add(new PlayPool(power.Name, parts.Sum(p => p.Dots), detail, "Powers"));
        }
        return pools;
    }

    /// <summary>Supporting Cast roll their category rating (WoD Core p.114); Edges add where the rules say.</summary>
    private static List<PlayPool> RatingPools(Character c)
    {
        var physical = c.Physical ?? SupportingCastRules.MinRating;
        var mental = c.Mental ?? SupportingCastRules.MinRating;
        var social = c.Social ?? SupportingCastRules.MinRating;
        var quickWitted = c.Edges.HasFlag(NpcEdge.QuickWitted);
        return
        [
            new("Physical", physical, $"Physical {physical}", "Ratings"),
            new("Mental", mental, $"Mental {mental}", "Ratings"),
            new("Social", SupportingCastRules.EffectiveSocial(social, c.Edges),
                quickWitted ? $"Social {social} + {SupportingCastRules.EdgeBonus} Quick-Witted" : $"Social {social}", "Ratings"),
        ];
    }
}
