using System.Buffers.Binary;

namespace Kirby.Runtime;

/// <summary>An integer RGB5 transform proved against the original captured palette.</summary>
public readonly record struct NdsBackgroundEffect(int Scale16, int Red5, int Green5, int Blue5, bool Supported)
{
    public bool Active => Supported && (Scale16 != 16 || Red5 != 0 || Green5 != 0 || Blue5 != 0);
    public static NdsBackgroundEffect Identity => new(16, 0, 0, 0, true);
    public static NdsBackgroundEffect Unsupported => new(16, 0, 0, 0, false);
    public int Apply5(int value, int channel) => System.Math.Clamp(value * Scale16 / 16 +
        (channel == 0 ? Red5 : channel == 1 ? Green5 : Blue5), 0, 31);
}

/// <summary>
/// Reads effects from the original scanout, without choosing an ability, timer,
/// shape or replacement color. Nonuniform palettes and unsupported blends are
/// reported explicitly instead of being replaced by a guessed screen flash.
/// </summary>
public static class NdsBackgroundEffectDecoder
{
    public const int PixelCount = 256 * 192;

    /// <summary>
    /// The effect of a frame the bridge did not draw in full (<see cref="NdsRuntime.MainFrame"/>),
    /// or null for a drawn frame, whose capture is to be read and decoded. The
    /// bridge leaves a scanline undrawn only where <see cref="Decode"/> finds the
    /// seed palette and no colour effect: a frame of such lines is the identity,
    /// and one with other lines among them has a nonuniform palette or effect.
    /// </summary>
    public static NdsBackgroundEffect? Undrawn(int mainFrame) => mainFrame switch
    {
        1 => NdsBackgroundEffect.Identity,
        2 => NdsBackgroundEffect.Unsupported,
        _ => null,
    };

    public static NdsBackgroundEffect Decode(ReadOnlySpan<byte> seedPalette,
        ReadOnlySpan<uint> paletteLines, ReadOnlySpan<uint> top, ReadOnlySpan<uint> metadata)
    {
        if (seedPalette.Length != 512 || paletteLines.Length != PixelCount ||
            top.Length != PixelCount || metadata.Length != PixelCount)
            throw new ArgumentException("Original scanout capture dimensions are invalid.");
        ReadOnlySpan<uint> palette = paletteLines[..256];
        for (int row = 0; row < 192; row++)
        {
            if (!paletteLines.Slice(row * 256, 256).SequenceEqual(palette))
                return NdsBackgroundEffect.Unsupported;
            for (int x = 0; x < 256; x++)
            {
                uint register = metadata[row * 256 + x];
                if ((register & 0x80000000) == 0 || (register & 0x00FFFF00) != 0)
                    return NdsBackgroundEffect.Unsupported;
            }
        }

        for (int scale = 16; scale >= 0; scale--)
        {
            int first = BinaryPrimitives.ReadUInt16LittleEndian(seedPalette);
            int r = (int)(palette[0] & 31) - (first & 31) * scale / 16;
            int g = (int)((palette[0] >> 5) & 31) - ((first >> 5) & 31) * scale / 16;
            int b = (int)((palette[0] >> 10) & 31) - ((first >> 10) & 31) * scale / 16;
            var candidate = new NdsBackgroundEffect(scale, r, g, b, true);
            bool matches = true;
            for (int i = 0; i < 256 && matches; i++)
            {
                int before = BinaryPrimitives.ReadUInt16LittleEndian(seedPalette[(i * 2)..]);
                for (int c = 0; c < 3; c++)
                    matches &= candidate.Apply5((before >> (5 * c)) & 31, c) == (int)((palette[i] >> (5 * c)) & 31);
            }
            if (matches) return candidate;
        }

        // Crash first draws its growing circle with OBJ. Its later full flash
        // changes extended BG palettes, so standard-palette fitting alone is
        // insufficient. Require every visible actual BG pixel to agree with the
        // changed backdrop color before accepting a constant transform.
        int backdrop = (int)palette[0];
        if (backdrop == BinaryPrimitives.ReadUInt16LittleEndian(seedPalette))
            return NdsBackgroundEffect.Unsupported;
        uint color6 = (uint)(((backdrop & 31) << 1) | (((backdrop >> 5) & 31) << 9) | (((backdrop >> 10) & 31) << 17));
        int backgroundPixels = 0;
        foreach (uint pixel in top)
        {
            uint layer = pixel >> 24;
            if (layer is 1 or 2 or 4 or 8 or 32)
            {
                backgroundPixels++;
                if ((pixel & 0x00FFFFFF) != color6) return NdsBackgroundEffect.Unsupported;
            }
        }
        return backgroundPixels == 0 ? NdsBackgroundEffect.Unsupported :
            new(0, backdrop & 31, (backdrop >> 5) & 31, (backdrop >> 10) & 31, true);
    }
}
