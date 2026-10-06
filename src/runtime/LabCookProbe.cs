using System.Text.Json;
using dc.en;
using dc.en.inter;
using dc.en.mob;
using dc.tool.atk;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in check that original Cook consumes a real host Zombie, the host death
/// path drops exactly one food item, and Kirby eats it through host interaction.
/// </summary>
internal static class LabCookProbe
{
    private const byte Cook = 24;
    private static string run = "";
    private static uint? start, setupStart, cookReadyAt, cookPressAt, interactAt;
    private static uint lastTick, lastReportFrame;
    private static bool hurt, setupFed;
    private static Mob? hurter, victim;
    private static int? victimUid, victimLife;
    private static int? hostLifeBeforeHurt, sourceHealthBeforeHurt, hostLifeBeforeFood, sourceHealthBeforeFood;
    private static int? hostLifeAfterFood, sourceHealthAfterFood;
    private static uint? killFrame, pickFrame;
    private static double[]? killPosition;
    private static int victimFoodLoot = -1;
    private static bool cookCaptureSeen;
    private static string? pickedItem;
    private static int? hostLifeAtPick, hostLifeAfterPickCall;
    private static int lastHostLife = -1, lastSourceHealth = -1;
    private static int interactKey = -1;
    private static readonly HashSet<nint> baselineFood = [];
    private static readonly List<ItemDrop> newFood = [];

    private static void Stage(string phase) =>
        System.IO.File.AppendAllText(Path.Combine(run, "cook-events.txt"), $"{lastTick}: {phase}\n");

