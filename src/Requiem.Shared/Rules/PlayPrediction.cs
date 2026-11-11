using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;

namespace Requiem.Shared.Rules;

/// <summary>
/// What a table action does, worked out in the browser so the screen changes at the tap instead of after the round
/// trip. The server applies the same action (PlayEndpoints.Apply) and has the last word: the browser settles on its
/// answer once the actions in flight are done. A test runs both side by side to keep them in step.
/// </summary>
public static class PlayPrediction
{
    /// <returns>
    /// The character after the action; or the error the server would give; or neither, for an action only the server
    /// can apply (adding a wound, compel or condition needs the id the server gives it).
    /// </returns>
    public static (PlayCharacter? Next, string? Error) Apply(PlayCharacter c, PlayAction action)
    {
        switch (action.Kind)
        {
            case PlayActionKind.HealWound:
            {
                if (c.Wounds.FirstOrDefault(w => w.Id == action.TargetId) is not { } wound) return (null, "That wound is already gone.");
                var left = wound.Amount - Math.Max(1, action.Amount);
                return (c with { Wounds = left <= 0 ? Without(c.Wounds, wound) : Replace(c.Wounds, wound, wound with { Amount = left }) }, null);
            }

            case PlayActionKind.RemoveWound:
                return (c with { Wounds = c.Wounds.Where(w => w.Id != action.TargetId).ToList() }, null);

            case PlayActionKind.HealWithVitae:
            {
                if (!c.HoldsVitae) return (null, "Only vampires and ghouls heal with Vitae.");
                if (c.Wounds.FirstOrDefault(w => w.Id == action.TargetId) is not { } wound) return (null, "That wound is already gone.");
                var cost = HealingRules.Cost(wound.Type, wound.Amount);
                var vitae = c.VitaeCurrent ?? 0;
                if (cost.Vitae > vitae) return (null, $"Healing it takes {cost.Vitae} Vitae; only {vitae} left.");
                var willpower = c.WillpowerCurrent;
                if (cost.Willpower > 0)
                {
                    if (!c.CanSpendWillpower) return (null, "Aggravated wounds also cost Willpower, which Supporting Cast never spend.");
                    if (cost.Willpower > willpower) return (null, $"Healing it also takes {cost.Willpower} Willpower; only {willpower} left.");
                    willpower -= cost.Willpower;
                }
                return (c with { VitaeCurrent = vitae - cost.Vitae, WillpowerCurrent = willpower, Wounds = Without(c.Wounds, wound) }, null);
            }

            case PlayActionKind.AdjustWillpower:
            {
                if (!c.CanSpendWillpower) return (null, "Supporting Cast never spend Willpower.");
                var willpower = c.WillpowerCurrent + action.Amount;
                if (willpower < 0) return (null, "No Willpower left to spend.");
                return (c with { WillpowerCurrent = Math.Min(willpower, c.WillpowerDots) }, null);
            }

            case PlayActionKind.RegainWillpower:
            {
                if (!c.CanSpendWillpower) return (null, "Supporting Cast never spend Willpower.");
                if (action.Source is not { } source) return (null, "Say what restored it: Concept, Virtue or Vice.");
                if (c.WillpowerCurrent >= c.WillpowerDots) return (null, "Willpower is already full.");
                return (c with { WillpowerCurrent = WillpowerRules.Regain(source, c.WillpowerCurrent, c.WillpowerDots) }, null);
            }

            case PlayActionKind.AdjustVitae:
            {
                if (c.MaxVitae is not { } capacity || c.VitaeCurrent is null) return (null, "Only vampires and ghouls hold Vitae.");
                var vitae = c.VitaeCurrent.Value + action.Amount;
                if (vitae < 0) return (null, $"Only {c.VitaeCurrent} Vitae left.");
                return (c with { VitaeCurrent = Math.Min(vitae, capacity) }, null);
            }

            case PlayActionKind.AdjustBoost:
            {
                if (!c.HoldsVitae) return (null, "Only vampires and ghouls can spend Vitae on Fortify Form.");
                if (action.Boost is not { } discipline) return (null, "Choose Potence, Celerity or Fortitude.");
                var current = c.Boosts.GetValueOrDefault(discipline);
                var target = Math.Max(0, current + action.Amount);
                var cap = c.BoostCaps.GetValueOrDefault(discipline);
                if (target > cap)
                    return (null, c.IsGhoul && target > GhoulRules.MaxFortifyBoost
                        ? $"A ghoul gains at most {GhoulRules.MaxFortifyBoost} {discipline} from Vitae."
                        : $"{discipline} from Fortify Form can't exceed {Traits.FortifyCap(discipline)} ({cap}).");
                var cost = Math.Max(0, target - current); // 1 Vitae per point; letting it lapse refunds nothing
                var vitae = c.VitaeCurrent ?? 0;
                if (cost > vitae) return (null, $"Only {vitae} Vitae left.");
                return (c with { VitaeCurrent = vitae - cost, Boosts = With(c.Boosts, discipline, target) }, null);
            }

            case PlayActionKind.ClearBoosts:
                return (c with { Boosts = c.Boosts.ToDictionary(b => b.Key, _ => 0) }, null);

            case PlayActionKind.RemoveCompel:
                return (c with { Compels = c.Compels.Where(x => x.Id != action.TargetId).ToList() }, null);

            case PlayActionKind.RemoveCondition:
                return (c with { Conditions = c.Conditions.Where(x => x.Id != action.TargetId).ToList() }, null);

            case PlayActionKind.GainAntipathia or PlayActionKind.BotchSpell or PlayActionKind.Oblation:
            {
                if (c.Koldun is not { } koldun) return (null, $"Only those who know {KoldunRules.RitualName} gather Antipathia.");
                if (action.Kind == PlayActionKind.Oblation)
                    return koldun.Antipathia == 0
                        ? (null, "No Antipathia to take off.")
                        : (c with { Koldun = koldun with { Antipathia = KoldunRules.Oblation(koldun.Antipathia) } }, null);
                var (antipathia, _) = KoldunRules.Gain(koldun.Antipathia, action.Amount, action.Kind == PlayActionKind.BotchSpell);
                return (c with { Koldun = koldun with { Antipathia = antipathia } }, null);
            }

            default:
                return (null, null);
        }
    }

    private static List<T> Without<T>(IEnumerable<T> items, T item) where T : class => items.Where(i => !ReferenceEquals(i, item)).ToList();

    private static List<T> Replace<T>(IEnumerable<T> items, T old, T replacement) where T : class =>
        items.Select(i => ReferenceEquals(i, old) ? replacement : i).ToList();

    private static Dictionary<FortifyDiscipline, int> With(IReadOnlyDictionary<FortifyDiscipline, int> boosts, FortifyDiscipline d, int value)
    {
        var copy = boosts.ToDictionary(b => b.Key, b => b.Value);
        copy[d] = value;
        return copy;
    }
}
