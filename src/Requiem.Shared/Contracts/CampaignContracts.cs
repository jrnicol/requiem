using System.ComponentModel.DataAnnotations;
using Requiem.Shared.Accounts;
using Requiem.Shared.Campaigns;

namespace Requiem.Shared.Contracts;

// Campaigns, members and invite links (PHASE2.md milestone 2.1).

/// <summary>One row of "my campaigns".</summary>
public sealed record CampaignSummary(
    Guid Id,
    string Name,
    string? Description,
    CampaignRole MyRole,
    int PlayerCount,
    int CharacterCount,
    DateTimeOffset? ArchivedAt);

/// <param name="Email">Shown to Storytellers only.</param>
/// <param name="IsMe">The member is the signed-in user.</param>
public sealed record CampaignMemberView(string UserId, string DisplayName, string? Email, CampaignRole Role, DateTimeOffset JoinedAt, bool IsMe = false);

public sealed record CampaignInviteView(
    Guid Id,
    CampaignRole Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int? UsesLeft,
    DateTimeOffset? RevokedAt)
{
    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now && UsesLeft is not 0;
}

/// <summary>A campaign as one member sees it. Invites are listed for Storytellers only.</summary>
public sealed record CampaignView(
    Guid Id,
    string Name,
    string? Description,
    string? ChronicleTenet,
    string? CoterieScript,
    CharacterTemplates AllowedTemplates,
    DateTimeOffset? ArchivedAt,
    CampaignRole MyRole,
    IReadOnlyList<CampaignMemberView> Members,
    IReadOnlyList<CampaignInviteView> Invites,
    CreationSettings? Creation = null)
{
    public bool IsStoryteller => MyRole == CampaignRole.Storyteller;
}

/// <summary>Create or edit a campaign's settings (Storytellers).</summary>
public sealed class CampaignForm : IValidatableObject
{
    [Required(ErrorMessage = "Give the campaign a name."), StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(4000)] public string? Description { get; set; }

    /// <summary>The fourth Script, the theme everyone plays to (VtR p.136).</summary>
    [StringLength(500)] public string? ChronicleTenet { get; set; }

    [StringLength(500)] public string? CoterieScript { get; set; }

    public CharacterTemplates AllowedTemplates { get; set; } = CharacterTemplates.Vampire;

    public CreationSettings Creation { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((AllowedTemplates & CharacterTemplates.All) == CharacterTemplates.None)
            yield return new ValidationResult("Allow at least one kind of character.", [nameof(AllowedTemplates)]);
        // The nested settings aren't validated on their own.
        if (Creation is null) yield break;
        var creation = new List<ValidationResult>();
        Validator.TryValidateObject(Creation, new ValidationContext(Creation), creation, validateAllProperties: true);
        foreach (var result in creation)
            yield return new ValidationResult(result.ErrorMessage, result.MemberNames.Select(m => $"{nameof(Creation)}.{m}"));
    }
}

/// <summary>Any member may edit the shared Coterie Script (PHASE2.md decision 7); edits are audited.</summary>
public sealed class CoterieScriptForm
{
    [StringLength(500)] public string? Text { get; set; }
}

public sealed class InviteForm
{
    [Range(1, 90, ErrorMessage = "Links last 1 to 90 days.")]
    public int ExpiresInDays { get; set; } = 14;

    /// <summary>Null for any number of players until it expires.</summary>
    [Range(1, 50, ErrorMessage = "Allow 1 to 50 uses, or leave it unlimited.")]
    public int? MaxUses { get; set; }
}

/// <summary>A new invite. The token appears here once and is never stored or shown again.</summary>
public sealed record CreatedInvite(CampaignInviteView Invite, string Token)
{
    public string JoinPath => $"join/{Token}";
}

/// <summary>What the join page shows before anyone signs in.</summary>
/// <param name="Problem">Why the link can't be used (expired, used up, revoked, unknown); null when it can.</param>
public sealed record InvitePreview(string? CampaignName, string? StorytellerName, CampaignRole Role, string? Problem)
{
    public bool IsUsable => Problem is null;
}

/// <summary>The result of accepting an invite or joining through one.</summary>
public sealed record JoinedCampaign(Guid CampaignId, CurrentUser User);

/// <summary>Moves an NPC into another campaign. The user must tell both.</summary>
public sealed record MoveCharacterRequest(Guid CampaignId);
