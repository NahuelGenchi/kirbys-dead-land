using System.Text.Json;
using dc.en;
using dc.en.inter;
using dc.en.mob;
using dc.en.mob.boss;
using dc.tool;
using dc.tool.hero;
using dc.tool.weap;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using Entity = dc.Entity;
using Path = System.IO.Path;
using LevelInfo = Hashlink.Virtuals.virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_;

namespace Kirby.Runtime;

/// <summary>
/// Lab-only scenes for the README clips (#274). KIRBY_LAB_SHOWCASE names one
/// scene, which shows one mechanic played as a player would: the original's
/// buttons, the host's keys, and enemies left to their own AI, except a target
/// that only takes hits, which holds still (a Zombie's leap carries it over
/// Kirby and the attack would miss). What a player
/// would have done earlier (a copy ability, a shield in the inventory, an item on
/// the floor, an enemy walking up) is set up before <see cref="Recording"/>
/// starts, and the capture keeps only the recorded stretch. With
/// KIRBY_LAB_FIXED_STEP each host frame is 1/60 s, so the clip plays evenly
/// however long a capture takes. showcase.jsonl records each step by host frame.
/// </summary>
internal static class LabShowcaseProbe
{
    /// <summary>True while the scene's clip is recorded.</summary>
    internal static bool Recording { get; private set; }

    private static string? run;
    private static string scene = "";
    private static int phase, phaseAt, attacks, selects;
    private static int? awakeAt;
    private static bool done, fed, parried, countered;
    private static Mob? target, second;
    private static ItemDrop? item;
    private static InventItem? shield;
    private static int shieldTap = -1;
    // Host key presses (Interact, the Heal key) by key code: the frame each was pressed.
    private static readonly Dictionary<int, int> keyAt = [];
    private static int lastPoll = -1;
    private static Entity? projectile;
    private static Behemoth? boss;
    // The scene's copy ability, as the original's number (Cutter 2 ... Sleep 25).
    private static readonly byte ability = byte.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_SHOWCASE_ABILITY"), out byte a) ? a : (byte)0;
    // How far ahead the scene's enemy or item is placed, in host pixels (negative:
    // behind). The first room's wall is about 180 px to the right of where Kirby
    // wakes; its floor runs further to the left.
    private static readonly double placeAt = double.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_SHOWCASE_PLACE_PX"), out double p) ? p : 200;
    private static bool facesLeft;
    // Frames from a grenade landing by Kirby to the shield tap; its fuse is about 50.
    private static readonly int parryDelay = int.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_SHOWCASE_PARRY_DELAY"), out int l) ? l : 40;

    // Frames between placing a scene's enemy or item and the recording: a new
    // enemy fades in, and the camera settles.
    private const int Settle = 90;
    private static int Now => dc.hxd.Timer.Class.frameCount;
    private static int InPhase => Now - phaseAt;
    private static double X(Entity e) => (e.cx + e.xr) * 24;
    private static double Y(Entity e) => (e.cy + e.yr) * 24;
    private static ushort Toward(double dx) => dx >= 0 ? (ushort)16 : (ushort)32;

    public static void Install(string output)
    {
        run = output;
        scene = Environment.GetEnvironmentVariable("KIRBY_LAB_SHOWCASE") ?? "";
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        if (scene == "boss")
            dc.Hook_User.newGame += (orig, user, seed, level, custom, scoring, gameMode) =>
                orig(user, seed, ((HaxeProxyBase)dc.Data.Class.level.resolve("Bridge".AsHaxeString(), false)).ToVirtual<LevelInfo>(), custom, scoring, gameMode);
        Hook_BaseShield.onShieldStartParry += (orig, weapon) => { orig(weapon); parried = true; Event("parry-start"); };
        Hook_BaseShield.onShieldCounterSuccessful += (orig, weapon, attack, flag) => { orig(weapon, attack, flag); countered = true; Event("parry-counter"); };
        // The shield answers a weapon button the host reads through its weapon
        // manager, as LabCombatProbe found; a tap is held there for 4 frames.
        Hook_HeroWeaponsManager.hasButtonDownForWeapon += (orig, manager, weapon) => ShieldTapped(weapon, 4) || orig(manager, weapon);
        Hook_HeroWeaponsManager.hasButtonPressedForWeapon += (orig, manager, weapon) => ShieldTapped(weapon, 1) || orig(manager, weapon);
        // The host alone reads Interact and key presses; the Heal key is also
        // held in the key table, which KirbyInput reads (#176).
        dc.hxd.Hook__Key.isDown += (orig, key) => keyAt.TryGetValue(key, out int at) && Now - at < 4 || orig(key);
        dc.hxd.Hook__Key.isPressed += (orig, key) => keyAt.TryGetValue(key, out int at) && Now == at || orig(key);
        KirbyInput.TestOverride = Input;
    }

