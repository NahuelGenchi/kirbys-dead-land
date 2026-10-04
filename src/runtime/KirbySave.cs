using System.IO.Compression;
using System.Text.Json;
using Newtonsoft.Json;

namespace Kirby.Runtime;

/// <summary>DCCM's per-slot sidecar owns this data alongside the host save.</summary>
public sealed class KirbySave
{
    private string? snapshot;
    [JsonIgnore] public Func<string?>? Capture { get; set; }
    // DCCM serializes on the game thread immediately before saving its sidecar.
    // Capture here preserves the exact CPU/action state at that save boundary.
    public string? Snapshot { get => Capture is null ? snapshot : Capture(); set => snapshot = value; }
}

internal sealed class KirbySnapshot
{
    public int Schema { get; set; } = 1;
    public string RomSha256 { get; set; } = NdsRuntime.RomSha256;
    public string? Ability { get; set; }
    public string? Shield { get; set; }
    public bool PendingCopy { get; set; }
    public bool KirbyActive { get; set; } = true;
    public EnemyBinding[] Enemies { get; set; } = [];
    public HostBodySnapshot? HostBody { get; set; }
    public byte[] Machine { get; set; } = [];

    public string Encode()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
            System.Text.Json.JsonSerializer.Serialize(gzip, this);
        return Convert.ToBase64String(output.ToArray());
    }
    public static KirbySnapshot Decode(string text)
    {
        if (text.Length > 64 * 1024 * 1024) throw new InvalidDataException("Kirby save exceeds the supported size.");
        using var source = new MemoryStream(Convert.FromBase64String(text));
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var bounded = new MemoryStream();
        byte[] buffer = new byte[65536];
        int read;
        while ((read = gzip.Read(buffer)) > 0)
        {
            if (bounded.Length + read > 64 * 1024 * 1024) throw new InvalidDataException("Kirby save expands beyond the supported size.");
            bounded.Write(buffer, 0, read);
        }
        var value = System.Text.Json.JsonSerializer.Deserialize<KirbySnapshot>(bounded.ToArray())
            ?? throw new InvalidDataException("Kirby save is empty.");
        if (value.Schema != 1 || value.RomSha256 != NdsRuntime.RomSha256 || value.Machine.Length < 16 ||
            !value.Machine.AsSpan(0, 4).SequenceEqual("MELN"u8))
            throw new InvalidDataException("Kirby save does not match the original-ROM runtime.");
        return value;
    }
}

internal sealed record HostBodySnapshot(double Radius, double Height, double BodyHeight, int MaxLife);