    public static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        dc.Hook_Entity.kill += (orig, entity) =>
        {
            if (victim is not null && entity.HashlinkPointer == victim.HashlinkPointer && killFrame is null)
            {
                killFrame = lastTick;
                killPosition = [(entity.cx + entity.xr) * 24, (entity.cy + entity.yr) * 24];
                victimFoodLoot = 0;
                for (int i = 0; i < victim.loots.length; i++)
                    if (victim.loots.getDyn(i) is dc.LootType.InvItem loot && loot.Param0.isHealing()) victimFoodLoot++;
                Stage($"Victim kill: life={entity.life} foodLoot={victimFoodLoot}");
            }
            orig(entity);
        };
        Hook_Hero.pickItem += (orig, hero, entity, item, done) =>
        {
            if (entity is ItemDrop drop && newFood.Any(food => food.HashlinkPointer == drop.HashlinkPointer))
            {
                pickFrame = lastTick;
                pickedItem = item.toString().ToString();
                hostLifeAtPick = hero.life;
                orig(hero, entity, item, done);
                hostLifeAfterPickCall = hero.life;
                Stage($"Host pickItem: {pickedItem}; host life {hostLifeAtPick} -> {hostLifeAfterPickCall} inside the call");
                return;
            }
            orig(hero, entity, item, done);
        };
        // Host interaction reads the bound key directly (action 6). Hold only
        // that real binding, as a player would, while Kirby stands on the food.
        dc.hxd.Hook__Key.isDown += (orig, key) =>
            (key == interactKey && interactAt is uint at && lastTick - at < 8) || orig(key);
        dc.hxd.Hook__Key.isPressed += (orig, key) =>
            (key == interactKey && interactAt is uint at && lastTick == at) || orig(key);
        KirbyInput.TestOverride = Input;
    }

    private static Mob Spawn(Hero hero, double dx)
    {
        var mob = Zombie.Class.create(hero._level, hero.cx + 2, hero.cy, 1, 1);
        mob.setPosPixel((hero.cx + hero.xr) * 24 + dx, (hero.cy + hero.yr) * 24);
        if (mob.yr == 0) { mob.cy--; mob.yr = 1; }
        mob.lockAiS(30);
        return mob;
    }

    private static IEnumerable<ItemDrop> Food(Hero hero)
    {
        var entities = hero._level.entities;
        for (int i = 0; i < entities.length; i++)
            if (entities.getDyn(i) is ItemDrop drop && !drop.destroyed && drop.item?.isHealing() == true) yield return drop;
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null) return 0;
        uint tick = lastTick = machine.State.Frame;
        start ??= tick;
        uint elapsed = tick - start.Value;
        var hero = session.Hero;
        if (elapsed < 180) return 0;
        // Lose health first, in base form, so the hit cannot knock Cook off.
        if (!hurt)
        {
            hurt = true;
            hostLifeBeforeHurt = hero.life;
            sourceHealthBeforeHurt = machine.Health;
            hurter = Spawn(hero, 200);
            AttackUtils.Class.hit(AttackUtils.Class.createFromMob(hurter, 40, null), hero);
            hurter.kill();
            Stage($"Hurt Kirby: host {hostLifeBeforeHurt}, source {sourceHealthBeforeHurt}");
            return 0;
        }
        if (elapsed < 300) return 0;
        // Prepare Cook through original inhale, supplied swallowed content and DOWN.
        if (cookReadyAt is null)
        {
            if (machine.State.Ability != 1 && machine.State.Ability != Cook) { setupStart = null; return 4; }
            setupStart ??= tick;
            if (machine.State.Ability == Cook) { cookReadyAt = tick; Stage("Cook ready"); return 0; }
            if (tick - setupStart.Value < 30) return 2;
            if (!setupFed) { setupFed = machine.Feed(Cook); if (!setupFed) return 2; }
            return 128;
        }
        if (victim is null)
        {
            if (tick - cookReadyAt.Value < 60) return 0; // Original copy animation.
            foreach (var food in Food(hero)) baselineFood.Add(food.HashlinkPointer);
            hostLifeBeforeFood = hero.life;
            sourceHealthBeforeFood = machine.Health;
            victim = Spawn(hero, 36);
            victimUid = victim.__uid;
            victimLife = victim.life;
            Stage($"Zombie uid={victimUid} life={victimLife}; baseline food={baselineFood.Count}");
            return 0;
        }
        cookPressAt ??= tick + 20;
        if (tick >= cookPressAt && tick < cookPressAt + 40) return 2; // Original Cook.
        var target = newFood.FirstOrDefault(food => !food.destroyed);
        if (target is null || !target.landed || interactAt is not null) return 0;
        double dx = (target.cx + target.xr) * 24 - (hero.cx + hero.xr) * 24;
        if (dx > 6) return 16;
        if (dx < -6) return 32;
        var bindings = hero.controller.parent.get_bindings().primary;
        unsafe { if (6 < bindings.length) interactKey = ((int*)bindings.bytes)[6]; }
        interactAt = tick;
        Stage($"On food dx={dx:0.0}; pressing interact key {interactKey}, focused={hero.focused?.HashlinkPointer == target.HashlinkPointer}");
        return 0;
    }

    public static void Poll()
    {
        var session = KirbyMod.Instance?.ActiveSession;
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (run.Length == 0 || session is null || machine is null || machine.State.Frame == lastReportFrame) return;
        lastReportFrame = machine.State.Frame;
        var hero = session.Hero;
        if (hero.life != lastHostLife || machine.Health != lastSourceHealth)
        {
            lastHostLife = hero.life; lastSourceHealth = machine.Health;
            Stage($"Health host {lastHostLife}, source {lastSourceHealth}");
        }
        for (uint slot = 1; slot <= 31; slot++) cookCaptureSeen |= (machine.Proxy(slot).Flags & 128) != 0;
        if (victim is not null)
            foreach (var food in Food(hero))
                if (!baselineFood.Contains(food.HashlinkPointer) && newFood.All(seen => seen.HashlinkPointer != food.HashlinkPointer))
                {
                    newFood.Add(food);
                    Stage($"New host food {food.item.toString()} at {(food.cx + food.xr) * 24:0.0},{(food.cy + food.yr) * 24:0.0}");
                }
        if (pickFrame is uint picked && lastReportFrame - picked >= 30 && hostLifeAfterFood is null)
        {
            hostLifeAfterFood = hero.life;
            sourceHealthAfterFood = machine.Health;
        }
        System.IO.File.WriteAllText(Path.Combine(run, "cook-food.json"), JsonSerializer.Serialize(new
        {
            frame = lastReportFrame,
            sourceAbility = machine.State.Ability,
            hostLifeBeforeHurt, sourceHealthBeforeHurt,
            hostLifeBeforeFood, sourceHealthBeforeFood,
            victimUid, victimLife,
            cookCaptureSeen,
            victimKilled = victim is not null && (victim.destroyed || victim.life <= 0),
            killFrame, killPosition, victimFoodLoot,
            baselineFood = baselineFood.Count,
            newFoodCount = newFood.Count,
            newFood = newFood.Select(food => new
            {
                item = food.item?.toString().ToString(),
                x = (food.cx + food.xr) * 24, y = (food.cy + food.yr) * 24,
                food.landed, food.destroyed,
            }),
            interactKey, interactAt,
            pickFrame, pickedItem, hostLifeAtPick, hostLifeAfterPickCall,
            hostLifeAfterFood, sourceHealthAfterFood,
            currentHostLife = hero.life, currentSourceHealth = machine.Health,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
