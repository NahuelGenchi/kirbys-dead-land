using dc.tool;
using Kirby.Core;

namespace Kirby.Runtime;

/// <summary>
/// Keeps the Collector entries that do nothing for Kirby (#99) out of the
/// Collector in a Kirby run. The panel lists an entry when <c>baseFilter</c>
/// accepts it, and an upgrade's "unlock N more items" gate reads
/// <c>countUnlockedItems</c>; a found hidden entry counts there as unlocked, so
/// the gates move as in a normal run. While <c>canInvestOnItem</c> checks its
/// prerequisites (Health Flask I first, then any required item), a hidden entry
/// counts as unlocked (#101), so a profile that never bought the hidden flask can
/// still invest. Nothing is written to the save.
/// <para>
/// The host asks <c>hasUnlockedItem</c> a few times every frame for its own
/// reasons, and a hooked call costs a managed call and its garbage whatever the
/// handler decides. Only <c>canInvestOnItem</c>'s checks need it, so its handler is
/// installed from the first check and taken off between host frames once no
/// check has run for a while (#187).
/// </para>
/// </summary>
internal static class KirbyCollector
{
    /// <summary>Host item ID → (group, cell cost), read once from the item sheet.</summary>
    private static Dictionary<string, (int Group, int CellCost)>? rows;
    /// <summary>Depth of <c>canInvestOnItem</c> calls in a Kirby run.</summary>
    private static int prerequisiteCheck;
    /// <summary>Whether the <c>hasUnlockedItem</c> handler is installed, and host frames since a check last needed it.</summary>
    private static bool holding;
    private static int idle;
    // Host frames the handler stays after the last check, so a Collector panel
    // that checks its entries again does not add and remove it each time.
    private const int Linger = 30;

    /// <summary>Whether the <c>hasUnlockedItem</c> handler is installed now (lab report).</summary>
    internal static bool Holding => holding;
    /// <summary>Lab-only: list every entry and keep the host count, to compare with the filtered Collector.</summary>
    internal static bool TestUnfiltered;
    /// <summary>Lab-only observer of each Collector entry the panel tested: (item ID, listed).</summary>
    internal static Action<string, bool>? TestFiltered;

    public static void Install()
    {
        dc.ui.Hook_CollectorPanel.baseFilter += (orig, panel, progress) =>
        {
            string? id = progress.itemId?.ToString();
            bool listed = orig(panel, progress) && !(Active() && Hides(id));
            if (id is not null) TestFiltered?.Invoke(id, listed);
            return listed;
        };
        Hook_ItemMetaManager.countUnlockedItems += (orig, meta) =>
            Active() ? CollectorMenu.CountUnlocked(orig(meta), Found(meta)) : orig(meta);
        Hook_ItemMetaManager.canInvestOnItem += (orig, meta, id) =>
        {
            if (!Active()) return orig(meta, id);
            Hold();
            prerequisiteCheck++;
            try { return orig(meta, id); }
            finally { prerequisiteCheck--; }
        };
    }

    private static void Hold()
    {
        idle = 0;
        if (holding) return;
        DccmDetours.Attach("tool.ItemMetaManager.hasUnlockedItem");
        Hook_ItemMetaManager.hasUnlockedItem += HasUnlockedItem;
        holding = true;
    }

    /// <summary>
    /// Remove the <c>hasUnlockedItem</c> handler once no check has needed it for a
    /// while, and DCCM's detour with it (#178). Called once between host frames.
    /// </summary>
    internal static void Relax()
    {
        if (!holding || ++idle <= Linger) return;
        Hook_ItemMetaManager.hasUnlockedItem -= HasUnlockedItem;
        DccmDetours.Detach("tool.ItemMetaManager.hasUnlockedItem");
        holding = false;
    }

    private static bool HasUnlockedItem(Hook_ItemMetaManager.orig_hasUnlockedItem orig, ItemMetaManager meta, dc.String? id)
    {
        bool unlocked = orig(meta, id);
        return prerequisiteCheck > 0 && id?.ToString() is { } item && Row(item) is { } row
            ? CollectorMenu.MeetsPrerequisite(item, row.Group, unlocked) : unlocked;
    }

    private static bool Active() => !TestUnfiltered && KirbyMod.Instance?.ActiveSession is not null;

    internal static bool Hides(string? id) => id is not null && Row(id) is { } row && CollectorMenu.Hides(id, row.Group);

    /// <summary>Every entry the player has found (revealed), as the host's count walks them.</summary>
    internal static IEnumerable<(string Id, int Group, int CellCost, bool Unlocked)> Found(ItemMetaManager meta)
    {
        var progress = meta.itemProgress;
        for (int i = 0; i < progress.length; i++)
            if (progress.getDyn(i) is ItemProgress entry && entry.itemId?.ToString() is { } id && Row(id) is { } row)
                yield return (id, row.Group, row.CellCost, entry.unlocked);
    }

    private static (int Group, int CellCost)? Row(string id)
    {
        if (rows is null)
        {
            var sheet = dc.Data.Class.item.byIndex;
            var read = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
            for (int i = 0; i < sheet.get_length(); i++)
            {
                dynamic row = sheet.getDyn(i);
                read[(string)row.id.ToString()] = ((int)row.group, (int)row.cellCost);
            }
            rows = read;
        }
        return rows.TryGetValue(id, out var found) ? found : null;
    }
}
