using System.Buffers.Binary;

namespace Kirby.Core;

/// <summary>
/// The original bottom-screen copy-ability icon: a 32×32, 4bpp, non-affine
/// sub-engine OBJ drawn at screen (4, 4). Colour 0 is transparent. The icon is
/// decoded from the running machine's sub OAM, OBJ VRAM and OBJ palette, so it
/// always matches what the original game shows.
/// </summary>
public static class AbilityIcon
{
    public const int Size = 32;
    public const int ScreenX = 4, ScreenY = 4;
    /// <summary>Bytes of 4bpp tile data in one 1D-mapped 32×32 OBJ.</summary>
    public const int TileBytes = Size * Size / 2;

    public readonly record struct Sprite(int TileByte, int Palette, bool FlipX, bool FlipY);

    /// <summary>Find the icon OBJ in sub-engine OAM; null when it is absent or uses an unsupported format.</summary>
    public static Sprite? Find(uint display, ReadOnlySpan<byte> oam)
    {
        if (oam.Length != 1024) throw new ArgumentException("Sub OAM is 1024 bytes.", nameof(oam));
        // 2D OBJ mapping lays tiles out in a 32-tile-wide sheet; the original HUD uses 1D.
        if ((display & 0x10) == 0) return null;
        int tileUnit = 32 << (int)((display >> 20) & 3);
        for (int index = 0; index < 128; index++)
        {
            ushort a0 = Word(oam, index * 8), a1 = Word(oam, index * 8 + 2), a2 = Word(oam, index * 8 + 4);
            bool affine = (a0 & 0x100) != 0, hidden = !affine && (a0 & 0x200) != 0;
            if (hidden || affine || (a0 & 0x2000) != 0 || ((a0 >> 10) & 3) >= 2) continue;
            if (a0 >> 14 != 0 || a1 >> 14 != 2) continue; // Square, 32×32.
            if ((a0 & 255) != ScreenY || (a1 & 511) != ScreenX) continue;
            return new Sprite((a2 & 1023) * tileUnit, a2 >> 12, (a1 & 0x1000) != 0, (a1 & 0x2000) != 0);
        }
        return null;
    }

    /// <summary>Decode 1D-mapped 4bpp tiles and one 16-colour BGR555 palette bank to 32×32 RGBA.</summary>
    public static void Decode(Sprite sprite, ReadOnlySpan<byte> tiles, ReadOnlySpan<byte> palette, Span<byte> rgba)
    {
        if (tiles.Length != TileBytes || palette.Length != 32 || rgba.Length != Size * Size * 4)
            throw new ArgumentException("Unexpected icon buffer dimensions.");
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            int tx = sprite.FlipX ? Size - 1 - x : x, ty = sprite.FlipY ? Size - 1 - y : y;
            int offset = ((ty / 8) * (Size / 8) + tx / 8) * 32 + (ty & 7) * 4 + (tx & 7) / 2;
            int colorIndex = (tiles[offset] >> ((tx & 1) * 4)) & 15;
            int target = (y * Size + x) * 4;
            if (colorIndex == 0)
            {
                rgba.Slice(target, 4).Clear();
                continue;
            }
            ushort color = Word(palette, colorIndex * 2);
            rgba[target] = Expand5(color & 31);
            rgba[target + 1] = Expand5((color >> 5) & 31);
            rgba[target + 2] = Expand5((color >> 10) & 31);
            rgba[target + 3] = 255;
        }
    }

    private static ushort Word(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));
}
