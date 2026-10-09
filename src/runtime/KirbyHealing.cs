using dc.en;
using Kirby.Core;

namespace Kirby.Runtime;

/// <summary>
/// Kirby has no health flask (#101). <c>HealFlask.isEnabled</c> already turns
/// the flask off for the host's own reasons (the Captain Chicken pet); a Kirby
/// session turns it off the same way, so F never drinks and the fountain and
/// flask recharges have no flask to fill. The fountain's own effect
/// (<c>Fountain.applyEffect</c>, at the end of its cinematic) heals Kirby by
/// <see cref="Healing.FountainHeal"/> instead of his full health; the host
/// still marks the fountain used and clears infection.
/// </summary>
internal static class KirbyHealing
{
    private static bool fountainEffect;
    /// <summary>Lab-only observer of each fountain heal: (requested by the host, given).</summary>
    internal static Action<int, int>? TestFountainHeal;

    public static void Install()
    {
        dc.tool.mainSkills.Hook_HealFlask.isEnabled += (orig, flask) => !IsKirby(flask.hero) && orig(flask);
        dc.en.inter.Hook_Fountain.applyEffect += (orig, fountain, hero) =>
        {
            if (!IsKirby(hero)) { orig(fountain, hero); return; }
            fountainEffect = true;
            try { orig(fountain, hero); }
            finally { fountainEffect = false; }
        };
        Hook_Hero.heal += (orig, hero, amount) =>
        {
            if (fountainEffect && IsKirby(hero))
            {
                int given = Healing.FountainHeal(hero.maxLife);
                TestFountainHeal?.Invoke(amount, given);
                amount = given;
            }
            orig(hero, amount);
        };
    }

    private static bool IsKirby(Hero? hero) =>
        hero is not null && KirbyMod.Instance?.ActiveSession?.Hero.HashlinkPointer == hero.HashlinkPointer;
}
