using System.Buffers.Binary;
using System.Runtime.InteropServices;
using dc.en;
using dc.h2d;
using dc.h3d.mat;
using dc.hxd;
using dc.libs.heaps.slib;
using HaxeProxy.Runtime;
using ModCore.Utilities;

namespace Kirby.Runtime;

/// <summary>
/// Draws the original machine's live OBJ tiles and palettes. Source OAM chooses
/// poses, flips and timing; this class does not select a replacement animation.
/// The source player/effect palette allocation (banks 0..7) is retained while
/// enemy banks 8..15 are excluded from the host character layer.
/// </summary>
public sealed unsafe class KirbySpriteRenderer : IDisposable
{
    private const int Canvas = 512;
    private const int Center = Canvas / 2;
    private readonly Hero hero;
    private readonly NdsBackgroundEffects backgroundEffects;
    private readonly byte[] oam = new byte[1024];
    private readonly byte[] vram = new byte[256 * 1024];
    private readonly byte[] palette = new byte[512];
    private readonly byte[] registers = new byte[4];
    private readonly byte[] rgba = new byte[Canvas * Canvas * 4];
    private Pixels? pixels;
    private Texture? texture;
    private Bitmap? bitmap;
    private HSprite? equipment;
    private SpriteLib? equipmentLibrary;
    private bool ready;
    private bool disposed;
    private uint lastSourceFrame = uint.MaxValue;
    private int bitmapLayer = -1, equipmentLayer = -1;
    public int RenderedObjects { get; private set; }
    public int UnsupportedObjects { get; private set; }
    public bool EquipmentVisible => equipment?.visible ?? false;
    public NdsBackgroundEffect BackgroundEffect => backgroundEffects.Effect;
    public int UnsupportedBackgroundFrames => backgroundEffects.UnsupportedFrames;

    public KirbySpriteRenderer(Hero hero)
    {
        this.hero = hero ?? throw new ArgumentNullException(nameof(hero));
        backgroundEffects = new NdsBackgroundEffects(hero);
        Hook_HSprite.draw += DrawHostSprite;
    }

    private void DrawHostSprite(Hook_HSprite.orig_draw orig, HSprite sprite, RenderContext context)
    {
        // Body pixels are supplied by the original machine. Baked-in native
        // weapon pixels are redrawn separately; attached children keep drawing.
        if (!disposed && ready && hero.spr is { } host && sprite.HashlinkPointer == host.HashlinkPointer)
            return;
        orig(sprite, context);
    }

    private static void ReadGpu(NdsRuntime nds, uint address, byte[] data)
    {
        fixed (byte* pointer = data)
        {
            int status = NativeReadGpu(nds.Handle, address, pointer, (nuint)data.Length);
            if (status != 0) throw new InvalidOperationException($"NDS graphics read failed at {address:X8}: {status}.");
        }
    }

