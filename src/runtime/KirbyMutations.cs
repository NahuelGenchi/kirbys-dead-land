using dc.en;
using dc.tool;
using HaxeProxy.Runtime;
using Kirby.Core;
using ModCore.Utilities;

namespace Kirby.Runtime;

/// <summary>
/// Kirby versions of the host mutations (#89): the skill-cooldown mutations do
/// nothing in a Kirby run, so they feed <see cref="CopyCritical"/> instead. The
/// rules live in <c>src/core</c>; this reads the host's state and reports events.
/// </summary>
internal static class KirbyMutations
{
    /// <summary>Host affect IDs Heart of Ice checks on its target: slowed, stunned, frozen and rooted.</summary>
    private static readonly int[] ControlAffects = [15, 8, 23, 133];

    public static void Install()
    {
        // Mutation cards and the pause screen describe a mutation through
        // getGameplayDesc; in a Kirby run the cooldown mutations say what they do now,
        // and the others call every attack a copy attack.
        Hook_InventItem.getGameplayDesc += (orig, item, level) =>
        {
            if (KirbyMod.Instance?.ActiveSession is not { } session) return orig(item, level);
            string? id = item.getItemKind()?.ToString();
            if (MutationDescriptions.Describe(id, session.Rules.Mutations.Tuning) is { } text) return text.AsHaxeString();
            var original = orig(item, level);
            // Every other mutation keeps the host's text and numbers; only "melee" and
            // "ranged" become "copy attack" (#91).
            return id is not null && id.StartsWith("P_", StringComparison.Ordinal) &&
                CopyAttackWording.Rewrite(original?.ToString()) is { } changed ? changed.AsHaxeString() : original;
        };
        // Blind Faith: the host reports each parry that lands on an enemy.
        dc.en.Hook_Hero.onPerfectParry += (orig, hero, attack) =>
        {
            orig(hero, attack);
            if (KirbyMod.Instance?.ActiveSession is { } session && session.Hero.HashlinkPointer == hero.HashlinkPointer)
                session.Rules.Mutations.Parry();
        };
    }

    /// <summary>The cooldown mutations the hero holds, from the host inventory.</summary>
    public static KirbyMutation Owned(Hero hero)
    {
        var owned = KirbyMutation.None;
        foreach (var id in new[] { MutationDescriptions.KillerInstinct, MutationDescriptions.BlindFaith, MutationDescriptions.HeartOfIce,
                     MutationDescriptions.HuntersInstinct, MutationDescriptions.MasterOfArms })
            if (hero.inventory.hasItem(id.AsHaxeString())) owned |= MutationDescriptions.Mutation(id);
        return owned;
    }

    /// <summary>
    /// Heart of Ice's target test, as <c>HeroAttackSource.postHitResultUpdate</c> reads it:
    /// the target is slowed, stunned, frozen or rooted and within 4 cells of the hero.
    /// </summary>
    public static bool HeartOfIceTarget(Hero hero, dc.Entity mob)
    {
        if (!ControlAffects.Any(mob.hasAffect)) return false;
        double dx = hero.cx + hero.xr - (mob.cx + mob.xr);
        double dy = hero.cy + hero.yr - 0.020833333333333332 * hero.hei - (mob.cy + mob.yr - 0.020833333333333332 * mob.hei);
        return dx * dx + dy * dy <= 16;
    }
}
