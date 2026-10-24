using Requiem.Shared.Rules;

namespace Requiem.Shared.Tests.Rules;

public class GenerationAndHumanityTests
{
    [Theory]
    [InlineData(13, 9)]
    [InlineData(12, 10)]
    [InlineData(11, 11)]
    [InlineData(10, 11)]
    [InlineData(8, 13)]
    [InlineData(5, 15)]
    public void Generation_strength_matches_chart(int generation, int expected) =>
        Assert.Equal(expected, GenerationRules.Strength(generation));

    [Fact]
    public void Generation_below_four_has_no_cap() =>
        Assert.Null(GenerationRules.Strength(3));

    [Fact]
    public void Book_example_generation_12_humanity_8_bp_3_drops_humanity_to_7() =>
        Assert.Equal(7, HumanityRules.ClampToGeneration(humanity: 8, bloodPotency: 3, generation: 12));

    [Fact]
    public void Humanity_within_cap_is_unchanged() =>
        Assert.Equal(6, HumanityRules.ClampToGeneration(humanity: 6, bloodPotency: 1, generation: 12));

    [Fact]
    public void Elder_feeding_allows_vampires_only_at_bp_six()
    {
        Assert.False(GenerationRules.Feeding(5, bloodPotency: 5).Allowed.HasFlag(FeedingSource.Vampires));
        Assert.True(GenerationRules.Feeding(5, bloodPotency: 6).Allowed.HasFlag(FeedingSource.Vampires));
        Assert.True(GenerationRules.Feeding(12, bloodPotency: 1).Allowed.HasFlag(FeedingSource.Animals));
        Assert.False(GenerationRules.Feeding(10, bloodPotency: 1).Allowed.HasFlag(FeedingSource.Animals));
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(9, 4)]
    [InlineData(7, 3)]
    [InlineData(4, 2)]
    [InlineData(2, 2)]
    public void Stains_pool_follows_humanity_chart(int humanity, int expected) =>
        Assert.Equal(expected, HumanityRules.StainsDicePool(humanity));

    [Fact]
    public void Humanity_one_is_lost() => Assert.Null(HumanityRules.StainsDicePool(1));
}
