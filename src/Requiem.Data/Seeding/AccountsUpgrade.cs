using Microsoft.EntityFrameworkCore;
using Requiem.Data.Entities;
using Requiem.Shared.Campaigns;

namespace Requiem.Data.Seeding;

/// <summary>What <see cref="AccountsUpgrade.RunAsync"/> changed.</summary>
public sealed record AccountsUpgradeResult(bool RenamedRole, int Campaigns, int Characters, int Scenes)
{
    public bool Changed => RenamedRole || Campaigns > 0;
}

/// <summary>
/// Brings data from before "no account types" (2026-10-24) up to date, idempotently, after migrations:
/// <list type="bullet">
/// <item>The site-wide GameMaster role becomes Admin, which only looks after the rules catalog.</item>
/// <item>Characters and scenes outside any campaign (the old personal roster) move into a new campaign
/// per owner, "Imported NPCs", which the owner tells. Unowned ones, made before Phase 2, go to the
/// first admin.</item>
/// </list>
/// </summary>
public static class AccountsUpgrade
{
    public const string ImportedCampaignName = "Imported NPCs";
    private const string OldRole = "GAMEMASTER";
    private const string AdminRole = "Admin";

    public static async Task<AccountsUpgradeResult> RunAsync(RequiemDbContext db, TimeProvider? time = null, CancellationToken ct = default)
    {
        var now = (time ?? TimeProvider.System).GetUtcNow();

        var renamed = false;
        var old = await db.Roles.SingleOrDefaultAsync(r => r.NormalizedName == OldRole, ct);
        if (old is not null && !await db.Roles.AnyAsync(r => r.NormalizedName == AdminRole.ToUpperInvariant(), ct))
        {
            old.Name = AdminRole;
            old.NormalizedName = AdminRole.ToUpperInvariant();
            old.ConcurrencyStamp = Guid.NewGuid().ToString();
            renamed = true;
        }
        await db.SaveChangesAsync(ct);

        var characters = await db.Characters.Where(c => c.CampaignId == null).ToListAsync(ct);
        var scenes = await db.Scenes.Where(s => s.CampaignId == null).ToListAsync(ct);
        if (characters.Count == 0 && scenes.Count == 0) return new(renamed, 0, 0, 0);

        var userIds = await db.Users.Select(u => u.Id).ToListAsync(ct);
        var admin = await (
                from userRole in db.UserRoles
                join role in db.Roles on userRole.RoleId equals role.Id
                where role.NormalizedName == AdminRole.ToUpperInvariant()
                select userRole.UserId)
            .OrderBy(id => id).FirstOrDefaultAsync(ct);
        // Nobody to give them to yet (a fresh install before setup): try again on the next start.
        string? OwnerOf(string? userId) => userId is not null && userIds.Contains(userId) ? userId : admin;

        var campaigns = new Dictionary<string, Campaign>();
        Campaign? CampaignFor(string? userId)
        {
            if (OwnerOf(userId) is not { } owner) return null;
            if (campaigns.TryGetValue(owner, out var campaign)) return campaign;
            campaign = new Campaign
            {
                Id = Guid.CreateVersion7(now), Name = ImportedCampaignName, CreatedAt = now,
                AllowedTemplates = CharacterTemplates.All,
                Description = "Characters and scenes from before campaigns. Rename this, or move NPCs into your other campaigns.",
            };
            campaign.Members.Add(new CampaignMember { UserId = owner, Role = CampaignRole.Storyteller, JoinedAt = now });
            db.Campaigns.Add(campaign);
            return campaigns[owner] = campaign;
        }

        var movedCharacters = 0;
        foreach (var character in characters)
        {
            if (CampaignFor(character.OwnerUserId) is not { } campaign) continue;
            character.CampaignId = campaign.Id;
            movedCharacters++;
        }
        var movedScenes = 0;
        foreach (var scene in scenes)
        {
            if (CampaignFor(scene.OwnerUserId) is not { } campaign) continue;
            scene.CampaignId = campaign.Id;
            movedScenes++;
        }

        // Links between characters that ended up apart (different owners) would cross campaigns.
        var campaignOf = characters.Where(c => c.CampaignId is not null).ToDictionary(c => c.Id, c => c.CampaignId);
        foreach (var character in characters)
        {
            if (character.SireId is { } sire && campaignOf.TryGetValue(sire, out var sireCampaign) && sireCampaign != character.CampaignId)
                character.SireId = null;
            if (character.DomitorId is { } domitor && campaignOf.TryGetValue(domitor, out var domitorCampaign) && domitorCampaign != character.CampaignId)
                character.DomitorId = null;
        }

        await db.SaveChangesAsync(ct);
        return new(renamed, campaigns.Count, movedCharacters, movedScenes);
    }
}