    /// <summary>Press a host action's key for 4 frames, as a player's tap.</summary>
    private static void Press(Hero hero, int action)
    {
        var bindings = hero.controller.parent.get_bindings().primary;
        int key;
        unsafe { key = action < bindings.length ? ((int*)bindings.bytes)[action] : -1; }
        if (key < 0) return;
        keyAt[key] = Now;
        Event("press", new { action, key });
    }

    private static void HoldPressed(Hero hero)
    {
        foreach (int action in new[] { 6, 7 })
        {
            var bindings = hero.controller.parent.get_bindings().primary;
            int key;
            unsafe { key = action < bindings.length ? ((int*)bindings.bytes)[action] : -1; }
            LabSmokeCapture.HostKeyCode(key, key >= 0 && keyAt.TryGetValue(key, out int at) && Now - at < 4);
        }
    }

    private static bool ShieldTapped(Weapon weapon, int frames) =>
        shieldTap >= 0 && Now - shieldTap < frames && shield is not null && weapon.item.permanentId == shield.permanentId;

    private static void Event(string name, object? data = null) => System.IO.File.AppendAllText(Path.Combine(run!, "showcase.jsonl"),
        JsonSerializer.Serialize(new { hostFrame = Now, sourceFrame = KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame, name, data }) + "\n");

    private static void Next(string why) { phase++; phaseAt = Now; Event($"phase {phase}: {why}"); }

    private static void Record(bool on)
    {
        if (Recording == on) return;
        Recording = on;
        Event(on ? "record-start" : "record-end");
        if (!on) done = true;
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        var hero = session.Hero;
        if (machine is null || run is null || done) return 0;
        if (!hero.awake || hero._level.game.hasCinematic()) return scene == "boss" && awakeAt is not null ? (ushort)16 : (ushort)0;
        awakeAt ??= Now;
        if (Now - awakeAt.Value < 60) return 0;
        HoldPressed(hero);
        facesLeft = machine.State.FacesLeft;
        try
        {
            return scene switch
            {
                "inhale" => Inhale(hero, machine, spit: false),
                "spit" => Inhale(hero, machine, spit: true),
                "ability" => Attack(hero, machine),
                "drop" => Drop(hero, machine),
                "parry" => Parry(hero, machine),
                "item" => Item(hero, machine, limited: false),
                "fslot" => Item(hero, machine, limited: true),
                "projectile" => Projectile(hero, machine),
                "boss" => Boss(hero, machine),
                _ => 0,
            };
        }
        catch (Exception error)
        {
            Event("error", error.ToString());
            Record(false);
            return 0;
        }
    }

    /// <summary>A copy ability through the original inhale, supplied contents and Down, before the clip.</summary>
    private static bool Prepare(NdsRuntime machine, out ushort keys)
    {
        keys = 0;
        if (ability is < 2 or > 25 || machine.State.Ability == ability) return true;
        if (!fed) { fed = machine.Feed(ability); keys = 2; return false; }
        keys = 128;
        return false;
    }

    private static Mob SpawnZombie(Hero hero, double dx, bool still = false)
    {
        var mob = Zombie.Class.create(hero._level, hero.cx + (dx >= 0 ? 2 : -2), hero.cy, 1, 1);
        mob.setPosPixel(X(hero) + dx, Y(hero));
        if (mob.yr == 0) { mob.cy--; mob.yr = 1; }
        if (still) mob.lockAiS(60);
        Event("placed", new { kind = "Zombie", dx, still });
        return mob;
    }

    private static Mob SpawnGrenader(Hero hero, double dx)
    {
        var mob = Grenader.Class.create(hero._level, hero.cx + (dx >= 0 ? 5 : -5), hero.cy, 1, 1);
        mob.setPosPixel(X(hero) + dx, Y(hero));
        if (mob.yr == 0) { mob.cy--; mob.yr = 1; }
        Event("placed", new { kind = "Grenader", dx });
        return mob;
    }

    /// <summary>The nearest hostile grenade or bullet within this many pixels of Kirby, if any.</summary>
    private static Entity? Incoming(Hero hero, double within)
    {
        Entity? near = null;
        var entities = hero._level.entities;
        for (int j = 0; j < entities.length; j++)
            if (entities.getDyn(j) is Entity { destroyed: false } e && e is Grenade or Bullet && e.isOpponent(hero) &&
                Math.Abs(X(e) - X(hero)) < within && (near is null || Math.Abs(X(e) - X(hero)) < Math.Abs(X(near) - X(hero))))
                near = e;
        return near;
    }

