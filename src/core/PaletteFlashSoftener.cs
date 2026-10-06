using System.Buffers.Binary;

namespace Kirby.Core;

/// <summary>
/// Tones down the original game's palette flashes. After a hit the ROM
/// replaces Kirby's body palette banks on 3 of every 6 frames, first with an
/// orange set and then with a half-whitened set, which it also uses on 2 of
/// every 4 frames while guarding. On the DS screen this is a mild blink; on a
/// large monitor the whitened Kirby disappears against light scenery. A
/// palette is considered stable once it is unchanged for a few frames; while
/// a bank differs from its stable palette, the shown palette is blended
/// between the two by <see cref="Strength"/>. Lasting changes, such as a new
/// copy ability, become the stable palette after <see cref="StableFrames"/>
/// frames, so only flashes shorter than that are softened.
/// </summary>
public sealed class PaletteFlashSoftener
{
    public const int EntriesPerBank = 16;
    private readonly ushort[][] stable, pending;
    private readonly int[] pendingFrames;

    /// <param name="banks">Number of leading 16-colour banks to soften.</param>
    /// <param name="strength">Shown share of the flash palette: 1 keeps the original, 0 removes the flash.</param>
    /// <param name="stableFrames">Frames a changed palette must persist before it is adopted.</param>
    public PaletteFlashSoftener(int banks, double strength, int stableFrames = 4)
    {
        if (banks is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(banks));
        if (stableFrames < 1) throw new ArgumentOutOfRangeException(nameof(stableFrames));
        Strength = double.IsFinite(strength) ? Math.Clamp(strength, 0, 1) : 1;
        StableFrames = stableFrames;
        stable = new ushort[banks][];
        pending = new ushort[banks][];
        pendingFrames = new int[banks];
    }

    public double Strength { get; }
    public int StableFrames { get; }
    public int Banks => stable.Length;

    /// <summary>Forget the stable palettes, for example after the machine reloads a state.</summary>
    public void Reset()
    {
        Array.Clear(stable);
        Array.Clear(pending);
        Array.Clear(pendingFrames);
    }

    /// <summary>Rewrite the leading banks of a 512-byte RGB555 OBJ palette in place.</summary>
    public void Apply(Span<byte> palette)
    {
        if (palette.Length < Banks * EntriesPerBank * 2) throw new ArgumentException("Palette is too short.", nameof(palette));
        if (Strength >= 1) return;
        for (int bank = 0; bank < Banks; bank++)
        {
            Span<byte> bytes = palette.Slice(bank * EntriesPerBank * 2, EntriesPerBank * 2);
            var current = new ushort[EntriesPerBank];
            for (int i = 0; i < EntriesPerBank; i++) current[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i * 2)..]);
            ushort[]? known = stable[bank];
            if (known is null || current.AsSpan().SequenceEqual(known))
            {
                stable[bank] ??= current;
                pending[bank] = null!;
                pendingFrames[bank] = 0;
                continue;
            }
            if (pending[bank] is { } candidate && current.AsSpan().SequenceEqual(candidate)) pendingFrames[bank]++;
            else { pending[bank] = current; pendingFrames[bank] = 1; }
            if (pendingFrames[bank] >= StableFrames)
            {
                stable[bank] = current;
                pending[bank] = null!;
                pendingFrames[bank] = 0;
                continue;
            }
            for (int i = 0; i < EntriesPerBank; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes[(i * 2)..], Blend(known[i], current[i], Strength));
        }
    }

    /// <summary>Blend two RGB555 colours per channel; index 0 keeps the current entry (it is transparent).</summary>
    public static ushort Blend(ushort from, ushort to, double share)
    {
        int result = 0;
        for (int shift = 0; shift <= 10; shift += 5)
        {
            int a = (from >> shift) & 31, b = (to >> shift) & 31;
            result |= (int)Math.Round(a + (b - a) * share) << shift;
        }
        return (ushort)result;
    }
}
