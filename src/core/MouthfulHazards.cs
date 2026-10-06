namespace Kirby.Core;

/// <summary>What the host does with a paired projectile after a source step.</summary>
public enum HazardAction { Follow, Hold, Remove, Release }

/// <summary>
/// Host projectiles (arrows, grenades) that Kirby can inhale. The ROM performs the
/// suction, mouthful, spit star and swallow; a projectile's source object carries
/// no copy byte, so the original mouthful is a plain star and swallowing it gives
/// nothing. These rules only decide which host objects take part and what the
/// host does with them once the original engine reports a result.
/// </summary>
public static class MouthfulHazards
{
    /// <summary>
    /// Only live, enabled projectiles fired by Kirby's opponents are paired.
    /// Kirby's own bow arrows and anything already turned back stay host-only.
    /// </summary>
    public static bool CanPair(bool hostile, bool live, bool disabled) => hostile && live && !disabled;

    /// <summary>
    /// Mouthful or Cook capture consumes the projectile; it never awards a copy.
    /// Any source damage to its collision object (an original attack striking it)
    /// destroys it as well. While the original suction controls it, the host must
    /// hold it still so it neither hits Kirby nor detonates.
    /// </summary>
    public static HazardAction Resolve(bool swallowed, bool cooked, bool gone, int sourceDamage, bool controlled)
    {
        if (swallowed || cooked || sourceDamage > 0) return HazardAction.Remove;
        if (gone) return HazardAction.Release;
        return controlled ? HazardAction.Hold : HazardAction.Follow;
    }
}
