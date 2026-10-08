namespace Kirby.Core;

/// <summary>
/// Makes one button start Kirby's original slide, the move Down + jump (S +
/// Space by default) starts on the ground. A new press sends Down + A for one
/// source frame and the original controller decides the slide, as it does for
/// S + Space; a press off the ground does nothing, so it never jumps or
/// floats. On the supplied ROM one Down + A frame starts the full 36-frame
/// slide whether or not Down stays held, from standing or running.
/// </summary>
public sealed class SlideKey
{
    public const ushort A = 1, Up = 64, Down = 128;
    private bool held, pending;

    /// <param name="mask">DS buttons for the next source frame.</param>
    /// <param name="button">The slide button is held.</param>
    /// <param name="action">The original action before this source frame.</param>
    /// <param name="previous">DS buttons sent on the previous source frame.</param>
    public ushort Filter(ushort mask, bool button, ushort action, ushort previous)
    {
        bool press = button && !held;
        held = button;
        if (pending)
        {
            pending = false;
            return KirbyActions.CanSlide(action) ? Slide(mask) : mask;
        }
        if (!press || !KirbyActions.CanSlide(action)) return mask;
        // A already held would not be a new press: release it for one frame first.
        if ((previous & A) != 0)
        {
            pending = true;
            return (ushort)((mask | Down) & ~(A | Up));
        }
        return Slide(mask);
    }

    private static ushort Slide(ushort mask) => (ushort)((mask | Down | A) & ~Up);

    public void Reset() => held = pending = false;
}
