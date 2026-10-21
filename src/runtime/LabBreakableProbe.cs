using System.Text.Json;
using dc.en.inter;
using ModCore.Utilities;
using LevelInfo = Hashlink.Virtuals.virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in copy attack against a genuine generated level door, or with
/// KIRBY_LAB_BREAKABLE_TARGET=gold a real host PileOfGold, or with
/// KIRBY_LAB_BREAKABLE_TARGET=hidden a real HiddenBlock ("Scratch the wall"),
/// spawned beside Kirby; hidden-base presses attack in base form instead.
/// KIRBY_LAB_BREAKABLE_TARGET=collector starts in the first transition level and
/// attacks the Collector's own exit door (a locked, breakable TriggeredDoor) while
/// Kirby holds unspent cells (#233).
/// </summary>
internal static class LabBreakableProbe
{
    private static readonly string? targetKind = Environment.GetEnvironmentVariable("KIRBY_LAB_BREAKABLE_TARGET");
    private static readonly bool Gold = targetKind == "gold";
    private static readonly bool BaseForm = targetKind == "hidden-base";
    private static readonly bool Hidden = targetKind is "hidden" or "hidden-base";
    private static readonly bool Collector = targetKind == "collector";
    private const int CollectorCells = 20;
    private static string? run;
    private static uint? setupStart, start;
    private static bool fed;
    private static dc.Entity? target;
    private static int initialLife, damage, goldDrops, activations;
    private static int? cellsBefore;
    private static uint lastFrame;

