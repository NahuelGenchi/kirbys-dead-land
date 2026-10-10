namespace Kirby.Runtime;

/// <summary>
/// Host flip platforms (<c>FlipPF</c>, the Castlevania iron platforms) flip when
/// the hero lands on them. They listen to the hero's land signal, which only the
/// host's own physics raises, and the original places Kirby with that physics
/// held still. Kirby's landing is therefore handed to them directly: the signal
/// itself also drives the host dive, roll and wing skills, and Hero.onLand adds
/// a stun and fall damage the original has no counterpart for.
/// </summary>
internal static class KirbyFlipPlatforms
{
    /// <summary>Tell every live flip platform in Kirby's level that he has landed; returns how many.</summary>
    internal static int Land(dc.en.Hero hero)
    {
        var entities = hero._level.entities;
        int told = 0;
        for (int i = 0; i < entities.length; i++)
        {
            if (entities.getDyn(i) is not dc.en.ltrap.FlipPF { destroyed: false } platform) continue;
            platform.onHeroLand(0);
            told++;
        }
        return told;
    }
}
