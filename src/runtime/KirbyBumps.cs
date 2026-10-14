using Kirby.Core;

namespace Kirby.Runtime;

/// <summary>
/// A carnivorous plant is a trampoline. Its bite hops the hero it catches
/// (<c>close</c> bumps him at 0.5 cells a fixed step), and for the 1.25 s it
/// stays shut <c>bumpEntity</c> throws the hero who touches it (at most once
/// every 0.5 s) or who starts an air jump just above it: it clears his
/// velocities and bumps him at 1.5 cells a fixed step. The original places
/// Kirby with the host's physics held still, so those bumps would be discarded;
/// they are handed to the provider, which replays them around the original
/// controller (#132, #138).
/// </summary>
internal static class KirbyBumps
{
    // tool.Cooldown key of the plant's "open" timer: it is closed while this runs.
    private const int ClosedKey = 201326592;
    private static bool launched;

    internal static void Install() =>
        dc.en.ltrap.Hook_CarnivorousPlant.bumpEntity += (orig, plant, entity) =>
        {
            orig(plant, entity);
            if (entity is not null && Kirby(out var hero) && hero.HashlinkPointer == entity.HashlinkPointer) TakeBump(hero);
        };

    private static bool Kirby(out dc.en.Hero hero)
    {
        hero = KirbyMod.Instance?.ActiveSession?.Hero!;
        return hero is { destroyed: false } && KirbyMod.Instance?.NativeProvider is not null;
    }

    /// <summary>Hand the upward bump the host just left on the hero to the provider.</summary>
    private static void TakeBump(dc.en.Hero hero, bool bite = false)
    {
        // bumpEntity returns early unless the plant is closed, and Entity.bump
        // scales by the hero's bump resistance: read what it left.
        double up = -hero.bdy;
        hero.bdx = hero.bdy = 0;
        if (up <= 0) return;
        launched = true;
        KirbyMod.Instance!.NativeProvider!.Launch(up, bite);
    }

    /// <summary>The plant's bite: its small bump of a bitten hero is his hop.</summary>
    internal static void Close(dc.en.ltrap.CarnivorousPlant plant, Action original)
    {
        if (!Kirby(out var hero)) { original(); return; }
        hero.bdx = hero.bdy = 0;
        original();
        TakeBump(hero, bite: true);
    }

    /// <summary>
    /// The host finds a hero touching a closed plant in its circular collisions,
    /// with whatever body the hero has then; for Kirby that is his projected
    /// hurtbox. Test the Beheaded's circle instead (the caller has put his body
    /// at Kirby's foot) and let the plant's own onTouch decide.
    /// </summary>
    internal static void TouchClosed(dc.en.ltrap.CarnivorousPlant plant)
    {
        if (plant.destroyed || !Kirby(out var hero) || hero.life <= 0 || hero._level?.HashlinkPointer != plant._level?.HashlinkPointer ||
            !plant.cd.fastCheck.exists(ClosedKey)) return;
        if (BumpLaunch.Touches((plant.cx + plant.xr) * 24, (plant.cy + plant.yr) * 24, plant.radius, plant.hei,
                (hero.cx + hero.xr) * 24, (hero.cy + hero.yr) * 24, hero.radius, hero.hei))
            plant.onTouch(hero);
    }

    /// <summary>
    /// Kirby's jump press in the air, as the Beheaded's air jump
    /// (<c>AirJump*.onStart</c>): the first closed plant within reach under him
    /// throws him. True when one did.
    /// </summary>
    internal static bool AirJump(dc.en.Hero hero)
    {
        var entities = hero._level.entities;
        for (int i = 0; i < entities.length; i++)
        {
            if (entities.getDyn(i) is not dc.en.ltrap.CarnivorousPlant { destroyed: false } plant ||
                !BumpLaunch.AirJumpReach(plant.cx, plant.cy, plant.yr, hero.cx, hero.cy, hero.yr)) continue;
            launched = false;
            // Nothing happens unless the plant is closed.
            plant.bumpEntity(hero);
            if (launched) return true;
        }
        return false;
    }
}
