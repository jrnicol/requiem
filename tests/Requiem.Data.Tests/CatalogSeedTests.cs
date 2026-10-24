using Microsoft.EntityFrameworkCore;
using Requiem.Data.Entities;
using Requiem.Data.Seeding;
using Requiem.Shared.Characters;
using Requiem.Shared.Rules;

namespace Requiem.Data.Tests;

public class CatalogSeedDataTests
{
    private static readonly CatalogSeedData Data = CatalogSeeder.Load();

    [Fact]
    public void Bloodlines_cover_the_weakness_chart()
    {
        Assert.Equal(13, Data.Bloodlines.Count);
        Assert.Equal(7, Data.Bloodlines.Count(b => b.Kind == BloodlineKind.Bloodline));
        Assert.Equal(5, Data.Bloodlines.Count(b => b.Kind == BloodlineKind.MagiHouse));
        Assert.Equal("Caitiff", Assert.Single(Data.Bloodlines, b => b.Kind == BloodlineKind.Caitiff).Name);
    }

    [Fact]
    public void Every_bloodline_has_weakness_and_compel_text()
    {
        foreach (var b in Data.Bloodlines)
        {
            Assert.False(string.IsNullOrWhiteSpace(b.WeaknessText), b.Name);
            Assert.False(string.IsNullOrWhiteSpace(b.CompelName), b.Name);
            Assert.False(string.IsNullOrWhiteSpace(b.CompelText), b.Name);
            Assert.NotNull(b.SourcePage);
            if (b.Kind != BloodlineKind.Caitiff) Assert.False(string.IsNullOrWhiteSpace(b.WeaknessName), b.Name);
        }
    }

    [Fact]
    public void Ten_predator_types_with_ventrue_restrictions()
    {
        Assert.Equal(10, Data.PredatorTypes.Count);
        Assert.Equal(["Bagger", "Farmer"],
            Data.PredatorTypes.Where(p => p.Restrictions?.Contains("Ventrue") == true).Select(p => p.Name).Order());
    }

    [Fact]
    public void Base_compels_fill_the_d10_table()
    {
        var rolls = Data.Compels.Where(c => c.Kind == CompelKind.Base).Select(c => c.Roll).Order().ToList();
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (int?)i), rolls);
        Assert.All(Data.Compels.Where(c => c.Kind == CompelKind.Rule), c =>
        {
            Assert.Null(c.Roll);
            Assert.False(string.IsNullOrWhiteSpace(c.Trigger), c.Name);
        });
    }

    [Fact]
    public void Edges_cover_every_rules_engine_flag()
    {
        var flags = Data.Edges.Where(e => e.Edge is not null).Select(e => e.Edge!.Value).ToHashSet();
        var expected = Enum.GetValues<NpcEdge>().Where(e => e != NpcEdge.None).ToHashSet();
        Assert.Equal(expected, flags);
    }

    [Theory]
    [InlineData(RuleSource.WodCore, MeritKind.Merit, 44)]
    [InlineData(RuleSource.WodCore, MeritKind.Flaw, 34)]
    [InlineData(RuleSource.Vampire, MeritKind.Merit, 16)]
    [InlineData(RuleSource.Vampire, MeritKind.Flaw, 21)]
    public void Merit_and_flaw_counts_by_book(RuleSource source, MeritKind kind, int expected) =>
        Assert.Equal(expected, Data.Merits.Count(m => m.Source == source && m.Kind == kind));

    [Fact]
    public void Merits_have_text_pages_and_sane_dot_ranges()
    {
        Assert.All(Data.Merits, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Description), m.Name);
            Assert.False(string.IsNullOrWhiteSpace(m.Cost), m.Name);
            Assert.NotNull(m.SourcePage);
            if (m.MinDots is { } min && m.MaxDots is { } max) Assert.True(min <= max, m.Name);
        });
    }

    [Theory]
    [InlineData(PowerGroupKind.CaitiffDiscipline, 9)]
    [InlineData(PowerGroupKind.BloodlineDiscipline, 7)]
    [InlineData(PowerGroupKind.BloodSorcery, 5)]
    [InlineData(PowerGroupKind.SabbatRitae, 2)]
    [InlineData(PowerGroupKind.BasePower, 1)]
    [InlineData(PowerGroupKind.GhoulPower, 1)]
    public void Power_groups_follow_the_table_of_contents(PowerGroupKind kind, int expected) =>
        Assert.Equal(expected, Data.PowerGroups.Count(g => g.Kind == kind));

    [Fact]
    public void Every_bloodline_and_magi_house_owns_exactly_one_power_group()
    {
        foreach (var bloodline in Data.Bloodlines.Where(b => b.Kind != BloodlineKind.Caitiff))
            Assert.Single(Data.PowerGroups, g => g.Bloodline == bloodline.Name);
        Assert.All(Data.PowerGroups.Where(g => g.Kind == PowerGroupKind.CaitiffDiscipline), g => Assert.Null(g.Bloodline));
    }

    [Theory]
    [InlineData("Child", 3, 2)]
    [InlineData("Damned", 2, 1)]
    public void Same_named_flaws_keep_each_books_cost(string name, int coreDots, int vampireDots)
    {
        Assert.Equal(coreDots, Data.Merits.Single(m => m.Name == name && m.Source == RuleSource.WodCore).MinDots);
        Assert.Equal(vampireDots, Data.Merits.Single(m => m.Name == name && m.Source == RuleSource.Vampire).MinDots);
    }
}

