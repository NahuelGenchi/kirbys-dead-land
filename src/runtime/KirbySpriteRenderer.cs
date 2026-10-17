using System.Buffers.Binary;
using System.Runtime.InteropServices;
using dc.en;
using dc.h2d;
using dc.h3d.mat;
using dc.hxd;
using Kirby.Core;
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
    private const int CanvasWidth = NdsObjRenderer.CanvasWidth, CanvasHeight = NdsObjRenderer.CanvasHeight;
    private readonly Hero hero;
    private readonly NdsBackgroundEffects backgroundEffects;
    private byte[] oam = new byte[1024];
    private byte[] vram = new byte[256 * 1024];
    private byte[] palette = new byte[512];
    // The inputs of the canvas now in the texture (see UpdateTexture).
    private byte[] shownOam = new byte[1024], shownVram = new byte[256 * 1024], shownPalette = new byte[512];
    private readonly FarObject[] shownFar = new FarObject[256];
    private int shownFarCount = -1;
    private uint shownDisplay;
    private (int X, int Y) shownOrigin;
    private readonly byte[] registers = new byte[4];
    // The part of the texture's pixels that may not be transparent; all of it before the first drawing.
    private NdsObjRenderer.Area drawn = NdsObjRenderer.Area.Canvas;
    // The bridge keeps at most 256 culled pieces per frame.
    private readonly FarObject[] farBuffer = new FarObject[256];
    private Pixels? pixels;
    private Texture? texture;
    private Bitmap? bitmap;
    private HSprite? equipment;
    private SpriteLib? equipmentLibrary;
    private dc.shader.NormalMap? equipmentNormal;
    private bool disposed;
    private uint lastSourceFrame = uint.MaxValue;
    private int bitmapLayer = -1, equipmentLayer = -1;
    // headPopStart reaches its white flash on frame 20; Kirby appears just before it.
    private const int ArrivalRevealFrame = 17;
    private bool arriving, detached;
    // Whole host pixels Kirby and his equipment are drawn below their physical
    // position: the lag of the elevator sprite he stands on (see SetDrawOffset).
    private double drawOffsetY, bitmapBaseY, equipmentBaseY;
    public int RenderedObjects { get; private set; }
    public int BodyPixels { get; private set; }
    /// <summary>Body pixel bounds relative to Kirby's anchor in the last texture (diagnostics).</summary>
    public (int Left, int Top, int Right, int Bottom) BodyBounds { get; private set; }
    public uint TextureFrame => lastSourceFrame;
    public int UnsupportedObjects { get; private set; }
    public bool EquipmentVisible => equipment?.visible ?? false;
    public NdsBackgroundEffect BackgroundEffect => backgroundEffects.Effect;
    public int UnsupportedBackgroundFrames => backgroundEffects.UnsupportedFrames;

    // The hurt and guard flashes rewrite Kirby's first two palette banks.
    private readonly PaletteFlashSoftener? softener;

    public KirbySpriteRenderer(Hero hero, double flashStrength = 1)
    {
        this.hero = hero ?? throw new ArgumentNullException(nameof(hero));
        backgroundEffects = new NdsBackgroundEffects(hero);
        if (flashStrength < 1) softener = new PaletteFlashSoftener(2, flashStrength);
        // Body pixels are supplied by the original machine. The host body sprite
        // draws the outfit's blank page; weapon pixels are redrawn separately.
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
        if (detached || hero.destroyed || hero.spr?.parent is null) return;
        NdsState state = nds.State;
        var mark = LabPerfProbe.Start();
        backgroundEffects.Render(nds);
        LabPerfProbe.Stop(LabPerfProbe.Section.Background, mark);
        EnsureTexture();
        UpdateTexture(nds, state);
        HSprite hostSprite = hero.spr;
        UpdateArrival(hostSprite);
        AttachSibling(bitmap!, hostSprite, ref bitmapLayer, false);
        // The canvas centre is the original's whole-pixel position (it draws
        // from an integer camera). Anchor it to that pixel in host space, as
        // the original does, instead of the fractional physics position; at
        // the Kirby camera zoom a fraction of a pixel is several screen pixels.
        bitmap!.x = System.Math.Floor((hero.cx + hero.xr) * 24 + 1e-6);
        bitmapBaseY = System.Math.Floor((hero.cy + hero.yr) * 24 - 14.5 + 1e-6);
        bitmap.y = bitmapBaseY + drawOffsetY;
        bitmap.alpha = hostSprite.alpha;
        bitmap.visible = hero.visible && hostSprite.visible && !arriving;
        bitmap.posChanged = true;
        mark = LabPerfProbe.Start();
        RenderEquipment(hostSprite, state.Ability == 1);
        KeepBodyInFront();
        LabPerfProbe.Stop(LabPerfProbe.Section.Equipment, mark);
    }

    /// <summary>
    /// Draw Kirby and his equipment this many host pixels below their physical
    /// position. Host elevators draw their platform behind their physical
    /// position while they move (up to a tick of travel); Kirby standing on one
    /// is drawn with the same lag, so he stays on the platform as drawn instead
    /// of floating above it on the way up and sinking into it on the way down.
    /// Applies at once, whether or not Render already ran this frame.
    /// </summary>
    public void SetDrawOffset(double offset)
    {
        offset = System.Math.Round(offset);
        if (offset == drawOffsetY) return;
        drawOffsetY = offset;
        if (bitmap is not null) { bitmap.y = bitmapBaseY + offset; bitmap.posChanged = true; }
        if (equipment is not null) { equipment.y = equipmentBaseY + offset; equipment.posChanged = true; }
    }

    public double DrawOffset => drawOffsetY;

    /// <summary>Forget flash-softening history after the machine loads another state.</summary>
    public void ResetPalette() => softener?.Reset();

    /// <summary>True while the original body is still in the host scene.</summary>
    public bool Attached => !disposed && bitmap?.parent is not null;

    /// <summary>
    /// The host replaces a dead hero with its own corpse and spirit. Keep the
    /// original body where Kirby fell, without host equipment or a background
    /// tint, so the original death animation can finish over the host sequence.
    /// </summary>
    public void Detach()
    {
        if (disposed || detached) return;
        detached = true;
        if (equipment is not null) equipment.visible = false;
        backgroundEffects.Hide();
        if (bitmap is not null) bitmap.visible = !arriving;
    }

    public void RenderDetached(NdsRuntime nds)
    {
        if (disposed || !detached || bitmap?.parent is null) return;
        UpdateTexture(nds, nds.State);
    }

    private void UpdateTexture(NdsRuntime nds, NdsState state)
    {
        if (lastSourceFrame != state.Frame)
        {
            var mark = LabPerfProbe.Start();
            ReadGpu(nds, 0x04000000, registers);
            uint display = BinaryPrimitives.ReadUInt32LittleEndian(registers);
            ReadGpu(nds, 0x07000000, oam);
            ReadGpu(nds, 0x06400000, vram);
            ReadGpu(nds, 0x05000200, palette);
            softener?.Apply(palette);
            // OAM shows the previous source frame; centre it on that frame's
            // position of Kirby. The bitmap itself follows the current host
            // position, so only the one-frame-old pose differs, as on the DS.
            (int originX, int originY) = nds.DisplayedOrigin;
            // Pieces the original culled outside its screen, for the same OAM (#67).
            int farCount = nds.ReadFarObjects(farBuffer);
            LabPerfProbe.Stop(LabPerfProbe.Section.Read, mark);
            lastSourceFrame = state.Frame;
            // The decoder is pure, so the same inputs draw the same canvas: a frame
            // that changed none of them (Kirby holding a pose) keeps the texture
            // as uploaded instead of redrawing and uploading 2 MB again.
            if (farCount == shownFarCount && display == shownDisplay && (originX, originY) == shownOrigin &&
                oam.AsSpan().SequenceEqual(shownOam) && palette.AsSpan().SequenceEqual(shownPalette) &&
                MemoryMarshal.AsBytes(farBuffer.AsSpan(0, farCount)).SequenceEqual(MemoryMarshal.AsBytes(shownFar.AsSpan(0, farCount))) &&
                vram.AsSpan().SequenceEqual(shownVram)) return;
            mark = LabPerfProbe.Start();
            // Drawn straight into the texture's own pixels, clearing only what the last drawing covered.
            var rgba = new Span<byte>((void*)pixels!.bytes.b, CanvasWidth * CanvasHeight * 4);
            (int rendered, int unsupported) = NdsObjRenderer.Render(display, oam, vram, palette, rgba, ref drawn, originX, originY,
                far: farBuffer.AsSpan(0, farCount));
            LabPerfProbe.Stop(LabPerfProbe.Section.Decode, mark);
            mark = LabPerfProbe.Start();
            RenderedObjects = rendered;
            UnsupportedObjects = unsupported;
            BodyPixels = NdsObjRenderer.LastBodyPixels;
            BodyBounds = NdsObjRenderer.LastBodyBounds;
            texture!.uploadPixels(pixels, Ref<int>.In(0), Ref<int>.In(0));
            LabPerfProbe.Stop(LabPerfProbe.Section.Upload, mark);
            LabPerfProbe.Uploaded();
            (oam, shownOam) = (shownOam, oam); (vram, shownVram) = (shownVram, vram); (palette, shownPalette) = (shownPalette, palette);
            farBuffer.AsSpan(0, farCount).CopyTo(shownFar);
            (shownFarCount, shownDisplay, shownOrigin) = (farCount, display, (originX, originY));
        }
    }

    /// <summary>
    /// The host arrival cinematic animates the Beheaded's corpse being entered
    /// by its spirit, then flashes as the head ignites. Kirby has no corpse:
    /// he stays hidden through that sequence and appears in the closing flash,
    /// instead of standing while the slime crawls over him.
    /// </summary>
    private void UpdateArrival(HSprite host)
    {
        if (hero._level?.game?.hasCinematic() != true) { arriving = false; return; }
        string animation = host.groupName?.ToString() ?? "";
        if (animation == "rebirth") arriving = true;
        else if (animation is "headPopLoop" or "headPopEnd" ||
                 (animation == "headPopStart" && host.frame >= ArrivalRevealFrame)) arriving = false;
    }

    /// <summary>
    /// Host weapon frames are drawn for the Beheaded's tall body, so blades and
    /// shields cross Kirby's face; with the host glow they wash him out. Keep
    /// the original body in front, so held equipment shows around Kirby instead.
    /// </summary>
    private void KeepBodyInFront()
    {
        if (equipment?.parent is not { } parent || bitmap?.parent?.HashlinkPointer != parent.HashlinkPointer) return;
        int body = parent.getChildIndex(bitmap), held = parent.getChildIndex(equipment);
        if (body > held) return;
        if (parent is Layers layers) layers.over(bitmap);
        else parent.addChildAt(bitmap, held);
    }

    private void RenderEquipment(HSprite host, bool baseForm)
    {
        string animation = host.groupName?.ToString() ?? "";
        // Evolved Kirby retains the equipped shield's existing block/parry
        // animation. The controller independently gates actual weapon use.
        bool shield = animation.StartsWith("block", StringComparison.OrdinalIgnoreCase) ||
                      animation.StartsWith("parry", StringComparison.OrdinalIgnoreCase);
        bool first = equipment is null;
        if (first) LabActivationTrace.Mark("Equipment atlas get begin", gc: true);
        SpriteLib currentLibrary = EquipmentAtlas();
        if (first) LabActivationTrace.Mark("Equipment atlas get returned", gc: true);
        if (equipment is null || equipmentLibrary?.HashlinkPointer != currentLibrary.HashlinkPointer)
        {
            equipment?.remove();
            equipmentLayer = -1;
            equipmentLibrary = currentLibrary;
            equipmentNormal = null;
            equipmentLibrary.preventAutoDispose();
            equipmentLibrary.ensureTexturesAllocated();
            if (first) LabActivationTrace.Mark("Equipment textures allocated", gc: true);
            equipment = new HSprite(equipmentLibrary, host.groupName, Ref<int>.In(host.frame), null!);
            equipment.pivot.isUndefined = false;
            equipment.pivot.usingFactor = true;
            equipment.pivot.centerFactorX = equipment.pivot.centerFactorY = .5;
            equipment.setFrame(host.frame);
            if (first) LabActivationTrace.Mark("Equipment sprite created");
        }
        AttachSibling(equipment, host, ref equipmentLayer, true);
        equipment.set(equipmentLibrary!, host.groupName, Ref<int>.In(host.frame), Ref<bool>.In(false));
        ConfigureEquipmentShaders(host);
        if (first) LabActivationTrace.Mark("Equipment shaders configured");
        // The host's palette and lighting shaders are rendered in its opaque
        // actor pass. A new HSprite defaults to Alpha, which selects a different
        // LightedLayers pass and does not reproduce the host material.
        equipment.blendMode = host.blendMode;
        equipment.color = host.color;
        equipment.x = host.x;
        equipmentBaseY = host.y;
        equipment.y = host.y + drawOffsetY;
        equipment.scaleX = host.scaleX;
        equipment.scaleY = host.scaleY;
        equipment.rotation = host.rotation;
        equipment.alpha = host.alpha;
        equipment.visible = hero.visible && host.visible && (baseForm || shield) && !arriving;
        equipment.posChanged = true;
    }

    /// <summary>
    /// The outfit's equipment atlas, through the host asset manager. The manager
    /// resets each atlas's "used" flag before a biome transition and destroys
    /// the unused libraries in flushCache; preventAutoDispose only protects GPU
    /// eviction, not that lifecycle. Reacquiring through get marks the live
    /// atlas used and replaces a library whose pages were destroyed during the
    /// transition. A Kirby run also gets it just before each level's flushCache
    /// (#193), so the first level loads it during the load and a level after a
    /// death keeps it, instead of the arrival's first frame loading it (0.37 s)
    /// or making its textures again (0.34 s).
    /// </summary>
    internal static SpriteLib EquipmentAtlas() =>
        dc.Assets.Class.lib.get("atlas/kirbySuperStarUltraWeapons.atlas".AsHaxeString());

    private void ConfigureEquipmentShaders(HSprite host)
    {
        // Palette/glow uniforms are intentionally shared with the equipped host
        // items. Normal UVs, however, belong to this separately packed atlas.
        // Sharing the host NormalMap also lets host updates overwrite its texture.
        Texture normalTexture = equipmentLibrary!.getNormalMapFromSprite(equipment!);
        equipmentNormal ??= new dc.shader.NormalMap(normalTexture);
        equipmentNormal.texture__ = normalTexture;
        equipmentNormal.scale__.x = host.scaleX < 0 ? -1 : 1;
        // Host status changes can insert/remove a shader below the existing list
        // head. Compare every shader identity rather than only that head pointer.
        var copied = equipment!.shaders;
        bool matches = copied?.s.HashlinkPointer == equipmentNormal.HashlinkPointer;
        copied = copied?.next;
        for (var entry = host.shaders; entry is not null; entry = entry.next)
        {
            if (entry.s is dc.shader.NormalMap) continue;
            if (copied?.s.HashlinkPointer != entry.s.HashlinkPointer) matches = false;
            copied = copied?.next;
        }
        if (matches && copied is null) return;
        var shaders = new List<dc.hxsl.Shader>();
        for (var entry = host.shaders; entry is not null; entry = entry.next)
            if (entry.s is not dc.shader.NormalMap) shaders.Add(entry.s);
        dc.hxsl.ShaderList? tail = null;
        for (int i = shaders.Count - 1; i >= 0; i--)
            tail = new dc.hxsl.ShaderList(shaders[i], tail!);
        equipment!.shaders = new dc.hxsl.ShaderList(equipmentNormal, tail!);
    }

    private void EnsureTexture()
    {
        if (bitmap is not null) return;
        pixels = Pixels.Class.alloc(CanvasWidth, CanvasHeight, new PixelFormat.RGBA());
        texture = Texture.Class.fromPixels(pixels, null!);
        texture.name = "kirby-original-nds-obj".AsHaxeString();
        texture.set_filter(new dc.h3d.mat.Filter.Nearest());
        texture.preventAutoDispose();
        Tile tile = new(texture, 0, 0, CanvasWidth, CanvasHeight,
            Ref<int>.In(-NdsObjRenderer.CenterX), Ref<int>.In(-NdsObjRenderer.CenterY));
        bitmap = new Bitmap(tile, null!) { smooth = false, scaleX = 1, scaleY = 1 };
        AttachSibling(bitmap, hero.spr, ref bitmapLayer, false);
    }

    /// <summary>
    /// Kirby's body and equipment are drawn at the end of the host's
    /// <c>DP_FOREGROUND</c> layer, not beside the hero sprite. The level's solid
    /// terrain art is a tile group in that layer, drawn over the hero layer, so
    /// any part of Kirby overlapping it (his sprite is larger than the cells he
    /// occupies, and he floats along walls and ceilings) was hidden: he looked
    /// like a sliver at an edge and vanished inside it (#116). Appended last,
    /// he draws over the terrain art but below the layer's fog and effects.
    /// </summary>
    private void AttachSibling(dc.h2d.Object child, HSprite host, ref int priorLayer, bool beneathBody)
    {
        dc.h2d.Object parent = host.parent;
        if (parent is Layers layers)
        {
            int layer = System.Math.Min(dc.Const.Class.DP_FOREGROUND, layers.layerCount - 1);
            if (child.parent?.HashlinkPointer != parent.HashlinkPointer || priorLayer != layer)
                layers.addChildAt(child, layer);
            priorLayer = layer;
        }
        else if (child.parent?.HashlinkPointer != parent.HashlinkPointer)
        {
            int hostIndex = parent.getChildIndex(host);
            parent.addChildAt(child, beneathBody && bitmap?.parent?.HashlinkPointer == parent.HashlinkPointer
                ? parent.getChildIndex(bitmap) : hostIndex + 1);
            priorLayer = -1;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        backgroundEffects.Dispose();
        bitmap?.remove();
        equipment?.remove();
        texture?.dispose();
        pixels?.dispose();
        bitmap = null; texture = null; pixels = null; equipment = null; equipmentLibrary = null;
        equipmentNormal = null;
    }

    [DllImport("kssu_bridge", EntryPoint = "kssu_read_gpu", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeReadGpu(nint handle, uint address, byte* destination, nuint length);
}
