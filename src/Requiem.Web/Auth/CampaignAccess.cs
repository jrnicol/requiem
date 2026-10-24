using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Requiem.Data;
using Requiem.Data.Entities;
using Requiem.Shared.Accounts;
using Requiem.Shared.Campaigns;

namespace Requiem.Web.Auth;

/// <summary>
/// What the signed-in user may see and change (PHASE2.md §4). Every endpoint that touches characters or
/// scenes filters through here, so access is enforced on the server rather than by what the UI shows.
/// A character outside the user's reach reads as "not found", so its existence doesn't leak.
/// </summary>
public sealed class CampaignAccess(RequiemDbContext db, IHttpContextAccessor http)
{
    private ClaimsPrincipal User =>
        http.HttpContext?.User ?? throw new InvalidOperationException("CampaignAccess needs a request.");

    public string UserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No signed-in user.");

    /// <summary>Site GMs keep a personal roster and create campaigns.</summary>
    public bool IsSiteGm => User.IsInRole(Roles.GameMaster);

    /// <summary>Campaigns in which the user is a Storyteller.</summary>
    public IQueryable<Guid> StorytellerCampaignIds()
    {
        var userId = UserId;
        return db.CampaignMembers
            .Where(m => m.UserId == userId && m.Role == CampaignRole.Storyteller)
            .Select(m => m.CampaignId);
    }

    /// <summary>
    /// Characters the user runs as GM: a site GM's personal roster (their own, plus characters made
    /// before Phase 2, which have no owner) and every character in a campaign they tell.
    /// </summary>
    public IQueryable<Character> ManagedCharacters(IQueryable<Character>? source = null)
    {
        var userId = UserId;
        var siteGm = IsSiteGm;
        var storyteller = StorytellerCampaignIds();
        return (source ?? db.Characters).Where(c =>
            (c.CampaignId == null && siteGm && (c.OwnerUserId == null || c.OwnerUserId == userId))
            || (c.CampaignId != null && storyteller.Contains(c.CampaignId.Value)));
    }

    /// <summary>Scenes the user runs: personal ones (as for characters) and their campaigns' scenes.</summary>
    public IQueryable<Scene> ManagedScenes(IQueryable<Scene>? source = null)
    {
        var userId = UserId;
        var siteGm = IsSiteGm;
        var storyteller = StorytellerCampaignIds();
        return (source ?? db.Scenes).Where(s =>
            (s.CampaignId == null && siteGm && (s.OwnerUserId == null || s.OwnerUserId == userId))
            || (s.CampaignId != null && storyteller.Contains(s.CampaignId.Value)));
    }

    /// <summary>The user's role in a campaign, or null when they aren't a member (or it doesn't exist).</summary>
    public async Task<CampaignRole?> RoleInAsync(Guid campaignId, CancellationToken ct)
    {
        var userId = UserId;
        return await db.CampaignMembers
            .Where(m => m.CampaignId == campaignId && m.UserId == userId)
            .Select(m => (CampaignRole?)m.Role)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<bool> IsStorytellerAsync(Guid campaignId, CancellationToken ct) =>
        await RoleInAsync(campaignId, ct) == CampaignRole.Storyteller;

    public Task<bool> CanManageCharacterAsync(Guid id, CancellationToken ct) =>
        ManagedCharacters().AnyAsync(c => c.Id == id, ct);

    /// <summary>True when every one of <paramref name="ids"/> is a character the user runs.</summary>
    public async Task<bool> CanManageAllAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 || await ManagedCharacters().CountAsync(c => ids.Contains(c.Id), ct) == ids.Distinct().Count();
}