    private static bool Alive(Mob? mob) => mob is { destroyed: false, life: > 0 };

    /// <summary>Walk toward the target until it is this close; true once there.</summary>
    private static bool Approach(Hero hero, Entity entity, double distance, out ushort keys)
    {
        double dx = X(entity) - X(hero);
        keys = Math.Abs(dx) > distance ? Toward(dx) : (ushort)0;
        return keys == 0;
    }

    /// <summary>Turn to face the target (the original's facing): a press toward it while Kirby faces away.</summary>
    private static ushort Face(Hero hero, Entity entity) =>
        Math.Sign(X(entity) - X(hero)) is int side && side != 0 && (side < 0) != facesLeft ? Toward(side) : (ushort)0;

    private static ushort Inhale(Hero hero, NdsRuntime machine, bool spit)
    {
        bool mouthful = (machine.ActionFlags & 2) != 0;
        switch (phase)
        {
            case 0:
                // The room's wall is close on the right: the spit goes left, where the floor is long.
                target = SpawnZombie(hero, spit ? -100 : placeAt);
                if (spit) second = SpawnZombie(hero, -160, still: true);
                Next("enemy placed ahead");
                return 0;
            case 1:
                if (InPhase < Settle) return 0;
                Record(true); Next("walk up");
                return 0;
            case 2:
                if (!Alive(target)) { Next("target gone"); return 0; }
                if (Approach(hero, target!, 70, out ushort walk) || InPhase > 240) Next("in range");
                return walk;
            case 3:
                if (mouthful) { Next("in the mouth"); return 0; }
                if (InPhase > 180) { Event("inhale timed out"); Record(false); return 0; }
                return (ushort)(2 | Face(hero, target!));
            case 4:
                if (InPhase < 24) return 0;
                Next(spit ? "spit" : "swallow");
                return 0;
            case 5:
                if (spit)
                {
                    if (second is { } other && Alive(other) && InPhase < 4) return Face(hero, other);
                    if (InPhase < 10) return 2;
                    Next("spat"); return 0;
                }
                if (machine.State.Ability != 1 || InPhase > 60) { Next($"copied {machine.State.Ability}"); return 0; }
                return 128;
            default:
                if (InPhase >= (spit ? 80 : 110)) Record(false);
                return 0;
        }
    }

    // The target's distance and the button hold per attack, by original ability
    // number: the target stands where the attack reaches, and Kirby attacks from
    // where he stands (walking up, he slides into it).
    private static (double Range, int Hold) Style(byte id) => id switch
    {
        8 => (150, 6),    // Wheel rolls into it.
        16 => (120, 40),  // Jet charges, then dashes.
        2 => (90, 6),     // Cutter's blade flies out and back.
        4 => (60, 6),     // Beam.
        10 or 11 => (50, 45), // Ice and Fire breathe while held.
        18 or 19 => (35, 6),  // Sword and Hammer.
        _ => (50, 6),
    };

    private static ushort Attack(Hero hero, NdsRuntime machine)
    {
        switch (phase)
        {
            case 0:
                if (!Prepare(machine, out ushort keys)) return keys;
                Next($"copy {machine.State.Ability}");
                return 0;
            case 1:
                if (InPhase < 40) return 0;
                target = SpawnZombie(hero, Style(ability).Range, still: true);
                Next("enemy placed in reach");
                return 0;
            case 2:
                if (InPhase < Settle) return 0;
                Record(true); Next("look");
                return 0;
            case 3:
                if (InPhase < 20) return 0;
                attacks = 0; Next("attack");
                return 0;
            case 4:
            {
                var (_, hold) = Style(ability);
                int cycle = hold + 24, at = InPhase % cycle;
                if (at == 0 && InPhase > 0) attacks++;
                if (!Alive(target) || attacks >= 3) { Next(Alive(target) ? "three attacks" : "target down"); return 0; }
                ushort face = Face(hero, target!);
                return at < hold ? (ushort)(2 | face) : face;
            }
            default:
                if (InPhase >= 70) Record(false);
                return 0;
        }
    }

