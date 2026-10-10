namespace Kirby.Core;

/// <summary>
/// Tells when the original controller lands Kirby after a fall, the moment
/// Dead Cells raises its hero land signal for the Beheaded. The host never sees
/// it, because the original places Kirby and the host's own physics is held
/// still. A ladder or chain is not a fall, and neither is a ground action.
/// </summary>
public sealed class HostLanding
{
    private bool falling;

    /// <param name="action">The original controller action after this source frame.</param>
    /// <param name="vy">The original's vertical velocity, positive downward.</param>
    /// <returns>True on the frame Kirby reaches the ground having moved downward since he left it.</returns>
    public bool Update(ushort action, int vy)
    {
        if (KirbyActions.OnGround(action))
        {
            bool landed = falling;
            falling = false;
            return landed;
        }
        // A ladder, chain or vine holds him; leaving it by a fall starts again.
        falling = action != KirbyActions.Climb && (falling || vy > 0);
        return false;
    }

    public void Reset() => falling = false;
}