public class PowerSeedDataTests
{
    private static readonly CatalogSeedData Data = CatalogSeeder.Load();

    [Fact]
    public void Every_dotted_discipline_has_its_five_levels()
    {
        foreach (var group in Data.PowerGroups.Where(g => g.Kind is PowerGroupKind.CaitiffDiscipline or PowerGroupKind.BloodlineDiscipline))
        {
            var levels = Data.Powers.Where(p => p.Group == group.Name).Select(p => p.Level).ToList();
            Assert.All(levels, l => Assert.InRange(l ?? 0, 1, 5));
            // Investiture's powers are all bought at the first dot, in any order (VtR p.158).
            if (group.Name == "Investiture") Assert.All(levels, l => Assert.Equal(1, l));
            else Assert.Equal([1, 2, 3, 4, 5], levels.Order().Select(l => l!.Value));
        }
    }

    [Fact]
    public void Rituals_and_ritae_have_no_level()
    {
        var kinds = Data.PowerGroups.ToDictionary(g => g.Name, g => g.Kind);
        var learned = Data.Powers.Where(p => kinds[p.Group] is PowerGroupKind.BloodSorcery or PowerGroupKind.SabbatRitae).ToList();
        Assert.NotEmpty(learned);
        Assert.All(learned, p => Assert.Null(p.Level));
    }

    [Fact]
    public void Every_power_group_has_powers()
    {
        foreach (var group in Data.PowerGroups)
            Assert.Contains(Data.Powers, p => p.Group == group.Name);
    }

    [Fact]
    public void Imported_powers_keep_the_books_text()
    {
        var mesmerize = Data.Powers.Single(p => p.Group == "Dominate" && p.Name == "Mesmerize");
        Assert.Equal(2, mesmerize.Level);
        Assert.Equal(2, mesmerize.VitaeCost);
        Assert.Equal(Skill.Expression, mesmerize.DicePoolSkill);
        Assert.Equal("Dominate", mesmerize.DicePoolDiscipline);
        Assert.Equal("target's Wits", mesmerize.OpposedBy);
        Assert.StartsWith("The subject does not obey", mesmerize.DramaticFailureText);

        var aura = Data.Powers.Single(p => p.Name == "Aura Perception");
        Assert.Equal(5, aura.BonusOptions!.Count);
        Assert.Equal("If the target is under Dominion.", aura.BonusOptions[0].Effect);

        Assert.Equal("Chantry", Data.Powers.Single(p => p.Name == "Oppress the Heart").Category);
    }
}

public class CatalogSeederTests : IDisposable
{
    private static readonly CatalogSeedData Data = CatalogSeeder.Load();
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task First_run_adds_everything_and_second_adds_nothing()
    {
        await using (var ctx = _db.CreateContext())
        {
            var first = await CatalogSeeder.SeedAsync(ctx);
            Assert.Equal(new CatalogSeedResult(13, 10, 115, 18, 5, 25, Data.Powers.Count, Concepts: 31), first);
        }

        await using (var ctx = _db.CreateContext())
        {
            Assert.Equal(0, (await CatalogSeeder.SeedAsync(ctx)).Total);
            Assert.Equal(115, await ctx.MeritDefinitions.CountAsync());
        }
    }

