using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Shared.Tests.Rules;

public class GhoulRulesTests
{
    [Theory]
    [InlineData(CharacterKind.Vampire, 1, 10)]
    [InlineData(CharacterKind.Vampire, 3, 14)]
    [InlineData(CharacterKind.Ghoul, null, 8)]
    [InlineData(CharacterKind.Mortal, null, null)]
    public void Vitae_capacity_by_kind(CharacterKind kind, int? bloodPotency, int? expected) =>
        Assert.Equal(expected, GhoulRules.VitaeCapacity(kind, bloodPotency));

    [Theory]
    [InlineData(CharacterKind.Vampire, 4, 4)]
    [InlineData(CharacterKind.Ghoul, 4, 1)]
    [InlineData(CharacterKind.Ghoul, 0, 0)]
    [InlineData(CharacterKind.Mortal, 4, 0)]
    public void Fortify_cap_by_kind(CharacterKind kind, int attribute, int expected) =>
        Assert.Equal(expected, GhoulRules.FortifyCap(kind, attribute));

    [Fact]
    public void A_ghoul_sheet_keeps_vitae_within_eight_and_one_dot_of_each_boost()
    {
        var form = new FullSheetForm { Name = "Ada", Kind = CharacterKind.Ghoul, VitaeCurrent = 12, PotenceBoost = 3 };
        form.Attributes[CharacterAttribute.Strength] = 3;
        form.NormalizeForKind();

        Assert.Equal(8, form.VitaeCurrent);
        Assert.Equal(1, form.PotenceBoost);
        Assert.Null(form.BloodPotency);
        Assert.Equal(7, form.Morality);
    }

    [Fact]
    public void A_mortal_sheet_loses_vitae_and_boosts()
    {
        var form = new FullSheetForm { Name = "Tom", Kind = CharacterKind.Mortal, VitaeCurrent = 5, CelerityBoost = 1 };
        form.NormalizeForKind();
        Assert.Null(form.VitaeCurrent);
        Assert.Equal(0, form.CelerityBoost);
    }

    [Fact]
    public void Supporting_cast_ghouls_keep_their_vitae()
    {
        var form = new SupportingCastForm { Name = "Driver", Kind = CharacterKind.Ghoul, VitaeCurrent = 3 };
        form.ClearNonVampireFields();
        form.ApplyVampireDefaults();
        Assert.Equal(3, form.VitaeCurrent);

        var fresh = new SupportingCastForm { Name = "New", Kind = CharacterKind.Ghoul };
        fresh.ApplyVampireDefaults();
        Assert.Equal(GhoulRules.MaxVitae, fresh.VitaeCurrent);
    }
}
