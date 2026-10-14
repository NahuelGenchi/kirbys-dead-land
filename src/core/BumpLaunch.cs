namespace Kirby.Core;

/// <summary>
/// A host bump that throws the hero upward (the carnivorous plant, while it is
/// closed). The host first clears the hero's velocities, then each fixed step
/// moves him by his falling speed less the bump, lets gravity build the fall
/// and lets the bump decay. Kirby's motion belongs to the original controller,
/// which has no such impulse: while a launch lasts, the host replays those
/// steps as ascent before each source frame and the original's own vertical
/// speed is held at zero, so he rises as the Beheaded does from a stand, a fall
/// or a float. At the top the original controller falls on its own.
/// </summary>
public sealed class BumpLaunch
{
    // The Beheaded, per fixed step (30 a second), in host cells. Read from the
    // host (Entity gravity and gravityFrict) and from a lab recording of him on
    // a plant, which these three numbers reproduce to five decimals: he rises
    // 242.2 px in 17 steps from the plant's 1.5 cells a step.
    public const double Gravity = 0.065, GravityFriction = 0.9, BumpFriction = 0.94;
    /// <summary>The Beheaded's body in pixels (lab reading of the host hero): what a plant is sized for.</summary>
    public const double BeheadedRadius = 16.8, BeheadedHeight = 54;
    /// <summary>Source frames in one host fixed step.</summary>
    public const int FramesPerStep = 2;
    /// <summary>Distance left under a ceiling, as for the other added motion (<see cref="SpeedMatch.Margin"/>).</summary>
    public const double CeilingMargin = SpeedMatch.Margin;

    // Pixels a fixed step: what is left of the bump, the fall gravity has built,
    // and the current step's rise; then the source frames of that step done.
    private double bump, fall, rise;
    private int frame;

    public bool Active => rise > 0;

    /// <summary>The hero's rise in pixels over each fixed step of a bump of this speed (pixels a step), until gravity wins.</summary>
    public static IEnumerable<double> Steps(double pixelsPerStep)
    {
        double bump = pixelsPerStep, fall = 0;
        while (true)
        {
            double speed = fall + Gravity * 24;
            if (bump <= speed) yield break;
            yield return bump - speed;
            fall = speed * GravityFriction;
            bump *= BumpFriction;
        }
    }

    /// <summary>
    /// The host's test in <c>AirJump*.onStart</c>: an air jump started no more than
    /// two cells to the side of a closed plant, in its row or above, with the feet
    /// no more than 48 px above the plant's own position, is thrown by the plant.
    /// </summary>
    public static bool AirJumpReach(int plantCx, int plantCy, double plantYr, int heroCx, int heroCy, double heroYr) =>
        Math.Abs(plantCx - heroCx) <= 2 && plantCy >= heroCy && (plantCy + plantYr) * 24 <= (heroCy + heroYr) * 24 + 48;

    /// <summary>
    /// The host's circular collision of two round bodies, each centred half its
    /// height above its feet: they touch when the centres are no farther apart
    /// than the two radii. Positions are the entities' own (feet) in pixels.
    /// </summary>
    public static bool Touches(double x1, double feet1, double radius1, double height1,
        double x2, double feet2, double radius2, double height2)
    {
        double dx = x1 - x2, dy = feet1 - height1 * 0.5 - (feet2 - height2 * 0.5), reach = radius1 + radius2;
        return dx * dx + dy * dy <= reach * reach;
    }

    /// <summary>Begin a launch from the host's upward bump speed in pixels a fixed step, from rest.</summary>
    public void Start(double pixelsPerStep)
    {
        bump = pixelsPerStep; fall = 0;
        Next();
    }

    private void Next()
    {
        double speed = fall + Gravity * 24;
        rise = Math.Max(0, bump - speed);
        fall = speed * GravityFriction;
        bump *= BumpFriction;
        frame = 0;
    }

    /// <summary>
    /// Ascent for the next source frame, given the free space above Kirby's head.
    /// A ceiling ends the launch, as the host clears a bump there.
    /// </summary>
    public double Ascent(double free)
    {
        if (!Active) return 0;
        double want = rise / FramesPerStep, room = Math.Max(0, free - CeilingMargin);
        if (want >= room) { Reset(); return room; }
        if (++frame == FramesPerStep) Next();
        return want;
    }

    public void Reset() => rise = bump = fall = 0;
}
