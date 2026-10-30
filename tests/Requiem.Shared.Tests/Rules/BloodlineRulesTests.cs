using Requiem.Shared.Rules;

namespace Requiem.Shared.Tests.Rules;

public class BloodlineRulesTests
{
    // Book examples: "1 at 1, 2 at 4, 3 at 7, and 4 at 10".
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(6, 2)]
    [InlineData(7, 3)]
    [InlineData(9, 3)]
    [InlineData(10, 4)]
    public void Brujah_minimum_hunger_and_tremere_damage_share_the_scale(int bp, int expected)
    {
        Assert.Equal(expected, BloodlineRules.BrujahMinimumHungerDice(bp));
        Assert.Equal(expected, BloodlineRules.TremereDreadCountenanceDamage(bp));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(9, 5)]
    [InlineData(10, 5)]
    public void Gangrel_predation_is_half_blood_potency_rounded_up(int bp, int expected) =>
        Assert.Equal(expected, BloodlineRules.GangrelPredationRating(bp));

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(10)]
    public void Nosferatu_disgusted_equals_blood_potency(int bp) =>
        Assert.Equal(bp, BloodlineRules.NosferatuDisgustedRating(bp));

    [Fact]
    public void Fixed_bloodline_compel_ratings()
    {
        Assert.Equal(6, BloodlineRules.BrujahDestroyerRating);
        Assert.Equal(3, BloodlineRules.NosferatuSpitefulRating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Blood_potency_out_of_range_throws(int bp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BloodlineRules.BrujahMinimumHungerDice(bp));
        Assert.Throws<ArgumentOutOfRangeException>(() => BloodlineRules.TremereDreadCountenanceDamage(bp));
        Assert.Throws<ArgumentOutOfRangeException>(() => BloodlineRules.GangrelPredationRating(bp));
        Assert.Throws<ArgumentOutOfRangeException>(() => BloodlineRules.NosferatuDisgustedRating(bp));
    }

    [Theory]
    // Sated Brujah still carry their Rage floor.
    [InlineData(10, 1, 1)]
    [InlineData(20, 7, 3)]
    [InlineData(50, 10, 4)]
    // Once Bloodlust is above the floor, Bloodlust wins.
    [InlineData(1, 1, 5)]
    [InlineData(2, 4, 4)]
    // Bloodlust 1 at the threshold, but the BP 4 floor is 2.
    [InlineData(5, 4, 2)]
    public void Brujah_hunger_dice_never_drop_below_the_floor(int vitae, int bp, int expected) =>
        Assert.Equal(expected, BloodRules.HungerDice(vitae, bp, BloodlineRules.BrujahMinimumHungerDice(bp)));

    [Fact]
    public void Hunger_dice_without_a_minimum_equal_bloodlust()
    {
        Assert.Equal(0, BloodRules.HungerDice(10, 1));
        Assert.Equal(BloodRules.Bloodlust(3, 1), BloodRules.HungerDice(3, 1));
    }

    [Fact]
    public void Brujah_floor_applies_to_dice_pools()
    {
        var hunger = BloodRules.HungerDice(currentVitae: 14, bloodPotency: 4, BloodlineRules.BrujahMinimumHungerDice(4));
        Assert.Equal(new DicePoolSplit(4, 2), DicePools.Split(6, hunger));
        Assert.Equal(new DicePoolSplit(6, 0), DicePools.Split(6, hunger, isHumanityRoll: true));
    }

    [Theory]
    [InlineData("MagiHouse", "House", "Lich Weakness", "House Tremere")]
    [InlineData("Bloodline", "Bloodline", "Weakness", "Tremere")]
    [InlineData("Caitiff", "Bloodline", "Weakness", "Tremere")]
    [InlineData(null, "Bloodline", "Weakness", "Tremere")]
    public void Magi_have_a_house_and_a_lich_weakness(string? kind, string label, string weakness, string named) =>
        Assert.Equal((label, weakness, named), (BloodlineRules.KindLabel(kind), BloodlineRules.WeaknessLabel(kind), BloodlineRules.Named("Tremere", kind)));
}
