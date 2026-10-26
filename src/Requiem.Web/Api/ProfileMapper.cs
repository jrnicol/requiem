using Requiem.Data.Entities;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Contracts;

namespace Requiem.Web.Api;

/// <summary>The profile fields every sheet shares (ICharacterProfile), and the public card players see.</summary>
internal static class ProfileMapper
{
    public static T WithProfile<T>(this T form, Character c) where T : ICharacterProfile
    {
        form.Appearance = c.Appearance;
        form.Reputation = c.Reputation;
        form.Backstory = c.Backstory;
        form.PortraitUrl = c.PortraitUrl;
        form.IsShared = !c.IsPlayerCharacter && c.Visibility == NpcVisibility.Known;
        form.Reveals = c.IsPlayerCharacter ? NpcReveal.None : c.Reveals;
        return form;
    }

    /// <summary>The portrait isn't set here (it has its own endpoints), and player characters are never shared.</summary>
    public static void ApplyProfile(ICharacterProfile form, Character c)
    {
        c.Appearance = Clean(form.Appearance);
        c.Reputation = Clean(form.Reputation);
        c.Backstory = Clean(form.Backstory);
        c.Visibility = !c.IsPlayerCharacter && form.IsShared ? NpcVisibility.Known : NpcVisibility.Hidden;
        c.Reveals = c.IsPlayerCharacter ? NpcReveal.None : form.Reveals & AllReveals;
    }

    /// <summary>
    /// A Known NPC's card: its name, and only the details the Storyteller has revealed. Storytellers also get its
    /// tier, which players don't (it says how fully the Storyteller has written the NPC up).
    /// </summary>
    public static KnownNpc ToKnownNpc(Character c, bool forStoryteller)
    {
        bool Shows(NpcReveal reveal) => c.Reveals.HasFlag(reveal);
        return new KnownNpc(
            c.Id,
            c.Name,
            Shows(NpcReveal.Portrait) ? c.PortraitUrl : null,
            Shows(NpcReveal.Faction) ? c.Faction : null,
            Shows(NpcReveal.Bloodline) ? c.Bloodline?.Name : null,
            Shows(NpcReveal.Generation) ? c.Generation : null,
            Shows(NpcReveal.Appearance) ? c.Appearance : null,
            Shows(NpcReveal.Reputation) ? c.Reputation : null,
            Shows(NpcReveal.Backstory) ? c.Backstory : null,
            Shows(NpcReveal.Kind) ? c.Kind : null,
            forStoryteller ? c.Tier : null);
    }

    private static readonly NpcReveal AllReveals = Enum.GetValues<NpcReveal>().Aggregate(NpcReveal.None, (all, r) => all | r);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
