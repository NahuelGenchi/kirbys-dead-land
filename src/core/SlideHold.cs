namespace Kirby.Core;

/// <summary>
/// Keeps Kirby's slide, his roll, going for as long as he is inside a Dead
/// Cells roll-zone tunnel (#115). The host extends the Beheaded's roll until he
/// is out of the zone; the original slide runs a fixed 36 frames (about 131 px
/// at the matched speed) and stopped partway through a longer tunnel, as it
/// does on open floor.
///
/// The original slide's controller counts it in two bytes: the steps left
/// (eight steps of four frames: the full-speed part counts 8 to 5, the slowdown
/// 4 to 1, and the slide ends at 0) and the frames left in the current part
/// (15 down to 0). While Kirby is inside a tunnel the runtime writes back the
/// values the slide had on entering, so the same part keeps playing at full
/// speed and the roll animation never stops or restarts. When he is clear, a
/// slide still in its full-speed part goes straight on to the slowdown, the
/// tail of any slide. A slide that stops advancing (a wall inside the tunnel)
/// or is hit goes back to the original at once.
/// </summary>
public sealed class SlideHold
{
    /// <summary>Offset of the steps-left byte in the original controller block (player+0x40).</summary>
    public const uint StepsOffset = 0x6CB;
    /// <summary>Offset of the frames-left byte of the current part of the slide.</summary>
    public const uint PhaseOffset = 0x6D8;
    /// <summary>The original horizontal velocity of the slide's full-speed part.</summary>
    public const int Velocity = SpeedMatch.SlideFullVelocity;
    /// <summary>The last of the full-speed part's four-frame steps; the slowdown's are 4 to 1.</summary>
    public const byte LastFastStep = 5;

    private bool captured, blocked;
    private byte steps, phase;

    /// <summary>The slide's counters are being held this frame, and its velocity must stay at full speed.</summary>
    public bool Holding { get; private set; }

    /// <summary>
    /// Decide the next source frame. Returns the (steps, frames) values to write
    /// into the slide's controller before it runs, or null to leave the original alone.
    /// </summary>
    /// <param name="action">The original action before this source frame.</param>
    /// <param name="vx">The original horizontal velocity: zero in the slide's stopped last frames.</param>
    /// <param name="inside">Kirby's body overlaps a roll-zone cell.</param>
    /// <param name="recoil">A host hit reaches the original this frame.</param>
    /// <param name="read">The slide's current counters; asked for once when a hold begins.</param>
    public (byte Steps, byte Phase)? Begin(ushort action, int vx, bool inside, bool recoil, Func<(byte Steps, byte Phase)> read)
    {
        if (action != KirbyActions.Slide) { Reset(); return null; }
        if (recoil) blocked = true;
        if (vx != 0 && inside && !blocked)
        {
            if (!captured) { (steps, phase) = read(); captured = true; }
            Holding = true;
            return (steps, phase);
        }
        bool was = Holding;
        Holding = false;
        captured = false;
        // Out of the tunnel while still in the full-speed part: start the slowdown the way the
        // original does at the end of that part (last step, no frames left).
        return was && !blocked && steps >= LastFastStep ? (LastFastStep, (byte)0) : null;
    }

    /// <summary>After the source frame: a held slide that did not advance is against a wall; stop holding it.</summary>
    public void Finish(bool moved)
    {
        if (Holding && !moved) blocked = true;
    }

    public void Reset() => Holding = captured = blocked = false;
}
