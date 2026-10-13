namespace Kirby.Runtime;

/// <summary>
/// A carnivorous plant that has closed on the hero throws him upward:
/// <c>CarnivorousPlant.bumpEntity</c> clears his velocities and bumps him at
/// 1.5 cells a fixed step. The original places Kirby with the host's physics
/// held still, so that bump would be discarded; it is handed to the provider,
/// which replays it around the original controller (#132).
/// </summary>
internal static class KirbyBumps
{
    internal static void Install() =>
        dc.en.ltrap.Hook_CarnivorousPlant.bumpEntity += (orig, plant, entity) =>
        {
            orig(plant, entity);
            if (entity is null || KirbyMod.Instance is not { ActiveSession.Hero: { } hero, NativeProvider: { } provider } ||
                hero.HashlinkPointer != entity.HashlinkPointer) return;
            // bumpEntity returns early outside the plant's bump window, and
            // Entity.bump scales by the hero's bump resistance: read what it left.
            double up = -hero.bdy;
            hero.bdx = hero.bdy = 0;
            if (up > 0) provider.Launch(up);
        };
}
