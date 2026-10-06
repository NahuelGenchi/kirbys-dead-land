namespace Kirby.Core;

/// <summary>
/// Turns the host walking its hero during a cinematic, such as leaving through
/// a level's exit door, into a held direction for the original controller.
/// Host fixed updates do not move the hero on every rendered frame, so the
/// direction is held briefly across frames without movement. A jump of more
/// than a cell is the host placing the hero, not walking.
/// </summary>
public sealed class CinematicWalk
{
    /// <summary>Host pixels per frame below which the hero is standing.</summary>
    public const double MinStep = 0.05;
    public const double MaxStep = 24;
    public const double HoldSeconds = 0.2;
    private double? lastX;
    private double still;

    /// <summary>-1 left, 1 right, 0 standing.</summary>
    public int Direction { get; private set; }

    /// <param name="hostX">The hero's host X in pixels this frame.</param>
    /// <param name="delta">Seconds since the previous frame.</param>
    public int Update(double hostX, double delta)
    {
        double moved = lastX is double previous ? hostX - previous : 0;
        lastX = hostX;
        double distance = Math.Abs(moved);
        if (distance > MaxStep) { Direction = 0; still = 0; }
        else if (distance >= MinStep) { Direction = Math.Sign(moved); still = 0; }
        else if ((still += delta) > HoldSeconds) Direction = 0;
        return Direction;
    }

    public void Reset() { lastX = null; still = 0; Direction = 0; }
}
