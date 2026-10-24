using Microsoft.EntityFrameworkCore;
using Requiem.Data.Entities;

namespace Requiem.Data.Tests;

public class CharacterLinkTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Deleting_a_character_clears_links_and_keeps_others()
    {
        Guid sireId, childId, rivalId;
        await using (var ctx = _db.CreateContext())
        {
            var sire = new Character { Name = "Sire" };
            var child = new Character { Name = "Childe", Sire = sire };
            var rival = new Character { Name = "Rival" };
            ctx.AddRange(sire, child, rival);
            ctx.Relationships.Add(new CharacterRelationship { FromCharacter = rival, ToCharacter = sire, Kind = RelationshipKind.Rival });
            ctx.Relationships.Add(new CharacterRelationship { FromCharacter = rival, ToName = "Detective Ruiz", Kind = RelationshipKind.Contact });
            ctx.BloodBonds.Add(new BloodBond { Thrall = child, Regnant = sire, Stage = 3 });
            ctx.Boons.Add(new Boon { Debtor = rival, Creditor = sire, Reason = "Safe passage" });
            await ctx.SaveChangesAsync();
            (sireId, childId, rivalId) = (sire.Id, child.Id, rival.Id);
        }

        await using (var ctx = _db.CreateContext())
            Assert.True(await ctx.DeleteCharacterAsync(sireId));

        await using (var ctx = _db.CreateContext())
        {
            Assert.False(await ctx.Characters.AnyAsync(c => c.Id == sireId));
            Assert.Null((await ctx.Characters.SingleAsync(c => c.Id == childId)).SireId);
            Assert.Empty(await ctx.BloodBonds.ToListAsync());
            Assert.Empty(await ctx.Boons.ToListAsync());
            // The rival's link to someone outside the app survives.
            var remaining = Assert.Single(await ctx.Relationships.ToListAsync());
            Assert.Equal(rivalId, remaining.FromCharacterId);
            Assert.Equal("Detective Ruiz", remaining.ToName);
        }
    }

    [Fact]
    public async Task Deleting_a_missing_character_returns_false()
    {
        await using var ctx = _db.CreateContext();
        Assert.False(await ctx.DeleteCharacterAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Deleting_a_character_removes_its_own_rows()
    {
        Guid id;
        await using (var ctx = _db.CreateContext())
        {
            var c = new Character
            {
                Name = "Doomed",
                Wounds = [new() { Description = "sunlight", Amount = 4, Type = Shared.Rules.DamageType.Aggravated }],
                XpLedger = [new() { Amount = 4, Reason = "Scripts" }],
            };
            ctx.Characters.Add(c);
            await ctx.SaveChangesAsync();
            id = c.Id;
        }

        await using (var ctx = _db.CreateContext())
            await ctx.DeleteCharacterAsync(id);

        await using (var ctx = _db.CreateContext())
        {
            Assert.Equal(0, await ctx.Set<CharacterWound>().CountAsync());
            Assert.Equal(0, await ctx.Set<XpEntry>().CountAsync());
        }
    }

    [Fact]
    public async Task Blood_bond_to_self_is_rejected()
    {
        await using var ctx = _db.CreateContext();
        var c = new Character { Name = "Narcissus" };
        ctx.Characters.Add(c);
        ctx.BloodBonds.Add(new BloodBond { Thrall = c, Regnant = c });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
