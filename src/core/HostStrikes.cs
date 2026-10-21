namespace Kirby.Core;

/// <summary>
/// Kirby's moves that Dead Cells' door checks treat as the Beheaded's roll.
/// Doors are paired with an original object that resists capture, as bosses
/// do, so Suplex's grab, which only captures, could never break one.
/// </summary>
public static class HostStrikes
{
    /// <summary>The original horizontal velocity (2 px a frame) a strike needs.</summary>
    public const int Velocity = 512;
    /// <summary>Suplex's original ability ID.</summary>
    public const byte Suplex = 6;

    /// <summary>The fast part of the slide, Kirby's roll: it bursts doors and breakable tiles.</summary>
    public static bool Slide(ushort action, int vx) => action == KirbyActions.Slide && Math.Abs(vx) >= Velocity;

    /// <summary>
    /// Suplex's grab lunge while it carries Kirby at speed (its dash grab, and
    /// its standing, Down+B and mid-air lunges): it bursts doors.
    /// <paramref name="motion"/> is <see cref="SpeedMatch.ActionFilter.Effective"/>.
    /// </summary>
    public static bool Grab(byte ability, ushort motion, int vx) =>
        ability == Suplex && motion == SpeedMatch.Carry && Math.Abs(vx) >= Velocity;

    /// <summary>
    /// Whether a host door takes Kirby's hits. A key door (<c>LockedDoor</c> and its
    /// subclasses) never does (#110). Any other door is turned away only when it is
    /// locked and not breakable, as <c>Door.applyAttackResult</c> does: the
    /// Collector's exit is a locked, breakable <c>TriggeredDoor</c> that the player
    /// breaks to leave with unspent cells (#233).
    /// </summary>
    public static bool DoorTakesHits(bool keyDoor, bool locked, bool breakable) => !keyDoor && (!locked || breakable);
}
