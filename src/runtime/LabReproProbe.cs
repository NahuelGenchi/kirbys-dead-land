using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using dc.en;
using dc.hl.types;
using dc.tool;
using dc.tool.weap;
using HashlinkNET.Native.Impl;
using ModCore.Utilities;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>Opt-in keyboard/controller reproduction with copied inventory and normal tutorials.</summary>
internal static unsafe class LabReproProbe
{
    private static string? run;
    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private static readonly JsonSerializerOptions TraceOptions = new()
    { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private static double? began;
    private static double elapsed;
    private static uint? lastFrame;
    private static bool bindingsWritten;
    private static bool itemsWritten;
    private static HashSet<int> held = [], pressed = [];
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo InputField = typeof(KirbyNdsProvider).GetField("input", Private)!;
    private static readonly FieldInfo EnemiesField = typeof(KirbyNdsProvider).GetField("enemies", Private)!;
    private static readonly FieldInfo RendererField = typeof(KirbyNdsProvider).GetField("renderer", Private)!;
    private static readonly FieldInfo EquipmentField = typeof(KirbySpriteRenderer).GetField("equipment", Private)!;
    private static readonly FieldInfo LibraryField = typeof(KirbySpriteRenderer).GetField("equipmentLibrary", Private)!;
    private static readonly FieldInfo BitmapField = typeof(KirbySpriteRenderer).GetField("bitmap", Private)!;
    private static nint originalBitmap;
    private static bool hideOriginalBitmap;
    private static double hideFrom = 12, hideUntil = 14;
    private static bool healthFixture, lethalDone, hitDone, mobScalingWritten, abilityReady, abilityFed;
    private static uint? abilityStart;
    private static string toggleMode = "";
    private static double lastScheduledZoom = double.NaN;
    private static int healthPhase;
    private static HashSet<int> padHeld = [], padPressed = [];
    private static Step[]? steps;
    private sealed record Step(double At, double Until, int[]? Actions, int[]? Keys, int[]? PadKeys);
    private static int Get(ArrayBytes_Int array, int index) => index >= 0 && index < array.length ? ((int*)array.bytes)[index] : -1;

    internal static void Install(string output)
    {
        run = output;
        healthFixture = Environment.GetEnvironmentVariable("KIRBY_LAB_REPRO_HEALTH") == "1";
        if (double.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_HIDE_BITMAP_FROM"), out double from)) hideFrom = from;
        if (double.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_HIDE_BITMAP_UNTIL"), out double until)) hideUntil = until;
        string? plan = Environment.GetEnvironmentVariable("KIRBY_LAB_REPRO_PLAN");
        if (plan is not null) steps = JsonSerializer.Deserialize<Step[]>(File.ReadAllText(plan),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        dc.hxd.Hook__Key.isDown += (orig, key) => held.Contains(key) || orig(key);
        dc.hxd.Hook__Key.isPressed += (orig, key) => pressed.Contains(key) || orig(key);
        Hook_Controller.padIsDown += (orig, controller, key) => padHeld.Contains(key) || orig(controller, key);
        Hook_Controller.padIsPressed += (orig, controller, key) => padPressed.Contains(key) || orig(controller, key);
        dc.h2d.Hook_Bitmap.draw += (orig, bitmap, context) =>
        { if (!hideOriginalBitmap || bitmap.HashlinkPointer != originalBitmap) orig(bitmap, context); };
        Hook_Hero.blink += (orig, hero, frames) =>
        {
            File.AppendAllText(Path.Combine(run!, "repro-events.jsonl"), JsonSerializer.Serialize(new
            { elapsed, name = "hero-blink", frames, frame = KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame }, TraceOptions) + "\n");
            orig(hero, frames);
        };
        dc.Hook_Main.update += (orig, main) =>
        {
            orig(main);
            var h = ModCore.Modules.Game.Instance.HeroInstance;
            if (run is null || h?.spr is null || Environment.GetEnvironmentVariable("KIRBY_LAB_VISIBILITY") != "1") return;
            var renderer = KirbyMod.Instance?.NativeProvider is { } prov ? RendererField.GetValue(prov) as KirbySpriteRenderer : null;
            var bmp = renderer is null ? null : BitmapField.GetValue(renderer) as dc.h2d.Bitmap;
            var light = h.selfLight;
            File.AppendAllText(Path.Combine(run, "visibility.jsonl"), JsonSerializer.Serialize(new
            { elapsed, frame = KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame, heroVisible = h.visible,
              light = light is null ? null : new { light.killed, light.coreColor, light.coreAlpha, light.intensity, light.globalAlpha,
                  core = light.core is not null, coreVisible = light.core?.visible, coreA = light.core?.a, range = light.l?.range },
              sprVisible = h.spr.visible, sprAlpha = h.spr.alpha, entityAlpha = h.sprAlpha, invis = h.invisibilityAlpha,
              bitmapVisible = bmp?.visible, bitmapAlpha = bmp?.alpha, h.life }, TraceOptions) + "\n");
        };
        Hook_BaseShield.onShieldStartParry += (orig, weapon) =>
        { Event("shield-start", weapon); orig(weapon); };
        Hook_BaseShield.onShieldCounterSuccessful += (orig, weapon, attack, flag) =>
        { Event("shield-counter", weapon); orig(weapon, attack, flag); };
        KirbyNdsProvider.Diagnostic = (name, data) => File.AppendAllText(Path.Combine(run!, "repro-events.jsonl"),
            JsonSerializer.Serialize(new { elapsed, name, frame = KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame, data }, TraceOptions) + "\n");
        // Do not replace source buttons, inventory, tutorials, or weapon-manager queries.
    }

    private static void Event(string name, Weapon weapon) => File.AppendAllText(Path.Combine(run!, "repro-events.jsonl"),
        JsonSerializer.Serialize(new { elapsed, name, weapon = weapon.GetType().FullName, item = weapon.item.permanentId }) + "\n");

    internal static void Poll()
    {
        if (run is null) return;
        var provider = KirbyMod.Instance?.NativeProvider;
        var nds = provider?.Machine;
        var hero = ModCore.Modules.Game.Instance.HeroInstance;
        // The normal outfit has no source machine; inputs and host positions
        // are still traced so host movement can be compared with Kirby's.
        if (hero is not { destroyed: false } || hero._level is null || provider is null)
        { held.Clear(); pressed.Clear(); padHeld.Clear(); padPressed.Clear(); return; }
        if (began is null && hero.awake && !hero._level.game.hasCinematic()) began = clock.Elapsed.TotalSeconds;
        elapsed = began is double at ? clock.Elapsed.TotalSeconds - at : -1;
        if (healthFixture && nds is not null) HealthFixture(hero, nds);
        if (nds is not null && !abilityReady && byte.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_REPRO_ABILITY"), out byte wantedAbility) &&
            wantedAbility is >= 2 and <= 25)
        {
            // Lab-only: prepare a copy ability through the original inhale, supplied
            // contents and Down before the normal-binding timeline starts.
            if (nds.State.Ability == wantedAbility) { abilityReady = true; KirbyInput.TestOverride = null; began = clock.Elapsed.TotalSeconds; }
            else
            {
                abilityStart ??= nds.State.Frame;
                uint since = nds.State.Frame - abilityStart.Value;
                KirbyInput.TestOverride = _ =>
                {
                    if (since < 30) return 2;
                    if (!abilityFed) { abilityFed = nds.Feed(wantedAbility); if (!abilityFed) return 2; }
                    return 128;
                };
                return;
            }
        }
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_TOGGLES") is { } toggles)
        {
            string mode = "";
            foreach (var part in toggles.Split(','))
                if (part.Split(':') is [var from, var value] && double.TryParse(from, out double t) && elapsed >= t) mode = value;
            if (mode != toggleMode)
            {
                toggleMode = mode;
                var renderer2 = RendererField.GetValue(provider) as KirbySpriteRenderer;
                var bmp2 = renderer2 is null ? null : BitmapField.GetValue(renderer2) as dc.h2d.Bitmap;
                if (hero.selfLight?.l is { } pl) pl.visible = !mode.Contains("nolight");
                if (hero.heroHead is { } head)
                {
                    if (head.eye is not null) head.eye.visible = mode != "nohead";
                    if (head.headNormalSb is not null) head.headNormalSb.visible = mode != "nohead";
                    if (head.headAddSb is not null) head.headAddSb.visible = mode != "nohead";
                }
                if (bmp2 is not null && mode.Contains("blend")) bmp2.blendMode = mode.Contains("blendnone") ? new dc.h2d.BlendMode.None() : new dc.h2d.BlendMode.Alpha();
                File.AppendAllText(Path.Combine(run, "repro-events.jsonl"), JsonSerializer.Serialize(new
                { elapsed, name = "toggle", mode, eye = hero.heroHead?.eye is not null, eyeVisible = hero.heroHead?.eye?.visible,
                  eyeAlpha = hero.heroHead?.eye?.alpha, heroHasHead = hero.heroHead?.heroHasHead, lightRange = hero.selfLight?.l?.range }) + "\n");
            }
        }
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_ZOOM_SCHEDULE") is { } zooms && hero._level.viewport is { } vp)
        {
            double? wanted = null; bool update = false;
            foreach (var part in zooms.Split(','))
                if (part.Split(':') is [var from, var value] && double.TryParse(from, out double t) && elapsed >= t)
                { update = value.EndsWith('u'); if (double.TryParse(value.TrimEnd('u'), out double z)) wanted = z; }
            if (wanted is double target && target != lastScheduledZoom)
            {
                lastScheduledZoom = target;
                vp.set_zoom(target);
                if (update) vp.updateSizes();
                File.AppendAllText(Path.Combine(run, "repro-events.jsonl"), JsonSerializer.Serialize(new
                { elapsed, name = "zoom", target, update, vp.zoom, vp.minZoom, vp.wid, vp.hei }) + "\n");
            }
        }
        if (nds is not null && !hitDone && double.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_REPRO_HIT_AT"), out double hitAt) &&
            elapsed >= hitAt && hero.life > 0)
        {
            hitDone = true;
            // KIRBY_LAB_REPRO_HIT_SIDE=-1 places the attacker on Kirby's left.
            int side = Environment.GetEnvironmentVariable("KIRBY_LAB_REPRO_HIT_SIDE") == "-1" ? -1 : 1;
            var attacker = dc.en.mob.Zombie.Class.create(hero._level, hero.cx + 2 * side, hero.cy, 1, 1);
            attacker.lockAiS(10);
            int before = hero.life, dir = hero.dir;
            var attack = dc.tool.atk.AttackUtils.Class.createFromMob(attacker, 20, null);
            dc.tool.atk.AttackUtils.Class.hit(attack, hero);
            File.AppendAllText(Path.Combine(run, "repro-events.jsonl"), JsonSerializer.Serialize(new
            { elapsed, name = "host-attack-20", side, dir, hit = attack.hitResult?.GetType().Name, before, hero.life, hero.maxLife,
              sourceHealth = nds.Health, frame = nds.State.Frame }) + "\n");
        }
        if (nds is not null && !lethalDone && double.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_REPRO_LETHAL_AT"), out double lethalAt) &&
            elapsed >= lethalAt && hero.life > 0)
        {
            // Lab-only: a real host Zombie attack against low health, delivered
            // through the normal host attack path into the original collision.
            lethalDone = true;
            hero.life = 3;
            var attacker = dc.en.mob.Zombie.Class.create(hero._level, hero.cx + 2, hero.cy, 1, 1);
            attacker.lockAiS(10);
            var attack = dc.tool.atk.AttackUtils.Class.createFromMob(attacker, 10, null);
            dc.tool.atk.AttackUtils.Class.hit(attack, hero);
            File.AppendAllText(Path.Combine(run, "repro-events.jsonl"), JsonSerializer.Serialize(new
            { elapsed, name = "lethal-host-attack", hero.life, sourceHealth = nds.Health, frame = nds.State.Frame }) + "\n");
        }
        hideOriginalBitmap = Environment.GetEnvironmentVariable("KIRBY_LAB_HIDE_BITMAP") == "1" && elapsed >= hideFrom && elapsed < hideUntil;
        var bindings = hero.controller.parent.get_bindings();
        if (!mobScalingWritten && elapsed >= 0.5 && Environment.GetEnvironmentVariable("KIRBY_LAB_MOB_SCALING") == "1")
        {
            mobScalingWritten = true;
            var scale = dc.Const.Class.scaleMobLifeToTier;
            var rows = new List<object>();
            for (int i = 0; i < hero._level.entities.length; i++)
                if (hero._level.entities.getDyn(i) is Mob mob && !mob.destroyed)
                {
                    double baseLife = 0;
                    try { var lifeData = mob._infos.life; baseLife = Convert.ToDouble(lifeData is dc.hl.types.ArrayBase arr ? arr.getDyn(0) : lifeData); } catch { }
                    rows.Add(new { type = mob.type?.ToString(), baseLife, mob.maxLife, mob.life, mob.elite, mob.trueLifeTier, boss = mob is dc.en.mob.Boss,
                        scaled = scale.Invoke(baseLife, mob.trueLifeTier), unit = scale.Invoke(1000.0, mob.trueLifeTier) / 1000 });
                }
            var curve = Enumerable.Range(0, 25).Select(t => new { t, unit = scale.Invoke(1000.0, t) / 1000 }).ToArray();
            File.WriteAllText(Path.Combine(run, "mob-scaling.json"), JsonSerializer.Serialize(new
            { level = hero._level.GetType().FullName, brutality = hero.getTierValueFromTierId("Brutality".AsHaxeString()),
              tactic = hero.getTierValueFromTierId("Tactic".AsHaxeString()), survival = hero.getTierValueFromTierId("Survival".AsHaxeString()),
              rows, curve }, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        }
        if (!itemsWritten && elapsed >= 0)
        {
            itemsWritten = true;
            var nearby = new List<object>();
            for (int i = 0; i < hero._level.entities.length; i++)
                if (hero._level.entities.getDyn(i) is dc.Entity entity && !entity.destroyed &&
                    (entity is dc.en.Interactive || entity is Mob))
                    nearby.Add(new { type = entity.GetType().FullName, uid = entity.__uid,
                        x = (entity.cx + entity.xr) * 24, y = (entity.cy + entity.yr) * 24,
                        entity.life, entity.hei, entity.radius,
                        itemId = entity is dc.en.inter.ItemDrop itemDrop ? itemDrop.item._itemData.id?.ToString() : null,
                        shield = entity is dc.en.inter.ItemDrop drop && drop.item.hasTag("Shield".AsHaxeString()) });
            File.WriteAllText(Path.Combine(run, "generated-entities.json"), JsonSerializer.Serialize(nearby, TraceOptions));
        }
        if (!bindingsWritten)
        {
            bindingsWritten = true;
            File.WriteAllText(Path.Combine(run, "physical-bindings.json"), JsonSerializer.Serialize(new
            {
                primary = Values(bindings.primary), secondary = Values(bindings.secondary), third = Values(bindings.third),
                padA = Values(bindings.padA), padB = Values(bindings.padB), padC = Values(bindings.padC),
                hero.controller.parent.mode, keyboardX = 88, arrowDown = 40,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        HashSet<int> next = [], nextPad = [];
        if (elapsed >= 0)
        {
            if (steps is not null)
            {
                foreach (var step in steps.Where(s => elapsed >= s.At && elapsed < s.Until))
                {
                    foreach (int action in step.Actions ?? []) next.Add(Get(bindings.primary, action));
                    foreach (int key in step.Keys ?? []) next.Add(key);
                    foreach (int key in step.PadKeys ?? []) nextPad.Add(key);
                }
            }
            else
            {
                if (elapsed is >= 2 and < 6 && elapsed % 1 < .2) next.Add(Get(bindings.primary, 2));
                if (elapsed is >= 7 and < 11 && elapsed % 1 < .25) next.Add(Get(bindings.primary, 3));
                if (elapsed is >= 12 and < 15) next.Add(Get(bindings.primary, 3));
            }
        }
        next.Remove(-1);
        pressed = next.Except(held).ToHashSet();
        held = next;
        padPressed = nextPad.Except(padHeld).ToHashSet();
        padHeld = nextPad;
        File.AppendAllText(Path.Combine(run, "host-motion.jsonl"), JsonSerializer.Serialize(new
        { elapsed, ms = clock.Elapsed.TotalMilliseconds, frame = nds?.State.Frame, x = (hero.cx + hero.xr) * 24, y = (hero.cy + hero.yr) * 24,
          hero.dx, hero.dy, animation = hero.spr?.groupName?.ToString(), zoom = hero._level.viewport?.zoom,
          vpWid = hero._level.viewport?.wid, vpHei = hero._level.viewport?.hei }, TraceOptions) + "\n");
        if (nds is null) return;
        var state = nds.State;
        if (lastFrame == state.Frame) return;
        lastFrame = state.Frame;
        var enemies = (KirbyEnemies)EnemiesField.GetValue(provider)!;
        var renderer = RendererField.GetValue(provider) as KirbySpriteRenderer;
        var equipment = renderer is null ? null : EquipmentField.GetValue(renderer) as dc.libs.heaps.slib.HSprite;
        var library = renderer is null ? null : LibraryField.GetValue(renderer) as dc.libs.heaps.slib.SpriteLib;
        var bitmap = renderer is null ? null : BitmapField.GetValue(renderer) as dc.h2d.Bitmap;
        originalBitmap = bitmap?.HashlinkPointer ?? 0;
        uint[] words = new uint[16];
        for (int i = 0; i < words.Length; i++) words[i] = state.StateWords[i];
        var inventory = Enumerable.Range(0, hero.inventory.nbWeapons).Select(slot =>
        {
            var item = hero.inventory.getEquippedWeaponOn(slot);
            var weapon = item is null ? null : hero.weaponsManager.findItemInMain(item);
            return new { slot, item = item?.permanentId, weapon = weapon?.GetType().FullName };
        }).ToArray();
        var bar = hero._level.game.hud.heroLifeBar;
        File.AppendAllText(Path.Combine(run, "repro-trace.jsonl"), JsonSerializer.Serialize(new
        {
            elapsed, frame = state.Frame, input = InputField.GetValue(provider), flash = Flash(nds), bank0 = Bank0(nds), held, pressed, padHeld, padPressed, hideOriginalBitmap,
            source = new { state.Player, state.X, state.Y, state.VxRaw, state.VyRaw, state.Ability, state.Kind,
                state.Flags, state.FacingRaw, state.Graphic, state.Animation, health = nds.Health, words },
            host = new { x = (hero.cx + hero.xr) * 24, y = (hero.cy + hero.yr) * 24, hero.life, hero.maxLife, hero.curRally,
                hero.dx, hero.dy, hero.bdx, hero.bdy, hero.radius, hero.hei, hero.awake,
                animation = hero.spr?.groupName?.ToString(), frame = hero.spr?.frame,
                sprX = hero.spr?.x, sprY = hero.spr?.y, hero.dir, shaders = Shaders(hero.spr?.shaders),
                blend = hero.spr?.blendMode?.GetType().Name },
            controller = new { available = KirbyInput.Available(hero), hero.controller.manualLock,
                hero.controller.parent.isLocked, hero.controller.parent.suspendTimer, now = Lib_std.sys_time(),
                exclusive = hero.controller.parent.exclusiveId?.ToString(), cinematic = hero._level.game.hasCinematic(),
                tutorial = hero._level.game.user.tutorial.current?.ToString() },
            inventory,
            hud = new { bar.healTrail, bar.lastHealTime, bar.freezed,
                current = new { bar.curState.life, bar.curState.maxLife, bar.curState.recover, bar.curState.bonusLife },
                old = new { bar.oldState.life, bar.oldState.maxLife, bar.oldState.recover, bar.oldState.bonusLife } },
            body = new { renderer?.RenderedObjects, renderer?.UnsupportedObjects, renderer?.BodyPixels, renderer?.TextureFrame,
                bitmapVisible = bitmap?.visible, bitmapAlpha = bitmap?.alpha, bitmapX = bitmap?.x, bitmapY = bitmap?.y, parent = bitmap?.parent is not null },
            equipment = new { renderer?.EquipmentVisible, animation = equipment?.groupName?.ToString(),
                frame = equipment?.frame, x = equipment?.x, y = equipment?.y,
                scaleX = equipment?.scaleX, scaleY = equipment?.scaleY,
                sprite = equipment?.HashlinkPointer.ToInt64(), library = library?.HashlinkPointer.ToInt64(), shaders = Shaders(equipment?.shaders),
                blend = equipment?.blendMode?.GetType().Name, bitmapBlend = bitmap?.blendMode?.GetType().Name },
            bindings = enemies.Capture(),
            proxies = Enumerable.Range(1, 31).Select(slot => {
                var p = nds.Proxy((uint)slot);
                return new { slot, p.Entity, p.Flags, p.Hp, p.XRaw, p.YRaw, p.NormalUpdate };
            }).Where(p => p.Flags != 0 || p.Entity != 0).ToArray(),
        }, TraceOptions) + "\n");
    }

    private static void HealthFixture(Hero hero, NdsRuntime nds)
    {
        void Record(string name) => File.AppendAllText(Path.Combine(run!, "repro-health.jsonl"), JsonSerializer.Serialize(new
        {
            elapsed, name, hero.life, hero.maxLife, hero.curRally, hero.bonusLife,
            sourceHealth = nds.Health, sourceFrame = nds.State.Frame,
            active = KirbyMod.Instance?.ActiveSession is not null,
            hud = new { hero._level.game.hud.heroLifeBar.healTrail,
                current = new { hero._level.game.hud.heroLifeBar.curState.life, hero._level.game.hud.heroLifeBar.curState.maxLife },
                old = new { hero._level.game.hud.heroLifeBar.oldState.life, hero._level.game.hud.heroLifeBar.oldState.maxLife } },
        }, TraceOptions) + "\n");
        if (healthPhase == 0 && elapsed >= 1)
        {
            nds.SetHealth(57, 76);
            hero.setLifeAndRally(57, 57);
            Record("wounded"); healthPhase++;
        }
        else if (healthPhase == 1 && elapsed >= 2)
        {
            for (int i = 0; i < 12; i++) hero.updateMaxLife();
            hero.updateLifeBar(); Record("twelve-maxlife-recalculations"); healthPhase++;
        }
        else if (healthPhase == 2 && elapsed >= 4)
        {
            dc.en.inter.ItemDrop? shield = null;
            string? requestedItem = Environment.GetEnvironmentVariable("KIRBY_LAB_REPRO_PICKUP");
            for (int i = 0; i < hero._level.entities.length; i++)
                if (hero._level.entities.getDyn(i) is dc.en.inter.ItemDrop { destroyed: false } drop &&
                    (requestedItem is null ? drop.item.hasTag("Shield".AsHaxeString()) : drop.item._itemData.id?.ToString() == requestedItem))
                { shield = drop; break; }
            if (shield is not null)
            {
                hero.setPosPixel((shield.cx + shield.xr) * 24 - 14, (shield.cy + 1) * 24);
                if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
                hero.dx = hero.dy = hero.bdx = hero.bdy = 0;
                File.AppendAllText(Path.Combine(run!, "repro-events.jsonl"), JsonSerializer.Serialize(new
                { elapsed, name = "fixture-near-existing-item", shield.__uid, shield.pickLocked, shield.longPressPickup,
                    canActivate = shield.canBeActivated(hero), item = shield.item.permanentId,
                    itemId = shield.item._itemData.id?.ToString() }) + "\n");
            }
            Record("near-generated-item"); healthPhase++;
        }
        else if (healthPhase == 3 && elapsed >= 15)
        {
            for (int i = 0; i < 12; i++) hero.updateMaxLife();
            hero.updateLifeBar(); Record("after-pickup-and-recalculation"); healthPhase++;
        }
        else if (healthPhase == 4 && elapsed >= 24)
        { Record("before-normal-outfit"); hero.applySkin(null!); healthPhase++; }
        else if (healthPhase == 5 && elapsed >= 29)
        { Record("normal-outfit"); hero.applySkin("KirbySuperStarUltra".AsHaxeString()); healthPhase++; }
        else if (healthPhase == 6 && elapsed >= 31)
        { Record("kirby-restored"); healthPhase++; }
        else if (healthPhase == 7 && elapsed >= 33 && Environment.GetEnvironmentVariable("KIRBY_LAB_REPRO_ENCOUNTER") == "1")
        {
            dc.en.mob.Zombie? nearest = null;
            double distance = double.PositiveInfinity;
            for (int i = 0; i < hero._level.entities.length; i++)
                if (hero._level.entities.getDyn(i) is dc.en.mob.Zombie { destroyed: false, life: > 0 } mob)
                {
                    double next = Math.Abs(mob.cx - hero.cx) + Math.Abs(mob.cy - hero.cy);
                    if (next < distance) { distance = next; nearest = mob; }
                }
            if (nearest is not null)
            {
                hero.setPosPixel((nearest.cx + nearest.xr) * 24 - 110, (nearest.cy + nearest.yr) * 24);
                if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
                hero.dx = hero.dy = hero.bdx = hero.bdy = 0;
                File.AppendAllText(Path.Combine(run!, "repro-events.jsonl"), JsonSerializer.Serialize(new
                { elapsed, name = "fixture-near-existing-zombie", nearest.__uid, nearest.life,
                    x = (nearest.cx + nearest.xr) * 24, y = (nearest.cy + nearest.yr) * 24 }) + "\n");
            }
            Record("near-natural-enemy"); healthPhase++;
        }
        else if (healthPhase == 8 && elapsed >= 39 && Environment.GetEnvironmentVariable("KIRBY_LAB_REPRO_MOB_COMPARISON") == "1")
        { Record("before-normal-enemy-comparison"); hero.applySkin(null!); healthPhase++; }
        else if (healthPhase == 9 && elapsed >= 41)
        { Record("normal-enemy-comparison"); hero.applySkin("KirbySuperStarUltra".AsHaxeString()); healthPhase++; }
    }

    [System.Runtime.InteropServices.DllImport("kssu_bridge", EntryPoint = "kssu_read_gpu")]
    private static extern int ReadGpu(nint handle, uint address, byte* data, nuint length);
    private static string Bank0(NdsRuntime nds)
    {
        byte* data = stackalloc byte[32];
        return ReadGpu(nds.Handle, 0x05000200, data, 32) == 0 ? Convert.ToHexString(new ReadOnlySpan<byte>(data, 32)) : "";
    }
    private static int Flash(NdsRuntime nds)
    {
        ushort value = 0;
        return ReadGpu(nds.Handle, 0x05000202, (byte*)&value, 2) == 0 ? value : -1;
    }

    private static int[] Values(ArrayBytes_Int array) => Enumerable.Range(0, array.length).Select(i => Get(array, i)).ToArray();
    private static string[] Shaders(dc.hxsl.ShaderList? shaders)
    {
        List<string> names = [];
        for (var next = shaders; next is not null && names.Count < 16; next = next.next)
            names.Add(next.s.GetType().FullName ?? "?");
        return names.ToArray();
    }
}
