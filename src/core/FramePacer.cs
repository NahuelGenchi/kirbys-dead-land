namespace Kirby.Core;

/// <summary>
/// Chooses how many original DS frames to run for each host frame.
/// A display within <see cref="LockTolerance"/> of the DS refresh presents
/// exactly one original frame per host frame, as an emulator synchronized to
/// vblank does. Accumulating wall time against such a display (for example
/// 59.96 Hz against the DS's 59.826 Hz) periodically drops or doubles a frame,
/// which reads as stutter in movement and animation. Other refresh rates and
/// host hitches accumulate real time instead.
/// </summary>
public sealed class FramePacer(double sourceRate)
{
    public const double DsRefreshRate = 33513982.0 / 560190.0;
    public const double LockTolerance = 0.03;
    private const double Smoothing = 0.05;
    private const double MaximumDelta = 0.25;
    private double credit = 0.5, interval;

    public FramePacer() : this(DsRefreshRate) { }

    public double SourceRate { get; } = sourceRate > 0 ? sourceRate : throw new ArgumentOutOfRangeException(nameof(sourceRate));

    /// <summary>True while host frames are presented one original frame each.</summary>
    public bool Locked { get; private set; }

    /// <param name="delta">Host frame time in seconds.</param>
    public int Steps(double delta)
    {
        delta = double.IsFinite(delta) ? Math.Clamp(delta, 0, MaximumDelta) : 0;
        bool hitch = delta * SourceRate >= 1.5;
        // A hitch is not a refresh-rate change; keep it out of the estimate.
        if (!hitch) interval = interval <= 0 ? delta : interval + (delta - interval) * Smoothing;
        Locked = !hitch && Math.Abs(interval * SourceRate - 1) < LockTolerance;
        if (Locked)
        {
            // A centred phase rounds a later host hitch to the nearest frame count.
            credit = 0.5;
            return 1;
        }
        credit += delta * SourceRate;
        int steps = (int)Math.Floor(credit);
        credit -= steps;
        return steps;
    }

    /// <summary>Forget accumulated time, for example while the host is paused.</summary>
    public void Reset() => credit = 0.5;
}