    [Fact]
    public async Task Seeded_bloodline_disciplines_link_to_their_bloodline()
    {
        await using var ctx = _db.CreateContext();
        await CatalogSeeder.SeedAsync(ctx);
        var tartarus = await ctx.PowerGroups.Include(g => g.Bloodline).SingleAsync(g => g.Name == "Tartarus");
        Assert.Equal("Brujah", tartarus.Bloodline!.Name);
        Assert.Equal(PowerGroupKind.BloodlineDiscipline, tartarus.Kind);
    }

    [Fact]
    public async Task Gm_edits_survive_reseeding()
    {
        await using (var ctx = _db.CreateContext())
        {
            await CatalogSeeder.SeedAsync(ctx);
            var ventrue = await ctx.Bloodlines.SingleAsync(b => b.Name == "Ventrue");
            ventrue.WeaknessText = "House ruling: prey is chosen at the Embrace.";
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.CreateContext())
        {
            await CatalogSeeder.SeedAsync(ctx);
            var ventrue = await ctx.Bloodlines.SingleAsync(b => b.Name == "Ventrue");
            Assert.Equal("House ruling: prey is chosen at the Embrace.", ventrue.WeaknessText);
        }
    }

    [Fact]
    public async Task A_deleted_entry_is_restored_on_the_next_run()
    {
        await using (var ctx = _db.CreateContext())
        {
            await CatalogSeeder.SeedAsync(ctx);
            ctx.CompelDefinitions.Remove(await ctx.CompelDefinitions.SingleAsync(c => c.Name == "Ravenous"));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.CreateContext())
        {
            var result = await CatalogSeeder.SeedAsync(ctx);
            Assert.Equal(new CatalogSeedResult(0, 0, 0, 1, 0, 0), result);
            Assert.Equal(4, (await ctx.CompelDefinitions.SingleAsync(c => c.Name == "Ravenous")).Roll);
        }
    }

    [Fact]
    public async Task Seeded_powers_are_unreviewed_and_link_their_dice_pool_discipline()
    {
        await using var ctx = _db.CreateContext();
        await CatalogSeeder.SeedAsync(ctx);

        var command = await ctx.Powers.Include(p => p.PowerGroup).SingleAsync(p => p.Name == "Command");
        Assert.Equal("Dominate", command.PowerGroup.Name);
        Assert.Equal(command.PowerGroupId, command.DicePoolPowerGroupId);
        Assert.False(await ctx.Powers.AnyAsync(p => p.IsReviewed));
        Assert.Equal(5, await ctx.Set<PowerBonusOption>().CountAsync(b => b.PowerId == ctx.Powers.Single(p => p.Name == "Aura Perception").Id));
    }

    [Fact]
    public async Task Reviewed_powers_survive_reseeding()
    {
        await using (var ctx = _db.CreateContext())
        {
            await CatalogSeeder.SeedAsync(ctx);
            var awe = await ctx.Powers.SingleAsync(p => p.Name == "Awe");
            awe.SuccessText = "Corrected by the GM.";
            awe.IsReviewed = true;
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.CreateContext())
        {
            Assert.Equal(0, (await CatalogSeeder.SeedAsync(ctx)).Powers);
            var awe = await ctx.Powers.SingleAsync(p => p.Name == "Awe");
            Assert.True(awe.IsReviewed);
            Assert.Equal("Corrected by the GM.", awe.SuccessText);
        }
    }

    [Fact]
    public async Task Seeded_entries_are_usable_from_characters()
    {
        await using var ctx = _db.CreateContext();
        await CatalogSeeder.SeedAsync(ctx);

        var nosferatu = await ctx.Bloodlines.SingleAsync(b => b.Name == "Nosferatu");
        var sandman = await ctx.PredatorTypes.SingleAsync(p => p.Name == "Sandman");
        var touchstone = await ctx.MeritDefinitions.SingleAsync(m => m.Name == "Touchstone");
        ctx.Characters.Add(new Character
        {
            Name = "Edmund",
            Kind = CharacterKind.Vampire,
            Bloodline = nosferatu,
            PredatorType = sandman,
            Merits = [new() { MeritDefinition = touchstone, Name = touchstone.Name, Kind = touchstone.Kind, Dots = 3 }],
        });
        await ctx.SaveChangesAsync();

        Assert.Equal("Hideous Visage of the Beast",
            await ctx.Characters.Where(c => c.Name == "Edmund").Select(c => c.Bloodline!.WeaknessName).SingleAsync());
    }
}
