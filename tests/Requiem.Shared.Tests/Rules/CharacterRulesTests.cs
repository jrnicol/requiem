using Requiem.Shared.Rules;

namespace Requiem.Shared.Tests.Rules;

public class CharacterRulesTests
{
    [Fact]
    public void Derived_stats_from_full_sheet()
    {
        Assert.Equal(7, DerivedStats.Health(stamina: 2));
        Assert.Equal(8, DerivedStats.Health(stamina: 2, size: 6));
        Assert.Equal(2, DerivedStats.Defense(wits: 2, dexterity: 3));
        Assert.Equal(5, DerivedStats.StartingWillpower(wits: 2, resolve: 3));
        Assert.Equal(6, DerivedStats.StartingHumanity());
    }

    [Theory]
    [InlineData(CharacterAttribute.Perception, TraitCategory.Mental)]
    [InlineData(CharacterAttribute.Resolve, TraitCategory.Social)]
    [InlineData(CharacterAttribute.Stamina, TraitCategory.Physical)]
    public void Attribute_categories_follow_house_sheet(CharacterAttribute attribute, TraitCategory expected) =>
        Assert.Equal(expected, Traits.CategoryOf(attribute));

    [Theory]
    [InlineData(Skill.Technology, TraitCategory.Mental)]
    [InlineData(Skill.Athletics, TraitCategory.Physical)]
    [InlineData(Skill.Weaponry, TraitCategory.Physical)]
    [InlineData(Skill.Empathy, TraitCategory.Social)]
    public void Skill_categories_follow_house_sheet(Skill skill, TraitCategory expected) =>
        Assert.Equal(expected, Traits.CategoryOf(skill));

    [Fact]
    public void Each_category_has_three_attributes_and_five_skills()
    {
        foreach (var category in Enum.GetValues<TraitCategory>())
        {
            Assert.Equal(3, Enum.GetValues<CharacterAttribute>().Count(a => Traits.CategoryOf(a) == category));
            Assert.Equal(5, Enum.GetValues<Skill>().Count(s => Traits.CategoryOf(s) == category));
        }
    }

    [Fact]
    public void Supporting_cast_derives_from_categories_and_edges()
    {
        Assert.Equal(2, SupportingCastRules.Health(physical: 2));
        Assert.Equal(7, SupportingCastRules.Health(physical: 2, NpcEdge.Tough));
        Assert.Equal(3, SupportingCastRules.Willpower(mental: 3));
        Assert.Equal(8, SupportingCastRules.Willpower(mental: 3, NpcEdge.Unshakable | NpcEdge.Tough));
        // Book example: mechanic, Mental 2, Specialty +1, Asset +2 = 5 dice.
        Assert.Equal(5, SupportingCastRules.DicePool(rating: 2, specialties: 1, assets: 1));
    }

    [Fact]
    public void Supporting_cast_ratings_are_one_to_five() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SupportingCastRules.Health(6));
}
