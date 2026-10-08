using Kirby.Core;

namespace Kirby.Runtime;

/// <summary>
/// Keeps the mutations that cannot work for Kirby (#90) out of every mutation
/// choice in a Kirby run. The mutation menu and the mutation NPC both ask
/// <c>isVisible</c>, and the NPC counts what it offers with it, so both are
/// filtered or the NPC would offer picks the menu does not show.
/// </summary>
internal static class KirbyMutationMenu
{
    public static void Install()
    {
        dc.ui.Hook_PerkSelect.isVisible += (orig, menu, key) => orig(menu, key) && !Hides(key);
        dc.en.inter.npc.Hook_PerkMaster.isVisible += (orig, npc, key) => orig(npc, key) && !Hides(key);
    }

    /// <summary>Lab-only: show every mutation, to compare with the filtered menu.</summary>
    internal static bool TestUnfiltered;

    private static bool Hides(object key) =>
        !TestUnfiltered && KirbyMod.Instance?.ActiveSession is not null && MutationMenu.IsHidden(key?.ToString());
}
