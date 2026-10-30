using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;

namespace Requiem.Shared.Contracts;

/// <summary>
/// The parts of a character that describe them rather than their traits, on every kind of sheet: a portrait,
/// how they look, what people say about them, and their history. For NPCs, also what players may see.
/// </summary>
public interface ICharacterProfile
{
    /// <summary>What others notice: looks, manner, dress.</summary>
    string? Appearance { get; set; }

    /// <summary>What people say about them.</summary>
    string? Reputation { get; set; }

    string? Backstory { get; set; }

    /// <summary>Set by the portrait endpoints (upload or remove); ignored when the form is saved.</summary>
    string? PortraitUrl { get; set; }

    /// <summary>NPCs: the campaign's players see this NPC's public card. Ignored for player characters.</summary>
    bool IsShared { get; set; }

    /// <summary>NPCs: what the public card shows beyond the name.</summary>
    NpcReveal Reveals { get; set; }

    /// <summary>The sheet's concurrency token; a portrait change gives the character a new one.</summary>
    Guid Version { get; set; }
}

public static class CharacterProfile
{
    public const int MaxDescriptionLength = 4000;
    public const int MaxBackstoryLength = 8000;
}

/// <summary>
/// A character's profile. For a Known NPC as the campaign's players see it, only what the Storyteller has revealed is set;
/// a player character's profile has every detail, with <see cref="PlayedBy"/> and <see cref="Concept"/>. <see cref="Tier"/>
/// is set only for those who can open the character's editor (the campaign's Storytellers, and a player character's player),
/// who need it for the links; <see cref="CanPlay"/> says whether they can also open it at the table.
/// </summary>
public sealed record KnownNpc(
    Guid Id,
    string Name,
    string? PortraitUrl,
    string? Faction,
    string? Bloodline,
    int? Generation,
    string? Appearance,
    string? Reputation,
    string? Backstory,
    CharacterKind? Kind = null,
    CharacterTier? Tier = null,
    IReadOnlyList<string>? Titles = null,
    bool IsPlayerCharacter = false,
    string? PlayedBy = null,
    bool CanPlay = false,
    string? Concept = null,
    string? BloodlineKind = null)
{
    /// <summary>"House" for a Magi, "Bloodline" otherwise.</summary>
    public string BloodlineLabel => Rules.BloodlineRules.KindLabel(BloodlineKind);
}

/// <summary>After a portrait upload or removal: its address (null when removed) and the character's new version.</summary>
public sealed record PortraitResult(string? PortraitUrl, Guid Version);
