namespace Kirby.Core;

/// <summary>
/// Kirby's Ice copy ability freezes host enemies with Dead Cells' own Freeze
/// status instead of the original ice block (issue #42). The original breath
/// still decides when and how hard Ice hits; the host owns the frozen state,
/// its thaw slow and stun, resistances and the death.
/// </summary>
public static class IceFreeze
{
    /// <summary>Original ability ID of Ice (the source state's ability byte).</summary>
    public const byte SourceAbility = 10;

    // The host Frost Blast (tool.weap.Freeze.onExecute): affect 23 for the
    // weapon's props.duration (3 s) times getDiminishingFactor(3, 1, 8).
    public const int FreezeAffect = 23;
    public const double FreezeSeconds = 3;
    public const int DiminishingKey = 3;
    public const int DiminishingFullPowerUses = 1;
    public const int DiminishingMaxUses = 8;

    /// <summary>
    /// Kirby holds Ice in the source step. His hits are then host Frost damage,
    /// which the host lets through a freeze instead of shattering it (as with its
    /// own ice weapons), and a host enemy killed meanwhile keeps no original
    /// remnant: its source object is retired at once, so the original lethal Ice
    /// reaction (the kickable ice block) never runs. Other abilities keep their
    /// original death reactions until the native destructor completes.
    /// </summary>
    public static bool Applies(byte sourceAbility) => sourceAbility == SourceAbility;

    /// <summary>
    /// An Ice hit that leaves the host enemy alive freezes it, unless it is
    /// already frozen: a continuous breath keeps hitting it, and refreezing on
    /// every hit would spend the host's diminishing returns while it stays frozen.
    /// </summary>
    public static bool Freezes(byte sourceAbility, int sourceDamage, bool hostAlive, bool frozen) =>
        Applies(sourceAbility) && sourceDamage > 0 && hostAlive && !frozen;
}
