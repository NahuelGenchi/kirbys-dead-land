using System.Runtime.InteropServices;
using dc.en;
using dc.h2d;
using dc.h2d.col;
using HaxeProxy.Runtime;

namespace Kirby.Runtime;

/// <summary>
/// Transfers the original source background palette effect onto host scenery.
/// Actual OBJ supplies spatial effects such as Crash's growing circle. Host
/// colors use ordinary continuous blending; the source effect coefficients and
/// timing are inferred only when the complete original palette proves them.
/// </summary>
public sealed unsafe class NdsBackgroundEffects : IDisposable
{
    private readonly Hero hero;
    private readonly uint[] top = new uint[NdsBackgroundEffectDecoder.PixelCount];
    private readonly uint[] metadata = new uint[NdsBackgroundEffectDecoder.PixelCount];
    private readonly uint[] paletteLines = new uint[NdsBackgroundEffectDecoder.PixelCount];
    private Graphics? overlay;
    private uint lastFrame = uint.MaxValue;
    private NdsBackgroundEffect drawn = NdsBackgroundEffect.Unsupported;
    private bool disposed;
    public NdsBackgroundEffect Effect { get; private set; } = NdsBackgroundEffect.Identity;
    public int UnsupportedFrames { get; private set; }

    public NdsBackgroundEffects(Hero hero) => this.hero = hero;

    public void Render(NdsRuntime nds)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (hero.destroyed || hero._level?.scroller is not { } scroller) return;
        uint frame = nds.State.Frame;
        if (lastFrame != frame)
        {
            Read(nds, top, 0); Read(nds, metadata, 2); Read(nds, paletteLines, 4);
            Effect = NdsBackgroundEffectDecoder.Decode(nds.SourceBgPalette, paletteLines, top, metadata);
            if (!Effect.Supported) UnsupportedFrames++;
            lastFrame = frame;
        }
        if (!Effect.Active)
        {
            if (overlay is not null) overlay.visible = false;
            return;
        }
        double alpha = 1 - Effect.Scale16 / 16.0;
        double r = Effect.Red5 / (31.0 * alpha);
        double g = Effect.Green5 / (31.0 * alpha);
        double b = Effect.Blue5 / (31.0 * alpha);
        if (alpha <= 0 || r < 0 || r > 1 || g < 0 || g > 1 || b < 0 || b > 1)
        {
            if (overlay is not null) overlay.visible = false;
            return;
        }
        overlay ??= new Graphics(null!);
        if (overlay.parent?.HashlinkPointer != scroller.HashlinkPointer)
            scroller.addChildAt(overlay, dc.Const.Class.DP_ROOM_MAIN_BACK);
        if (drawn != Effect)
        {
            int color = ((int)System.Math.Round(r * 255) << 16) |
                        ((int)System.Math.Round(g * 255) << 8) | (int)System.Math.Round(b * 255);
            overlay.clear();
            overlay.beginFill(Ref<int>.In(color), Ref<double>.In(alpha));
            overlay.drawRect(0, 0, 1, 1);
            overlay.endFill();
            drawn = Effect;
        }
        Scene scene = scroller.getScene();
        Point origin = scroller.globalToLocal(new Point(Ref<double>.In(0), Ref<double>.In(0)));
        Point extent = scroller.globalToLocal(new Point(Ref<double>.In(scene.width), Ref<double>.In(scene.height)));
        overlay.x = origin.x;
        overlay.y = origin.y;
        overlay.scaleX = extent.x - origin.x;
        overlay.scaleY = extent.y - origin.y;
        overlay.visible = hero.visible;
        overlay.posChanged = true;
    }

    public void Hide()
    {
        if (overlay is not null) overlay.visible = false;
    }

    private static void Read(NdsRuntime nds, uint[] data, uint plane)
    {
        fixed (uint* pointer = data)
            if (NativeReadLayers(nds.Handle, pointer, (nuint)data.Length, plane) != 0)
                throw new InvalidOperationException("Original background capture read failed.");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        overlay?.remove();
        overlay = null;
    }

    [DllImport("kssu_bridge", EntryPoint = "kssu_read_layers", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeReadLayers(nint handle, uint* destination, nuint count, uint plane);
}
