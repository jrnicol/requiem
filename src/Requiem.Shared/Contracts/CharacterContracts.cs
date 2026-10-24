using Requiem.Shared.Characters;
using Requiem.Shared.Rules;

namespace Requiem.Shared.Contracts;

/// <summary>One row of the roster.</summary>
/// <param name="Health">Full sheets only: Stamina + Size.</param>
/// <param name="WillpowerDots">Full sheets only.</param>
/// <param name="Morality">Full sheets only: Humanity for vampires, Morality otherwise.</param>
public sealed record CharacterSummary(
    Guid Id,
    string Name,
    CharacterTier Tier,
    CharacterKind Kind,
    string? Concept,
    string? Faction,
    string? BloodlineName,
    int? Physical,
    int? Mental,
    int? Social,
    NpcEdge Edges,
    int? BloodPotency,
    bool IsArchived,
    DateTimeOffset UpdatedAt,
    int? Health = null,
    int? WillpowerDots = null,
    int? Morality = null,
    Guid? CampaignId = null);

/// <param name="CampaignId">Only that campaign's characters; otherwise those of every campaign the user tells.</param>
public sealed record CharacterQuery(
    string? Search = null,
    CharacterTier? Tier = null,
    CharacterKind? Kind = null,
    bool IncludeArchived = false,
    Guid? CampaignId = null);