    public static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        if (Collector)
            dc.Hook_User.newGame += (orig, user, seed, level, custom, scoring, gameMode) =>
                orig(user, seed, ((HaxeProxy.Runtime.HaxeProxyBase)dc.Data.Class.level.resolve("T_Courtyard".AsHaxeString(), false))
                    .ToVirtual<LevelInfo>(), custom, scoring, gameMode);
        Hook_Door.onDamage += (orig, door, attack) => { orig(door, attack); Damaged(door, attack); };
        Hook_PileOfGold.onDamage += (orig, pile, attack) => { orig(pile, attack); Damaged(pile, attack); };
        Hook_HiddenBlock.onDamage += (orig, block, attack) => { orig(block, attack); Damaged(block, attack); };
        Hook_HiddenBlock.onActivate += (orig, block, by, longPress) =>
        {
            if (target?.HashlinkPointer == block.HashlinkPointer) activations++;
            orig(block, by, longPress);
        };
        Hook_PileOfGold.dropLoot += (orig, pile, x, y, power) =>
        {
            if (target?.HashlinkPointer == pile.HashlinkPointer) goldDrops++;
            orig(pile, x, y, power);
        };
        KirbyInput.TestOverride = Input;
    }

    private static void Damaged(dc.Entity entity, dc.tool.atk.AttackData attack)
    {
        if (target?.HashlinkPointer != entity.HashlinkPointer) return;
        damage += Math.Max(0, attack.inflictedDmg);
        System.IO.File.AppendAllText(Path.Combine(run!, "breakable-hits.jsonl"), JsonSerializer.Serialize(new
        {
            frame = KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame,
            attack.finalDmg, attack.inflictedDmg, entity.life, broken = (entity as Door)?.broken,
        }) + "\n");
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null) return 0;
        if (!BaseForm && machine.State.Ability != 18)
        {
            if (machine.State.Ability != 1) { setupStart = null; return 4; }
            setupStart ??= machine.State.Frame;
            if (machine.State.Frame - setupStart.Value < 30) return 2;
            if (!fed) { fed = machine.Feed(18); if (!fed) return 2; }
            return 128;
        }
        if (start is null)
        {
            var hero = session.Hero;
            if (Gold)
            {
                // Constructed and initialized as level markers spawn it.
                var pile = new PileOfGold(hero._level, hero.cx, hero.cy);
                pile.init();
                target = pile;
            }
            else if (Hidden)
            {
                // The wall's reward is a real host item (a small food, as the cook probe uses).
                var reward = dc.tool.InventItem.Class.fromItem(dc.level.LootGen.Class.getSmallHealingKind(), 1);
                var block = new HiddenBlock(hero._level, hero.cx, hero.cy, 1, reward);
                block.init();
                target = block;
            }
            else target = Enumerable.Range(0, hero._level.entities.length)
                .Select(i => hero._level.entities.getDyn(i)).OfType<Door>()
                .Where(d => !d.destroyed && d.life > 0 && d._breakable && d.locked == Collector && !d.broken &&
                    (!Collector || d is dc.en.inter.door.TriggeredDoor))
                .OrderBy(d => Math.Abs(d.cx - hero.cx) + Math.Abs(d.cy - hero.cy)).FirstOrDefault();
            if (target is null) return 0;
            if (Collector)
            {
                // Unspent cells keep the Collector's door closed, as in the owner's run.
                if (hero.cells < CollectorCells) hero.addCells(CollectorCells - hero.cells, HaxeProxy.Runtime.Ref<bool>.In(false));
                cellsBefore = hero.cells;
            }
            initialLife = target.life;
            hero.setPosPixel((target.cx + target.xr) * 24 - 28, (target.cy + target.yr) * 24);
            if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
            start = machine.State.Frame;
        }
        uint elapsed = machine.State.Frame - start.Value;
        // The transform's Right press and the copy attack's own motion can carry
        // Kirby past a wall. Keep him beside it while the attack runs.
        if (Hidden && target is not null && elapsed < 1000)
            session.Hero.setPosPixel((target.cx + target.xr) * 24 - 28, (target.cy + target.yr) * 24);
        // Copy acquisition changes the ability before its original transform
        // animation releases control. Let it finish before pressing attack.
        if (elapsed is >= 100 and < 115) return 16;
        if (elapsed is >= 120 and < 250) return 2;
        // The pile, wall or Collector's door (2 life) takes one life per hit: keep tapping attack until it breaks.
        if ((Gold || Hidden || Collector) && elapsed is >= 250 and < 1000) return (ushort)((elapsed / 8) % 2 == 0 ? 2 : 0);
        return 0;
    }

    public static void Poll()
    {
        var session = KirbyMod.Instance?.ActiveSession;
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (run is null || session is null || machine is null || lastFrame == machine.State.Frame) return;
        lastFrame = machine.State.Frame;
        var proxies = Enumerable.Range(1, 31).Select(slot =>
        {
            var proxy = machine.Proxy((uint)slot);
            return new { slot, proxy.Entity, proxy.Flags, proxy.Hp, x = proxy.XRaw / 65536.0, y = proxy.YRaw / 65536.0 };
        }).Where(p => p.Entity != 0).ToArray();
        System.IO.File.AppendAllText(Path.Combine(run, "breakable-trace.jsonl"), JsonSerializer.Serialize(new
        {
            frame = lastFrame,
            heroX = (session.Hero.cx + session.Hero.xr) * 24,
            heroY = (session.Hero.cy + session.Hero.yr) * 24,
            x = target is null ? 0 : (target.cx + target.xr) * 24,
            y = target is null ? 0 : (target.cy + target.yr) * 24,
            radius = target?.radius, height = target?.hei,
            canBeHit = target?.canBeHitBy(session.Hero),
            proxies,
        }) + "\n");
        // Floor items beside a secret wall: its reward once the wall opens.
        var drops = target is null ? Array.Empty<object>() : Enumerable.Range(0, session.Hero._level.entities.length)
            .Select(i => session.Hero._level.entities.getDyn(i)).OfType<dc.en.inter.ItemDrop>()
            .Where(d => !d.destroyed && Math.Abs(d.cx - target.cx) <= 3 && Math.Abs(d.cy - target.cy) <= 3)
            .Select(d => (object)new { d.cx, d.cy, item = d.item?.getItemKind().ToString() }).ToArray();
        System.IO.File.WriteAllText(Path.Combine(run, "breakable.json"), JsonSerializer.Serialize(new
        {
            frame = lastFrame, sourceAbility = machine.State.Ability,
            target = target?.GetType().FullName, targetUid = target?.__uid,
            initialLife, life = target?.life, destroyed = target?.destroyed,
            broken = (target as Door)?.broken, damage, goldDrops, activations, drops,
            locked = (target as Door)?.locked, breakable = (target as Door)?._breakable,
            closed = (target as Door)?._closed, cellsBefore, cells = session.Hero.cells,
            // A secret wall passes only when the copy hit activates it and its reward lies beside it.
            passed = BaseForm ? target is not null && activations == 0 && target.life == initialLife && damage == 0 :
                Hidden ? activations > 0 && drops.Length > 0 :
                target is not null && damage > 0 && ((target as Door)?.broken == true || target.life <= 0 || target.destroyed),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
