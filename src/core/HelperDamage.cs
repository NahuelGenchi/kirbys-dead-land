namespace Kirby.Core;

/// <summary>
/// The original CPU helper's damage to host enemies (#128, a mod balance choice,
/// not original behavior). The helper's hits arrive as the same source HP drop
/// as Kirby's, so they are told apart by the original attack records that touch
/// the enemy and scaled by a separate, lower host-damage-per-original-point
/// scale than Kirby's copy attacks. Because the host applies those hits as Kirby's,
/// the helper's hits are also what keeps an enemy's counter-attack from reaching Kirby (#206).
/// </summary>
public static class HelperDamage
{
    /// <summary>
    /// Hit-list slot of an object's own body (hurtbox). Its damage word is a touch
    /// value, not an attack: Kirby's and the helper's bodies both carry 2.
    /// </summary>
    public const uint BodySlot = 14;

    /// <summary>
    /// The helper's strength relative to Kirby's, from the two host-damage-per-point
    /// scales. 1 (no difference) when either scale is unset or not positive.
    /// </summary>
    public static double Ratio(double kirbyScale, double helperScale) =>
        double.IsFinite(kirbyScale) && double.IsFinite(helperScale) && kirbyScale > 0 && helperScale > 0
            ? helperScale / kirbyScale
            : 1;

    /// <summary>
    /// An enemy's source HP drop is the helper's only when one of its damaging
    /// records touches the enemy and none of Kirby's does. When both touch it in
    /// one frame the hit counts as Kirby's.
    /// </summary>
    public static bool IsHelperHit(bool helperRecordTouches, bool kirbyRecordTouches) =>
        helperRecordTouches && !kirbyRecordTouches;

    /// <summary>
    /// A host attack at Kirby is the struck enemy's own counter to a hit the helper
    /// landed (#206) when it comes from the very enemy that hit is landing on. The
    /// host applies the helper's hit as Kirby's, so an enemy that answers its
    /// attacker (the Thorny's spikes) answers Kirby for it. <paramref name="struck"/>
    /// is the enemy a helper hit is landing on now, 0 when none is.
    /// </summary>
    public static bool IsCounter(nint struck, nint source) => struck != 0 && struck == source;
}
