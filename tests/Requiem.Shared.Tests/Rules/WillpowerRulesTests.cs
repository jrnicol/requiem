using Requiem.Shared.Rules;

namespace Requiem.Shared.Tests.Rules;

public class WillpowerRulesTests
{
    [Theory]
    [InlineData(WillpowerSource.Virtue, 1, 5, 5)] // all spent points back
    [InlineData(WillpowerSource.Vice, 1, 5, 2)]
    [InlineData(WillpowerSource.Concept, 0, 5, 1)]
    [InlineData(WillpowerSource.Concept, 5, 5, 5)] // never above the dots
    public void Regain_follows_the_source(WillpowerSource source, int current, int dots, int expected) =>
        Assert.Equal(expected, WillpowerRules.Regain(source, current, dots));

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Vice_applies_when_out_of_willpower(int current, bool expected) =>
        Assert.Equal(expected, WillpowerRules.ViceApplies(current));
}
