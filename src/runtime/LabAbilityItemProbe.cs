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
    private static readonly bool affixTest = Environment.GetEnvironmentVariable("KIRBY_LAB_ITEMS_AFFIXES") == "1";
    private static dc.en.mob.Zombie? zombie;
    private static HashSet<string>? entityTypesBefore;
    private static readonly HashSet<string> spawnedTypes = [];
    private static readonly List<int> lifeAfterAttacks = [];
    private static int uses;
    private static bool carriedAffix;

    private static HashSet<string> EntityTypes(Hero hero)
    {
        var types = new HashSet<string>();
        var entities = hero._level.entities;
        for (int j = 0; j < entities.length; j++)
            if (entities.getDyn(j) is dc.Entity { destroyed: false } e) types.Add(e.GetType().FullName ?? "");
        return types;
    }
    private static string? swordAbility, grenadeAbility, copied, slotAbility;
    private static bool followedSuction;
    private static bool swordUsefulToKirby = true, swordPaired, firstInMouth, mouthEmptied, spatCopied = true, returnedToOrigin;
    private static bool secondInMouth, slotHoldsSwallowedItem, hudShowsCopy, slotClearedAfterDiscard, hudClearedAfterDiscard;

    private static readonly bool keep = Environment.GetEnvironmentVariable("KIRBY_LAB_ITEMS_KEEP") == "1";
    private static readonly bool resume = Environment.GetEnvironmentVariable("KIRBY_LAB_ITEMS_RESUME") == "1";

    internal static void Install(string output)
    {
        run = output;
        KirbyHud.TestCardDrawn = (card, ability) =>
        {
            var texts = new List<string>();
            void Collect(dc.h2d.Object node, int depth)
            {
                if (node is dc.ui.Text text) texts.Add(text.text?.ToString() ?? "");
                if (depth < 8)
                    for (int i = 0; i < node.children.length; i++)
                        if (node.children.getDyn(i) is dc.h2d.Object child) Collect(child, depth + 1);
            }
            Collect(card, 0);
            // The pause screen stops the game, so each card is written as it is drawn.
            File.AppendAllText(Path.Combine(output, "pause-cards.jsonl"),
                JsonSerializer.Serialize(new { ability, item = card.ii?.getItemKind()?.ToString(), texts }) + "\n");
        };
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
        var host = InventItem.Class.fromItem(id.AsHaxeString(), null);
        // #57: a host-rolled affix on the replaced weapon must carry over.
        if (affixTest && id == "BroadSword") host.addAffix("BleedOnHit".AsHaxeString(), Ref<bool>.In(true));
        var drop = new ItemDrop(hero._level, cx, hero.cy, host, true, Ref<bool>.In(false));
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
        if (resume) { Resumed(session, frame); return 0; }
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
            if (affixTest)
            {
                carriedAffix = sword.item.hasAffix("BleedOnHit".AsHaxeString());
                sword.replaceItem(KirbyAbilityItems.Create("Sword", 1));
            }
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
                    if (affixTest)
                    {
                        // #57: give the copy's item an on-use and an on-hit affix, then fight a Zombie.
                        foreach (var affix in new[] { "FrontArrow", "BleedOnHit" })
                            swordItem?.addAffix(affix.AsHaxeString(), Ref<bool>.In(true));
                        zombie = dc.en.mob.Zombie.Class.create(hero._level, hero.cx + 3 * (hero.dir >= 0 ? 1 : -1), hero.cy, 1, 1);
                        // Enough life to survive the attacks, so a bleed shows as life lost afterwards.
                        zombie.maxLife = zombie.life = 30000;
                        entityTypesBefore = EntityTypes(hero);
                        KirbyAbilityItems.TestUsed = _ => uses++;
                        var copyItem = KirbyAbilityItems.CopyItem(hero);
                        Event("affix-setup", new
                        {
                            copyItem = copyItem?.getItemKind()?.ToString(), manager = hero.weaponsManager?.GetType().FullName,
                            weapon = copyItem is null ? null : hero.weaponsManager?.findItemInMain(copyItem)?.GetType().FullName,
                            affixes = new[] { "FrontArrow", "BleedOnHit" }.Select(a => copyItem?.hasAffix(a.AsHaxeString())).ToArray(),
                        });
                        Next(frame, 10);
                        return 0;
                    }
                    if (keep)
                    {
                        // Keep the copy for a save: give its item a rerolled-style affix, as the forge would.
                        var grenade = "Grenade".AsHaxeString();
                        swordItem?.addAffix(grenade, Ref<bool>.In(true));
                        Event("kept", new { grenadeAffix = swordItem?.hasAffix(grenade) });
                        Finish(frame);
                        return 0;
                    }
                    Next(frame, 5);
                    return 0;
                }
                return t is >= 10 and < 16 ? (ushort)128 : (ushort)0;
            case 10: // Attack the Zombie with B presses for 240 frames, then watch its life without attacking.
                if (zombie is { destroyed: false })
                {
                    zombie.lockAiS(1);
                    if (t >= 240) lifeAfterAttacks.Add(zombie.life);
                }
                if (t < 240)
                {
                    var newTypes = EntityTypes(hero).Except(entityTypesBefore ?? []).ToHashSet();
                    foreach (var type in newTypes) spawnedTypes.Add(type);
                    return Pulse(t, 2);
                }
                if (t >= 420)
                {
                    bool bleeding = lifeAfterAttacks.Count > 1 && lifeAfterAttacks[^1] < lifeAfterAttacks[0];
                    // The host's arrow affix fires a dc.en.Bullet.
                    bool arrows = spawnedTypes.Contains("dc.en.Bullet");
                    File.WriteAllText(Path.Combine(run!, "ability-affixes.json"), JsonSerializer.Serialize(new
                    {
                        copied = session.Rules.AbilityId, carriedAffix, uses, spawnedTypes, arrows,
                        lifeAfterAttacks = lifeAfterAttacks.Where((_, i) => i % 20 == 0).ToArray(), bleeding,
                        zombieDead = zombie is null || zombie.destroyed || zombie.life <= 0,
                        passed = carriedAffix && uses > 0 && arrows && (bleeding || zombie is null || zombie.destroyed || zombie.life <= 0),
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    finishedAt = frame;
                }
                return 0;
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

    /// <summary>
    /// After <c>--resume-game</c> of a run saved with KIRBY_LAB_ITEMS_KEEP: the
    /// copy's item must still hold its affix, and the pause card must show it.
    /// </summary>
    private static void Resumed(KirbySession session, uint frame)
    {
        var hero = session.Hero;
        if (frame - start!.Value < 300) return;
        finishedAt = frame;
        var slots = Enumerable.Range(0, hero.inventory.nbWeapons).Select(slot => hero.inventory.getEquippedWeaponOn(slot)).Select(item => item is null ? null : new
        {
            item = item.getItemKind().ToString(), level = item.getRawItemLevel(),
            grenade = item.hasAffix("Grenade".AsHaxeString()), colorless = item.hasAffix("Colorless".AsHaxeString()),
        }).ToArray();
        File.WriteAllText(Path.Combine(run!, "ability-items-resume.json"), JsonSerializer.Serialize(new
        {
            ability = session.Rules.AbilityId, slots,
            passed = session.Rules.AbilityId is not null && slots.Any(s => s is { grenade: true, colorless: true }),
        }, new JsonSerializerOptions { WriteIndented = true }));
        // Then the normal pause screen (its constructor pauses the game); see pause-cards.jsonl.
        _ = new dc.ui.pause.DefaultPause();
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
            // Ability items are "useful" to Kirby since #61 (a shop short of gold says so); the floor press
            // never picks one up, which the suction and mouth checks below establish.
            passed = swordAbility is not null && grenadeAbility is not null && shieldKept && swordPaired && followedSuction &&
                firstInMouth && mouthEmptied && !spatCopied && returnedToOrigin && secondInMouth && copied == swordAbility &&
                slotAbility == swordAbility && slotHoldsSwallowedItem && hudShowsCopy && slotClearedAfterDiscard && hudClearedAfterDiscard,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
