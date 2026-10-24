using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Requiem.Data;
using Requiem.Data.Entities;
using Requiem.Shared.Accounts;
using Requiem.Shared.Campaigns;

namespace Requiem.Web.Auth;

/// <summary>
/// What the signed-in user may see and change (PHASE2.md §4). There are no Storyteller accounts: the
/// user's role comes from each campaign they belong to, so the same person can tell one campaign and
/// play in another. Every endpoint that touches characters or scenes filters through here, so access is
/// enforced on the server rather than by what the UI shows. Anything outside the user's reach reads as
/// "not found", so its existence doesn't leak.
/// </summary>
public sealed class CampaignAccess(RequiemDbContext db, IHttpContextAccessor http)
{
    private ClaimsPrincipal User =>
        http.HttpContext?.User ?? throw new InvalidOperationException("CampaignAccess needs a request.");

    public string UserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No signed-in user.");

    /// <summary>Looks after the shared rules catalog. Gives no say in anyone's campaign.</summary>
    public bool IsAdmin => User.IsInRole(Roles.Admin);

    /// <summary>Campaigns in which the user is a Storyteller.</summary>
    public IQueryable<Guid> StorytellerCampaignIds()
    {
        var userId = UserId;
        return db.CampaignMembers
            .Where(m => m.UserId == userId && m.Role == CampaignRole.Storyteller)
            .Select(m => m.CampaignId);
    }

    /// <summary>Characters the user runs: every character in a campaign they tell.</summary>
    public IQueryable<Character> ManagedCharacters(IQueryable<Character>? source = null)
    {
        var storyteller = StorytellerCampaignIds();
        return (source ?? db.Characters).Where(c => c.CampaignId != null && storyteller.Contains(c.CampaignId.Value));
    }

    /// <summary>Scenes the user runs: those of the campaigns they tell.</summary>
    public IQueryable<Scene> ManagedScenes(IQueryable<Scene>? source = null)
    {
        var storyteller = StorytellerCampaignIds();
        return (source ?? db.Scenes).Where(s => s.CampaignId != null && storyteller.Contains(s.CampaignId.Value));
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

    /// <summary>Why the user can't add a character or scene to <paramref name="campaignId"/>, or null when they can.</summary>
    public async Task<string?> CampaignProblemAsync(Guid? campaignId, CancellationToken ct) =>
        campaignId is not { } id ? "Choose a campaign."
        : await IsStorytellerAsync(id, ct) ? null
        : "Unknown campaign.";

    public Task<bool> CanManageCharacterAsync(Guid id, CancellationToken ct) =>
        ManagedCharacters().AnyAsync(c => c.Id == id, ct);

    /// <summary>True when every one of <paramref name="ids"/> is a character of <paramref name="campaignId"/> the user runs.</summary>
    public async Task<bool> AllInCampaignAsync(Guid? campaignId, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0
        || await ManagedCharacters().CountAsync(c => c.CampaignId == campaignId && ids.Contains(c.Id), ct) == ids.Distinct().Count();
}
