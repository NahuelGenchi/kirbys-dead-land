using Kirby.Core;
using ModCore.Utilities;

namespace Kirby.Runtime;

/// <summary>
/// The forge in a Kirby run (#58). The passage forge (<c>ForgeUnderground</c>)
/// lists each item in the hero's weapon slots, backpack and skill slots that can
/// take a "+" (<c>InventItem.canBeForged</c>), with a reroll of its affixes and
/// an upgrade, both for gold. The copy's ability item goes through both as a
/// Dead Cells weapon does: the reroll keeps its level, "+", Legendary and
/// Colorless, the upgrade adds a "+", and copy damage reads the item's bonus
/// every frame. The forge lists only ability items and shields: the starting
/// Rusty Sword and any other host weapon or skill never show.
/// </summary>
internal static class KirbyForge
{
    /// <summary>Lab-only: list every item the host would, to compare with the filtered forge.</summary>
    internal static bool TestUnfiltered;
    /// <summary>Lab-only observer of each item the forge tested: (item ID, listed).</summary>
    internal static Action<string, bool>? TestListed;

    public static void Install()
    {
        dc.ui.Hook_ForgeUnderground.addItem += (orig, forge, item, parent) =>
        {
            if (item is null) return orig(forge, item, parent);
            bool hidden = Active() &&
                !AbilityItems.ForgeLists(KirbyAbilityItems.IsAbilityItem(item), item.hasTag("Shield".AsHaxeString()));
            var row = hidden ? null : orig(forge, item, parent);
            TestListed?.Invoke(item.getItemKind()?.ToString() ?? "", row is not null);
            return row!;
        };
    }

    private static bool Active() => !TestUnfiltered && KirbyMod.Instance?.ActiveSession is not null;
}
