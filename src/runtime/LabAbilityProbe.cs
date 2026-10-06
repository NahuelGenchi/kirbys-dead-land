using Path = System.IO.Path;
using System.Text.Json;
using dc.en;
using dc.en.mob;
using dc.tool;
using dc.tool.atk;
using Kirby.Core;
using ModCore.Utilities;

namespace Kirby.Runtime;

/// <summary>Measures original ability damage and incoming host damage in a saved evolved run.</summary>
internal static class LabAbilityProbe
{
    private static string? run;
    private static uint? start;
    private static Mob? target, attacker;
    private static int initialLife, receivedDamage;
    private static int? beforeHostLife, beforeSourceHealth;
    private static uint? incomingFrame;
    private static int? afterAttackHostLife, afterAttackSourceHealth;
    private static uint lastFrame;
    private static string? initialAbility;
    private static uint? setupStart;
    private static bool setupFed, setupDone;
    private static bool savedAttackState;
    private static int[]? scrolls;
    private static bool scrollsGiven;
    private static readonly bool hudFixture = Environment.GetEnvironmentVariable("KIRBY_LAB_TEST_HUD") == "1";
    private static bool hudEquipped;
    private static uint? hudSettle;
    private static string? lastHud;
    private static int[]? tiers;
    private static readonly List<object> copyHits = [];
    // KIRBY_LAB_ABILITY_PLAN: original buttons by frame after the ability is ready,
    // [{"from":30,"to":55,"mask":2}, ...], instead of the damage test. Original
    // objects are then logged each frame in host pixels (ability-actors.jsonl).
    private sealed record PlanStep(uint From, uint To, ushort Mask);
    private static List<PlanStep>? plan;

