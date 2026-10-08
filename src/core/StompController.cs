namespace Kirby.Core;

/// <summary>
/// Dead Cells' dive attack for Kirby. The original controller caps a fall at
/// 3.5 pixels a frame and has no ground pound; while a stomp is active it keeps
/// its own falling (or exhaling) animation and physics, the host adds descent
/// through free space above the floor, and the host's dive skill resolves the
/// landing: shockwave, stun, damage and breakable floors.
/// </summary>
public sealed class StompController
{
    public const ushort ButtonA = 1, ButtonB = 2;
    /// <summary>
    /// Host descent added per source frame on top of the original 3.5 px fall:
    /// 15 px a frame in all, the Beheaded's dive of about 900 px/s.
    /// </summary>
    public const double ExtraDescent = 11.5;
    /// <summary>
    /// Distance above the floor left to the original fall. The descent comes
    /// before the original frame, whose floor test starts from where its previous
    /// frame ended, so one full fall step (3.5 px) and a pixel are left for it.
    /// </summary>
    public const double LandingMargin = 4.5;
    /// <summary>Safety limit: a stomp that has not landed after three seconds is cancelled.</summary>
    public const int MaxFrames = 180;
    // Button that frees Kirby for the dive on its first frame, or 0.
    private ushort release;
    // Whether the latest frame on a ladder pressed A, so the next one releases it.
    private bool kicked;

    public bool Active { get; private set; }
    public int Frames { get; private set; }
    public double StartFeetY { get; private set; }

    /// <summary>Begin a stomp from the given original action; false when that action cannot stomp.</summary>
    public bool TryStart(ushort action, double feetY)
    {
        if (Active || !KirbyActions.CanStomp(action)) return false;
        Active = true;
        Frames = 0;
        StartFeetY = feetY;
        // A floating Kirby is full of air: he exhales it, as when he stops
        // floating normally. On a ladder or chain the original A kicks him off (see Mask).
        release = action == KirbyActions.Float ? ButtonB : (ushort)0;
        kicked = false;
        return true;
    }

    /// <summary>
    /// Buttons for the next source frame, given the original action before it:
    /// none while stomping, except one releasing press. While the original
    /// still climbs, A is pressed every other frame until it lets go: the
    /// original ignores A for the first frames of climbing down from a ladder top.
    /// </summary>
    public ushort Mask(ushort requested, ushort action)
    {
        if (!Active) return requested;
        if (action == KirbyActions.Climb)
        {
            kicked = !kicked;
            return kicked ? ButtonA : (ushort)0;
        }
        ushort mask = release;
        release = 0;
        return mask;
    }

    /// <summary>
    /// Host descent for the next source frame, given the free space under Kirby's
    /// feet and the original action. None while the original still climbs: it
    /// would carry a climbing Kirby down the ladder, short of the floor.
    /// </summary>
    public double Descent(double free, ushort action) =>
        Active && action != KirbyActions.Climb ? Math.Clamp(free - LandingMargin, 0, ExtraDescent) : 0;

    public enum Outcome { None, Landed, Cancelled }

    /// <summary>Advance after a source frame.</summary>
    /// <param name="landed">Kirby's feet are on the floor, or the original controller reports a ground action.</param>
    /// <param name="interrupted">A hit, water, death or a scene change ended the dive.</param>
    public Outcome Advance(bool landed, bool interrupted)
    {
        if (!Active) return Outcome.None;
        Frames++;
        if (interrupted || (!landed && Frames > MaxFrames)) { Reset(); return Outcome.Cancelled; }
        if (!landed) return Outcome.None;
        Reset();
        return Outcome.Landed;
    }

    /// <summary>Fall height in host cells, as the host dive reports it.</summary>
    public double Height(double feetY) => Math.Max(0, (feetY - StartFeetY) / 24);

    public void Reset()
    {
        Active = false;
        release = 0;
        kicked = false;
        Frames = 0;
    }
}
