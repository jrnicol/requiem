using Microsoft.EntityFrameworkCore;
using Requiem.Data.Entities;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;

namespace Requiem.Data.Tests;

/// <summary>The Phase 2 migration must leave existing characters playable and owned by the GM roster.</summary>
public class CampaignUpgradeTests
{
    [Fact]
    public void Existing_characters_come_through_as_active_hidden_and_unowned()
    {
        using var db = new SqliteTestDatabase(upToMigration: "AddScenesAndConditions");
        var id = Guid.NewGuid();
        db.Execute($"""
            INSERT INTO Characters (Id, Name, Tier, Kind, Edges, Size, IsThinblood, PotenceBoost, CelerityBoost, FortitudeBoost,
                IsInTorpor, IsStaked, IsMasquerading, IsUnderDominion, IsArchived, CreatedAt, UpdatedAt, Version)
            VALUES ('{id.ToString().ToUpperInvariant()}', 'Marcus Vale', 'SupportingCast', 'Vampire', 0, 5, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, '{Guid.NewGuid().ToString().ToUpperInvariant()}')
            """);

        db.MigrateToLatest();

        using var ctx = db.CreateContext();
        var marcus = ctx.Characters.AsNoTracking().Single(c => c.Id == id);
        Assert.Equal(CharacterStatus.Active, marcus.Status);
        Assert.Equal(NpcVisibility.Hidden, marcus.Visibility);
        Assert.False(marcus.IsPlayerCharacter);
        Assert.Null(marcus.OwnerUserId);
        Assert.Null(marcus.CampaignId);
    }

    [Fact]
    public async Task Campaigns_hold_members_invites_links_and_audit_entries()
    {
        using var db = new SqliteTestDatabase();
        await using var ctx = db.CreateContext();
        var user = new RequiemUser { Id = "u1", UserName = "st@example.test", Email = "st@example.test", DisplayName = "ST" };
        ctx.Users.Add(user);
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(), Name = "Chicago by Night", AllowedTemplates = CharacterTemplates.Vampire | CharacterTemplates.Ghoul,
            CoterieScript = "We never leave one behind", CreatedAt = db.Time.GetUtcNow(),
        };
        campaign.Members.Add(new CampaignMember { UserId = user.Id, Role = CampaignRole.Storyteller, JoinedAt = db.Time.GetUtcNow() });
        campaign.Invites.Add(new CampaignInvite
        {
            Id = Guid.NewGuid(), TokenHash = new string('a', 64), ExpiresAt = db.Time.GetUtcNow().AddDays(7), UsesLeft = 3,
            CreatedByUserId = user.Id, CreatedAt = db.Time.GetUtcNow(),
        });
        ctx.Campaigns.Add(campaign);

        var pc = new Character
        {
            Name = "Ilsa", Tier = CharacterTier.FullSheet, Kind = CharacterKind.Ghoul, IsPlayerCharacter = true,
            Status = CharacterStatus.Draft, OwnerUserId = user.Id, CampaignId = campaign.Id, DomitorName = "Mister Grey",
            Links = [new CharacterLink { Kind = LinkKind.Support, WithCharacterIds = [Guid.NewGuid()], At = db.Time.GetUtcNow() }],
        };
        ctx.Characters.Add(pc);
        ctx.AuditEntries.Add(new AuditEntry
        {
            CampaignId = campaign.Id, CharacterId = pc.Id, UserId = user.Id, Action = "link.add", Summary = "Support", At = db.Time.GetUtcNow(),
        });
        await ctx.SaveChangesAsync();

        await using var read = db.CreateContext();
        var saved = await read.Campaigns.Include(c => c.Members).Include(c => c.Invites).SingleAsync();
        Assert.Equal(CharacterTemplates.Vampire | CharacterTemplates.Ghoul, saved.AllowedTemplates);
        Assert.Equal(CampaignRole.Storyteller, Assert.Single(saved.Members).Role);
        Assert.Equal(3, Assert.Single(saved.Invites).UsesLeft);
        var savedPc = await read.Characters.Include(c => c.Links).SingleAsync(c => c.Id == pc.Id);
        Assert.Equal((CharacterStatus.Draft, "Mister Grey"), (savedPc.Status, savedPc.DomitorName));
        Assert.Single(Assert.Single(savedPc.Links).WithCharacterIds);
        Assert.Equal("link.add", (await read.AuditEntries.SingleAsync()).Action);

        // The database refuses to delete a campaign that still has characters. (A context that has the
        // characters loaded would detach them instead, so the delete endpoint must also check first.)
        await using var delete = db.CreateContext();
        delete.Campaigns.Remove(await delete.Campaigns.SingleAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => delete.SaveChangesAsync());
    }
}
