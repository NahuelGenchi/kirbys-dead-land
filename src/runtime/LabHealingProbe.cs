using System.Text.Json;
using dc.en;
using dc.en.inter;
using dc.en.mob;
using dc.tool.atk;
using ModCore.Utilities;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in check that Kirby has no health flask (#101): a real host attack hurts
/// him, the real Heal binding (F by default, action 7) is held without healing,
/// then a real health fountain next to him is used through the real Interact
/// binding (action 6) and heals half his maximum health.
/// </summary>
internal static class LabHealingProbe
{
    private const int HealAction = 7, InteractAction = 6;
    private static string run = "";
    private static uint? start, healFrom, interactAt, effectFrame;
    private static uint lastTick, lastReportFrame, spawnedAt;
    private static int presses;
    private static int hits;
    private static int healKey = -1, interactKey = -1;
    private static Fountain? fountain;
    private static int? lifeAfterHurt, lifeAfterHeal, sourceAfterHeal, lifeBeforeFountain, lifeAfterFountain, sourceAfterFountain, maxLife;
    private static bool? flaskEnabled, flaskCanUse, flaskUnlocked;
    private static bool? flaskInSkills, flaskStowed, flaskRestored, flaskStowedAgain;
    private static string? skillsStowed, skillsRestored, skillsStowedAgain;
    private static int flaskStarts;
    private static (int Requested, int Given)? fountainHeal;
    private static bool? fountainUsed;
    private static readonly List<string> events = [];
    private static bool stored, refilled, swordReady, fed;
    private static uint? swordFrom;
    private static uint? refreshAt;
    private static bool exitTaken;
    private const byte SwordByte = 18;
    private static readonly bool withSword = Environment.GetEnvironmentVariable("KIRBY_LAB_HEALING_SWORD") == "1";
    private static readonly bool takeExit = Environment.GetEnvironmentVariable("KIRBY_LAB_HEALING_EXIT") == "1";
    private static nint potionPointer;
    private static string? lastSlotState;

    private static void Stage(string phase)
    {
        events.Add($"{lastTick}: {phase}");
        System.IO.File.AppendAllText(Path.Combine(run, "healing-events.txt"), $"{lastTick}: {phase}\n");
    }

    public static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        // The owner's profile already owns Health Flask I when a run starts (#109).
        dc.tool.Hook_ItemMetaManager.hasUnlockedItem += (orig, meta, id) => id?.ToString() == "Flask1" || orig(meta, id);
        dc.ui.Hook_HUD.setHealings += (orig, hud, healings, max) =>
        {
            Stage($"HUD.setHealings({healings},{max})");
            orig(hud, healings, max);
        };
        dc.ui.Hook_HUD.refreshItems += (orig, hud) => { Stage("HUD.refreshItems"); orig(hud); };
        dc.ui.hud.Hook_Skill.setIcon += (orig, skill, tile) =>
        {
            if (skill.HashlinkPointer == potionPointer)
                Stage($"skillPotion.setIcon tile={(tile is null ? "null" : tile.HashlinkPointer.ToString("X"))}");
            orig(skill, tile);
        };
        dc.tool.mainSkills.Hook_HealFlask.onStart += (orig, flask) => { flaskStarts++; Stage("HealFlask.onStart"); orig(flask); };
        dc.en.inter.Hook_Fountain.onActivate += (orig, f, by, longPress) =>
        {
            Stage($"Fountain.onActivate longPress={longPress} used={f.used} life={by.life}/{by.maxLife}");
            orig(f, by, longPress);
            Stage($"Fountain.onActivate done used={f.used}");
        };
        dc.en.inter.Hook_Fountain.applyEffect += (orig, f, by) => { Stage("Fountain.applyEffect"); orig(f, by); };
        KirbyHealing.TestFountainHeal = (requested, given) =>
        {
            fountainHeal = (requested, given);
            effectFrame = lastTick;
            Stage($"Fountain heal: host asked {requested}, Kirby got {given}");
        };
        // Hold only the real bindings, as a player would. The Heal key is held in
        // the host's key table, which KirbyInput reads (#176); the host alone reads
        // Interact and key presses, through these hooks.
        dc.hxd.Hook__Key.isDown += (orig, key) =>
            key == interactKey && interactAt is uint at && lastTick - at < 8 || orig(key);
        dc.hxd.Hook__Key.isPressed += (orig, key) =>
            key == healKey && healFrom is uint from && lastTick == from ||
            key == interactKey && interactAt is uint at && lastTick == at || orig(key);
        KirbyInput.TestOverride = Input;
    }

    private static int Binding(Hero hero, int action)
    {
        var bindings = hero.controller.parent.get_bindings().primary;
        unsafe { return action < bindings.length ? ((int*)bindings.bytes)[action] : -1; }
    }

    private static dc.tool.mainSkills.HealFlask? Flask(Hero hero) => InSkills(hero) ?? KirbyHealing.StowedFlask;

    // The flask among the hero's main skills, where a Kirby session leaves none (#186).
    private static dc.tool.mainSkills.HealFlask? InSkills(Hero hero)
    {
        var skills = hero.mainSkillsManager?.skills;
        if (skills is null) return null;
        for (int i = 0; i < skills.length; i++)
            if (skills.getDyn(i) is dc.tool.mainSkills.HealFlask flask) return flask;
        return null;
    }

    private static string Skills(Hero hero)
    {
        var skills = hero.mainSkillsManager?.skills;
        if (skills is null) return "";
        var names = new List<string>();
        for (int i = 0; i < skills.length; i++) names.Add(skills.getDyn(i)?.GetType().Name ?? "null");
        return string.Join(",", names);
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null) return 0;
        uint tick = lastTick = machine.State.Frame;
        start ??= tick;
        uint elapsed = tick - start.Value;
        var hero = session.Hero;
        LabSmokeCapture.HostKey(hero, HealAction, healFrom is uint from && tick - from < 90);
        if (flaskInSkills is null)
        {
            // The session holds the flask out of the hero's skills (#186). Ending it
            // puts the flask back in its place; a new one takes it out again.
            flaskInSkills = InSkills(hero) is not null;
            flaskStowed = KirbyHealing.StowedFlask is not null;
            skillsStowed = Skills(hero);
            KirbyHealing.Deactivate();
            flaskRestored = InSkills(hero) is not null && KirbyHealing.StowedFlask is null;
            skillsRestored = Skills(hero);
            KirbyHealing.Activate(hero);
            flaskStowedAgain = InSkills(hero) is null && KirbyHealing.StowedFlask is not null;
            skillsStowedAgain = Skills(hero);
            Stage($"Flask in skills={flaskInSkills} stowed={flaskStowed}; restored={flaskRestored} [{skillsRestored}]; stowed again={flaskStowedAgain} [{skillsStowedAgain}]");
        }
        if (flaskUnlocked is null)
        {
            // The flask must be owned for its absence to mean anything.
            var meta = hero._level.game.user.itemMeta;
            flaskUnlocked = meta.hasUnlockedItem("Flask1".AsHaxeString());
            if (flaskUnlocked == false)
            {
                // The host's own unlock path, then its HUD rebuild.
                meta.unlockItem("Flask1".AsHaxeString());
                Flask(hero)?.onUnlockItem("Flask1".AsHaxeString());
                hero.hudInitItems();
            }
            Stage($"Health Flask I owned at start: {flaskUnlocked}");
        }
        if (elapsed < 180) return 0;
        if (withSword && !swordReady)
        {
            // A main copy ability turns the HUD overlay and its host refreshes on (#109).
            swordFrom ??= tick;
            uint n = tick - swordFrom.Value;
            if (machine.State.Ability == SwordByte && session.Rules.AbilityId == "Sword")
            {
                if (n > 200) { swordReady = true; start = tick - 180; Stage("Sword ready"); }
                return 0;
            }
            if (!fed) { if (n < 50) return 2; fed = machine.Feed(SwordByte); return 2; }
            return 128;
        }
        if (refreshAt is uint due && lastTick >= due)
        {
            refreshAt = null;
            Stage("HUD.refreshItems() called by the probe");
            hero._level?.game?.hud?.refreshItems();
        }
        if (!stored)
        {
            // An ability waiting in the F slot must survive the fountain (#109).
            stored = true;
            session.Rules.Stored.Take("Mike");
            Stage("Stored Mike in the F slot");
        }
        // Two real hits, after the first one's invulnerability, so the fountain's
        // half heal is not capped by maximum health.
        if (hits < 2 && elapsed >= 180 + 120u * (uint)hits)
        {
            hits++;
            var zombie = Zombie.Class.create(hero._level, hero.cx + 2, hero.cy, 1, 1);
            zombie.lockAiS(30);
            AttackUtils.Class.hit(AttackUtils.Class.createFromMob(zombie, 40, null), hero);
            zombie.kill();
            Stage($"Hurt Kirby: life {hero.life}/{hero.maxLife}");
            return 0;
        }
        if (elapsed < 420) return 0;
        if (!refilled && Flask(hero) is { } host && lastTick - (healFrom ?? lastTick) > 100)
        {
            refilled = true;
            Stage("Host flask refill (Heal.onFountainRefill)");
            host.onFountainRefill();
            refreshAt = lastTick + 20;
        }
        if (healFrom is null)
        {
            lifeAfterHurt = hero.life;
            maxLife = hero.maxLife;
            healKey = Binding(hero, HealAction);
            var flask = Flask(hero);
            flaskEnabled = flask?.isEnabled();
            flaskCanUse = flask?.canUse();
            healFrom = tick;
            Stage($"Holding Heal key {healKey} for 90 frames; flask in skills={InSkills(hero) is not null} stowed={KirbyHealing.StowedFlask is not null} enabled={flaskEnabled} canUse={flaskCanUse}");
            return 0;
        }
        if (tick - healFrom.Value < 150) return 0;
        if (fountain is null)
        {
            lifeAfterHeal = hero.life;
            sourceAfterHeal = machine.Health;
            fountain = new Fountain(hero._level, hero.cx, hero.cy, false);
            fountain.init();
            spawnedAt = tick;
            interactKey = Binding(hero, InteractAction);
            Stage($"Fountain spawned at Kirby's cell; life {hero.life}");
            return 0;
        }
        if (effectFrame is uint healed && tick - healed > 150 && !exitTaken && takeExit)
        {
            // The next level builds a new Hero: the F slot must come along (#109).
            exitTaken = true;
            dc.en.inter.Exit? exit = null;
            for (int i = 0; i < hero._level.entities.length; i++)
                if (hero._level.entities.getDyn(i) is dc.en.inter.Exit candidate && !candidate.locked &&
                    !string.IsNullOrEmpty(candidate.destLevel?.ToString())) { exit = candidate; break; }
            if (exit is null) { Stage("No unlocked exit"); return 0; }
            hero.setPosPixel((exit.cx + exit.xr - 1.5) * 24, (exit.cy + exit.yr) * 24);
            if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
            Stage($"Taking exit to {exit.destLevel}; F slot {session.Rules.Stored.Ability} x{session.Rules.Stored.Uses}");
            exit.onActivate(hero, false);
            return 0;
        }
        // Press Interact once a second while focused, until the fountain is used.
        if (tick - spawnedAt >= 60 && !fountain.used && (interactAt is null || tick - interactAt.Value >= 60) && presses < 5)
        {
            bool focused = hero.focused?.HashlinkPointer == fountain.HashlinkPointer;
            bool can = fountain.canBeActivated(hero);
            if (!focused) { Stage($"Not focused (can={can})"); interactAt = tick; return 0; }
            lifeBeforeFountain = hero.life;
            interactAt = tick;
            presses++;
            Stage($"Focused on the fountain (canBeActivated={can}); pressing Interact key {interactKey}");
        }
        return 0;
    }

    public static void Poll()
    {
        var session = KirbyMod.Instance?.ActiveSession;
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (run.Length == 0 || session is null || machine is null || machine.State.Frame == lastReportFrame) return;
        lastReportFrame = machine.State.Frame;
        var hero = session.Hero;
        var skill = hero._level?.game?.hud?.skillPotion;
        if (skill is not null) potionPointer = skill.HashlinkPointer;
        string slotState = $"stored={session.Rules.Stored.Ability} uses={session.Rules.Stored.Uses} " +
            $"item={KirbyFSlot.Item is not null} visible={skill?.visible} icon={skill?.icon?.tile?.HashlinkPointer.ToString("X")} life={hero.life}";
        if (slotState != lastSlotState)
        {
            lastSlotState = slotState;
            Stage($"F slot: {slotState}");
        }
        if (effectFrame is uint at && lastReportFrame - at >= 30 && lifeAfterFountain is null)
        {
            lifeAfterFountain = hero.life;
            sourceAfterFountain = machine.Health;
            fountainUsed = fountain?.used;
        }
        System.IO.File.WriteAllText(Path.Combine(run, "healing.json"), JsonSerializer.Serialize(new
        {
            frame = lastReportFrame,
            flaskUnlocked, maxLife, lifeAfterHurt,
            flaskInSkills, flaskStowed, flaskRestored, flaskStowedAgain,
            skillsStowed, skillsRestored, skillsStowedAgain,
            flaskInSkillsNow = InSkills(hero) is not null, flaskStowedNow = KirbyHealing.StowedFlask is not null,
            healKey, flaskEnabled, flaskCanUse, flaskStarts,
            lifeAfterHeal, sourceAfterHeal,
            interactKey, interactAt, focused = fountain is not null && hero.focused?.HashlinkPointer == fountain.HashlinkPointer,
            lifeBeforeFountain,
            fountainRequested = fountainHeal?.Requested, fountainGiven = fountainHeal?.Given,
            lifeAfterFountain, sourceAfterFountain, fountainUsed,
            currentLife = hero.life, currentSource = machine.Health,
            slot = slotState,
            events,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
