using dc.en;

namespace Kirby.Runtime;

/// <summary>
/// Host interactions Dead Cells grants a rolling hero. Kirby's slide is his
/// roll: during its fast part, breakable tiles he slides into shatter and
/// doors burst open, through the host's own BreakableTile and Door checks.
/// Roll zones are opened by the terrain export (see KirbyNdsProvider).
/// </summary>
internal static class KirbyHostSkills
{
    public static void Install()
    {
        Hook_Hero.canBreakDoors += (orig, hero) => orig(hero) || SlideStrike(hero);
        Hook_Hero.canBreakTiles += (orig, hero) => orig(hero) || SlideStrike(hero);
    }

    private static bool SlideStrike(Hero hero) =>
        KirbyMod.Instance is { ActiveSession: { } session, NativeProvider.SlideStrike: true } &&
        session.Hero.HashlinkPointer == hero.HashlinkPointer;
}