    public void Render(NdsRuntime nds)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (hero.destroyed || hero.spr?.parent is null) return;
        NdsState state = nds.State;
        backgroundEffects.Render(nds);
        EnsureTexture();
        if (lastSourceFrame != state.Frame)
        {
            ReadGpu(nds, 0x04000000, registers);
            uint display = BinaryPrimitives.ReadUInt32LittleEndian(registers);
            ReadGpu(nds, 0x07000000, oam);
            ReadGpu(nds, 0x06400000, vram);
            ReadGpu(nds, 0x05000200, palette);
            (int rendered, int unsupported) = NdsObjRenderer.Render(display, oam, vram, palette, rgba,
                (int)System.Math.Floor(state.X) - state.CameraX, (int)System.Math.Floor(state.Y) - state.CameraY);
            RenderedObjects = rendered;
            UnsupportedObjects = unsupported;
            Marshal.Copy(rgba, 0, pixels!.bytes.b, rgba.Length);
            texture!.uploadPixels(pixels, Ref<int>.In(0), Ref<int>.In(0));
            lastSourceFrame = state.Frame;
        }
        HSprite hostSprite = hero.spr;
        AttachSibling(bitmap!, hostSprite, ref bitmapLayer, false);
        bitmap!.x = hostSprite.x;
        bitmap.y = hostSprite.y - 14.5;
        bitmap.alpha = hostSprite.alpha;
        bitmap.visible = hero.visible && hostSprite.visible;
        bitmap.posChanged = true;
        RenderEquipment(hostSprite, state.Ability == 1);
        ready = true;
    }

    private void RenderEquipment(HSprite host, bool baseForm)
    {
        string animation = host.groupName?.ToString() ?? "";
        // Evolved Kirby retains the equipped shield's existing block/parry
        // animation. The controller independently gates actual weapon use.
        bool shield = animation.StartsWith("block", StringComparison.OrdinalIgnoreCase) ||
                      animation.StartsWith("parry", StringComparison.OrdinalIgnoreCase);
        // The asset manager resets each atlas's "used" flag before a biome
        // transition and explicitly destroys unused libraries in flushCache.
        // preventAutoDispose only protects GPU eviction, not that lifecycle.
        // Reacquire through get so it marks the live atlas used and replaces a
        // library whose pages were destroyed during the transition.
        SpriteLib currentLibrary = dc.Assets.Class.lib.get("atlas/kirbySuperStarUltraWeapons.atlas".AsHaxeString());
        if (equipment is null || equipmentLibrary?.HashlinkPointer != currentLibrary.HashlinkPointer)
        {
            equipment?.remove();
            equipmentLayer = -1;
            equipmentLibrary = currentLibrary;
            equipmentLibrary.preventAutoDispose();
            equipmentLibrary.ensureTexturesAllocated();
            equipment = new HSprite(equipmentLibrary, host.groupName, Ref<int>.In(host.frame), null!);
            equipment.pivot.isUndefined = false;
            equipment.pivot.usingFactor = true;
            equipment.pivot.centerFactorX = equipment.pivot.centerFactorY = .5;
            equipment.setFrame(host.frame);
        }
        AttachSibling(equipment, host, ref equipmentLayer, true);
        equipment.set(equipmentLibrary!, host.groupName, Ref<int>.In(host.frame), Ref<bool>.In(false));
        // The outfit palette retains every original host equipment index.
        equipment.shaders = host.shaders;
        equipment.color = host.color;
        equipment.x = host.x;
        equipment.y = host.y;
        equipment.scaleX = host.scaleX;
        equipment.scaleY = host.scaleY;
        equipment.rotation = host.rotation;
        equipment.alpha = host.alpha;
        equipment.visible = hero.visible && host.visible && (baseForm || shield);
        equipment.posChanged = true;
    }

    private void EnsureTexture()
    {
        if (bitmap is not null) return;
        pixels = Pixels.Class.alloc(Canvas, Canvas, new PixelFormat.RGBA());
        texture = Texture.Class.fromPixels(pixels, null!);
        texture.name = "kirby-original-nds-obj".AsHaxeString();
        texture.set_filter(new dc.h3d.mat.Filter.Nearest());
        texture.preventAutoDispose();
        Tile tile = new(texture, 0, 0, Canvas, Canvas, Ref<int>.In(-Center), Ref<int>.In(-Center));
        bitmap = new Bitmap(tile, null!) { smooth = false, scaleX = 1, scaleY = 1 };
        AttachSibling(bitmap, hero.spr, ref bitmapLayer, false);
    }

    private void AttachSibling(dc.h2d.Object child, HSprite host, ref int priorLayer, bool afterBody)
    {
        dc.h2d.Object parent = host.parent;
        int hostIndex = parent.getChildIndex(host);
        if (parent is Layers layers)
        {
            // Layers.addChildAt receives a layer ID, unlike Object.addChildAt.
            // Find the host's layer using the engine's exclusive end indices.
            int layer = 0;
            int count = System.Math.Min(layers.layerCount, layers.layersIndexes.length);
            while (layer < count && hostIndex >= Marshal.ReadInt32(layers.layersIndexes.bytes, layer * 4)) layer++;
            if (layer >= count) throw new InvalidOperationException("The host sprite has no render layer.");
            if (child.parent?.HashlinkPointer != parent.HashlinkPointer || priorLayer != layer)
                layers.addChildAt(child, layer);
            priorLayer = layer;
        }
        else if (child.parent?.HashlinkPointer != parent.HashlinkPointer)
        {
            parent.addChildAt(child, afterBody && bitmap?.parent?.HashlinkPointer == parent.HashlinkPointer
                ? parent.getChildIndex(bitmap) + 1 : hostIndex + 1);
            priorLayer = -1;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Hook_HSprite.draw -= DrawHostSprite;
        backgroundEffects.Dispose();
        bitmap?.remove();
        equipment?.remove();
        texture?.dispose();
        pixels?.dispose();
        bitmap = null; texture = null; pixels = null; equipment = null; equipmentLibrary = null;
    }

    [DllImport("kssu_bridge", EntryPoint = "kssu_read_gpu", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeReadGpu(nint handle, uint address, byte* destination, nuint length);
}
