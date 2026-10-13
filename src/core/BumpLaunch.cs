namespace Kirby.Core;

/// <summary>
/// A host bump that throws the hero upward (the carnivorous plant, after it
/// closes on him). The host adds the bump to its own velocity and lets it decay
/// by a fixed factor each fixed step while gravity pulls the hero back. Kirby's
/// motion belongs to the original controller, which has no such impulse: the
/// host adds the same decaying ascent before each source frame, through free
/// space under the ceiling, and the original controller falls on its own.
/// </summary>
public sealed class BumpLaunch
{
    /// <summary>The host's <c>Entity.bumpFrict</c>: what is left of a bump after each of its 30 fixed steps a second.</summary>
    public const double StepFriction = 0.82;
    /// <summary>The same decay over one of the original's 60 frames a second.</summary>
    public static readonly double FrameFriction = Math.Sqrt(StepFriction);
    /// <summary>Distance left under a ceiling, as for the other added motion (<see cref="SpeedMatch.Margin"/>).</summary>
    public const double CeilingMargin = SpeedMatch.Margin;
    /// <summary>Ascent in pixels a frame below which the launch is over.</summary>
    public const double MinVelocity = 0.25;

    private double velocity;

    public bool Active => velocity > 0;

    /// <summary>Whole distance a bump of this speed (pixels a fixed step) covers before gravity, in pixels.</summary>
    public static double Reach(double pixelsPerStep) => pixelsPerStep / (1 - StepFriction);

    /// <summary>
    /// Begin a launch from the host's upward bump speed in pixels a fixed step.
    /// The first frame's ascent is the one whose decay covers the same distance.
    /// </summary>
    public void Start(double pixelsPerStep)
    {
        double frame = Reach(pixelsPerStep) * (1 - FrameFriction);
        velocity = frame >= MinVelocity ? frame : 0;
    }

    /// <summary>
    /// Ascent for the next source frame, given the free space above Kirby's head.
    /// A ceiling ends the launch, as the host clears a bump there.
    /// </summary>
    public double Ascent(double free)
    {
        if (!Active) return 0;
        double room = Math.Max(0, free - CeilingMargin);
        double step = Math.Min(velocity, room);
        velocity *= FrameFriction;
        if (step >= room || velocity < MinVelocity) velocity = 0;
        return step;
    }

    public void Reset() => velocity = 0;
}
