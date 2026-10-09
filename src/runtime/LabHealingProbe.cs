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
    private static int flaskStarts;
    private static (int Requested, int Given)? fountainHeal;
    private static bool? fountainUsed;
    private static readonly List<string> events = [];

    private static void Stage(string phase)
    {
        events.Add($"{lastTick}: {phase}");
        System.IO.File.AppendAllText(Path.Combine(run, "healing-events.txt"), $"{lastTick}: {phase}\n");
    }

    public static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
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
        // Hold only the real bindings, as a player would.
        dc.hxd.Hook__Key.isDown += (orig, key) =>
            key == healKey && healFrom is uint from && lastTick - from < 90 ||
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

    private static dc.tool.mainSkills.HealFlask? Flask(Hero hero)
    {
        var skills = hero.mainSkillsManager?.skills;
        if (skills is null) return null;
        for (int i = 0; i < skills.length; i++)
            if (skills.getDyn(i) is dc.tool.mainSkills.HealFlask flask) return flask;
        return null;
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null) return 0;
        uint tick = lastTick = machine.State.Frame;
        start ??= tick;
        uint elapsed = tick - start.Value;
        var hero = session.Hero;
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
        if (healFrom is null)
        {
            lifeAfterHurt = hero.life;
            maxLife = hero.maxLife;
            healKey = Binding(hero, HealAction);
            var flask = Flask(hero);
            flaskEnabled = flask?.isEnabled();
            flaskCanUse = flask?.canUse();
            healFrom = tick;
            Stage($"Holding Heal key {healKey} for 90 frames; flask present={flask is not null} enabled={flaskEnabled} canUse={flaskCanUse}");
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
            healKey, flaskEnabled, flaskCanUse, flaskStarts,
            lifeAfterHeal, sourceAfterHeal,
            interactKey, interactAt, focused = fountain is not null && hero.focused?.HashlinkPointer == fountain.HashlinkPointer,
            lifeBeforeFountain,
            fountainRequested = fountainHeal?.Requested, fountainGiven = fountainHeal?.Given,
            lifeAfterFountain, sourceAfterFountain, fountainUsed,
            currentLife = hero.life, currentSource = machine.Health,
            events,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
