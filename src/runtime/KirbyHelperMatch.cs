using Kirby.Core;

namespace Kirby.Runtime;

/// <summary>
/// Speed match for the original CPU helper (#105). The helper is another
/// instance of Kirby's controller, so it moves at the original speeds while
/// Kirby, whom <see cref="SpeedMatch"/> speeds up to the Beheaded's, runs
/// nearly twice as fast and left it behind. After each source frame the helper
/// is moved on through free space along the motion it made, by the same rules
/// and with the same terrain limits as Kirby's: dashes, jumps, falls and
/// floating cover the matched distance, and the original keeps every contact.
/// </summary>
public sealed class KirbyHelperMatch
{
    // Host movement the original did not make itself: at most a few pixels a
    // frame. Larger differences are the helper being placed beside Kirby.
    private const double MaxStep = 32;
    // The helper stands on the same body as Kirby: his centre is 14.5 px above his feet.
    private const double FeetOffset = 14.5;
    private readonly SpeedMatch.ActionFilter actions = new();
    private NdsHelper start;
    private bool sampled;
    /// <summary>The helper moved on by the latest <see cref="Finish"/>, in host pixels (lab and tests).</summary>
    public (double X, double Y) LastAdded { get; private set; }

    /// <summary>Sample the helper after the window is placed and before the source frame runs.</summary>
    public void Begin(NdsRuntime nds)
    {
        sampled = NdsRuntime.CanMoveHelper && nds.TryHelper(out start);
        LastAdded = default;
    }

    /// <summary>
    /// Move the helper on by the matched distance of the source frame just run.
    /// <paramref name="originX"/> and <paramref name="originY"/> are the window's host cell origin;
    /// <paramref name="mounted"/> is true while Kirby rides it (its motion is then Kirby's, which keeps the original speed).
    /// </summary>
    public void Finish(NdsRuntime nds, int originX, int originY, bool mounted, SpeedMatch.Terrain terrain)
    {
        if (!sampled) { actions.Reset(); return; }
        sampled = false;
        if (!nds.TryHelper(out var end)) { actions.Reset(); return; }
        double x0 = start.X + originX * 24, feet0 = start.Y + originY * 24 + FeetOffset;
        double x1 = end.X + originX * 24, feet1 = end.Y + originY * 24 + FeetOffset;
        ushort motion = actions.Next(end.Action, end.VxRaw);
        if (mounted || Math.Abs(x1 - x0) > MaxStep || Math.Abs(feet1 - feet0) > MaxStep) return;
        // Everything the helper does by itself is motion it wants, so every
        // direction counts as the held one (a skid or a recoil moves too, which is harmless).
        int direction = Math.Sign(x1 - x0);
        var (x, feet) = SpeedMatch.Move(motion, actions.Direction(direction), end.VxRaw, x0, feet0, x1, feet1, terrain);
        int dx = checked((int)Math.Round((x - x1) * 65536)), dy = checked((int)Math.Round((feet - feet1) * 65536));
        if (dx == 0 && dy == 0) return;
        if (nds.MoveHelper(dx, dy)) LastAdded = (x - x1, feet - feet1);
    }

    public void Reset() { actions.Reset(); sampled = false; LastAdded = default; }
}