    public static void Install(string output)
    {
        run = output;
        string? requestedScrolls = Environment.GetEnvironmentVariable("KIRBY_LAB_TEST_SCROLLS");
        if (requestedScrolls is not null)
            scrolls = requestedScrolls.Split(',').Select(c => int.Parse(c, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        KirbyEnemies.TestObserver = (mob, sourceDamage, hostDamage) =>
        {
            var enemies = KirbyMod.Instance?.NativeProvider?.Enemies;
            if (target?.HashlinkPointer != mob.HashlinkPointer || enemies is null) return;
            copyHits.Add(new
            {
                frame = KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame,
                hostLifeBefore = mob.life,
                sourceDamage,
                hostDamage,
                colorlessTier = enemies.ColorlessTier,
                hostMultiplier = enemies.ColorlessMultiplier,
                // What the host itself computes for colorless gear with this base damage.
                hostColorlessDamage = (int)dc.Const.Class.scaleHeroValueToTier((double)sourceDamage, enemies.ColorlessTier),
            });
        };
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_ABILITY_PLAN") is { } planPath && System.IO.File.Exists(planPath))
            plan = JsonSerializer.Deserialize<List<PlanStep>>(System.IO.File.ReadAllText(planPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        dc.Hook_Entity.kill += (orig, entity) =>
        {
            if (target?.HashlinkPointer == entity.HashlinkPointer)
                System.IO.File.AppendAllText(Path.Combine(run!, "ability-target-kill.txt"),
                    $"Frame {KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame}, life {entity.life}, " +
                    $"position {(entity.cx + entity.xr) * 24},{(entity.cy + entity.yr) * 24}\n{Environment.StackTrace}\n");
            orig(entity);
        };
        Hook_Mob.onDamage += (orig, mob, attack) =>
        {
            orig(mob, attack);
            if (target?.HashlinkPointer == mob.HashlinkPointer)
            {
                bool fromKirby = attack.source?.HashlinkPointer == KirbyMod.Instance?.ActiveSession?.Hero.HashlinkPointer;
                if (fromKirby) receivedDamage += Math.Max(0, attack.inflictedDmg);
                System.IO.File.AppendAllText(Path.Combine(run!, "ability-damage-events.jsonl"), JsonSerializer.Serialize(new
                {
                    frame = KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame,
                    uid = mob.__uid,
                    fromKirby,
                    source = attack.source?.GetType().FullName,
                    x = (mob.cx + mob.xr) * 24, y = (mob.cy + mob.yr) * 24,
                    baseDmg = Convert.ToString(attack.baseDmg, System.Globalization.CultureInfo.InvariantCulture),
                    attack.rawFinalDmg,
                    attack.finalDmg,
                    attack.inflictedDmg,
                    attack.sourceTier,
                    attack.useHeroScaling,
                    remainingLife = mob.life,
                }) + "\n");
            }
        };
        KirbyInput.TestOverride = Input;
    }

    private static Mob Spawn(Hero hero)
    {
        var mob = Zombie.Class.create(hero._level, hero.cx + 2, hero.cy, 1, 1);
        mob.setPosPixel((hero.cx + hero.xr) * 24 + 36, (hero.cy + hero.yr) * 24);
        if (mob.yr == 0) { mob.cy--; mob.yr = 1; }
        mob.lockAiS(10);
        return mob;
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null) return 0;
        if (hudFixture && !hudEquipped)
        {
            // A new run starts with the starting sword in the first (Left-Click)
            // slot. Like the combat probe, let the run settle, then add a real
            // shield through the normal inventory before copying.
            hudSettle ??= machine.State.Frame;
            if (machine.State.Frame - hudSettle.Value < 180) return 0;
            hudEquipped = true;
            session.Hero.inventory.add(new InventItem(new InventItemKind.Weapon("StartShield".AsHaxeString())));
            if (session.Hero.weaponsManager is dc.tool.hero.weap.BeheadedWeaponsManager manager)
                manager.onEquippedItemsUpdated(HaxeProxy.Runtime.Ref<bool>.In(false));
            return 0;
        }
        if (!setupDone && byte.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_TEST_ABILITY"), out byte requested) &&
            requested is >= 2 and <= 25)
        {
            if (machine.State.Ability != 1 && machine.State.Ability != requested)
            { setupStart = null; return 4; }
            setupStart ??= machine.State.Frame;
            if (machine.State.Ability == requested) setupDone = true;
            else
            {
                uint setupElapsed = machine.State.Frame - setupStart.Value;
                if (setupElapsed < 30) return 2;
                if (!setupFed)
                {
                    setupFed = machine.Feed(requested);
                    if (!setupFed) return 2;
                }
                return 128;
            }
        }
        if (scrolls is not null && !scrollsGiven)
        {
            // Real scroll items through the normal inventory, then the host's
            // own tier computation (Hero.computeTiers counts these item IDs).
            string[] ids = ["BrutalityUp", "TacticUp", "SurvivalUp"];
            for (int stat = 0; stat < 3; stat++)
                for (int n = 0; n < scrolls[stat]; n++)
                    session.Hero.inventory.add(InventItem.Class.fromItem(ids[stat].AsHaxeString(), null));
            session.Hero.computeTiers();
            tiers = [session.Hero.brutalityTier, session.Hero.tacticTier, session.Hero.survivalTier];
            scrollsGiven = true;
        }
        start ??= machine.State.Frame;
        uint elapsed = machine.State.Frame - start.Value;
        initialAbility ??= session.Rules.AbilityId;
        if (plan is not null)
            return (ushort)plan.Where(step => elapsed >= step.From && elapsed < step.To).Aggregate(0, (mask, step) => mask | step.Mask);
        // A saved Wheel roll continues without a held button. End it before
        // positioning the fresh test target, then perform a new real attack.
        if (elapsed == 0 && machine.State.Ability == 8 && machine.State.VxRaw != 0) return 2;
        if (elapsed >= 120 && target is null)
        {
            target = Spawn(session.Hero);
            // The Ice fixture must exercise the original frozen death remnant,
            // which requires killing the proxy with the original breath.
            if (machine.State.Ability == 10 && setupFed) target.life = 16;
            initialLife = target.life;
        }
        if (elapsed is >= 135 and < 225) return 2;
        // The HUD fixture later discards the copy (original Select) to check the slot returns to its weapon.
        if (hudFixture && elapsed is >= 900 and < 904) return 4;
        if (elapsed == 226 && machine.State.Ability == 8) return 2;
        if (elapsed >= 350 && attacker is null)
        {
            attacker = Spawn(session.Hero);
            beforeHostLife = session.Hero.life;
            beforeSourceHealth = machine.Health;
            incomingFrame = machine.State.Frame;
            var attack = AttackUtils.Class.createFromMob(attacker, 10, null);
            AttackUtils.Class.hit(attack, session.Hero);
        }
        return 0;
    }

    private static void LogActors(KirbySession session, NdsRuntime machine, uint elapsed, (int X, int Y) window)
    {
        var word = new byte[4];
        uint Read32(uint address) { machine.Read(address, word); return BitConverter.ToUInt32(word); }
        uint player = machine.State.Player;
        var actors = new List<object>();
        for (uint slot = 0; slot < 7; slot++)
            for (uint node = Read32(0x02049dec + slot * 4), guard = 0; node >= 0x02000000 && node < 0x02400000 && guard < 256; node = Read32(node), guard++)
                if (node != player)
                    actors.Add(new { node, kind = (byte)Read32(node + 0x16), parent = Read32(node + 4) == player ? "kirby" : Read32(node + 4).ToString("X"),
                        x = (int)Read32(node + 0x80) / 65536.0 + window.X * 24, y = (int)Read32(node + 0x84) / 65536.0 + window.Y * 24 });
        var hero = session.Hero;
        System.IO.File.AppendAllText(Path.Combine(run!, "ability-actors.jsonl"), JsonSerializer.Serialize(new
        {
            frame = lastFrame, elapsed, action = machine.Action, ability = machine.State.Ability,
            x = (hero.cx + hero.xr) * 24, feet = (hero.cy + hero.yr) * 24, actors,
        }) + "\n");
    }

    private static string Rgb(dc.h3d.Vector color) =>
        $"{(int)Math.Round(color.x * 255):x2}{(int)Math.Round(color.y * 255):x2}{(int)Math.Round(color.z * 255):x2}";

    /// <summary>Record each HUD weapon slot whenever its drawn state changes.</summary>
    private static void RecordHud(KirbySession session, uint frame)
    {
        if (session.Hero._level?.game?.hud is not { } hud) return;
        var weapons = hud.skillWeapons;
        var slots = Enumerable.Range(0, weapons.length).Select(slot =>
        {
            var skill = (dc.ui.hud.Skill)weapons.getDyn(slot);
            var equipped = session.Hero.inventory.getEquippedWeaponOn(slot);
            return new
            {
                slot,
                equipped = equipped?.getItemKind()?.ToString(),
                slotItem = skill.ii?.getItemKind()?.ToString(),
                sameItem = skill.ii?.HashlinkPointer == equipped?.HashlinkPointer,
                icon = skill.icon?.tile?.innerTex?.name?.ToString(),
                frame = skill.iconFg.visible ? skill.iconFg.groupName?.ToString() : null,
                background = skill.iconBg.groupName?.ToString(),
                skill.disableAmmo,
                iconWidth = skill.icon?.tile?.width,
                iconScale = skill.icon?.scaleX,
                pixelScale = skill.get_pixelScale(),
                // The two colors the host's RGBReplace shader paints the slot background with.
                background1 = Rgb(skill.iconBgShader.colorR__),
                background2 = Rgb(skill.iconBgShader.colorG__),
            };
        }).ToArray();
        var shown = KirbyHud.Shown;
        string state = JsonSerializer.Serialize(new { ability = session.Rules.AbilityId, overlay = shown.Active, shownAbility = shown.Ability, shown.IconHash, slots });
        if (state == lastHud) return;
        lastHud = state;
        System.IO.File.AppendAllText(Path.Combine(run!, "hud.jsonl"), JsonSerializer.Serialize(new
        {
            frame, ability = session.Rules.AbilityId, overlay = shown.Active, shownAbility = shown.Ability,
            tiers = new[] { shown.Tiers.Item1, shown.Tiers.Item2, shown.Tiers.Item3 },
            iconHash = shown.IconHash.ToString("x16"), iconSize = dc.ui.hud.Skill.Class.ICON_SIZE,
            iconBackgroundSize = dc.ui.hud.Skill.Class.ICONBG_SIZE, slots,
        }) + "\n");
        if (shown.Active)
        {
            // Private evidence: the decoded original icon, for visual review.
            byte[] icon = KirbyHud.IconPixels.ToArray();
            System.IO.File.WriteAllBytes(Path.Combine(run!, $"hud-icon-{shown.IconHash:x16}.rgba"), icon);
        }
    }

    public static void Poll()
    {
        var session = KirbyMod.Instance?.ActiveSession;
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (run is null || session is null || machine is null || machine.State.Frame == lastFrame) return;
        lastFrame = machine.State.Frame;
        if (hudFixture) RecordHud(session, machine.State.Frame);
        if (plan is not null)
        {
            if (start is uint begun && KirbyMod.Instance?.NativeProvider?.SourceWindow is { } window) LogActors(session, machine, lastFrame - begun, window);
            return;
        }
        if (!savedAttackState && start is uint started && lastFrame - started >= 155)
        {
            machine.SaveState(Path.Combine(run, "ability-mid-attack.state"));
            System.IO.File.WriteAllText(Path.Combine(run, "ability-mid-attack.json"),
                JsonSerializer.Serialize(new { frame = lastFrame, elapsed = lastFrame - started }));
            savedAttackState = true;
        }
        if (target is not null)
        {
            var proxies = Enumerable.Range(1, 31).Select(slot =>
            {
                var proxy = machine.Proxy((uint)slot);
                return new { slot, proxy.Entity, proxy.Flags, proxy.Hp, x = proxy.XRaw / 65536.0, y = proxy.YRaw / 65536.0 };
            }).ToArray();
            System.IO.File.AppendAllText(Path.Combine(run, "ability-target-trace.jsonl"), JsonSerializer.Serialize(new
            {
                frame = lastFrame, target.life, target.destroyed,
                x = (target.cx + target.xr) * 24, y = (target.cy + target.yr) * 24,
                target.dx, target.dy, target.bdx, target.bdy, target.radius, target.hei,
                controlled = KirbyMod.Instance?.NativeProvider?.ControlsEnemy(target),
                proxies,
            }) + "\n");
        }
        if (incomingFrame is uint incoming && machine.State.Frame > incoming && afterAttackHostLife is null)
        {
            afterAttackHostLife = session.Hero.life;
            afterAttackSourceHealth = machine.Health;
        }
        System.IO.File.WriteAllText(Path.Combine(run, "ability-damage.json"), JsonSerializer.Serialize(new
        {
            frame = machine.State.Frame,
            initialAbility,
            ability = session.Rules.AbilityId,
            sourceAbility = machine.State.Ability,
            targetUid = target?.__uid,
            initialTargetLife = initialLife,
            currentTargetLife = target?.life,
            receivedDamage,
            targetDead = target is not null && (target.life <= 0 || target.destroyed),
            incomingAttackSent = attacker is not null,
            beforeHostLife,
            afterHostLife = session.Hero.life,
            beforeSourceHealth,
            afterSourceHealth = machine.Health,
            incomingFrame,
            afterAttackHostLife,
            afterAttackSourceHealth,
            scrolls,
            tiers,
            currentTiers = new[] { session.Hero.brutalityTier, session.Hero.tacticTier, session.Hero.survivalTier },
            colorlessTier = KirbyMod.Instance?.NativeProvider?.Enemies.ColorlessTier,
            hostMultiplier = KirbyMod.Instance?.NativeProvider?.Enemies.ColorlessMultiplier,
            coreMultiplier = KirbyMod.Instance?.NativeProvider?.Enemies is { } e ? ColorlessDamage.Multiplier(e.ColorlessTier) : (double?)null,
            copyHits,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
