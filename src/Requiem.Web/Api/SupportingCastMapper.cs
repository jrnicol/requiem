using Requiem.Data.Entities;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;

namespace Requiem.Web.Api;

/// <summary>Maps between the Supporting Cast form and the character entity.</summary>
internal static class SupportingCastMapper
{
    public static SupportingCastForm ToForm(Character c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Kind = c.Kind,
        Concept = c.Concept,
        Faction = c.Faction,
        Ambition = c.Ambition,
        Physical = c.Physical ?? 1,
        Mental = c.Mental ?? 1,
        Social = c.Social ?? 1,
        Edges = c.Edges,
        BloodlineId = c.BloodlineId,
        PredatorTypeId = c.PredatorTypeId,
        BloodPotency = c.BloodPotency,
        Generation = c.Generation,
        VitaeCurrent = c.VitaeCurrent,
        Specialties = c.Specialties.Where(s => s.Skill is null).OrderBy(s => s.Id).Select(s => s.Name).ToList(),
        Assets = c.Items.Where(i => i.Kind == ItemKind.Asset).OrderBy(i => i.SortOrder).Select(i => i.Name).ToList(),
        Notes = c.Notes,
        GmNotes = c.GmNotes,
        IsArchived = c.IsArchived,
        Version = c.Version,
    };

    /// <summary>Copies the form onto the entity. Call after validation; trims text and replaces lists.</summary>
    public static void Apply(SupportingCastForm form, Character c)
    {
        form.ClearNonVampireFields();
        form.ApplyVampireDefaults();

        c.Tier = CharacterTier.SupportingCast;
        c.Name = form.Name.Trim();
        c.Kind = form.Kind;
        c.Concept = Clean(form.Concept);
        c.Faction = Clean(form.Faction);
        c.Ambition = Clean(form.Ambition);
        c.Physical = form.Physical;
        c.Mental = form.Mental;
        c.Social = form.Social;
        c.Edges = form.Edges;
        c.BloodlineId = form.BloodlineId;
        c.PredatorTypeId = form.PredatorTypeId;
        c.BloodPotency = form.BloodPotency;
        c.Generation = form.Generation;
        c.VitaeCurrent = form.VitaeCurrent;
        c.Notes = Clean(form.Notes);
        c.GmNotes = Clean(form.GmNotes);
        c.IsArchived = form.IsArchived;

        var specialties = form.Specialties.Select(s => s.Trim()).ToList();
        if (!c.Specialties.Where(s => s.Skill is null).Select(s => s.Name).SequenceEqual(specialties))
        {
            c.Specialties.RemoveAll(s => s.Skill is null);
            c.Specialties.AddRange(specialties.Select(name => new CharacterSpecialty { Name = name }));
        }

        var assets = form.Assets.Select(a => a.Trim()).ToList();
        if (!c.Items.Where(i => i.Kind == ItemKind.Asset).OrderBy(i => i.SortOrder).Select(i => i.Name).SequenceEqual(assets))
        {
            c.Items.RemoveAll(i => i.Kind == ItemKind.Asset);
            c.Items.AddRange(assets.Select((name, i) => new CharacterItem { Kind = ItemKind.Asset, Name = name, SortOrder = i }));
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
