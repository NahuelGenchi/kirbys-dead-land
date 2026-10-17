using dc.en;

namespace Kirby.Runtime;

/// <summary>
/// Host graphics that belong to the Beheaded rather than Kirby. The running ROM
/// supplies Kirby's real graphics. The outfit atlas's in-game page is blank
/// (tools/build_outfit.py), so the host body, its arrival and death corpses and
/// afterimage trails draw nothing without a hook on every host sprite (#175).
/// </summary>
internal static class KirbyHostVisuals
{
    private static string outfitId = "";
    /// <summary>Set while original damage is applied through the host's hit path.</summary>
    internal static bool SuppressHitBlink;

    public static void Install(string outfit)
    {
        outfitId = outfit;
        // The Beheaded's spirit flame follows its neck, which sits above Kirby's
        // head. The outfit has no host head, but the arrival cinematic still
        // ignites it: silence its emitter while Kirby is active, and skip the
        // ignition burst that would leave a flame column on Kirby's head. The
        // spirit crawling to the body and leaving it at death keep their FX.
        dc.tool.Hook_HeroHead.updateHeadFx += (orig, head, time) =>
        {
            if (KirbyMod.Instance?.ActiveSession is null) orig(head, time);
        };
        dc.Hook_Fx.heroHeadPopFinal += (orig, fx, hero, color, flash, circle) =>
        {
            if (!KirbySelected() || hero is null) orig(fx, hero, color, flash, circle);
        };
        // The original controller already flashes Kirby's palette through its
        // invulnerability after a hit. The host's own hit blink pulses the hero
        // sprite's alpha to 0.3 every other frame, which the original body
        // follows, so Kirby appeared to vanish on each hit. Other host blinks,
        // such as immunity after a pit respawn, are unchanged.
        Hook_Hero.blink += (orig, hero, frames) =>
        {
            if (!SuppressHitBlink) orig(hero, frames);
        };
    }

    private static bool KirbySelected() =>
        string.Equals(dc.Main.Class.ME?.user?.heroSkin?.ToString(), outfitId, StringComparison.Ordinal);

    /// <summary>Clear head particles that were alive when Kirby took over the hero.</summary>
    public static void ClearHead(Hero hero) => hero.heroHead?.killAllParticles();
}
