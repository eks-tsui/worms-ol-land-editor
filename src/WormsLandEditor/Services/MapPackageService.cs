using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using WormsLandEditor.Localization;

namespace WormsLandEditor.Services;

internal sealed record MapPackageInfo(int SourceMapNumber, int TargetMapNumber, string WaterResourcePath, int Width, int Height);
internal sealed record MapPackageImportResult(bool Imported, string? CleanupWarning);

internal static class MapPackageService
{
    internal const int MaximumLandBytes = 32 * 1024 * 1024;
    internal const int MaximumDirectoryBytes = 64 * 1024 * 1024;
    internal const int MaximumManifestBytes = 16 * 1024;
    private const string Format = "worms-ol-map-package";

    public static void Export(string dataDirectory, int mapNumber, string outputZip)
    {
        ValidateMapNumber(mapNumber);
        string root = ValidateDataDirectory(dataDirectory);
        string landPath = LandPath(root, mapNumber), resourcePath = ResourcePath(root, mapNumber);
        string output = Path.GetFullPath(outputZip);
        MapPackagePaths.CheckFile(output);
        if (!Path.GetExtension(output).Equals(".zip", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(output).Contains(':'))
            throw new InvalidDataException(L10n.Package("ExportExtensionRequired"));
        if (output.Equals(landPath, StringComparison.OrdinalIgnoreCase) || output.Equals(resourcePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L10n.Package("ExportSourceConflict"));
        MapPackagePaths.CheckFile(landPath);
        MapPackagePaths.CheckFile(resourcePath);
        byte[] land = ReadFile(landPath, MaximumLandBytes), resources = ReadFile(resourcePath, MaximumDirectoryBytes);
        MapPackageBinary.LandMetadata metadata = MapPackageBinary.ValidateLand(land, mapNumber);
        MapPackageBinary.ValidateDirectory(resources);
        string landEntry = LandEntry(mapNumber), resourcesEntry = ResourceEntry(mapNumber);
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = Format, version = 1, sourceMapNumber = mapNumber, width = 1920, height = 696,
            land = new { path = landEntry, length = land.Length, sha256 = Hash(land) },
            resources = new { path = resourcesEntry, length = resources.Length, sha256 = Hash(resources) },
            water = new { path = metadata.WaterResourcePath, included = false }
        }, new JsonSerializerOptions { WriteIndented = true });
        string temporary = Path.Combine(Path.GetDirectoryName(output)!, $".worms-map-export-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteEntry(zip, "manifest.json", manifest);
                    WriteEntry(zip, landEntry, land);
                    WriteEntry(zip, resourcesEntry, resources);
                }
                file.Flush(flushToDisk: true);
            }
            // Validate our complete output before touching a previously existing ZIP.
            _ = ReadPackage(temporary);
            MapPackagePaths.CheckFile(output);
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            // The destination has not been truncated if serialization or validation failed.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static MapPackageImportResult Import(string zipPath, string dataDirectory, int targetMapNumber,
        Func<MapPackageInfo, bool> confirm) => Import(zipPath, dataDirectory, targetMapNumber, confirm, null);

    internal static MapPackageImportResult Import(string zipPath, string dataDirectory, int targetMapNumber,
        Func<MapPackageInfo, bool> confirm, IMapPackageFileOperations? operations)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        ValidateMapNumber(targetMapNumber);
        string root = ValidateDataDirectory(dataDirectory);
        string landPath = LandPath(root, targetMapNumber), resourcesPath = ResourcePath(root, targetMapNumber);
        MapPackagePaths.CheckFile(landPath);
        MapPackagePaths.CheckFile(resourcesPath);
        Package package = ReadPackage(zipPath);
        byte[] land = MapPackageBinary.RemapLand(package.Land, package.Metadata, targetMapNumber);
        var info = new MapPackageInfo(package.MapNumber, targetMapNumber, package.Metadata.WaterResourcePath, 1920, 696);
        // No stage directory or destination directory is created before this decision.
        if (!confirm(info)) return new(false, null);
        MapPackagePaths.CheckFile(landPath);
        MapPackagePaths.CheckFile(resourcesPath);
        string stage = Path.Combine(root, $".worms-map-import-{Guid.NewGuid():N}");
        bool prepared = false;
        try
        {
            MapPackagePaths.Check(stage);
            Directory.CreateDirectory(stage);
            string stagedLand = Path.Combine(stage, "new-land.dat"), stagedResources = Path.Combine(stage, "new-Level.Dir");
            WriteStaged(stagedLand, land);
            WriteStaged(stagedResources, package.Resources);
            prepared = true; // The common helper now owns cleanup and recovery files.
            string? warning = FileSetTransaction.CommitPrepared(stage,
                [new(stagedLand, landPath), new(stagedResources, resourcesPath)],
                (key, args) => key switch
                {
                    "Restored" => L10n.Package("ImportRestored", args),
                    "RecoveryRequired" => L10n.Package("ImportRecoveryRequired", args),
                    "CleanupWarning" => L10n.Package("ImportCleanupWarning", args),
                    _ => throw new ArgumentOutOfRangeException(nameof(key))
                }, MapPackagePaths.Check, operations);
            return new(true, warning);
        }
        finally
        {
            if (!prepared && Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }

    private static Package ReadPackage(string path)
    {
        MapPackagePaths.CheckFile(path);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            IReadOnlyDictionary<string, MapPackageZip.Entry> envelope = MapPackageZip.Inspect(file);
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            Require(zip.Entries.Count == 3, "InvalidPackage");
            var entries = zip.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
            Require(entries.ContainsKey("manifest.json") && envelope.ContainsKey("manifest.json"));
            byte[] manifestBytes = MapPackageZip.ReadBounded(entries["manifest.json"], envelope["manifest.json"], MaximumManifestBytes);
            using JsonDocument json = JsonDocument.Parse(manifestBytes, new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement manifest = json.RootElement;
            Object(manifest, "format", "version", "sourceMapNumber", "width", "height", "land", "resources", "water");
            Require(Text(manifest, "format") == Format && Number(manifest, "version") == 1
                && Number(manifest, "width") == 1920 && Number(manifest, "height") == 696);
            int number = Number(manifest, "sourceMapNumber");
            Require(number is >= 1 and <= 999);
            JsonElement landInfo = manifest.GetProperty("land"), resourcesInfo = manifest.GetProperty("resources"), water = manifest.GetProperty("water");
            Object(landInfo, "path", "length", "sha256");
            Object(resourcesInfo, "path", "length", "sha256");
            Object(water, "path", "included");
            string landEntry = LandEntry(number), resourcesEntry = ResourceEntry(number);
            Require(Text(landInfo, "path") == landEntry && Text(resourcesInfo, "path") == resourcesEntry
                && entries.ContainsKey(landEntry) && entries.ContainsKey(resourcesEntry));
            Require(envelope.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(["manifest.json", landEntry, resourcesEntry]), "InvalidPackage");
            Require(envelope.Values.Sum(entry => (long)entry.Length) <= (long)MaximumLandBytes + MaximumDirectoryBytes + MaximumManifestBytes, "SizeLimit");
            byte[] land = MapPackageZip.ReadBounded(entries[landEntry], envelope[landEntry], MaximumLandBytes);
            byte[] resources = MapPackageZip.ReadBounded(entries[resourcesEntry], envelope[resourcesEntry], MaximumDirectoryBytes);
            Require(Number(landInfo, "length") == land.Length && Number(resourcesInfo, "length") == resources.Length
                && Text(landInfo, "sha256").Equals(Hash(land), StringComparison.OrdinalIgnoreCase)
                && Text(resourcesInfo, "sha256").Equals(Hash(resources), StringComparison.OrdinalIgnoreCase));
            MapPackageBinary.LandMetadata metadata = MapPackageBinary.ValidateLand(land, number);
            MapPackageBinary.ValidateDirectory(resources);
            Require(water.GetProperty("included").ValueKind == JsonValueKind.False && Text(water, "path") == metadata.WaterResourcePath);
            return new(number, land, resources, metadata);
        }
        catch (Exception error) when (error is JsonException or EndOfStreamException or OverflowException or ArgumentException)
        {
            throw new InvalidDataException(L10n.Package("InvalidPackage"), error);
        }
    }

    private static void Object(JsonElement value, params string[] properties)
    {
        Require(value.ValueKind == JsonValueKind.Object);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject()) Require(names.Add(property.Name));
        Require(names.SetEquals(properties));
    }
    private static string Text(JsonElement value, string key)
    {
        JsonElement property = value.GetProperty(key);
        Require(property.ValueKind == JsonValueKind.String);
        return property.GetString()!;
    }
    private static int Number(JsonElement value, string key)
    {
        JsonElement property = value.GetProperty(key);
        Require(property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out _));
        return property.GetInt32();
    }
    private static void Require(bool value, string key = "InvalidManifest")
    { if (!value) throw new InvalidDataException(L10n.Package(key)); }
    private static string ValidateDataDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        MapPackagePaths.Check(full);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(L10n.Package("DataDirectoryMissing"));
        return full;
    }
    private static byte[] ReadFile(string path, int maximum)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(file.Length is > 0 && file.Length <= maximum, "SizeLimit");
        byte[] bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        Require(file.ReadByte() == -1, "SizeLimit");
        return bytes;
    }
    private static void WriteStaged(string path, byte[] bytes)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes);
        file.Flush(flushToDisk: true);
    }
    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        stream.Write(bytes);
    }
    private static void ValidateMapNumber(int number)
    { if (number is < 1 or > 999) throw new ArgumentOutOfRangeException(nameof(number), L10n.Package("InvalidMapNumber")); }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string LandPath(string root, int number) => Path.Combine(root, $"land{number:00}.dat");
    private static string ResourcePath(string root, int number) => Path.Combine(root, "Mfx", $"Tmap{number:00}", "Level.Dir");
    private static string LandEntry(int number) => $"Data/land{number:00}.dat";
    private static string ResourceEntry(int number) => $"Data/Mfx/Tmap{number:00}/Level.Dir";
    private sealed record Package(int MapNumber, byte[] Land, byte[] Resources, MapPackageBinary.LandMetadata Metadata);
}
