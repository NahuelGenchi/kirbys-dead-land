using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GameRes.Core.Pak;

// Independent parser oracle: read directly using the released DCCM reader.
// Never unpack files; every payload is consumed synchronously before disposal.
if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: Kirby.Pak.Verify <build-manifest.json> [res.pak]");
    return 2;
}

try
{
    string manifestPath = Path.GetFullPath(args[0]);
    string pakPath = args.Length == 2 ? Path.GetFullPath(args[1]) :
        Path.Combine(Path.GetDirectoryName(manifestPath)!, "res.pak");
    using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    string expectedPakHash = RequireHash(manifest.RootElement.GetProperty("pak_sha256").GetString(), "pak_sha256");
    var expected = manifest.RootElement.GetProperty("asset_sha256").EnumerateObject()
        .ToDictionary(p => p.Name, p => RequireHash(p.Value.GetString(), p.Name), StringComparer.Ordinal);
    if (expected.Count == 0)
        throw new InvalidDataException("Manifest contains no assets.");

    using FileStream stream = File.Open(pakPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    string actualPakHash = Convert.ToHexStringLower(SHA256.HashData(stream));
    if (actualPakHash != expectedPakHash)
        throw new InvalidDataException($"Archive SHA-256 mismatch: expected {expectedPakHash}, got {actualPakHash}.");
    stream.Position = 0;
    // Disable memory mapping to use the reader's synchronous ReadExactly path.
    using var pak = new PakFile(stream, leaveOpen: true, allowUseMemporyMappedFile: false);
    var observed = new HashSet<string>(StringComparer.Ordinal);
    var errors = new List<string>();
    int zeroLength = 0;
    long bytes = 0;
    foreach ((string path, PakFile.FileEntry file) in Enumerate(pak.Root, ""))
    {
        if (!observed.Add(path))
        {
            errors.Add($"Duplicate archive path: {path}");
            continue;
        }
        if (!expected.TryGetValue(path, out string? expectedHash))
        {
            errors.Add($"Unexpected archive entry: {path}");
            continue;
        }
        ReadOnlyMemory<byte> data = file.Data.Data;
        bytes += data.Length;
        if (data.IsEmpty)
            zeroLength++;
        string hash = Convert.ToHexStringLower(SHA256.HashData(data.Span));
        if (hash != expectedHash)
            errors.Add($"Payload SHA-256 mismatch: {path}, expected {expectedHash}, got {hash}.");
        int? headerChecksum = file.Checksum;
        file.CalcChecksum();
        if (headerChecksum != file.Checksum)
            errors.Add($"Header Adler-32 mismatch: {path}.");
    }
    foreach (string missing in expected.Keys.Except(observed, StringComparer.Ordinal))
        errors.Add($"Missing archive entry: {missing}");
    if (errors.Count != 0)
        throw new InvalidDataException(string.Join(Environment.NewLine, errors));

    Assembly reader = typeof(PakFile).Assembly;
    using FileStream readerStream = File.OpenRead(reader.Location);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        status = "PASS",
        manifest = manifestPath,
        archive = pakPath,
        archive_sha256 = actualPakHash,
        reader = reader.GetName().Name,
        reader_version = reader.GetName().Version?.ToString(),
        reader_sha256 = Convert.ToHexStringLower(SHA256.HashData(readerStream)),
        verified_assets = observed.Count,
        payload_bytes = bytes,
        zero_length_assets = zeroLength,
        exact_file_set = true,
        all_payload_sha256_match = true,
        all_header_adler32_match = true,
        extracted_files = 0,
        in_game_verification = false
    }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"FAIL: {error.GetType().Name}: {error.Message}");
    return 1;
}

static string RequireHash(string? value, string name)
{
    if (value is null || value.Length != 64 || !value.All(char.IsAsciiHexDigit))
        throw new InvalidDataException($"Invalid SHA-256 value for {name}.");
    return value.ToLowerInvariant();
}

static IEnumerable<(string Path, PakFile.FileEntry File)> Enumerate(PakFile.DirectoryEntry directory, string prefix)
{
    foreach (PakFile.Entry entry in directory.Entries)
    {
        if (string.IsNullOrEmpty(entry.Name) || entry.Name is "." or ".." ||
            entry.Name.Contains('/') || entry.Name.Contains('\\'))
            throw new InvalidDataException($"Noncanonical archive entry name: {entry.Name}");
        string path = prefix + entry.Name;
        if (entry is PakFile.DirectoryEntry child)
        {
            foreach (var file in Enumerate(child, path + "/"))
                yield return file;
        }
        else if (entry is PakFile.FileEntry file)
            yield return (path, file);
        else
            throw new InvalidDataException($"Unsupported archive entry: {path}");
    }
}
