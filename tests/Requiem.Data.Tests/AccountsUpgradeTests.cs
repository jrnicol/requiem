using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Requiem.Data.Entities;
using Requiem.Data.Seeding;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;

namespace Requiem.Data.Tests;

/// <summary>
/// "No account types" (2026-10-24): the GameMaster role becomes Admin, and the old personal roster moves
/// into a campaign its owner tells.
/// </summary>
public class AccountsUpgradeTests
{
    [Fact]
    public async Task The_gamemaster_role_becomes_admin()
    {
        using var db = new SqliteTestDatabase();
        await using (var ctx = db.CreateContext())
        {
            ctx.Roles.Add(new IdentityRole("GameMaster") { NormalizedName = "GAMEMASTER" });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = db.CreateContext())
            Assert.True((await AccountsUpgrade.RunAsync(ctx, db.Time)).RenamedRole);

        await using var read = db.CreateContext();
        Assert.Equal(("Admin", "ADMIN"), await read.Roles.Select(r => ValueTuple.Create(r.Name, r.NormalizedName)).SingleAsync());
    }

    [Fact]
    public async Task Loose_characters_and_scenes_move_into_a_campaign_their_owner_tells()
    {
        using var db = new SqliteTestDatabase();
        Guid legacy, alices, bobs, scene;
        await using (var ctx = db.CreateContext())
        {
            var admin = AddUser(ctx, "admin");
            ctx.Roles.Add(new IdentityRole("Admin") { Id = "r-admin", NormalizedName = "ADMIN" });
            ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = admin.Id, RoleId = "r-admin" });
            AddUser(ctx, "bob");

            // Made before Phase 2 (no owner), by the admin, and by another user; one sired by another owner's.
            var legacyNpc = Npc("Legacy Marcus", owner: null);
            var alicesNpc = Npc("Admin's Prince", owner: "admin");
            var bobsNpc = Npc("Bob's Childe", owner: "bob", sire: alicesNpc);
            ctx.Characters.AddRange(legacyNpc, alicesNpc, bobsNpc);
            var legacyScene = new Scene { Id = Guid.NewGuid(), Name = "Old Elysium", CreatedAt = db.Time.GetUtcNow(), UpdatedAt = db.Time.GetUtcNow() };
            ctx.Scenes.Add(legacyScene);
            await ctx.SaveChangesAsync();
            (legacy, alices, bobs, scene) = (legacyNpc.Id, alicesNpc.Id, bobsNpc.Id, legacyScene.Id);
        }

        await using (var ctx = db.CreateContext())
        {
            var result = await AccountsUpgrade.RunAsync(ctx, db.Time);
            Assert.Equal((2, 3, 1), (result.Campaigns, result.Characters, result.Scenes));
        }

        await using var read = db.CreateContext();
        var campaigns = await read.Campaigns.Include(c => c.Members).ToListAsync();
        Assert.All(campaigns, c => Assert.Equal(AccountsUpgrade.ImportedCampaignName, c.Name));
        var adminsCampaign = campaigns.Single(c => c.Members.Single().UserId == "admin");
        var bobsCampaign = campaigns.Single(c => c.Members.Single().UserId == "bob");
        Assert.All(campaigns, c => Assert.Equal(CampaignRole.Storyteller, c.Members.Single().Role));

        var characters = await read.Characters.AsNoTracking().ToDictionaryAsync(c => c.Id);
        // Unowned characters and scenes go to the admin, alongside the admin's own.
        Assert.Equal(adminsCampaign.Id, characters[legacy].CampaignId);
        Assert.Equal(adminsCampaign.Id, characters[alices].CampaignId);
        Assert.Equal(adminsCampaign.Id, (await read.Scenes.SingleAsync(s => s.Id == scene)).CampaignId);
        // Bob's childe ended up apart from its sire, so the link is cut.
        Assert.Equal(bobsCampaign.Id, characters[bobs].CampaignId);
        Assert.Null(characters[bobs].SireId);

        // Running it again changes nothing.
        await using var again = db.CreateContext();
        Assert.False((await AccountsUpgrade.RunAsync(again, db.Time)).Changed);
    }

    [Fact]
    public async Task Unowned_characters_wait_until_there_is_an_admin()
    {
        using var db = new SqliteTestDatabase();
        await using (var ctx = db.CreateContext())
        {
            ctx.Characters.Add(Npc("Before setup", owner: null));
            await ctx.SaveChangesAsync();
        }

        await using var run = db.CreateContext();
        Assert.Equal(0, (await AccountsUpgrade.RunAsync(run, db.Time)).Characters);
        Assert.Null((await run.Characters.SingleAsync()).CampaignId);
    }

    private static RequiemUser AddUser(RequiemDbContext ctx, string id)
    {
        var user = new RequiemUser { Id = id, UserName = $"{id}@example.test", Email = $"{id}@example.test", DisplayName = id };
        ctx.Users.Add(user);
        return user;
    }

    private static Character Npc(string name, string? owner, Character? sire = null) => new()
    {
        Name = name, Tier = CharacterTier.FullSheet, Kind = CharacterKind.Vampire, OwnerUserId = owner, Sire = sire,
        WillpowerDots = 2, WillpowerCurrent = 2,
    };
}
