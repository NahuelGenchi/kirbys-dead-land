using System.Buffers.Binary;

namespace Kirby.Runtime;

/// <summary>Pure original DS OBJ decoder shared by the host renderer and pixel verification.</summary>
public static class NdsObjRenderer
{
    public const int Canvas = 512;
    private const int Center = Canvas / 2;
    /// <summary>OAM x values from here to 511 are left of the screen (x - 512).</summary>
    public const int FarRightLimit = 384;
    /// <summary>Pixels drawn from palette bank 0 (Kirby's body) by the last call.</summary>
    [ThreadStatic] public static int LastBodyPixels;
    /// <summary>Canvas bounds of those pixels relative to the canvas centre (empty when none were drawn).</summary>
    [ThreadStatic] public static (int Left, int Top, int Right, int Bottom) LastBodyBounds;
    public static (int Rendered, int Unsupported) Render(uint display, byte[] oam, byte[] vram,
        byte[] palette, byte[] rgba, int sourceX, int sourceY, int lastPaletteBank = 7)
    {
        int bodyPixels = 0;
        int bodyLeft = int.MaxValue, bodyTop = int.MaxValue, bodyRight = int.MinValue, bodyBottom = int.MinValue;
        if (oam.Length != 1024 || palette.Length != 512 || vram.Length != 256 * 1024 || rgba.Length != Canvas * Canvas * 4)
            throw new ArgumentException("Unexpected DS graphics buffer dimensions.");
        Array.Clear(rgba);
        int rendered = 0, unsupported = 0;
        bool mapping1D = (display & 0x10) != 0;
        int tileUnit = 32 << (int)((display >> 20) & 3);
        // Higher numerical priority/OAM index draws behind lower values.
        for (int priority = 3; priority >= 0; priority--)
        for (int index = 127; index >= 0; index--)
        {
            ushort a0 = Word(oam, index * 8), a1 = Word(oam, index * 8 + 2), a2 = Word(oam, index * 8 + 4);
            if (((a2 >> 10) & 3) != priority || (a2 >> 12) > lastPaletteBank) continue;
            bool affine = (a0 & 0x100) != 0;
            if (!affine && (a0 & 0x200) != 0) continue;
            int shape = a0 >> 14, size = a1 >> 14;
            if (shape == 3) continue;
            int width = Width[shape, size], height = Height[shape, size];
            int boxWidth = affine && (a0 & 0x200) != 0 ? width * 2 : width;
            int boxHeight = affine && (a0 & 0x200) != 0 ? height * 2 : height;
            // The bridge widens the original OBJ cull to screen x -127..380
            // (widen_obj_cull), so far shots reach OAM; 384..511 are the left side.
            int x = a1 & 511, y = a0 & 255;
            if (x >= FarRightLimit) x -= 512;
            if (y >= 192) y -= 256;
            if (y + boxHeight <= 0 || y >= 192) continue;
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
                int cx = Center + x + dx - sourceX, cy = Center + y + dy - sourceY;
                if ((uint)cx >= Canvas || (uint)cy >= Canvas) continue;
                ushort color = Word(palette, paletteBase + colorIndex * 2);
                if (paletteBase == 0)
                {
                    bodyPixels++;
                    bodyLeft = Math.Min(bodyLeft, cx); bodyRight = Math.Max(bodyRight, cx);
                    bodyTop = Math.Min(bodyTop, cy); bodyBottom = Math.Max(bodyBottom, cy);
                }
                int target = (cy * Canvas + cx) * 4;
                rgba[target] = Expand5(color & 31);
                rgba[target + 1] = Expand5((color >> 5) & 31);
                rgba[target + 2] = Expand5((color >> 10) & 31);
                rgba[target + 3] = 255;
            }
            rendered++;
        }
        LastBodyPixels = bodyPixels;
        LastBodyBounds = bodyPixels == 0 ? default
            : (bodyLeft - Center, bodyTop - Center, bodyRight - Center, bodyBottom - Center);
        return (rendered, unsupported);
    }
    private static ushort Word(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));
    private static readonly int[,] Width = { { 8, 16, 32, 64 }, { 16, 32, 32, 64 }, { 8, 8, 16, 32 } };
    private static readonly int[,] Height = { { 8, 16, 32, 64 }, { 8, 8, 16, 32 }, { 16, 32, 32, 64 } };
}
