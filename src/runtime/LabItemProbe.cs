using System.Diagnostics;
using System.Text.Json;
using dc.en;
using dc.tool;
using ModCore.Utilities;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Lab-only pickup check. KIRBY_LAB_ITEM=kind@seconds drops an item next to
/// Kirby through the host ItemDrop: "weapon" picks a host weapon Kirby's rules
/// do not allow, "sword" an allowed sword. The normal-binding repro plan then
/// presses the host interact action; this probe records refusals, pickups and
/// the resulting inventory.
/// </summary>
internal static class LabItemProbe
{
    private static string? run;
    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private static double? began;
    private static readonly HashSet<string> dropped = [];
    private static dc.en.inter.ItemDrop? drop;
    private static dc.ui.ReplaceItem? dialog;
    private static bool validated;

    public static void Install(string output)
    {
        run = output;
        dc.Hook_Entity.popError += (orig, entity, message, color) =>
        { Event("pop-error", new { entity = entity.GetType().FullName, message = message?.ToString() }); return orig(entity, message, color); };
        // The replace dialog confirms on a held UI direction; the lab confirms it
        // through its own onValidate, the same path a player's choice takes.
        dc.ui.Hook_ReplaceItem.update += (orig, open) => { orig(open); dialog = open; };
        dc.ui.Hook_ReplaceItem.onValidate += (orig, dialog) =>
        {
            Event("replace-validate", new { id = Id(dialog.item) });
            orig(dialog);
            var hero = ModCore.Modules.Game.Instance.HeroInstance;
            Event("replaced", new { id = Id(dialog.item), dialog.item.posID, inventory = hero is null ? null : Inventory(hero) });
        };
        Hook_Hero.pickItem += (orig, hero, from, item, onComplete) =>
        { Event("pick-item", new { id = Id(item), item.posID }); orig(hero, from, item, onComplete); Event("picked", new { id = Id(item), item.posID, inventory = Inventory(hero) }); };
    }

    private static string? Id(InventItem item) => item.kind switch
    {
        InventItemKind.Weapon weapon => weapon.Param0?.ToString(),
        InventItemKind.Active active => active.Param0?.ToString(),
        _ => item.kind?.ToString(),
    };

    private static object Inventory(Hero hero) => Enumerable.Range(0, hero.inventory.nbWeapons)
        .Select(slot => hero.inventory.getEquippedWeaponOn(slot) is { } i ? $"{slot}:{Id(i)}:{i.posID}" : $"{slot}:-").ToArray();

    private static void Event(string name, object data) => File.AppendAllText(Path.Combine(run!, "item-events.jsonl"),
        JsonSerializer.Serialize(new { elapsed = began is double at ? clock.Elapsed.TotalSeconds - at : -1, name, data }) + "\n");

    public static void Poll()
    {
        if (run is null) return;
        var hero = ModCore.Modules.Game.Instance.HeroInstance;
        if (hero is not { destroyed: false } || hero._level is null || KirbyMod.Instance?.ActiveSession is null) return;
        if (began is null && hero.awake && !hero._level.game.hasCinematic()) began = clock.Elapsed.TotalSeconds;
        if (began is null || Environment.GetEnvironmentVariable("KIRBY_LAB_ITEM") is not { } specs) return;
        if (!validated && dialog is { } open && double.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_REPLACE_AT"), out double replaceAt) &&
            clock.Elapsed.TotalSeconds - began >= replaceAt)
        {
            validated = true;
            // Holding left selects the left item for replacement, then validates.
            open.select(open.liDesc, true);
            open.onValidate();
        }
        foreach (string spec in specs.Split(','))
        {
            if (dropped.Contains(spec) || spec.Split('@') is not [var kind, var at] ||
                !double.TryParse(at, out double time) || clock.Elapsed.TotalSeconds - began < time) continue;
            dropped.Add(spec);
            string? id = kind switch { "sword" => "BroadSword", "sword2" => "Katana", _ => PickDisallowedWeapon() };
            if (id is null) { Event("no-item", new { kind }); continue; }
            var item = new InventItem(new InventItemKind.Weapon(id.AsHaxeString()));
            drop = new dc.en.inter.ItemDrop(hero._level, hero.cx, hero.cy, item, false, HaxeProxy.Runtime.Ref<bool>.In(false));
            drop.init();
            Event("dropped", new { kind, id, inventory = Inventory(hero), useful = item.isUsefulFor(hero) });
        }
    }

    private static string? PickDisallowedWeapon()
    {
        var rows = dc.Data.Class.item.byIndex;
        var config = JsonSerializer.Deserialize<RuntimeConfig>(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(typeof(LabItemProbe).Assembly.Location)!, "config.json")))!;
        var entries = Enumerable.Range(0, rows.get_length()).Select(i => (dynamic)rows.getDyn(i))
            .Select(row => (Id: (string)row.id.ToString(), Group: (int)row.group, Droppable: (bool)row.droppable)).ToList();
        int swordGroup = entries.First(e => e.Id == "StartSword").Group;
        return entries.FirstOrDefault(e => e.Group == swordGroup && e.Droppable && !config.SwordIds.Contains(e.Id) &&
            !config.BowIds.Contains(e.Id) && !e.Id.Contains("Shield", StringComparison.Ordinal)).Id;
    }
}
