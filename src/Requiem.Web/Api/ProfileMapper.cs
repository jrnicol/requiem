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
            forStoryteller ? c.Tier : null,
            Shows(NpcReveal.Titles) ? TitlesOrNull(c) : null,
            CanPlay: forStoryteller);
    }

    /// <summary>An NPC's whole profile, for the campaign's Storytellers: every detail, revealed or not.</summary>
    public static KnownNpc ToFullProfile(Character c) => new(
        c.Id, c.Name, c.PortraitUrl, c.Faction, c.Bloodline?.Name, c.Generation,
        c.Appearance, c.Reputation, c.Backstory, c.Kind, c.Tier, TitlesOrNull(c), CanPlay: true);

    /// <summary>
    /// A player character's profile, for everyone in its campaign: every detail. <paramref name="canEdit"/> (its player and the
    /// Storytellers) adds the tier the links need.
    /// </summary>
    public static KnownNpc ToPlayerProfile(Character c, string playedBy, bool canEdit, bool canPlay) => new(
        c.Id, c.Name, c.PortraitUrl, c.Faction, c.Bloodline?.Name, c.Generation,
        c.Appearance, c.Reputation, c.Backstory, c.Kind, canEdit ? c.Tier : null, TitlesOrNull(c),
        IsPlayerCharacter: true, PlayedBy: playedBy, CanPlay: canPlay, Concept: c.Concept);

    private static IReadOnlyList<string>? TitlesOrNull(Character c) => c.Titles.Count > 0 ? c.Titles : null;

    /// <summary>Titles as saved: trimmed, without blanks or repeats, in the order given.</summary>
    public static List<string> CleanTitles(IEnumerable<string> titles) =>
        titles.Select(t => t?.Trim()).OfType<string>().Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>A form's titles must be the catalog's. Saved under the catalog's spelling by <see cref="CanonicalTitles"/>.</summary>
    public static System.ComponentModel.DataAnnotations.ValidationResult? TitlesProblem(IEnumerable<string> titles, Data.Stores.CatalogSnapshot catalog)
    {
        var unknown = CleanTitles(titles).Where(t => !catalog.Titles.Any(d => string.Equals(d.Name, t, StringComparison.OrdinalIgnoreCase))).ToList();
        return unknown.Count == 0 ? null
            : new($"Unknown title{(unknown.Count == 1 ? "" : "s")}: {string.Join(", ", unknown)}.", ["Titles"]);
    }

    /// <summary>The catalog's spelling of each title, so lookups at the table always match.</summary>
    public static List<string> CanonicalTitles(IEnumerable<string> titles, Data.Stores.CatalogSnapshot catalog) =>
        CleanTitles(titles).Select(t => catalog.Titles.FirstOrDefault(d => string.Equals(d.Name, t, StringComparison.OrdinalIgnoreCase))?.Name ?? t).ToList();

    private static readonly NpcReveal AllReveals = Enum.GetValues<NpcReveal>().Aggregate(NpcReveal.None, (all, r) => all | r);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