    private static ushort Drop(Hero hero, NdsRuntime machine)
    {
        switch (phase)
        {
            case 0:
                if (!Prepare(machine, out ushort keys)) return keys;
                Next($"copy {machine.State.Ability}");
                return 0;
            case 1:
                if (InPhase < 40) return 0;
                // The dropped star leaves Kirby backwards, toward this Zombie.
                target = SpawnZombie(hero, facesLeft ? 130 : -130, still: true);
                Next("enemy placed behind");
                return 0;
            case 2:
                if (InPhase < Settle) return 0;
                Record(true); Next("drop");
                return 0;
            case 3:
                if (InPhase < 30) return 0;
                if (attacks == 0)
                {
                    if (++selects > 3) { Event("no drop"); Record(false); return 0; }
                    attacks = 1; phaseAt = Now - 30; Event("select", new { machine.State.Ability });
                }
                if (InPhase < 38) return 4; // Original SELECT: discard the copy.
                if (machine.State.Ability != ability) { Next($"dropped, now {machine.State.Ability}"); return 0; }
                if (InPhase > 70) { Event("still copied", new { machine.State.Ability, machine.ActionFlags }); attacks = 0; phaseAt = Now - 20; }
                return 0;
            default:
                if (InPhase >= 150) Record(false);
                return 0;
        }
    }

    private static ushort Parry(Hero hero, NdsRuntime machine)
    {
        if (shield is not null)
            for (int slot = 0; slot < hero.inventory.nbWeapons; slot++)
                if (hero.inventory.getEquippedWeaponOn(slot)?.permanentId == shield.permanentId)
                    LabSmokeCapture.HostKey(hero, slot + 2, shieldTap >= 0 && Now - shieldTap < 4);
        switch (phase)
        {
            case 0:
                if (!Prepare(machine, out ushort keys)) return keys;
                shield = new InventItem(new InventItemKind.Weapon("StartShield".AsHaxeString()));
                hero.inventory.add(shield);
                if (hero.weaponsManager is dc.tool.hero.weap.BeheadedWeaponsManager manager)
                    manager.onEquippedItemsUpdated(Ref<bool>.In(false));
                Next("shield in the inventory");
                return 0;
            case 1:
                if (InPhase < 40) return 0;
                // A Grenader's throw is a clear attack to parry; a Zombie's leap goes over Kirby.
                target = SpawnGrenader(hero, Math.Min(placeAt, 170));
                Next("thrower placed ahead");
                return 0;
            case 2:
                if (InPhase < Settle) return 0;
                Record(true); Next("wait for a throw");
                return 0;
            case 3:
                // A grenade lands by Kirby and bursts after its fuse: the shield goes up just before.
                if (!Alive(target) || InPhase > 400) { Event("no throw"); Record(false); return 0; }
                if (Incoming(hero, 40) is { } grenade && Math.Abs(Y(grenade) - Y(hero)) < 30)
                {
                    projectile = grenade;
                    Event("landed", new { kind = grenade.GetType().Name, dx = X(grenade) - X(hero), dy = Y(grenade) - Y(hero) });
                    Next("landed");
                }
                return Face(hero, target!);
            case 4:
                if (projectile is { destroyed: true } && shieldTap < 0) { Event("burst before the tap"); Record(false); return 0; }
                if (InPhase >= parryDelay && shieldTap < 0) { shieldTap = Now; Event("shield tap"); }
                if (projectile is { destroyed: true }) { projectile = null; Event("burst"); Next("burst"); }
                return 0;
            default:
                if (InPhase >= 90) Record(false);
                return 0;
        }
    }

