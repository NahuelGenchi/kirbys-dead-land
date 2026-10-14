using System.Text.Json;
using dc.en;
using dc.en.inter;
using dc.en.mob;
using HaxeProxy.Runtime;
using Kirby.Core;
using ModCore.Utilities;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in check of the F slot (#102) in a real run. Base Kirby inhales next to a
/// floor Crash item (it must stay on the floor), gets Sword through the original
/// swallow, takes Crash with the real Interact binding, fires it with the real
/// Heal binding (F) beside a real Zombie, then takes Mike and fires its three
/// shouts, and finally takes Paint and Cook to check the replacement. With
/// KIRBY_LAB_FSLOT_MOUTHFUL=1 it instead stores Crash in base form, inhales a
/// Zombie, presses F while holding it, spits, and presses F again (#158).
/// </summary>
internal static class LabFSlotProbe
{
    private const int InteractAction = 6, HealAction = 7;
    private const byte Sword = 18;
    private static string run = "";
    private static uint? start, phaseAt;
    private static uint lastTick, lastReportFrame;
    private static int phase, interactKey = -1, healKey = -1, firesLeft;
    private static uint? interactAt, healAt;
    private static ItemDrop? drop, probeDrop;
    private static Mob? zombie;
    private static bool fed;
    private static readonly List<object> events = [], trace = [], finishes = [];
    private static readonly Dictionary<string, object?> result = [];

    private static void Stage(string text, object? data = null)
    {
        events.Add(new { frame = lastTick, phase, text, data });
        System.IO.File.AppendAllText(Path.Combine(run, "fslot-events.txt"), $"{lastTick} [{phase}] {text} {(data is null ? "" : JsonSerializer.Serialize(data))}\n");
    }

    public static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        dc.hxd.Hook__Key.isDown += (orig, key) =>
            key == interactKey && interactAt is uint i && lastTick - i < 8 ||
            key == healKey && healAt is uint h && lastTick - h < 5 || orig(key);
        dc.hxd.Hook__Key.isPressed += (orig, key) =>
            key == interactKey && interactAt is uint i && lastTick == i ||
            key == healKey && healAt is uint h && lastTick == h || orig(key);
        KirbyFSlot.TestFinished = (ability, outcome) =>
        {
            var session = KirbyMod.Instance?.ActiveSession;
            finishes.Add(new { frame = lastTick, ability, outcome.Used, outcome.MainRestored,
                main = session?.Rules.AbilityId, stored = session?.Rules.Stored.Ability, uses = session?.Rules.Stored.Uses });
            Stage("F press finished", new { ability, outcome.Used, outcome.MainRestored });
        };
        KirbyFSlot.TestTaken = (ability, replaced) => Stage("Taken", new { ability, replaced });
        KirbyInput.TestOverride = Input;
    }

    private static int Binding(Hero hero, int action)
    {
        var bindings = hero.controller.parent.get_bindings().primary;
        unsafe { return action < bindings.length ? ((int*)bindings.bytes)[action] : -1; }
    }

    private static ItemDrop Spawn(Hero hero, string ability, double dx = 0)
    {
        var item = KirbyAbilityItems.Create(ability, 1);
        var made = new ItemDrop(hero._level, hero.cx, hero.cy, item, false, Ref<bool>.In(false));
        made.init();
        if (dx != 0) made.setPosPixel((hero.cx + hero.xr) * 24 + dx, (hero.cy + hero.yr) * 24);
        return made;
    }

    private static int CountDrops(Hero hero, string ability)
    {
        int count = 0;
        var entities = hero._level.entities;
        for (int i = 0; i < entities.length; i++)
            if (entities.getDyn(i) is ItemDrop { destroyed: false } d && KirbyAbilityItems.AbilityOf(d.item) == ability) count++;
        return count;
    }

    private static void Next(uint tick, string text, object? data = null) { phase++; phaseAt = tick + 1; interactAt = null; Stage(text, data); }

    private static uint? decidedAt;
    private static ushort decided;

    // KirbyInput.Read runs twice per host frame (host update and frame); act once per source frame.
    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null) return 0;
        if (decidedAt == machine.State.Frame) return decided;
        decidedAt = machine.State.Frame;
        return decided = Decide(session, machine);
    }

    private static ushort Decide(KirbySession session, NdsRuntime machine)
    {
        uint tick = lastTick = machine.State.Frame;
        start ??= tick;
        phaseAt ??= tick;
        uint inPhase = tick - phaseAt.Value;
        var hero = session.Hero;
        interactKey = Binding(hero, InteractAction);
        healKey = Binding(hero, HealAction);
        switch (phase)
        {
            case 0: // settle
                if (tick - start.Value < 180) return 0;
                if (Environment.GetEnvironmentVariable("KIRBY_LAB_RESUME_GAME") == "1")
                {
                    result["resumed"] = new { stored = session.Rules.Stored.Ability, uses = session.Rules.Stored.Uses,
                        item = KirbyAbilityItems.AbilityOf(KirbyFSlot.Item), main = session.Rules.AbilityId, source = machine.State.Ability };
                    phase = 20; phaseAt = tick + 1;
                    Stage("Resumed", result["resumed"]);
                    return 0;
                }
                if (Environment.GetEnvironmentVariable("KIRBY_LAB_FSLOT_MOUTHFUL") == "1")
                {
                    phase = 40; phaseAt = tick + 1;
                    Stage("Settled, mouthful path", new { canChange = NdsRuntime.CanChangeAbility, canReadMove = machine.TryMove(out _) });
                    return 0;
                }
                Next(tick, "Settled", new { canChange = NdsRuntime.CanChangeAbility });
                return 0;
            case 20: // after a resume: F fires the restored slot
                if (inPhase == 30) healAt = tick;
                if (inPhase > 30 && !KirbyFSlot.Sequence.Active && finishes.Count >= 1)
                {
                    result["resumedFire"] = new { main = session.Rules.AbilityId, source = machine.State.Ability,
                        stored = session.Rules.Stored.Ability, uses = session.Rules.Stored.Uses };
                    Next(tick, "Restored slot fired", result["resumedFire"]);
                }
                return 0;
            case 1: // a floor Crash item beside base Kirby is never inhaled
                if (inPhase == 0) { probeDrop = Spawn(hero, "Crash", hero.dir * 30); return 0; }
                if (inPhase < 70) return 2;
                result["inhaleIgnoredLimited"] = probeDrop is { destroyed: false } && session.Rules.IsBaseForm && (machine.ActionFlags & 2) == 0;
                result["limitedDropPairable"] = probeDrop is not null && KirbyAbilityItems.IsAbilityDrop(probeDrop);
                probeDrop?.destroy();
                Next(tick, "Inhale beside Crash done", new { kept = result["inhaleIgnoredLimited"] });
                return 0;
            case 2: // Sword through the original inhale, supplied content and DOWN
                if (inPhase < 30) return 0;
                if (machine.State.Ability == Sword && session.Rules.AbilityId == "Sword")
                {
                    if (inPhase > 200) Next(tick, "Sword ready");
                    return 0;
                }
                if (!fed) { if (inPhase < 50) return 2; fed = machine.Feed(Sword); return 2; }
                return 128;
            case 3: // take Crash with Interact
                if (inPhase == 0) { drop = Spawn(hero, "Crash"); return 0; }
                if (interactAt is null && inPhase > 30 && hero.focused?.HashlinkPointer == drop!.HashlinkPointer) { interactAt = tick; return 0; }
                if (interactAt is uint at && tick - at > 20)
                {
                    result["crashTaken"] = new { stored = session.Rules.Stored.Ability, uses = session.Rules.Stored.Uses, dropGone = drop!.destroyed,
                        item = KirbyAbilityItems.AbilityOf(KirbyFSlot.Item), main = session.Rules.AbilityId, source = machine.State.Ability };
                    interactAt = null;
                    Next(tick, "Crash taken", result["crashTaken"]);
                }
                return 0;
            case 4: // a Zombie to hit, then F
                if (inPhase == 0)
                {
                    zombie = Zombie.Class.create(hero._level, hero.cx + 3, hero.cy, 1, 1);
                    zombie.lockAiS(60);
                    result["zombieLifeBefore"] = zombie.life;
                    return 0;
                }
                if (inPhase == 20) { healAt = tick; Stage("Pressing F", new { key = healKey }); }
                if (inPhase > 20 && !KirbyFSlot.Sequence.Active && finishes.Count >= 1)
                {
                    result["zombieLifeAfter"] = zombie?.life;
                    result["zombieDestroyed"] = zombie?.destroyed;
                    result["afterCrash"] = new { main = session.Rules.AbilityId, source = machine.State.Ability, stored = session.Rules.Stored.Ability };
                    Next(tick, "Crash fired", result["afterCrash"]);
                }
                return 0;
            case 5: // take Mike
                if (inPhase < 60) return 0;
                if (drop is null || drop.destroyed && interactAt is null && session.Rules.Stored.Ability != "Mike") { if (inPhase == 60) drop = Spawn(hero, "Mike"); }
                if (interactAt is null && inPhase > 90 && hero.focused?.HashlinkPointer == drop!.HashlinkPointer) { interactAt = tick; return 0; }
                if (interactAt is uint m && tick - m > 20)
                {
                    interactAt = null; firesLeft = 3;
                    Next(tick, "Mike taken", new { stored = session.Rules.Stored.Ability, uses = session.Rules.Stored.Uses });
                }
                return 0;
            case 6: // three shouts
                if (KirbyFSlot.Sequence.Active || inPhase < 60) return 0;
                if (firesLeft == 0)
                {
                    result["afterMike"] = new { stored = session.Rules.Stored.Ability, uses = session.Rules.Stored.Uses, main = session.Rules.AbilityId, source = machine.State.Ability };
                    Next(tick, "Mike used up", result["afterMike"]);
                    return 0;
                }
                if (healAt is null || tick - healAt.Value > 60)
                {
                    healAt = tick; firesLeft--; phaseAt = tick;
                    Stage("Pressing F for a shout", new { uses = session.Rules.Stored.Uses });
                }
                return 0;
            case 7: // Paint then Cook, away from the start room's own floor items: the replaced one goes back to the floor
                if (inPhase < 150) return 16;
                if (inPhase == 180) drop = Spawn(hero, "Paint");
                if (inPhase == 320) drop = Spawn(hero, "Cook");
                if (inPhase % 30 == 15) Stage("Focus", new { focused = hero.focused?.GetType().Name,
                    focusedAbility = hero.focused is ItemDrop f ? KirbyAbilityItems.AbilityOf(f.item) : null,
                    onDrop = drop is not null && hero.focused?.HashlinkPointer == drop.HashlinkPointer, heroX = (hero.cx + hero.xr) * 24 });
                if (inPhase > 180 && interactAt is null && drop is { destroyed: false } && hero.focused?.HashlinkPointer == drop.HashlinkPointer) interactAt = tick;
                if (interactAt is uint p && tick - p > 20) interactAt = null;
                if (inPhase == 500)
                {
                    double hx = (hero.cx + hero.xr) * 24;
                    int near(string ability)
                    {
                        int count = 0; var entities = hero._level.entities;
                        for (int i = 0; i < entities.length; i++)
                            if (entities.getDyn(i) is ItemDrop { destroyed: false } d && KirbyAbilityItems.AbilityOf(d.item) == ability &&
                                Math.Abs((d.cx + d.xr) * 24 - hx) < 72) count++;
                        return count;
                    }
                    result["replace"] = new { stored = session.Rules.Stored.Ability, uses = session.Rules.Stored.Uses,
                        paintNearKirby = near("Paint"), cookNearKirby = near("Cook"), heroX = hx };
                    Next(tick, "Replacement checked", result["replace"]);
                }
                return 0;
            case 8: // fire the stored Cook at a Zombie: the original Cook still drops host food
                if (inPhase == 0)
                {
                    foodBefore = Food(hero);
                    zombie = Zombie.Class.create(hero._level, hero.cx + 2, hero.cy, 1, 1);
                    zombie.lockAiS(60);
                    return 0;
                }
                if (inPhase == 20) healAt = tick;
                if (inPhase > 20 && !KirbyFSlot.Sequence.Active && finishes.Count >= 5)
                {
                    result["afterCook"] = new { zombieDestroyed = zombie?.destroyed, foodBefore, foodAfter = Food(hero),
                        main = session.Rules.AbilityId, source = machine.State.Ability, stored = session.Rules.Stored.Ability };
                    Next(tick, "Cook fired", result["afterCook"]);
                }
                return 0;
            case 9: // take the Paint that Cook replaced, then fire it
                if (!KirbyFSlot.Sequence.Active && finishes.Count >= 6)
                {
                    result["afterPaint"] = new { main = session.Rules.AbilityId, source = machine.State.Ability, stored = session.Rules.Stored.Ability };
                    Next(tick, "Paint fired", result["afterPaint"]);
                    return 0;
                }
                if (KirbyFSlot.Sequence.Active) return 0;
                if (session.Rules.Stored.Ability != "Paint")
                {
                    if (interactAt is null && hero.focused is ItemDrop { destroyed: false } focused && KirbyAbilityItems.AbilityOf(focused.item) == "Paint")
                        interactAt = tick;
                    if (interactAt is uint q && tick - q > 20) interactAt = null;
                    if (inPhase > 300) Next(tick, "Paint not reached");
                    return 0;
                }
                if (healAt is null || tick - healAt.Value > 60) { healAt = tick; Stage("Pressing F for Paint"); }
                return 0;
            case 10: // a last Mike stays stored for the save check, away from Cook's food
                if (inPhase < 60) return 16;
                if (inPhase == 90) drop = Spawn(hero, "Mike");
                if (inPhase % 60 == 30) Stage("Focus", new { focused = hero.focused?.GetType().Name,
                    focusedAbility = hero.focused is ItemDrop f10 ? KirbyAbilityItems.AbilityOf(f10.item) : null,
                    onDrop = drop is not null && hero.focused?.HashlinkPointer == drop.HashlinkPointer, dropAlive = drop is { destroyed: false },
                    dropX = drop is null ? 0 : (drop.cx + drop.xr) * 24, heroX = (hero.cx + hero.xr) * 24, interactAt, sequence = KirbyFSlot.Sequence.Step.ToString() });
                if (inPhase > 90 && interactAt is null && drop is { destroyed: false } && hero.focused?.HashlinkPointer == drop.HashlinkPointer) interactAt = tick;
                if (session.Rules.Stored.Ability == "Mike")
                {
                    // A normal host save, as the smoke capture's checkpoint makes, for the resume run.
                    result["saved"] = dc.Main.Class.ME.writeSave();
                    Next(tick, "Mike stored and saved", new { uses = session.Rules.Stored.Uses, saved = result["saved"] });
                }
                return 0;
            case 11: // F, then a real host hit while the stored ability loads: the press is retried
                if (inPhase == 10) { healAt = tick; finishesBeforeHit = finishes.Count; lifeBeforeHit = hero.life; }
                if (inPhase == 11)
                {
                    var attacker = Zombie.Class.create(hero._level, hero.cx + 1, hero.cy, 1, 1);
                    dc.tool.atk.AttackUtils.Class.hit(dc.tool.atk.AttackUtils.Class.createFromMob(attacker, 20, null), hero);
                    attacker.kill();
                    Stage("Hit during the switch", new { step = KirbyFSlot.Sequence.Step.ToString() });
                }
                if (inPhase > 11 && KirbyFSlot.Sequence.Active) steps.Add(KirbyFSlot.Sequence.Step.ToString());
                if (inPhase > 11 && !KirbyFSlot.Sequence.Active && finishes.Count > finishesBeforeHit)
                {
                    result["hitDuringSwitch"] = new { lifeBeforeHit, lifeAfter = hero.life, firingEntries = steps.Count(s => s == "Firing"),
                        finish = finishes[^1], main = session.Rules.AbilityId, source = machine.State.Ability,
                        stored = session.Rules.Stored.Ability, uses = session.Rules.Stored.Uses };
                    Next(tick, "Hit check done", result["hitDuringSwitch"]);
                }
                return 0;
            case 40: // #158: store Crash with Interact, still in base form
                if (inPhase == 0) { drop = Spawn(hero, "Crash"); return 0; }
                if (interactAt is null && inPhase > 30 && hero.focused?.HashlinkPointer == drop!.HashlinkPointer) { interactAt = tick; return 0; }
                if (interactAt is uint taken && tick - taken > 20 && session.Rules.Stored.Ability == "Crash")
                    Next(tick, "Crash stored", new { uses = session.Rules.Stored.Uses, main = session.Rules.AbilityId, source = machine.State.Ability });
                else if (inPhase > 400) Next(tick, "Crash not stored");
                return 0;
            case 41: // inhale a Zombie and hold it
                if (inPhase == 0)
                {
                    zombie = Zombie.Class.create(hero._level, hero.cx + 2 * hero.dir, hero.cy, 1, 1);
                    zombie.lockAiS(60);
                    return 0;
                }
                if (machine.HoldsMouthful)
                {
                    mouthfulAt ??= tick;
                    if (tick - mouthfulAt.Value >= 20) { usesBefore = session.Rules.Stored.Uses; Next(tick, "Holding a mouthful", Move(machine)); }
                    return 0;
                }
                if (inPhase > 300) { Next(tick, "No mouthful", Move(machine)); phase = 99; return 0; }
                // Let go of B once the catch is in (the mouth count), before the mouthful's stand.
                return inPhase > 20 && (machine.ActionFlags & 2) == 0 ? (ushort)2 : (ushort)0;
            case 42: // F while the mouthful is held: nothing starts and the mouthful stays
                if (inPhase == 10) { healAt = tick; Stage("Pressing F with a mouthful", Move(machine)); }
                if (inPhase > 10)
                {
                    pressStarted |= KirbyFSlot.Sequence.Active;
                    sourcesSeen.Add(machine.State.Ability);
                    mouthfulKept &= machine.HoldsMouthful;
                }
                if (inPhase == 90)
                {
                    result["fWithMouthful"] = new { pressStarted, mouthfulKept, sourcesSeen = sourcesSeen.Order().Select(source => (int)source).ToArray(),
                        stored = session.Rules.Stored.Ability, usesBefore, usesAfter = session.Rules.Stored.Uses,
                        main = session.Rules.AbilityId, move = Move(machine) };
                    Next(tick, "F with a mouthful done", result["fWithMouthful"]);
                }
                return 0;
            case 43: // spit, then F fires Crash as usual
                if (inPhase < 3) return 2;
                if (inPhase == 60)
                {
                    result["afterSpit"] = Move(machine);
                    healAt = tick; finishesBeforeSpitPress = finishes.Count; Stage("Pressing F after the spit");
                }
                if (inPhase > 60 && !KirbyFSlot.Sequence.Active && finishes.Count > finishesBeforeSpitPress)
                {
                    result["fAfterSpit"] = new { finish = finishes[^1], stored = session.Rules.Stored.Ability, uses = session.Rules.Stored.Uses,
                        main = session.Rules.AbilityId, source = machine.State.Ability };
                    Next(tick, "F after the spit done", result["fAfterSpit"]);
                }
                else if (inPhase > 900) Next(tick, "F after the spit did not finish");
                return 0;
            default:
                return 0;
        }
    }

    private static uint? mouthfulAt;
    private static int usesBefore, finishesBeforeSpitPress;
    private static bool pressStarted, mouthfulKept = true;
    private static readonly HashSet<byte> sourcesSeen = [];

    private static object Move(NdsRuntime machine) => machine.TryMove(out NdsMove move)
        ? new { action = move.Action.ToString("X4"), move.Carry, move.Ability, descriptor = move.Descriptor.ToString("X8"), mouthful = move.Mouthful }
        : new { action = machine.Action.ToString("X4"), Carry = (byte)0, Ability = machine.State.Ability, descriptor = "", mouthful = false };

    private static int foodBefore, finishesBeforeHit, lifeBeforeHit;
    private static readonly List<string> steps = [];

    private static int Food(Hero hero)
    {
        int count = 0;
        var entities = hero._level.entities;
        for (int i = 0; i < entities.length; i++)
            if (entities.getDyn(i) is ItemDrop { destroyed: false } d && d.item?.isHealing() == true) count++;
        return count;
    }

    public static void Poll()
    {
        var session = KirbyMod.Instance?.ActiveSession;
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (run.Length == 0 || session is null || machine is null || machine.State.Frame == lastReportFrame) return;
        lastReportFrame = machine.State.Frame;
        if (KirbyFSlot.Sequence.Active || trace.Count > 0 && trace.Count < 4000 && lastReportFrame % 30 == 0)
            trace.Add(new { frame = lastReportFrame, source = machine.State.Ability, main = session.Rules.AbilityId,
                step = KirbyFSlot.Sequence.Step.ToString(), pending = machine.AbilityChangePending, action = machine.Action.ToString("X4") });
        var skill = session.Hero._level?.game?.hud?.skillPotion;
        System.IO.File.WriteAllText(Path.Combine(run, "fslot.json"), JsonSerializer.Serialize(new
        {
            frame = lastReportFrame, phase,
            result, finishes,
            hud = new { visible = skill?.visible, hasIcon = skill?.icon?.tile is not null },
            stored = session.Rules.Stored.Ability, uses = session.Rules.Stored.Uses, main = session.Rules.AbilityId,
            events, trace,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
