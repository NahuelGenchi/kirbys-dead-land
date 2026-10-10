using System.Text.Json;
using dc.en;
using dc.en.inter;
using dc.tool;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in live check of challenge rewards and shops with ability items (#61,
/// #107). On a clear floor of a new run it creates, through their own
/// constructors, an item altar holding a Broadsword, a 60-second timed item
/// holding an explosive grenade, a shop booth holding a Lightning Whip and a
/// legendary altar holding a Legendary Lightning Whip. Each must hold an ability
/// item (the legendary altar's a Legendary one). With 5000 gold it then
/// activates each as the player's short press would: the altar must disable, the
/// timed item empty, the booth be bought (its price taken), the legendary altar
/// empty, and each item must lie on the floor as a floor ability item.
/// </summary>
internal static class LabRewardItemsProbe
{
    private static string? run;
    private static uint? start, placedAt, spawnedAt;
    private static bool done;
    private static ItemAltar? altar;
    private static TimedItem? timed;
    private static ShopBooth? booth;
    private static LegendaryAltar? legendary;
    private static InventItem? altarItem, timedItem, boothItem, legendaryItem;
    private static int moneyBefore, price;
    private static readonly List<object> steps = [];

    internal static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        KirbyInput.TestOverride = Input;
    }

    internal static void Poll() { }

    private static string? Id(InventItem? item) => item?.getItemKind()?.ToString();

    private static int OnFloor(Hero hero, InventItem? item)
    {
        if (item is null) return 0;
        int count = 0;
        var entities = hero._level.entities;
        for (int j = 0; j < entities.length; j++)
            if (entities.getDyn(j) is ItemDrop { destroyed: false } drop && drop.item?.HashlinkPointer == item.HashlinkPointer) count++;
        return count;
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null || run is null || done) return 0;
        var hero = session.Hero;
        uint frame = machine.State.Frame;
        start ??= frame;
        if (!hero.awake || hero._level.game.hasCinematic() || frame - start.Value < 180) return 0;
        if (placedAt is null)
        {
            steps.Add(new { clearSpot = LabAbilityItemProbe.MoveToClearFloor(hero) });
            placedAt = frame;
            return 0;
        }
        if (spawnedAt is null)
        {
            if (frame - placedAt.Value < 40) return 0;
            var level = hero._level;
            altar = new ItemAltar(level, hero.cx + 2, hero.cy, InventItem.Class.fromItem("BroadSword".AsHaxeString(), null));
            altar.init();
            timed = new TimedItem(level, hero.cx + 4, hero.cy, InventItem.Class.fromItem("ExplosiveGrenade".AsHaxeString(), null), 60);
            timed.init();
            booth = new ShopBooth(level, hero.cx - 3, hero.cy, InventItem.Class.fromItem("LightningWhip".AsHaxeString(), null), 1000);
            booth.init();
            var legendaryWeapon = InventItem.Class.fromItem("LightningWhip".AsHaxeString(), null);
            legendaryWeapon.addAffix("Legendary".AsHaxeString(), Ref<bool>.In(true));
            legendary = new LegendaryAltar(level, hero.cx + 6, hero.cy, legendaryWeapon);
            legendary.init();
            altarItem = altar.item; timedItem = timed.item; boothItem = booth.item; legendaryItem = legendary.item;
            price = booth.price;
            hero._level.game.data.money = 5000;
            moneyBefore = hero._level.game.data.money;
            steps.Add(new
            {
                spawned = new { altar = Id(altarItem), timed = Id(timedItem), booth = Id(boothItem), legendary = Id(legendaryItem), price },
            });
            spawnedAt = frame;
            return 0;
        }
        uint t = frame - spawnedAt.Value;
        if (t < 60) return 0;
        if (steps.Count == 2)
        {
            altar!.onActivate(hero, false);
            steps.Add(new { altar = new { enabled = altar.isEnabled, onFloor = OnFloor(hero, altarItem) } });
            return 0;
        }
        if (steps.Count == 3 && t >= 90)
        {
            timed!.onActivate(hero, false);
            steps.Add(new { timed = new { item = Id(timed.item), onFloor = OnFloor(hero, timedItem) } });
            return 0;
        }
        if (steps.Count == 4 && t >= 120)
        {
            booth!.onActivate(hero, false);
            int money = hero._level.game.data.money;
            steps.Add(new { booth = new { destroyed = booth.destroyed, money, onFloor = OnFloor(hero, boothItem) } });
            return 0;
        }
        if (steps.Count == 5 && t >= 150)
        {
            legendary!.onActivate(hero, false);
            steps.Add(new { legendary = new { item = Id(legendary.item), onFloor = OnFloor(hero, legendaryItem) } });
            return 0;
        }
        if (steps.Count == 6 && t >= 210)
        {
            done = true;
            int money = hero._level.game.data.money;
            bool Ability(InventItem? item) => KirbyAbilityItems.IsAbilityItem(item);
            File.WriteAllText(Path.Combine(run, "rewards.json"), JsonSerializer.Serialize(new
            {
                steps, price, moneyBefore, moneyAfter = money,
                legendaryAffix = legendaryItem?.hasAffix("Legendary".AsHaxeString()) ?? false,
                passed = Ability(altarItem) && Ability(timedItem) && Ability(boothItem) && Ability(legendaryItem) &&
                    legendaryItem!.hasAffix("Legendary".AsHaxeString()) && !altar!.isEnabled &&
                    timed!.item is null && booth!.destroyed && legendary!.item is null && money == moneyBefore - price &&
                    OnFloor(hero, altarItem) == 1 && OnFloor(hero, timedItem) == 1 && OnFloor(hero, boothItem) == 1 &&
                    OnFloor(hero, legendaryItem) == 1,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return 0;
    }
}