    private static ushort Item(Hero hero, NdsRuntime machine, bool limited)
    {
        bool mouthful = (machine.ActionFlags & 2) != 0;
        string wanted = Environment.GetEnvironmentVariable("KIRBY_LAB_SHOWCASE_ITEM") ?? (limited ? "Crash" : "Fire");
        switch (phase)
        {
            case 0:
            {
                var host = KirbyAbilityItems.Create(wanted, 1);
                double at = limited ? 100 : placeAt;
                item = new ItemDrop(hero._level, hero.cx + (int)(at / 24), hero.cy, host, true, Ref<bool>.In(false));
                item.init();
                // Crash's targets stand behind Kirby, inside the room.
                if (limited)
                    for (int i = 0; i < 2; i++) second = SpawnZombie(hero, facesLeft ? 110 + 50 * i : -110 - 50 * i, still: true);
                Event("placed", new { kind = "item", ability = KirbyAbilityItems.AbilityOf(item.item) });
                Next("item placed ahead");
                return 0;
            }
            case 1:
                if (InPhase < Settle) return 0;
                Record(true); Next("walk up");
                return 0;
            case 2:
                if (Approach(hero, item!, 20, out ushort walk) || InPhase > 240) Next("at the item");
                return walk;
            case 3:
                if (InPhase < (limited ? 100 : 50)) return 0; // Its card shows, long enough to read.
                Next(limited ? "take" : "inhale");
                return 0;
            case 4:
                if (limited)
                {
                    if (InPhase == 0) Press(hero, 6); // Interact: into the F slot.
                    if (InPhase >= 70) Next("use F");
                    return 0;
                }
                if (mouthful) { Next("in the mouth"); return 0; }
                if (InPhase > 180) { Event("inhale timed out"); Record(false); return 0; }
                return 2;
            case 5:
                if (limited)
                {
                    if (InPhase == 0) Press(hero, 7); // The Heal key fires the F slot.
                    if (InPhase >= 4) Next("fired");
                    return 0;
                }
                if (InPhase < 20) return 0;
                if (machine.State.Ability != 1 || InPhase > 80) { Next($"copied {machine.State.Ability}"); return 0; }
                return 128;
            default:
                if (InPhase >= (limited ? 200 : 110)) Record(false);
                return 0;
        }
    }

    private static ushort Projectile(Hero hero, NdsRuntime machine)
    {
        bool mouthful = (machine.ActionFlags & 2) != 0;
        switch (phase)
        {
            case 0:
                // By the room's left wall, where a Grenader cannot back away; at the right
                // wall, close to where Kirby wakes, it died at once in three takes.
                target = SpawnGrenader(hero, Math.Min(placeAt, -230));
                Next("thrower placed behind");
                return 0;
            case 1:
                if (InPhase < Settle) return 0;
                Record(true); Next("walk up");
                return 0;
            case 2:
                if (!Alive(target)) { Event("thrower gone"); Record(false); return 0; }
                if (Approach(hero, target!, 150, out ushort walk) || InPhase > 120) Next("wait for a throw");
                return walk;
            case 3:
                if (!Alive(target) || InPhase > 400) { Event("no throw"); Record(false); return 0; }
                if ((projectile = Incoming(hero, 110)) is not null) Next("incoming");
                return Face(hero, target!);
            case 4:
                if (mouthful) { Next("in the mouth"); return 0; }
                if (InPhase > 60) { Event("missed the throw"); phase = 3; phaseAt = Now; return 0; }
                return 2;
            case 5:
                if (InPhase < 16) return 0;
                if (InPhase < 20) return Face(hero, target!);
                if (InPhase < 30) return 2;
                Next("spat");
                return 0;
            default:
                if (InPhase >= 80) Record(false);
                return 0;
        }
    }

    private static ushort Boss(Hero hero, NdsRuntime machine)
    {
        switch (phase)
        {
            case 0:
                if (!Prepare(machine, out ushort keys)) return keys;
                Next($"copy {machine.State.Ability}");
                return 0;
            case 1:
                var entities = hero._level.entities;
                for (int i = 0; i < entities.length && boss is null; i++)
                    if (entities.getDyn(i) is Behemoth { destroyed: false } candidate) boss = candidate;
                // Walk into the arena; its own entry makes the boss ready.
                if (boss is null || !boss.isReady()) return 16;
                Next("boss ready");
                return 0;
            case 2:
                if (InPhase < Settle) return 0;
                Record(true); Next("fight");
                return 0;
            case 3:
            {
                if (boss!.destroyed || boss.life <= 0 || InPhase > 600) { Next("fight over"); return 0; }
                double dx = X(boss) - X(hero), reach = boss.radius + 30;
                if (Math.Abs(dx) > reach) return Toward(dx);
                int at = InPhase % 20;
                return (ushort)((at < 6 ? 2 : 0) | Face(hero, boss));
            }
            default:
                Record(false);
                return 0;
        }
    }

    internal static void Poll()
    {
        if (run is null || Now - lastPoll < 30) return;
        lastPoll = Now;
        System.IO.File.WriteAllText(Path.Combine(run, "showcase.json"), JsonSerializer.Serialize(new
        {
            scene, ability, phase, recording = Recording, done, parried, countered, attacks,
            targetLife = target?.life, targetAlive = Alive(target), secondAlive = second is null ? (bool?)null : Alive(second),
            item = item is null ? null : KirbyAbilityItems.AbilityOf(item.item),
            stored = KirbyMod.Instance?.ActiveSession?.Rules.Stored.Ability,
            sourceAbility = KirbyMod.Instance?.NativeProvider?.Machine?.State.Ability,
            bossLife = boss?.life,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
