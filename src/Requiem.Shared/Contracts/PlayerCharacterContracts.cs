using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;

namespace Requiem.Shared.Contracts;

// Player characters (PHASE2.md §5, milestone 2.2): players build them with the creation wizard as a Draft,
// submit them, and the Storyteller approves them (Active) or returns them with a note.

/// <summary>One row of a campaign's player characters, with the profile its card shows.</summary>
public sealed record PlayerCharacterSummary(
    Guid Id,
    Guid CampaignId,
    string Name,
    CharacterKind Kind,
    string? Concept,
    CharacterStatus Status,
    string OwnerName,
    bool IsMine,
    DateTimeOffset UpdatedAt,
    KnownNpc? Profile = null);

/// <summary>
/// A player character as its player or a Storyteller sees it. The sheet never carries the Storyteller's
/// notes when its player is looking.
/// </summary>
/// <param name="CanEdit">The viewer may save changes now: the player while it's a Draft (or its story once Active).</param>
/// <param name="IsStoryteller">The viewer tells the campaign, so may approve, return or retire it.</param>
public sealed record PlayerCharacterView(
    FullSheetForm Sheet,
    CharacterStatus Status,
    string OwnerName,
    bool IsMine,
    bool IsStoryteller,
    bool CanEdit,
    string? ReviewNote,
    string CampaignName,
    CharacterTemplates AllowedTemplates,
    CreationSettings? Creation = null);

/// <summary>The Storyteller sends a submitted character back to its player with what to change.</summary>
public sealed record ReturnCharacterRequest(string? Note);

/// <summary>The Storyteller retires a character, records its death, or brings it back into play.</summary>
public sealed record CharacterStatusRequest(CharacterStatus Status);
