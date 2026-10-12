using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Kirby.Runtime;

/// <summary>
/// An OBJ piece the original OAM builder culled, as the bridge reports it
/// (<c>kssu_far_objs</c>): full screen position, the original attributes, and
/// how many on-screen OAM entries were written before it.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FarObject
{
    public short X, Y;
    public ushort Attr0, Attr1, Attr2, Order;
}

/// <summary>Pure original DS OBJ decoder shared by the host renderer and pixel verification.</summary>
public static class NdsObjRenderer
{
    /// <summary>The canvas spans 512 px either side of the source origin and 256 px above and below.</summary>
    public const int CanvasWidth = 1024, CanvasHeight = 512;
    public const int CenterX = CanvasWidth / 2, CenterY = CanvasHeight / 2;
    /// <summary>Pixels drawn from palette bank 0 (Kirby's body) by the last call.</summary>
    [ThreadStatic] public static int LastBodyPixels;
    /// <summary>Canvas bounds of those pixels relative to the canvas centre (empty when none were drawn).</summary>
    [ThreadStatic] public static (int Left, int Top, int Right, int Bottom) LastBodyBounds;
    /// <summary>A canvas rectangle; right and bottom are exclusive, and the default is empty.</summary>
    public readonly record struct Area(int Left, int Top, int Right, int Bottom)
    {
        public static Area Canvas => new(0, 0, CanvasWidth, CanvasHeight);
        public bool IsEmpty => Right <= Left || Bottom <= Top;
        public Area Include(Area other) => other.IsEmpty ? this : IsEmpty ? other :
            new(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }
    /// <summary>
    /// Draw OAM plus the pieces the original builder culled. The builder keeps
    /// pieces at screen x -63..256 and y -63..192, so OAM is read over that
    /// range rather than the DS screen's; its unused entries are y 0xF8, attr1 0.
    /// </summary>
    public static (int Rendered, int Unsupported) Render(uint display, byte[] oam, byte[] vram,
        byte[] palette, byte[] rgba, int sourceX, int sourceY, int lastPaletteBank = 7, ReadOnlySpan<FarObject> far = default)
    {
        Area drawn = Area.Canvas;
        return Render(display, oam, vram, palette, rgba, ref drawn, sourceX, sourceY, lastPaletteBank, far);
    }
    /// <summary>
    /// The same drawing onto a canvas kept between calls. <paramref name="drawn"/>
    /// holds the only part of it that may not be transparent: that part is
    /// cleared, and the part this call's pieces cover is returned in it, so a
    /// small sprite costs its own pixels instead of the whole canvas.
    /// </summary>
    public static (int Rendered, int Unsupported) Render(uint display, byte[] oam, byte[] vram,
        byte[] palette, Span<byte> rgba, ref Area drawn, int sourceX, int sourceY, int lastPaletteBank = 7, ReadOnlySpan<FarObject> far = default)
    {
        int bodyPixels = 0;
        int bodyLeft = int.MaxValue, bodyTop = int.MaxValue, bodyRight = int.MinValue, bodyBottom = int.MinValue;
        if (oam.Length != 1024 || palette.Length != 512 || vram.Length != 256 * 1024 || rgba.Length != CanvasWidth * CanvasHeight * 4)
            throw new ArgumentException("Unexpected DS graphics buffer dimensions.");
        if (drawn.Left < 0 || drawn.Top < 0 || drawn.Right > CanvasWidth || drawn.Bottom > CanvasHeight)
            throw new ArgumentOutOfRangeException(nameof(drawn));
        if (!drawn.IsEmpty)
            for (int row = drawn.Top; row < drawn.Bottom; row++)
                rgba.Slice((row * CanvasWidth + drawn.Left) * 4, (drawn.Right - drawn.Left) * 4).Clear();
        drawn = default;
        int rendered = 0, unsupported = 0;
        bool mapping1D = (display & 0x10) != 0;
        int tileUnit = 32 << (int)((display >> 20) & 3);
        // Original OAM order: on-screen entry k at 2k+1, a far piece just before
        // the entry its Order names; later pieces draw behind earlier ones.
        int farCount = far.Length;
        // The bridge keeps at most 256 culled pieces a frame.
        Span<(int Key, ushort A0, ushort A1, ushort A2, int X, int Y)> pieces = farCount <= 256
            ? stackalloc (int Key, ushort A0, ushort A1, ushort A2, int X, int Y)[128 + farCount]
            : new (int Key, ushort A0, ushort A1, ushort A2, int X, int Y)[128 + farCount];
        for (int index = 0; index < 128; index++)
        {
            ushort a0 = Word(oam, index * 8), a1 = Word(oam, index * 8 + 2), a2 = Word(oam, index * 8 + 4);
            int x = a1 & 511, y = a0 & 255;
            pieces[index] = (((2 * index + 1) << 10) | 1023, a0, a1, a2, x > 256 ? x - 512 : x, y > 192 ? y - 256 : y);
        }
        for (int i = 0; i < farCount; i++)
        {
            FarObject piece = far[i];
            pieces[128 + i] = ((2 * piece.Order << 10) | i, piece.Attr0, piece.Attr1, piece.Attr2, piece.X, piece.Y);
        }
        // Every key is distinct, so the order does not depend on the sort used.
        pieces.Sort(static (a, b) => b.Key.CompareTo(a.Key));
        for (int priority = 3; priority >= 0; priority--)
        foreach (var (key, a0, a1, a2, x, y) in pieces)
        {
            if (((a2 >> 10) & 3) != priority || (a2 >> 12) > lastPaletteBank) continue;
            if (a0 == 0xF8 && a1 == 0 && (key & 1023) == 1023) continue;
            bool affine = (a0 & 0x100) != 0;
            if (!affine && (a0 & 0x200) != 0) continue;
            int shape = a0 >> 14, size = a1 >> 14;
            if (shape == 3) continue;
            int width = Width[shape, size], height = Height[shape, size];
            int boxWidth = affine && (a0 & 0x200) != 0 ? width * 2 : width;
            int boxHeight = affine && (a0 & 0x200) != 0 ? height * 2 : height;
            int mode = (a0 >> 10) & 3;
            if ((a0 & 0x2000) != 0 || mode >= 2)
            {
                unsupported++;
                continue; // 8bpp/bitmap/object-window formats need distinct palette rules.
            }
            // Keep the graphic visible, but expose a fidelity limitation rather
            // than silently reporting semitransparent OBJ blending as verified.
            if (mode == 1) unsupported++;
            int matrix = ((a1 >> 9) & 31) * 32;
            int pa = affine ? (short)Word(oam, matrix + 6) : 256;
            int pb = affine ? (short)Word(oam, matrix + 14) : 0;
            int pc = affine ? (short)Word(oam, matrix + 22) : 0;
            int pd = affine ? (short)Word(oam, matrix + 30) : 256;
            int baseByte = (a2 & 1023) * (mapping1D ? tileUnit : 32);
            int paletteBase = (a2 >> 12) * 32;
            int boxLeft = CenterX + x - sourceX, boxTop = CenterY + y - sourceY;
            drawn = drawn.Include(new(Math.Max(0, boxLeft), Math.Max(0, boxTop),
                Math.Min(CanvasWidth, boxLeft + boxWidth), Math.Min(CanvasHeight, boxTop + boxHeight)));
            for (int dy = 0; dy < boxHeight; dy++)
            for (int dx = 0; dx < boxWidth; dx++)
            {
                int tx, ty;
                if (affine)
                {
                    int rx = dx - boxWidth / 2, ry = dy - boxHeight / 2;
                    tx = ((pa * rx + pb * ry) >> 8) + width / 2;
                    ty = ((pc * rx + pd * ry) >> 8) + height / 2;
                }
                else
                {
                    tx = (a1 & 0x1000) != 0 ? width - dx - 1 : dx;
                    ty = (a1 & 0x2000) != 0 ? height - dy - 1 : dy;
                }
                if ((uint)tx >= width || (uint)ty >= height) continue;
                int tileRow = mapping1D ? width / 8 : 32;
                int byteOffset = baseByte + ((ty / 8) * tileRow + tx / 8) * 32 + (ty & 7) * 4 + (tx & 7) / 2;
                if ((uint)byteOffset >= vram.Length) continue;
                int colorIndex = (vram[byteOffset] >> ((tx & 1) * 4)) & 15;
                if (colorIndex == 0) continue;
                int cx = CenterX + x + dx - sourceX, cy = CenterY + y + dy - sourceY;
                if ((uint)cx >= CanvasWidth || (uint)cy >= CanvasHeight) continue;
                ushort color = Word(palette, paletteBase + colorIndex * 2);
                if (paletteBase == 0)
                {
                    bodyPixels++;
                    bodyLeft = Math.Min(bodyLeft, cx); bodyRight = Math.Max(bodyRight, cx);
                    bodyTop = Math.Min(bodyTop, cy); bodyBottom = Math.Max(bodyBottom, cy);
                }
                int target = (cy * CanvasWidth + cx) * 4;
                rgba[target] = Expand5(color & 31);
                rgba[target + 1] = Expand5((color >> 5) & 31);
                rgba[target + 2] = Expand5((color >> 10) & 31);
                rgba[target + 3] = 255;
            }
            rendered++;
        }
        LastBodyPixels = bodyPixels;
        LastBodyBounds = bodyPixels == 0 ? default
            : (bodyLeft - CenterX, bodyTop - CenterY, bodyRight - CenterX, bodyBottom - CenterY);
        return (rendered, unsupported);
    }
    private static ushort Word(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));
    private static readonly int[,] Width = { { 8, 16, 32, 64 }, { 16, 32, 32, 64 }, { 8, 8, 16, 32 } };
    private static readonly int[,] Height = { { 8, 16, 32, 64 }, { 8, 8, 16, 32 }, { 16, 32, 32, 64 } };
}
