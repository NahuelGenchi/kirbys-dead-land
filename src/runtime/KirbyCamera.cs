using System.Diagnostics;

namespace Kirby.Runtime;

/// <summary>
/// Frames the host camera like the original DS screen while Kirby plays.
/// The host viewport is about 400 host pixels tall, more than twice the DS
/// screen's 192, so at the host's zoom Kirby looked half size and moved across
/// the screen at half his original apparent speed. The host's own zoom is
/// used, with updated viewport sizes so its tracking stays centred.
/// </summary>
internal sealed class KirbyCamera(double zoom)
{
    // Time constant of the ease into and out of the Kirby framing.
    private const double EaseSeconds = 0.18;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastTime;
    private dc.Viewport? controlled;
    // The zoom the host itself wants (1, or its own arena framing) and the
    // value this class last applied on top of it.
    private double hostZoom = 1, applied = 1;

    /// <param name="level">The level Kirby plays in, including while his death animation finishes.</param>
    public void Update(dc.pr.Level? level, bool active)
    {
        double now = clock.Elapsed.TotalSeconds, dt = Math.Clamp(now - lastTime, 0, 0.1);
        lastTime = now;
        dc.Viewport? viewport = level?.viewport;
        if (!active || viewport is null || zoom <= 1 || viewport.destroyed) { Release(); return; }
        if (controlled?.HashlinkPointer != viewport.HashlinkPointer)
        {
            controlled = viewport;
            hostZoom = applied = viewport.zoom;
        }
        // The host changed its own zoom since the last frame: keep its framing
        // and apply the Kirby factor on top of it.
        if (Math.Abs(viewport.zoom - applied) > 1e-6) hostZoom = applied = viewport.zoom;
        // Host cinematics frame their own shots.
        if (level!.game.hasCinematic()) return;
        double target = hostZoom * zoom;
        if (applied == target) return;
        double next = applied + (target - applied) * (1 - Math.Exp(-dt / EaseSeconds));
        if (Math.Abs(next - target) < 0.002) next = target;
        Apply(viewport, next);
    }

    /// <summary>Restore the host framing when Kirby stops playing.</summary>
    public void Release()
    {
        if (controlled is { destroyed: false } viewport && Math.Abs(viewport.zoom - applied) < 1e-6 && applied != hostZoom)
            Apply(viewport, hostZoom);
        controlled = null;
    }

    private void Apply(dc.Viewport viewport, double value)
    {
        viewport.set_zoom(value);
        // The viewport tracks its target with its unzoomed size unless the
        // size is recomputed; the target would drift towards a corner.
        viewport.updateSizes();
        applied = value;
    }
}
