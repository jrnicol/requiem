using Requiem.Shared.Rules;

namespace Requiem.Shared.Tests.Rules;

public class SorceryRulesTests
{
    [Theory]
    [InlineData("Hekate", 6)]
    [InlineData("Vishap", 3)]
    [InlineData("Mayaparisatya", 9)]
    [InlineData("Noctis", 5)]
    [InlineData("Necromancy", 11)]
    public void Each_sorcery_lists_its_own_eruditions(string sorcery, int count) =>
        Assert.Equal(count, SorceryRules.Eruditions(sorcery).Count);

    [Fact]
    public void Necromancy_splits_its_eruditions_between_animus_and_mortuus()
    {
        var paths = SorceryRules.Eruditions("Necromancy").GroupBy(e => e.Path).ToDictionary(g => g.Key!, g => g.Count());
        Assert.Equal(new Dictionary<string, int> { ["Animus"] = 7, ["Mortuus"] = 4 }, paths);
        Assert.Empty(SorceryRules.Eruditions("Dominate"));
        Assert.Empty(SorceryRules.Eruditions(null));
    }

    // "Each Erudition used grants the magi 2 extra dice"; rolls are "reduced by the target's Blood Potency".
    [Theory]
    [InlineData(5, 0, 0, 5)]
    [InlineData(5, 3, 0, 11)]
    [InlineData(5, 1, 4, 3)]
    [InlineData(2, 0, 4, -2)]
    public void Eruditions_add_two_dice_and_the_targets_blood_potency_takes_dice_off(int pool, int eruditions, int targetBp, int expected) =>
        Assert.Equal(expected, SorceryRules.CastingDice(pool, eruditions, targetBp));

    // "If you used three eruditions, the ritual would take 3 hours' time plus whatever the base casting time of the ritual is."
    [Theory]
    [InlineData("1 minute", 0, "1 minute")]
    [InlineData("1 minute", 1, "1 minute + 1 hour")]
    [InlineData("1 minute", 3, "1 minute + 3 hours")]
    [InlineData(null, 2, "The Ritual's time + 2 hours")]
    public void Each_erudition_adds_an_hour_of_casting(string? baseTime, int eruditions, string expected) =>
        Assert.Equal(expected, SorceryRules.CastingTime(baseTime, eruditions));

    [Theory]
    [InlineData(PowerGroupKind.CaitiffDiscipline, false)]
    [InlineData(PowerGroupKind.BloodlineDiscipline, false)]
    [InlineData(PowerGroupKind.BloodSorcery, true)]
    [InlineData(PowerGroupKind.BasePower, true)]
    [InlineData(PowerGroupKind.SabbatRitae, true)]
    public void Disciplines_stop_by_day_but_sorcery_goes_on(PowerGroupKind kind, bool works) =>
        Assert.Equal(works, SorceryRules.WorksByDay(kind));
}
