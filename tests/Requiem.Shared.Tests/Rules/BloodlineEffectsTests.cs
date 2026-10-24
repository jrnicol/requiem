using Requiem.Shared.Rules;

namespace Requiem.Shared.Tests.Rules;

public class BloodlineEffectsTests
{
    [Theory]
    [InlineData("Brujah", 4, "Minimum Hunger dice", 2)]
    [InlineData("brujah", 4, "Destroyer (Compel)", 6)]
    [InlineData("Nosferatu", 5, "Disgusted (Compel)", 5)]
    [InlineData("Nosferatu", 5, "Spiteful (Compel)", 3)]
    [InlineData("Gangrel", 5, "Predation (Compel)", 3)]
    [InlineData(" Tremere ", 7, "Dread Countenance", 3)]
    public void Effects_are_found_by_catalog_name(string bloodline, int bp, string label, int expected) =>
        Assert.Equal(expected, Assert.Single(BloodlineRules.EffectsFor(bloodline, bp), e => e.Label == label).Value);

    [Theory]
    [InlineData("Ventrue")]
    [InlineData("Caitiff")]
    [InlineData(null)]
    public void Bloodlines_without_scaled_rules_have_no_effects(string? bloodline) =>
        Assert.Empty(BloodlineRules.EffectsFor(bloodline, 3));
}
