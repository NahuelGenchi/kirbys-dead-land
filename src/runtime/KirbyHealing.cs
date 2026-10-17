using dc.en;
using dc.tool.hero;
using dc.tool.mainSkills;
using Kirby.Core;

namespace Kirby.Runtime;

/// <summary>
/// Kirby has no health flask (#101). A Kirby session takes the hero's
/// <c>HealFlask</c> out of its main skills, as the host's Richter has none, so F
/// never drinks and the fountain and flask recharges have no flask to fill
/// (#186). The fountain's own effect (<c>Fountain.applyEffect</c>, at the end of
/// its cinematic) heals Kirby by <see cref="Healing.FountainHeal"/> instead of
/// his full health; the host still marks the fountain used and clears infection.
/// </summary>
internal static class KirbyHealing
{
    private static bool fountainEffect;
    /// <summary>Lab-only observer of each fountain heal: (requested by the host, given).</summary>
    internal static Action<int, int>? TestFountainHeal;
    /// <summary>The flask a Kirby session took out of a hero's skills, and its place there.</summary>
    private static (HeroMainSkillsManager Manager, HealFlask Flask, int Index)? stowed;

    /// <summary>The flask the active Kirby session holds out of the hero's skills.</summary>
    internal static HealFlask? StowedFlask => stowed?.Flask;

    public static void Install()
    {
        // A save writes the skill list by reference: the flask is in it for the
        // save, so the run keeps its flask. The host calls this only when saving.
        Hook_HeroMainSkillsManager.serialize += (orig, manager, context) =>
        {
            if (stowed is not { } held || held.Manager.HashlinkPointer != manager.HashlinkPointer)
            { orig(manager, context); return; }
            Insert(held);
            try { orig(manager, context); }
            finally { manager.unregisterSkill(held.Flask); }
        };
        dc.en.inter.Hook_Fountain.applyEffect += (orig, fountain, hero) =>
        {
            if (!IsKirby(hero)) { orig(fountain, hero); return; }
            fountainEffect = true;
            try { orig(fountain, hero); }
            finally { fountainEffect = false; }
        };
        Hook_Hero.heal += (orig, hero, amount) =>
        {
            if (fountainEffect && IsKirby(hero))
            {
                int given = Healing.FountainHeal(hero.maxLife);
                TestFountainHeal?.Invoke(amount, given);
                amount = given;
            }
            orig(hero, amount);
        };
    }

    /// <summary>
    /// A Kirby session starts: the flask leaves the hero's skills, through the
    /// host's own <c>unregisterSkill</c>. Every host path to it walks that list or
    /// asks for an enabled healing skill, and did nothing for the turned-off flask
    /// it replaced (#186). Its charges stay in its blackboard, which the manager
    /// keeps apart.
    /// </summary>
    internal static void Activate(Hero hero)
    {
        Deactivate();
        var manager = hero.mainSkillsManager;
        var skills = manager?.skills;
        if (manager is null || skills is null) return;
        for (int i = 0; i < skills.length; i++)
            if (skills.getDyn(i) is HealFlask flask)
            {
                manager.unregisterSkill(flask);
                stowed = (manager, flask, i);
                KirbyNdsProvider.Log?.Invoke($"Kirby session: the health flask left the hero's skills (place {i + 1} of {skills.length + 1}).");
                return;
            }
        KirbyNdsProvider.Log?.Invoke("Kirby session: the hero has no health flask.");
    }

    /// <summary>The session ends: the flask goes back to its place among the skills.</summary>
    internal static void Deactivate()
    {
        if (stowed is not { } held) return;
        stowed = null;
        Insert(held);
    }

    private static void Insert((HeroMainSkillsManager Manager, HealFlask Flask, int Index) held)
    {
        var skills = held.Manager.skills;
        if (skills is not null) skills.insert(Math.Min(held.Index, skills.length), held.Flask);
    }

    private static bool IsKirby(Hero? hero) =>
        hero is not null && KirbyMod.Instance?.ActiveSession?.Hero.HashlinkPointer == hero.HashlinkPointer;
}
