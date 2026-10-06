using dc.en;
using dc.libs.heaps.slib;
using ModCore.Utilities;

namespace Kirby.Runtime;

/// <summary>
/// Host graphics that belong to the Beheaded rather than Kirby. The outfit
/// atlas holds one static fallback pose for every host animation; the running
/// ROM supplies Kirby's real graphics, so that fallback must never be drawn.
/// </summary>
internal static class KirbyHostVisuals
{
    private const string OutfitAtlas = "atlas/kirbySuperStarUltra.atlas";
    private static string outfitId = "";
    private static nint outfitLibrary;
    /// <summary>Set while original damage is applied through the host's hit path.</summary>
    internal static bool SuppressHitBlink;

    public static void Install(string outfit)
    {
        outfitId = outfit;
        // The arrival corpse (before the hero exists), the hero body, its death
        // corpse and host afterimage trails all draw HSprites from the outfit
        // atlas, which would show the static fallback pose instead of Kirby.
        Hook_HSprite.draw += (orig, sprite, context) =>
        {
            if (outfitLibrary != 0 && sprite.lib?.HashlinkPointer == outfitLibrary) return;
            orig(sprite, context);
        };
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

    /// <summary>
    /// Track the outfit atlas through the host asset manager. The arrival
    /// corpse uses it before Hero.initGfx runs, and a biome transition can
    /// replace the library instance.
    /// </summary>
    public static void OnFrame()
    {
        outfitLibrary = KirbySelected() && dc.Main.Class.ME?.curProcess is dc.pr.Game
            ? dc.Assets.Class.lib.get(OutfitAtlas.AsHaxeString())?.HashlinkPointer ?? 0
            : 0;
    }

    /// <summary>Clear head particles that were alive when Kirby took over the hero.</summary>
    public static void ClearHead(Hero hero) => hero.heroHead?.killAllParticles();
}
