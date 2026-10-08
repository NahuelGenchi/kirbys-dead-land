using dc.tool;
using Kirby.Core;

namespace Kirby.Runtime;

/// <summary>
/// Keeps the Collector entries that do nothing for Kirby (#99) out of the
/// Collector in a Kirby run. The panel lists an entry when <c>baseFilter</c>
/// accepts it, and an upgrade's "unlock N more items" gate reads
/// <c>countUnlockedItems</c>; a found hidden entry counts there as unlocked, so
/// the gates move as in a normal run. Nothing is written to the save.
/// </summary>
internal static class KirbyCollector
{
    /// <summary>Host item ID → (group, cell cost), read once from the item sheet.</summary>
    private static Dictionary<string, (int Group, int CellCost)>? rows;

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
