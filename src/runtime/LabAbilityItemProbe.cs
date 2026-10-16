using System.Buffers.Binary;
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
/// KIRBY_LAB_ITEMS_STAR (#68) gives the copy's item a "+" and an affix before
/// the discard and follows the star it rides. With "catch" Kirby inhales the
/// star and swallows it, which must put that same item back in the slot; a
/// second discard then leaves the star alone, and the star and its item must be
/// gone after 7 seconds. With "spit" Kirby spits the caught star out, which
/// must lose the item with the copy.
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
    private static readonly string? starMode = Environment.GetEnvironmentVariable("KIRBY_LAB_ITEMS_STAR") switch
    {
        null or "" or "0" => null,
        "spit" => "spit",
        _ => "catch",
    };
    private static readonly List<object> starEvents = [];
    private static readonly Dictionary<uint, double> starX = [];
    private static uint? starAddress, discardedAt, carriedAt, caughtAt, inMouthAt, copiedBackAt, droppedAgainAt, carriedAgainAt, goneAt, lastStarSeen;
    private static int? shortenedFrom, shortenedTo, lastCountdown;
    private static object? enclosedSpot;
    private static readonly List<object> starTrace = [];
    private static string? itemBefore, itemAfter, goneReason;
    private static bool copiedBack, sameItemBack, hudShowsCopyBack, itemOnFloorAfter, itemInSlotAfter;
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
        if (starMode is null) return;
        KirbyStars.TestTrace = (address, kind, box) =>
        {
            if (kind != StarKind.Ability || KirbyMod.Instance?.NativeProvider?.Machine is not { } machine) return;
            uint frame = machine.State.Frame;
            starX[address] = box.CenterX;
            lastStarSeen = frame;
            Span<byte> countdown = stackalloc byte[2];
            machine.Read(address + AbilityStar.CountdownOffset, countdown);
            lastCountdown = BinaryPrimitives.ReadInt16LittleEndian(countdown);
            if (frame % 30 == 0) starTrace.Add(new { frame, address, x = box.CenterX, top = box.Top, countdown = lastCountdown });
        };
        KirbyStarItems.TestObserver = (kind, item, details) =>
        {
            uint? frame = KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame;
            bool ours = item is not null && swordItem is not null && item.HashlinkPointer == swordItem.HashlinkPointer;
            starEvents.Add(new { frame, kind, ours, details });
            Event("star-" + kind, new { ours, details });
            if (kind == "shortened" && shortenedFrom is null)
            {
                var value = JsonSerializer.SerializeToElement(details);
                shortenedFrom = value.GetProperty("from").GetInt32();
                shortenedTo = value.GetProperty("to").GetInt32();
            }
            if (!ours) return;
            switch (kind)
            {
                case "carried":
                    if (phase == 22) carriedAgainAt ??= frame; else carriedAt ??= frame;
                    starAddress = details as uint?;
                    break;
                case "caught": caughtAt ??= frame; break;
                case "in-mouth": inMouthAt ??= frame; break;
                case "gone": goneAt ??= frame; goneReason ??= details as string ?? "star ended"; break;
            }
        };
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

    /// <summary>
    /// For the star checks (#68): place Kirby on the nearest dry floor that walls
    /// at least three cells tall close on both sides within 30 cells, with no
    /// drop between them, no ability item on the floor within 12 cells and no
    /// enemy within 16.
    /// </summary>
    private static object? MoveToEnclosedFloor(Hero hero)
    {
        var map = hero._level.map;
        var items = new List<(int X, int Y)>();
        var enemies = new List<(int X, int Y)>();
        var entities = hero._level.entities;
        for (int j = 0; j < entities.length; j++)
        {
            if (entities.getDyn(j) is ItemDrop { destroyed: false } drop && KirbyAbilityItems.IsAbilityItem(drop.item)) items.Add((drop.cx, drop.cy));
            if (entities.getDyn(j) is Mob { destroyed: false } mob) enemies.Add((mob.cx, mob.cy));
        }
        bool Floor(int x, int y) => (Cell(map, x, y) & 3) == 0 && (Cell(map, x, y - 1) & 3) == 0 &&
            (Cell(map, x, y + 1) & 3) != 0 && map.getRoomAt(x, y) is not null && map.getWaterSurfaceY(x, y) <= 0;
        bool Wall(int x, int y) => (Cell(map, x, y) & 3) != 0 && (Cell(map, x, y - 1) & 3) != 0 && (Cell(map, x, y - 2) & 3) != 0;
        int? Reach(int x, int y, int step)
        {
            for (int i = 1; i <= 30; i++)
            {
                if (Wall(x + step * i, y)) return i;
                if (!Floor(x + step * i, y)) return null;
            }
            return null;
        }
        (int X, int Y, int Left, int Right)? best = null;
        int bestDistance = int.MaxValue;
        for (int y = 3; y < map.hei - 2; y++)
        for (int x = 2; x < map.wid - 2; x++)
        {
            if (!Floor(x, y) || Reach(x, y, -1) is not { } left || Reach(x, y, 1) is not { } right || left + right < 6) continue;
            if (items.Any(o => Math.Abs(o.X - x) <= 12 && Math.Abs(o.Y - y) <= 4)) continue;
            if (enemies.Any(e => Math.Abs(e.X - x) <= 16 && Math.Abs(e.Y - y) <= 8)) continue;
            int distance = Math.Abs(x - hero.cx) + Math.Abs(y - hero.cy);
            if (distance < bestDistance) { bestDistance = distance; best = (x, y, left, right); }
        }
        if (best is not { } spot) return null;
        hero.setPosPixel(spot.X * 24 + 12, (spot.Y + 1) * 24);
        if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
        hero.dx = hero.dy = hero.bdx = hero.bdy = 0;
        return new { spot.X, spot.Y, spot.Left, spot.Right };
    }

    /// <summary>KIRBY_LAB_ITEMS_QUALITY=N gives the probe's Broadsword N "+" before it is dropped (#56).</summary>
    private static readonly int quality = int.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_ITEMS_QUALITY"), out int q) ? q : 0;

    private static ItemDrop Spawn(Hero hero, string id, int cx)
    {
        var host = InventItem.Class.fromItem(id.AsHaxeString(), null);
        if (id == "BroadSword")
            for (int i = 0; i < quality; i++) host.addAffix("QualityUp".AsHaxeString(), Ref<bool>.In(true));
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
            // #68 needs a copy the original discards as a star; Sleep ends by itself without one.
            else if (starMode is not null) sword.replaceItem(KirbyAbilityItems.Create("Sword", 1));
            swordItem = sword.item;
            swordAbility = KirbyAbilityItems.AbilityOf(swordItem);
            grenadeAbility = KirbyAbilityItems.AbilityOf(grenade.item);
            swordUsefulToKirby = swordItem.isUsefulFor(hero);
            Event("spawned", new { swordAbility, grenadeAbility, shield = shield.item.getItemKind().ToString(),
                swordLevel = swordItem.getRawItemLevel(), colorless = swordItem.hasAffix("Colorless".AsHaxeString()),
                quality = swordItem.getUpgradeLevel(), bonus = swordItem.getDamageBonus(),
                infoLine = Kirby.Core.AbilityItems.InfoLine(swordItem.getDamageBonus()) });
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
                    if (starMode is not null && swordItem is not null)
                    {
                        // #68: a "+" and an affix the star must carry back.
                        swordItem.addAffix("QualityUp".AsHaxeString(), Ref<bool>.In(true));
                        swordItem.addAffix("Grenade".AsHaxeString(), Ref<bool>.In(true));
                        itemBefore = Describe(swordItem);
                        // The grenade's ability item lies where the star may bounce; the inhale must catch the star alone.
                        if (grenade is { destroyed: false }) grenade.destroy();
                        // A star that bounces off a ledge falls out of the original's 9-row window and ends early.
                        enclosedSpot = MoveToEnclosedFloor(hero);
                        Event("star-item", new { item = itemBefore, enclosedSpot });
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
                if (session.Rules.IsBaseForm && settledAt is null) { settledAt = t; discardedAt = frame; }
                if (settledAt is uint since && t - since >= (starMode is null ? 90 : 30) || t > 400)
                {
                    slotClearedAfterDiscard = session.Rules.IsBaseForm && SlotAbility(hero) is null;
                    hudClearedAfterDiscard = !HudItems(hero).Any(id => AbilityItems.AbilityOf(id) is not null);
                    Event("discarded", new { baseForm = session.Rules.IsBaseForm, slot = SlotAbility(hero), hud = HudItems(hero) });
                    if (starMode is not null) { Next(frame, 20); return 0; }
                    Finish(frame);
                    return 0;
                }
                return settledAt is null && t >= (starMode is null ? 10 : 60) ? Pulse(t, 4) : (ushort)0;
            case 20: // Go to the star and hold the original inhale until its item is in the mouth.
                if (inMouthAt is not null) { Next(frame, starMode == "spit" ? 23 : 21); return 0; }
                if (goneAt is not null || t > 420) { FinishStar(session, hero, frame); return 0; }
                if (caughtAt is not null) return 2;
                if (starAddress is not { } address || !starX.TryGetValue(address, out double x)) return 0;
                double dx = x - (hero.cx + hero.xr) * 24;
                ushort toward = dx < 0 ? (ushort)32 : (ushort)16;
                if (Math.Abs(dx) > 40) return toward;
                return Math.Sign(dx) != hero.dir && Math.Abs(dx) > 4 ? toward : (ushort)2;
            case 21: // Swallow with DOWN: the copy must come back with the same item.
                if (session.Rules.AbilityId == swordAbility && copiedBackAt is null) copiedBackAt = frame;
                if (copiedBackAt is not null && frame - copiedBackAt.Value >= 30 || t > 300)
                {
                    copiedBack = session.Rules.AbilityId == swordAbility;
                    itemAfter = Describe(swordItem);
                    sameItemBack = copiedBack && SlotHolds(hero, swordItem) && itemAfter == itemBefore;
                    hudShowsCopyBack = swordAbility is not null && HudItems(hero).Contains(AbilityItems.ItemId(swordAbility));
                    Event("star-copied-back", new { copiedBack, sameItemBack, itemAfter, hud = HudItems(hero) });
                    // The same item now waits for the second discard and the star's end.
                    starAddress = null; caughtAt = inMouthAt = null; goneAt = null; goneReason = null;
                    Next(frame, 22);
                    return 0;
                }
                // Down until the copy is back: a press while the inhale is still ending does nothing.
                return copiedBackAt is null && t >= 10 ? Pulse(t, 128) : (ushort)0;
            case 22: // Discard again and leave the star: it and its item must be gone after 7 s.
                if (session.Rules.IsBaseForm && droppedAgainAt is null) droppedAgainAt = frame;
                if (goneAt is not null && frame - goneAt.Value >= 60 || t > 900) { FinishStar(session, hero, frame); return 0; }
                return droppedAgainAt is null && t >= 10 ? Pulse(t, 4) : (ushort)0;
            case 23: // Spit the caught star out with B: the item must go with the copy.
                if (goneAt is not null && frame - goneAt.Value >= 90 || t > 400) { FinishStar(session, hero, frame); return 0; }
                return goneAt is null && t >= 20 ? Pulse(t, 2) : (ushort)0;
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

    /// <summary>What the star must carry: the item object, its level, "+", Legendary, affixes and damage bonus.</summary>
    private static string? Describe(InventItem? item) => item is null ? null : JsonSerializer.Serialize(new
    {
        pointer = item.HashlinkPointer.ToString("x"), item = item.getItemKind()?.ToString(), level = item.getRawItemLevel(),
        quality = item.getUpgradeLevel(), legendary = item.hasAffix("Legendary".AsHaxeString()),
        affixes = KirbyAbilityItems.Affixes(item), bonus = item.getDamageBonus(),
    });

    private static void FinishStar(KirbySession session, Hero hero, uint frame)
    {
        itemOnFloorAfter = FloorDrop(hero, swordItem) is not null;
        itemInSlotAfter = SlotHolds(hero, swordItem);
        bool baseForm = session.Rules.IsBaseForm;
        // From the second discard (catch) to the frame the star's item was gone.
        int? lifetime = droppedAgainAt is uint dropped && goneAt is uint gone ? (int)(gone - dropped) : null;
        // The star ran out (its last countdown) rather than falling out of the original's window.
        bool starLeft = lastStarSeen is uint seen && goneAt is uint end && seen <= end && lastCountdown <= 1;
        bool shortened = shortenedFrom == AbilityStar.OriginalLifetime && shortenedTo == AbilityStar.Lifetime;
        bool passed = starMode == "spit"
            ? carriedAt is not null && shortened && caughtAt is not null && inMouthAt is not null && goneReason == "spat" &&
              baseForm && !itemOnFloorAfter && !itemInSlotAfter
            : carriedAt is not null && shortened && copiedBack && sameItemBack && hudShowsCopyBack && carriedAgainAt is not null &&
              goneAt is not null && goneReason == "star ended" && lifetime is >= AbilityStar.Lifetime and <= AbilityStar.Lifetime + 30 &&
              starLeft && baseForm && !itemOnFloorAfter && !itemInSlotAfter;
        File.WriteAllText(Path.Combine(run!, "ability-star.json"), JsonSerializer.Serialize(new
        {
            mode = starMode, swordAbility, itemBefore, itemAfter, discardedAt, carriedAt, starAddress, shortenedFrom, shortenedTo,
            caughtAt, inMouthAt, copiedBackAt, copiedBack, sameItemBack, hudShowsCopyBack, droppedAgainAt, carriedAgainAt, goneAt, goneReason,
            lifetime, lastStarSeen, lastCountdown, starLeft, enclosedSpot, baseForm, itemOnFloorAfter, itemInSlotAfter, phase,
            events = starEvents, trace = starTrace, passed,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Finish(frame);
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
