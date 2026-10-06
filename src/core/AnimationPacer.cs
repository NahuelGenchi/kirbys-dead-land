namespace Kirby.Core;

/// <summary>
/// Keeps the original walk and dash cycles in step with the matched speed, so
/// Kirby's feet slide less. The original counts each pose down one frame at a
/// time and moves to the next pose on the frame the count is zero; taking an
/// extra frame off the count on a share of the frames plays the cycle faster,
/// up to about 1.67 times, since a pose cannot last less than one frame.
/// </summary>
public sealed class AnimationPacer
{
    private double owed;

    /// <summary>
    /// The pose count for the next original frame, given how much faster than
    /// the original the cycle should play (the matched speed's scale) and the
    /// count the original holds now.
    /// </summary>
    public byte Next(double rate, byte remaining)
    {
        owed += Math.Max(rate - 1, 0);
        if (owed >= 1 && remaining > 0) { owed -= 1; remaining--; }
        owed = Math.Min(owed, 1);
        return remaining;
    }

    public void Reset() => owed = 0;
}
