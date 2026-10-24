using Microsoft.EntityFrameworkCore;
using Requiem.Data.Entities;
using Requiem.Shared.Characters;
using Requiem.Shared.Rules;

namespace Requiem.Data.Tests;

public class CharacterPersistenceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Full_sheet_round_trips_with_its_parts()
    {
        Guid id;
        await using (var ctx = _db.CreateContext())
        {
            var auspex = new PowerGroup { Name = "Auspex", Kind = PowerGroupKind.CaitiffDiscipline };
            var character = new Character
            {
                Name = "Marguerite Vance",
                Tier = CharacterTier.FullSheet,
                Kind = CharacterKind.Vampire,
                Generation = 12,
                BloodPotency = 2,
                Humanity = 6,
                Attributes = [new() { Attribute = CharacterAttribute.Wits, Dots = 3 }],
                Skills = [new() { Skill = Skill.Empathy, Dots = 2 }],
                Specialties = [new() { Skill = Skill.Empathy, Name = "Lies" }],
                Disciplines = [new() { PowerGroup = auspex, Dots = 2 }],
                Wounds = [new() { Description = "knife", Amount = 3, Type = DamageType.Lethal }],
                Scripts = [new() { Slot = 1, Kind = ScriptKind.Humanity, Text = "Protect my sister" }],
                Tags = [new() { Name = "Primogen" }],
            };
            ctx.Characters.Add(character);
            await ctx.SaveChangesAsync();
            id = character.Id;
        }

        await using (var ctx = _db.CreateContext())
        {
            var loaded = await ctx.Characters
                .Include(c => c.Attributes).Include(c => c.Skills).Include(c => c.Specialties)
                .Include(c => c.Disciplines).ThenInclude(d => d.PowerGroup)
                .Include(c => c.Wounds).Include(c => c.Scripts).Include(c => c.Tags)
                .AsSplitQuery()
                .SingleAsync(c => c.Id == id);

            Assert.Equal(CharacterAttribute.Wits, Assert.Single(loaded.Attributes).Attribute);
            Assert.Equal("Lies", Assert.Single(loaded.Specialties).Name);
            Assert.Equal("Auspex", Assert.Single(loaded.Disciplines).PowerGroup.Name);
            Assert.Equal(DamageType.Lethal, Assert.Single(loaded.Wounds).Type);
            Assert.Equal("Primogen", Assert.Single(loaded.Tags).Name);
        }
    }

    [Fact]
    public async Task Saving_stamps_id_timestamps_and_version()
    {
        await using var ctx = _db.CreateContext();
        var character = new Character { Name = "Mook", Tier = CharacterTier.SupportingCast, Physical = 2, Mental = 1, Social = 1 };
        ctx.Characters.Add(character);
        await ctx.SaveChangesAsync();

        Assert.NotEqual(Guid.Empty, character.Id);
        Assert.Equal(_db.Time.GetUtcNow(), character.CreatedAt);
        var firstVersion = character.Version;
        Assert.NotEqual(Guid.Empty, firstVersion);

        _db.Time.Advance(TimeSpan.FromMinutes(5));
        character.Notes = "Bouncer at Elysium";
        await ctx.SaveChangesAsync();

        Assert.Equal(_db.Time.GetUtcNow(), character.UpdatedAt);
        Assert.NotEqual(character.CreatedAt, character.UpdatedAt);
        Assert.NotEqual(firstVersion, character.Version);
    }

    [Fact]
    public async Task Concurrent_edits_are_detected()
    {
        Guid id;
        await using (var ctx = _db.CreateContext())
        {
            var c = new Character { Name = "Contested" };
            ctx.Characters.Add(c);
            await ctx.SaveChangesAsync();
            id = c.Id;
        }

        await using var first = _db.CreateContext();
        await using var second = _db.CreateContext();
        var a = await first.Characters.SingleAsync(c => c.Id == id);
        var b = await second.Characters.SingleAsync(c => c.Id == id);

        a.Notes = "GM tablet";
        await first.SaveChangesAsync();
        b.Notes = "GM phone";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Enums_are_stored_as_text()
    {
        await using var ctx = _db.CreateContext();
        ctx.Characters.Add(new Character { Name = "Text check", Tier = CharacterTier.SupportingCast, Physical = 1, Mental = 1, Social = 1 });
        await ctx.SaveChangesAsync();

        var tier = await ctx.Database.SqlQueryRaw<string>("SELECT Tier AS Value FROM Characters").SingleAsync();
        Assert.Equal("SupportingCast", tier);
    }

    [Fact]
    public async Task Check_constraints_reject_out_of_range_values()
    {
        await using var ctx = _db.CreateContext();
        ctx.Characters.Add(new Character { Name = "Too strong", Tier = CharacterTier.SupportingCast, Physical = 6, Mental = 1, Social = 1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Script_slots_are_unique_per_character()
    {
        await using var ctx = _db.CreateContext();
        ctx.Characters.Add(new Character
        {
            Name = "Two firsts",
            Scripts = [new() { Slot = 1, Text = "a" }, new() { Slot = 1, Text = "b" }],
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Catalog_power_group_in_use_cannot_be_deleted()
    {
        await using var ctx = _db.CreateContext();
        var furor = new PowerGroup { Name = "Furor", Kind = PowerGroupKind.BloodlineDiscipline };
        ctx.Characters.Add(new Character { Name = "Brute", Disciplines = [new() { PowerGroup = furor, Dots = 1 }] });
        await ctx.SaveChangesAsync();

        await using var other = _db.CreateContext();
        other.PowerGroups.Remove(await other.PowerGroups.SingleAsync(g => g.Name == "Furor"));
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }
}
