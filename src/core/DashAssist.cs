namespace Kirby.Core;

/// <summary>
/// Makes a held horizontal direction dash, as Dead Cells' hero always runs,
/// by producing the original game's own double-tap: the direction is released
/// for one source frame after a new press, and once more after landing if
/// Kirby is still walking. The original controller still decides whether a
/// dash starts, so its speed, animation and every dash attack stay original.
/// </summary>
public sealed class DashAssist
{
    public const ushort Right = 16, Left = 32, Down = 128, Guard = 512;
    private const ushort Horizontal = Right | Left;
    // Original fixed-point velocities: walk 352 (1.375 px/frame), air 368,
    // dash 528 (2.0625 px/frame).
    private const int DashVelocity = 512;
    private int heldDirection;
    private bool releaseNext, airborne;
    private int groundFrames;

    /// <param name="mask">DS buttons for the next source frame.</param>
    /// <param name="enabled">False for a light analog tilt, which walks.</param>
    /// <param name="vx">Horizontal velocity after the previous source frame.</param>
    /// <param name="vy">Vertical velocity after the previous source frame.</param>
    public ushort Filter(ushort mask, bool enabled, int vx, int vy)
    {
        if (vy != 0) { airborne = true; groundFrames = 0; }
        else groundFrames++;
        int direction = (mask & Horizontal) switch { Right => 1, Left => -1, _ => 0 };
        // Down and guard combinations (slide, crouch, guard) keep their exact input.
        if (!enabled || direction == 0 || (mask & (Down | Guard)) != 0)
        {
            heldDirection = direction;
            releaseNext = false;
            return mask;
        }
        if (releaseNext)
        {
            releaseNext = false;
            heldDirection = direction;
            return (ushort)(mask & ~Horizontal);
        }
        if (direction != heldDirection)
        {
            heldDirection = direction;
            releaseNext = true;
            return mask;
        }
        // A jump from a standstill lands walking; the original keeps a dash
        // through a dash jump. Two still frames distinguish landing from an apex.
        if (airborne && groundFrames >= 2)
        {
            airborne = false;
            if (Math.Abs(vx) < DashVelocity) releaseNext = true;
        }
        return mask;
    }

    public void Reset()
    {
        heldDirection = 0;
        releaseNext = airborne = false;
        groundFrames = 0;
    }
}
