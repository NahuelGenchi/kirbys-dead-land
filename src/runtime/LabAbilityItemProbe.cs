using System.Text.Json;
using dc.en;
using dc.en.inter;
using dc.tool;
using HaxeProxy.Runtime;
using Kirby.Core;
using ModCore.Utilities;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in live check of Kirby ability items (#55). After a new run settles it
/// lists the level's floor items and places Kirby on a dry floor away from other
/// interactive objects. It then creates three new floor items through the normal
/// ItemDrop constructor: a sword in front of Kirby and a grenade behind him (both
/// must become ability items) and a shield (must stay). Kirby inhales the
/// sword's ability item and spits it out with B, which must put the item back
/// where it lay without a copy; he inhales it again and swallows it with DOWN,
/// which must copy its ability and put that same item in a weapon slot and the
/// HUD; SELECT then discards the copy, which must clear the slot.
/// </summary>
internal static class LabAbilityItemProbe
{
    private static string? run;
    private static uint? start, placedAt, spawnedAt, phaseAt, finishedAt, settledAt;
    private static int phase;
    private static object? scan, clearSpot;
    private static ItemDrop? sword, grenade, shield;
    private static InventItem? swordItem;
    private static (int Cx, int Cy) swordOrigin;
    private static ItemDrop? returned;
    private static string? swordAbility, grenadeAbility, copied, slotAbility;
    private static bool followedSuction;
    private static bool swordUsefulToKirby = true, swordPaired, firstInMouth, mouthEmptied, spatCopied = true, returnedToOrigin;
    private static bool secondInMouth, slotHoldsSwallowedItem, hudShowsCopy, slotClearedAfterDiscard, hudClearedAfterDiscard;

    internal static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        KirbyAbilityItems.TestReplaced = (original, ability) => Event("replaced", new { original, ability });
        KirbyAbilityItems.TestSlot = (ability, level, fromFloor) => Event("slot", new { ability, level, fromFloor });
        KirbyAbilityItems.TestReturned = (drop, cx, cy) => { returned = drop; Event("returned", new { cx, cy }); };
        KirbyInput.TestOverride = Input;
    }

    internal static void Poll() { }

    private static void Event(string kind, object value)
    {
        if (run is null) return;
        var entry = new { frame = KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame, kind, value };
        File.AppendAllText(Path.Combine(run, "ability-items.jsonl"), JsonSerializer.Serialize(entry) + "\n");
    }

    private static unsafe int Cell(dc.level.LevelMap map, int cx, int cy) =>
        cx < 0 || cy < 0 || cx >= map.wid || cy >= map.hei ? 3 : ((int*)map.collisions.bytes)[cy * map.wid + cx];

    /// <summary>
    /// Place Kirby on the nearest dry stretch of nine walkable floor cells with no
    /// other interactive object (items, lore, doors) within eight cells and no
    /// enemy within sixteen.
    /// </summary>
    internal static object? MoveToClearFloor(Hero hero)
    {
        var map = hero._level.map;
        var others = new List<(int X, int Y)>();
        var enemies = new List<(int X, int Y)>();
        var entities = hero._level.entities;
        for (int j = 0; j < entities.length; j++)
        {
            if (entities.getDyn(j) is dc.en.Interactive { destroyed: false } other) others.Add((other.cx, other.cy));
            if (entities.getDyn(j) is Mob { destroyed: false } mob) enemies.Add((mob.cx, mob.cy));
        }
        bool Walkable(int x, int y) => (Cell(map, x, y) & 3) == 0 && (Cell(map, x, y - 1) & 3) == 0 &&
            (Cell(map, x, y + 1) & 3) != 0 && map.getRoomAt(x, y) is not null && map.getWaterSurfaceY(x, y) <= 0;
        (int X, int Y)? best = null;
        int bestDistance = int.MaxValue;
        for (int y = 2; y < map.hei - 2; y++)
        for (int x = 6; x < map.wid - 6; x++)
        {
            if (!Enumerable.Range(x - 4, 9).All(c => Walkable(c, y))) continue;
            if (others.Any(o => Math.Abs(o.X - x) <= 8 && Math.Abs(o.Y - y) <= 4)) continue;
            if (enemies.Any(e => Math.Abs(e.X - x) <= 16 && Math.Abs(e.Y - y) <= 8)) continue;
            int distance = Math.Abs(x - hero.cx) + Math.Abs(y - hero.cy);
            if (distance < bestDistance) { bestDistance = distance; best = (x, y); }
        }
        if (best is not { } spot) return null;
        hero.setPosPixel(spot.X * 24 + 12, (spot.Y + 1) * 24);
        if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
        hero.dx = hero.dy = hero.bdx = hero.bdy = 0;
        return new { spot.X, spot.Y };
    }

    private static ItemDrop Spawn(Hero hero, string id, int cx)
    {
        var drop = new ItemDrop(hero._level, cx, hero.cy, InventItem.Class.fromItem(id.AsHaxeString(), null), true, Ref<bool>.In(false));
        drop.init();
        return drop;
    }

    private static object Scan(Hero hero)
    {
        var drops = new List<object>();
        var entities = hero._level.entities;
        for (int j = 0; j < entities.length; j++)
            if (entities.getDyn(j) is ItemDrop { destroyed: false } drop)
                drops.Add(new { item = drop.item.getItemKind().ToString(), ability = KirbyAbilityItems.AbilityOf(drop.item), drop.cx, drop.cy });
        return new { level = hero._level.map?.lootLevel, drops };
    }

    /// <summary>The live floor item holding this exact item (a spat-out item is a new floor entity).</summary>
    private static ItemDrop? FloorDrop(Hero hero, InventItem? item)
    {
        if (item is null) return null;
        var entities = hero._level.entities;
        for (int j = 0; j < entities.length; j++)
            if (entities.getDyn(j) is ItemDrop { destroyed: false } drop && drop.item.HashlinkPointer == item.HashlinkPointer) return drop;
        return null;
    }

    private static string? SlotAbility(Hero hero)
    {
        for (int slot = 0; slot < hero.inventory.nbWeapons; slot++)
            if (KirbyAbilityItems.AbilityOf(hero.inventory.getEquippedWeaponOn(slot)) is { } ability) return ability;
        return null;
    }

    private static bool SlotHolds(Hero hero, InventItem? item)
    {
        if (item is null) return false;
        for (int slot = 0; slot < hero.inventory.nbWeapons; slot++)
            if (hero.inventory.getEquippedWeaponOn(slot)?.HashlinkPointer == item.HashlinkPointer) return true;
        return false;
    }

    /// <summary>The item each HUD weapon slot is drawing from.</summary>
    private static string?[] HudItems(Hero hero)
    {
        if (hero._level?.game?.hud?.skillWeapons is not { } weapons) return [];
        var ids = new string?[weapons.length];
        for (int i = 0; i < weapons.length; i++)
            ids[i] = (weapons.getDyn(i) as dc.ui.hud.Skill)?.ii?.getItemKind()?.ToString();
        return ids;
    }

    /// <summary>Alternate a button every eight frames, so each press is a new original press.</summary>
    private static ushort Pulse(uint t, ushort mask) => (t / 8) % 2 == 1 ? mask : (ushort)0;

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        var provider = KirbyMod.Instance?.NativeProvider;
        if (machine is null || provider is null || run is null) return 0;
        Hero hero = session.Hero;
        uint frame = machine.State.Frame;
        start ??= frame;
        if (!hero.awake || hero._level.game.hasCinematic() || frame - start.Value < 180 || finishedAt is not null) return 0;
        if (placedAt is null)
        {
            scan = Scan(hero);
            clearSpot = MoveToClearFloor(hero);
            placedAt = frame;
            return 0;
        }
        if (spawnedAt is null)
        {
            if (frame - placedAt.Value < 60) return 0;
            int dir = hero.dir >= 0 ? 1 : -1;
            sword = Spawn(hero, "BroadSword", hero.cx + dir);
            grenade = Spawn(hero, "ExplosiveGrenade", hero.cx - 3 * dir);
            shield = Spawn(hero, "StartShield", hero.cx + 6 * dir);
            swordItem = sword.item;
            swordAbility = KirbyAbilityItems.AbilityOf(swordItem);
            grenadeAbility = KirbyAbilityItems.AbilityOf(grenade.item);
            swordUsefulToKirby = swordItem.isUsefulFor(hero);
            Event("spawned", new { swordAbility, grenadeAbility, shield = shield.item.getItemKind().ToString(),
                swordLevel = swordItem.getRawItemLevel(), colorless = swordItem.hasAffix("Colorless".AsHaxeString()) });
            spawnedAt = phaseAt = frame;
            phase = 1;
            return 0;
        }
        uint t = frame - phaseAt!.Value;
        var drop = FloorDrop(hero, swordItem);
        if (drop is not null && (provider.Enemies.Pairs(drop) || provider.ControlsEnemy(drop))) swordPaired = true;
        if (drop is not null && provider.ControlsEnemy(drop)) followedSuction = true;
        switch (phase)
        {
            case 1: // Stand by the item (its focus tooltip), then hold the original inhale until it is in the mouth.
                if (t < 60 && drop is not null) swordOrigin = (drop.cx, drop.cy);
                if (drop is null) { firstInMouth = true; Event("in-mouth", new { t }); Next(frame, 2); return 0; }
                if (t > 260) { Finish(frame); return 0; }
                return t >= 60 ? (ushort)2 : (ushort)0;
            case 2: // Spit with B until the original mouth is empty; the item must come back where it lay.
                // Stop pressing as soon as the item is back, or the next press would start a new inhale.
                if (!mouthEmptied && t >= 20 && (returned is not null || (machine.ActionFlags & 2) == 0)) { mouthEmptied = true; settledAt = t; }
                if (mouthEmptied && t - settledAt!.Value >= 90)
                {
                    var back = FloorDrop(hero, swordItem);
                    spatCopied = session.Rules.AbilityId is not null;
                    returnedToOrigin = back is not null && back.cx == swordOrigin.Cx && Math.Abs(back.cy - swordOrigin.Cy) <= 1;
                    Event("spat", new { spatCopied, back = back is null ? null : new { back.cx, back.cy }, origin = new { swordOrigin.Cx, swordOrigin.Cy } });
                    Next(frame, 3);
                    return 0;
                }
                if (t > 300) { Finish(frame); return 0; }
                return !mouthEmptied && t >= 20 ? Pulse(t, 2) : (ushort)0;
            case 3: // Inhale the returned item again.
                if (drop is null) { secondInMouth = true; Event("in-mouth-again", new { t }); Next(frame, 4); return 0; }
                if (t > 260) { Finish(frame); return 0; }
                return t >= 20 ? (ushort)2 : (ushort)0;
            case 4: // Swallow with DOWN; the copy and its item must reach the slot and the HUD.
                if (t >= 200)
                {
                    copied = session.Rules.AbilityId;
                    slotAbility = SlotAbility(hero);
                    slotHoldsSwallowedItem = SlotHolds(hero, swordItem);
                    hudShowsCopy = swordAbility is not null && HudItems(hero).Contains(AbilityItems.ItemId(swordAbility));
                    Event("copied", new { copied, slotAbility, slotHoldsSwallowedItem, hud = HudItems(hero) });
                    Next(frame, 5);
                    return 0;
                }
                return t is >= 10 and < 16 ? (ushort)128 : (ushort)0;
            case 5: // Discard with SELECT until base form; the slot and HUD must clear.
                if (session.Rules.IsBaseForm && settledAt is null) settledAt = t;
                if (settledAt is uint since && t - since >= 90 || t > 400)
                {
                    slotClearedAfterDiscard = session.Rules.IsBaseForm && SlotAbility(hero) is null;
                    hudClearedAfterDiscard = !HudItems(hero).Any(id => AbilityItems.AbilityOf(id) is not null);
                    Event("discarded", new { baseForm = session.Rules.IsBaseForm, slot = SlotAbility(hero), hud = HudItems(hero) });
                    Finish(frame);
                    return 0;
                }
                return settledAt is null && t >= 10 ? Pulse(t, 4) : (ushort)0;
        }
        return 0;
    }

    private static void Next(uint frame, int next)
    {
        phase = next;
        phaseAt = frame;
        settledAt = null;
    }

    private static void Finish(uint frame)
    {
        finishedAt = frame;
        bool shieldKept = shield is not null && !KirbyAbilityItems.IsAbilityItem(shield.item);
        File.WriteAllText(Path.Combine(run!, "ability-items.json"), JsonSerializer.Serialize(new
        {
            scan, clearSpot, swordAbility, grenadeAbility, shieldKept, swordUsefulToKirby, swordPaired, followedSuction, firstInMouth, mouthEmptied,
            spatCopied, returnedToOrigin, secondInMouth, copied, slotAbility, slotHoldsSwallowedItem, hudShowsCopy,
            slotClearedAfterDiscard, hudClearedAfterDiscard, phase,
            passed = swordAbility is not null && grenadeAbility is not null && shieldKept && !swordUsefulToKirby && swordPaired && followedSuction &&
                firstInMouth && mouthEmptied && !spatCopied && returnedToOrigin && secondInMouth && copied == swordAbility &&
                slotAbility == swordAbility && slotHoldsSwallowedItem && hudShowsCopy && slotClearedAfterDiscard && hudClearedAfterDiscard,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
