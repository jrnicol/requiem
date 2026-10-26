namespace Requiem.Shared.Campaigns;

// Phase 2: campaigns (chronicles) with Storytellers and players. See PHASE2.md.

/// <summary>A user's part in one campaign. The same user can be a Storyteller in one and a player in another.</summary>
public enum CampaignRole { Storyteller, Player }

/// <summary>Which character templates a campaign lets its players build (PHASE2.md decision 2).</summary>
[Flags]
public enum CharacterTemplates
{
    None = 0,
    Mortal = 1,
    Ghoul = 2,
    Vampire = 4,
    All = Mortal | Ghoul | Vampire,
}

/// <summary>Where a character is in its life: player characters start as drafts and need approval.</summary>
public enum CharacterStatus
{
    /// <summary>A player is still building it.</summary>
    Draft,
    /// <summary>Waiting for the Storyteller's approval.</summary>
    Submitted,
    /// <summary>In play. Every GM-made NPC starts here.</summary>
    Active,
    Retired,
    Dead,
}

/// <summary>Whether players know an NPC exists. Known NPCs show players a public card, never the sheet.</summary>
public enum NpcVisibility { Hidden, Known }

/// <summary>
/// What a Known NPC's public card shows players, beyond its name. The Storyteller reveals more as the players
/// find out; anything not revealed stays off the card.
/// </summary>
[Flags]
public enum NpcReveal
{
    None = 0,
    Portrait = 1,
    Faction = 2,
    Bloodline = 4,
    Generation = 8,
    Appearance = 16,
    Reputation = 32,
    Backstory = 64,
    /// <summary>Vampire, ghoul or mortal.</summary>
    Kind = 128,
}

/// <summary>The three ways to earn a Link with other player characters (WoD Core p.75).</summary>
public enum LinkKind { Camaraderie, Support, CedeSpotlight }
