using System.Security.Claims;
using Requiem.Data.Entities;
using Requiem.Data.Stores;
using Requiem.Shared.Accounts;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;

namespace Requiem.Web.Auth;

/// <summary>
/// What the signed-in user may see and change (PHASE2.md §4). There are no Storyteller accounts: the
/// user's role comes from each campaign they belong to, so the same person can tell one campaign and
/// play in another. Every endpoint that touches characters or scenes goes through here, so access is
/// enforced on the server rather than by what the UI shows. Anything outside the user's reach reads as
/// "not found", so its existence doesn't leak.
/// </summary>
/// <remarks>The user's campaigns and roles are read once per request; characters are then checked in memory.</remarks>
public sealed class CampaignAccess(ICampaignStore campaigns, ICharacterStore characters, ISceneStore scenes, IHttpContextAccessor http)
{
    private IReadOnlyDictionary<Guid, CampaignRole>? _roles;

    private ClaimsPrincipal User =>
        http.HttpContext?.User ?? throw new InvalidOperationException("CampaignAccess needs a request.");

    public string UserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No signed-in user.");

    /// <summary>Looks after the shared rules catalog. Gives no say in anyone's campaign.</summary>
    public bool IsAdmin => User.IsInRole(Roles.Admin);

    /// <summary>Every campaign the user belongs to, and their role in it.</summary>
    public async Task<IReadOnlyDictionary<Guid, CampaignRole>> RolesAsync(CancellationToken ct) =>
        _roles ??= await campaigns.RolesOfAsync(UserId, ct);

    /// <summary>Campaigns in which the user is a Storyteller.</summary>
    public async Task<List<Guid>> StorytellerCampaignIdsAsync(CancellationToken ct) =>
        (await RolesAsync(ct)).Where(r => r.Value == CampaignRole.Storyteller).Select(r => r.Key).ToList();

    /// <summary>The user's role in a campaign, or null when they aren't a member (or it doesn't exist).</summary>
    public async Task<CampaignRole?> RoleInAsync(Guid campaignId, CancellationToken ct) =>
        (await RolesAsync(ct)).TryGetValue(campaignId, out var role) ? role : null;

    public async Task<bool> IsStorytellerAsync(Guid campaignId, CancellationToken ct) =>
        await RoleInAsync(campaignId, ct) == CampaignRole.Storyteller;

    /// <summary>Why the user can't add a character or scene to <paramref name="campaignId"/>, or null when they can.</summary>
    public async Task<string?> CampaignProblemAsync(Guid? campaignId, CancellationToken ct) =>
        campaignId is not { } id ? "Choose a campaign."
        : await IsStorytellerAsync(id, ct) ? null
        : "Unknown campaign.";

    // ---------- Which characters ----------

    /// <summary>Characters the user runs: every character in a campaign they tell.</summary>
    public async Task<bool> ManagesAsync(Character c, CancellationToken ct) =>
        c.CampaignId is { } id && await IsStorytellerAsync(id, ct);

    /// <summary>The user's own player characters, in campaigns they still belong to.</summary>
    public async Task<bool> OwnsAsync(Character c, CancellationToken ct) =>
        c.IsPlayerCharacter && c.OwnerUserId == UserId && c.CampaignId is { } id && await RoleInAsync(id, ct) is not null;

    /// <summary>Player characters the user may open: their own, and every PC in a campaign they tell.</summary>
    public async Task<bool> SeesPlayerCharacterAsync(Character c, CancellationToken ct) =>
        c.IsPlayerCharacter && (await ManagesAsync(c, ct) || await OwnsAsync(c, ct));

    /// <summary>
    /// Characters the user can track at the table: everything they run as Storyteller, plus their own
    /// approved (Active) player characters.
    /// </summary>
    public async Task<bool> CanPlayAsync(Character c, CancellationToken ct) =>
        await ManagesAsync(c, ct) || (c.Status == CharacterStatus.Active && await OwnsAsync(c, ct));

    public async Task<Character?> ManagedCharacterAsync(Guid id, CancellationToken ct) =>
        await characters.GetAsync(id, ct) is { } c && await ManagesAsync(c, ct) ? c : null;

    public async Task<Character?> OwnCharacterAsync(Guid id, CancellationToken ct) =>
        await characters.GetAsync(id, ct) is { } c && await OwnsAsync(c, ct) ? c : null;

    public async Task<Character?> VisiblePlayerCharacterAsync(Guid id, CancellationToken ct) =>
        await characters.GetAsync(id, ct) is { } c && await SeesPlayerCharacterAsync(c, ct) ? c : null;

    public async Task<Character?> PlayableCharacterAsync(Guid id, CancellationToken ct) =>
        await characters.GetAsync(id, ct) is { } c && await CanPlayAsync(c, ct) ? c : null;

    /// <summary>Every character the user runs, or only those in <paramref name="campaignId"/>.</summary>
    public async Task<List<Character>> ManagedCharactersAsync(Guid? campaignId, CancellationToken ct)
    {
        var ids = await StorytellerCampaignIdsAsync(ct);
        if (campaignId is { } only) ids = ids.Contains(only) ? [only] : [];
        var all = new List<Character>();
        foreach (var id in ids) all.AddRange(await characters.ListByCampaignAsync(id, ct));
        return all;
    }

    public async Task<bool> CanManageCharacterAsync(Guid id, CancellationToken ct) =>
        await ManagedCharacterAsync(id, ct) is not null;

    /// <summary>True when every one of <paramref name="ids"/> is a character of <paramref name="campaignId"/> the user runs.</summary>
    public async Task<bool> AllInCampaignAsync(Guid? campaignId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return true;
        if (campaignId is not { } id || !await IsStorytellerAsync(id, ct)) return false;
        var wanted = ids.Distinct().ToList();
        var found = await characters.GetManyAsync(wanted, ct);
        return found.Count(c => c.CampaignId == id) == wanted.Count;
    }

    // ---------- Scenes ----------

    /// <summary>Scenes the user runs: those of the campaigns they tell.</summary>
    public async Task<Scene?> ManagedSceneAsync(Guid id, CancellationToken ct) =>
        await scenes.GetAsync(id, ct) is { CampaignId: { } campaignId } scene && await IsStorytellerAsync(campaignId, ct) ? scene : null;

    public async Task<List<Scene>> ManagedScenesAsync(Guid? campaignId, CancellationToken ct)
    {
        var ids = await StorytellerCampaignIdsAsync(ct);
        if (campaignId is { } only) ids = ids.Contains(only) ? [only] : [];
        var all = new List<Scene>();
        foreach (var id in ids) all.AddRange(await scenes.ListByCampaignAsync(id, ct));
        return all;
    }
}
