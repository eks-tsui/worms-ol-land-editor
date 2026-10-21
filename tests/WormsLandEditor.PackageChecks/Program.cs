using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using WormsLandEditor.Localization;
using WormsLandEditor.Services;

string root = Path.Combine(Path.GetTempPath(), "worms-package-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException("FAIL: " + label);
    checks++;
    Console.WriteLine("PASS: " + label);
}
string Folder(string name) { string path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
string Dat(string folder, int number) => Path.Combine(folder, $"land{number:00}.dat");
string Dir(string folder, int number) => Path.Combine(folder, "Mfx", $"Tmap{number:00}", "Level.Dir");
void WriteMap(string folder, int number, byte[]? land = null, byte[]? resources = null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Dir(folder, number))!);
    File.WriteAllBytes(Dat(folder, number), land ?? Fixtures.Land(number));
    File.WriteAllBytes(Dir(folder, number), resources ?? Fixtures.Directory());
}
Dictionary<string, byte[]> ReadZip(string path)
{
    using var zip = ZipFile.OpenRead(path);
    return zip.Entries.ToDictionary(e => e.FullName, e => { using var output = new MemoryStream(); using Stream input = e.Open(); input.CopyTo(output); return output.ToArray(); });
}
void WriteZip(string path, IEnumerable<(string Name, byte[] Bytes, int? Attributes)> entries)
{
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var entry in entries)
    {
        ZipArchiveEntry target = zip.CreateEntry(entry.Name, CompressionLevel.Optimal);
        if (entry.Attributes.HasValue) target.ExternalAttributes = entry.Attributes.Value;
        using Stream stream = target.Open(); stream.Write(entry.Bytes);
    }
}
void SyncManifest(Dictionary<string, byte[]> entries)
{
    JsonObject manifest = JsonNode.Parse(entries["manifest.json"])!.AsObject();
    foreach (string key in new[] { "land", "resources" })
    {
        JsonObject record = manifest[key]!.AsObject();
        if (!entries.TryGetValue(record["path"]!.GetValue<string>(), out byte[]? bytes)) continue;
        record["length"] = bytes.Length;
        record["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
    entries["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
}
int rejected = 0;
try
{
    string source = Folder("source");
    WriteMap(source, 1);
    string zipPath = Path.Combine(root, "land01.zip");
    MapPackageService.Export(source, 1, zipPath);
    Dictionary<string, byte[]> originalEntries = ReadZip(zipPath);
    Check(originalEntries.Keys.Order().SequenceEqual(new[] { "Data/Mfx/Tmap01/Level.Dir", "Data/land01.dat", "manifest.json" }.Order()), "Export contains exactly the three documented entries");
    Check(originalEntries["Data/land01.dat"].SequenceEqual(File.ReadAllBytes(Dat(source, 1))) && originalEntries["Data/Mfx/Tmap01/Level.Dir"].SequenceEqual(File.ReadAllBytes(Dir(source, 1))), "Export preserves all saved DAT and DIR bytes, including empty IMG and text resources");
    JsonObject exportedManifest = JsonNode.Parse(originalEntries["manifest.json"])!.AsObject();
    Check(exportedManifest["version"]!.GetValue<int>() == 1 && exportedManifest["water"]!["included"]!.GetValue<bool>() == false, "Manifest declares version 1 and external water");

    foreach (int textLength in new[] { 0, 1, 7 })
    {
        byte[] aliasedDirectory = Fixtures.Directory(aliasTextLength: textLength);
        string aliasSource = Folder("alias-source-" + textLength), aliasTarget = Folder("alias-target-" + textLength);
        WriteMap(aliasSource, 1, resources: aliasedDirectory);
        string aliasZip = Path.Combine(root, "alias-" + textLength + ".zip");
        MapPackageService.Export(aliasSource, 1, aliasZip);
        PreparedMapPackage aliased = MapPackageService.PrepareImport(aliasZip, 100, _ => true)!;
        Check(aliased.DirectoryBytes.SequenceEqual(aliasedDirectory) && !Directory.EnumerateFileSystemEntries(aliasTarget).Any(),
            $"Legacy {textLength}-byte index.txt alias loads with all DIR bytes unchanged and no target writes");
        _ = MapSaveService.Save(aliased, Dat(aliasTarget, 100), Dir(aliasTarget, 100));
        Check(File.ReadAllBytes(Dir(aliasTarget, 100)).SequenceEqual(aliasedDirectory),
            $"Explicit Save preserves the {textLength}-byte TXT alias and complete original DIR container");
    }

    string cancelled = Folder("cancelled");
    bool callback = false;
    PreparedMapPackage? cancel = MapPackageService.PrepareImport(zipPath, 100, info =>
    {
        callback = true;
        Check(!Directory.EnumerateFileSystemEntries(cancelled).Any(), "Validated confirmation occurs before staging or target-directory creation");
        Check(info.SourceMapNumber == 1 && info.TargetMapNumber == 100 && info.WaterResourcePath == @"data\water\Red" && info.Width == 1920 && info.Height == 696, "Confirmation receives validated source, target, dimensions, and external water");
        return false;
    });
    Check(callback && cancel is null && !Directory.EnumerateFileSystemEntries(cancelled).Any(), "Cancellation leaves an empty target completely unchanged");

    string target = Folder("target");
    WriteMap(target, 100, Fixtures.Land(100, "Blue"), Fixtures.Directory("old resources"));
    string unrelated = Path.Combine(Path.GetDirectoryName(Dir(target, 100))!, "keep-me.txt");
    File.WriteAllText(unrelated, "untouched");
    File.WriteAllText(Dat(target, 2), "other map");
    Directory.CreateDirectory(Path.Combine(target, "Water", "Red"));
    File.WriteAllText(Path.Combine(target, "Water", "Red", "Water.dir"), "shared water");
    string[] beforeImport = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().ToArray();
    PreparedMapPackage imported = MapPackageService.PrepareImport(zipPath, 100, _ => true)!;
    Check(Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().SequenceEqual(beforeImport),
        "Confirmed import creates no files or directories anywhere in the source or target trees");
    Check(imported.LandBytes.SequenceEqual(Fixtures.Land(100)) && imported.DirectoryBytes.SequenceEqual(Fixtures.Directory()),
        "Preparation returns mapped DAT and byte-identical complete DIR in memory");
    Check(File.ReadAllBytes(Dat(target, 100)).SequenceEqual(Fixtures.Land(100, "Blue"))
        && File.ReadAllBytes(Dir(target, 100)).SequenceEqual(Fixtures.Directory("old resources"))
        && !Directory.EnumerateDirectories(target, ".worms-map-*").Any(),
        "Confirmed import leaves existing DAT, DIR, and destination directories unchanged");
    string missingTarget = Path.Combine(root, "not-created-by-import");
    PreparedMapPackage newMap = MapPackageService.PrepareImport(zipPath, 999, _ => true)!;
    Check(!Directory.Exists(missingTarget) && newMap.LandBytes.SequenceEqual(Fixtures.Land(999)),
        "Preparing a new map never creates its target root or numbered resource directory");
    bool confirmationFailed = false;
    try { MapPackageService.PrepareImport(zipPath, 100, _ => throw new IOException("Injected confirmation failure")); }
    catch (IOException) { confirmationFailed = true; }
    Check(confirmationFailed && !Directory.EnumerateFileSystemEntries(cancelled).Any(),
        "Failed confirmation creates no target files or stage directories");
    foreach (int invalidNumber in new[] { -1, 0, 1000 })
    {
        bool confirmedInvalid = false, rejectedNumber = false;
        try { MapPackageService.PrepareImport(zipPath, invalidNumber, _ => { confirmedInvalid = true; return true; }); }
        catch (ArgumentOutOfRangeException) { rejectedNumber = true; }
        Check(rejectedNumber && !confirmedInvalid && !Directory.EnumerateFileSystemEntries(cancelled).Any(),
            $"Invalid target number {invalidNumber} is rejected before confirmation without target writes");
    }
    byte[] landCopy = imported.LandBytes.ToArray(), directoryCopy = imported.DirectoryBytes.ToArray();
    landCopy[0] = 0; directoryCopy[0] = 0;
    Check(imported.LandBytes[0] == 'L' && imported.DirectoryBytes[0] == 'D',
        "Editable byte copies cannot mutate the retained imported snapshot");
    File.Delete(zipPath);
    Check(MapSaveService.Save(imported, Dat(target, 100), Dir(target, 100)) is null,
        "Only explicit Save writes both files, without rereading the removed ZIP or running model serializers");
    WriteZip(zipPath, originalEntries.Select(p => (p.Key, p.Value, (int?)null)));
    Check(File.ReadAllBytes(Dat(target, 100)).SequenceEqual(Fixtures.Land(100)), "Actual DAT NameA maps 01 to 100 with byte preservation outside length and numbered path");
    Check(File.ReadAllBytes(Dir(target, 100)).SequenceEqual(Fixtures.Directory()), "Complete DIR remains byte-identical on overwrite");
    Check(File.ReadAllText(unrelated) == "untouched" && File.ReadAllText(Dat(target, 2)) == "other map" && File.ReadAllText(Path.Combine(target, "Water", "Red", "Water.dir")) == "shared water", "Unrelated Tmap files, other map numbers, and shared water are untouched");
    Check(!Directory.EnumerateDirectories(target, ".worms-map-*").Any(), "Successful explicit Save cleans its owned stage");
    string secondZip = Path.Combine(root, "land100.zip");
    MapPackageService.Export(target, 100, secondZip);
    string third = Folder("third");
    PreparedMapPackage remapped = MapPackageService.PrepareImport(secondZip, 2, _ => true)!;
    Check(!Directory.EnumerateFileSystemEntries(third).Any(), "Second import remains in memory until explicit Save");
    _ = MapSaveService.Save(remapped, Dat(third, 2), Dir(third, 2));
    Check(File.ReadAllBytes(Dat(third, 2)).SequenceEqual(Fixtures.Land(2)) && File.ReadAllBytes(Dir(third, 2)).SequenceEqual(Fixtures.Directory()), "Export/import/Save round trip 1 to 100 to 2 preserves binary map and water path");

    foreach (int failure in new[] { 1, 2 })
    foreach (bool afterWrite in new[] { false, true })
    {
        string folder = Folder($"rollback-{failure}-{afterWrite}");
        byte[] oldLand = Fixtures.Land(100, "Blue"), oldDirectory = Fixtures.Directory("original");
        WriteMap(folder, 100, oldLand, oldDirectory);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Dir(folder, 100))!, "unrelated"), "safe");
        try { MapSaveService.Save(imported, Dat(folder, 100), Dir(folder, 100), new InjectedOperations(failure, afterWrite)); throw new Exception("Expected failure"); }
        catch (IOException ex) { Check(ex.Message.Contains("restored", StringComparison.OrdinalIgnoreCase), $"Write {failure} {(afterWrite ? "partial" : "pre-write")} failure reports recovery"); }
        Check(File.ReadAllBytes(Dat(folder, 100)).SequenceEqual(oldLand) && File.ReadAllBytes(Dir(folder, 100)).SequenceEqual(oldDirectory), $"Write {failure} {(afterWrite ? "partial" : "pre-write")} failure restores both originals");
        Check(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Dir(folder, 100))!, "unrelated")) == "safe" && !Directory.EnumerateDirectories(folder, ".worms-map-*").Any(), "Failure preserves unrelated files and cleans stage");
    }
    string newFailure = Folder("new-failure");
    try { MapSaveService.Save(imported, Dat(newFailure, 100), Dir(newFailure, 100), new InjectedOperations(2, false)); throw new Exception("Expected failure"); }
    catch (IOException) { }
    Check(!Directory.EnumerateFileSystemEntries(newFailure).Any(), "Failed explicit new-map Save removes only its own new files and empty directories");
    string restoreFailure = Folder("restore-failure");
    WriteMap(restoreFailure, 100, Fixtures.Land(100, "Blue"), Fixtures.Directory("backup"));
    try { MapSaveService.Save(imported, Dat(restoreFailure, 100), Dir(restoreFailure, 100), new InjectedOperations(2, false, failRestore: true)); throw new Exception("Expected recovery failure"); }
    catch (IOException ex) { Check(ex.Message.Contains(".worms-map-save-", StringComparison.OrdinalIgnoreCase), "Rollback failure explicitly reports recovery directory"); }
    string recovery = Directory.GetDirectories(restoreFailure, ".worms-map-save-*").Single();
    Check(File.ReadAllBytes(Path.Combine(recovery, "original-0")).SequenceEqual(Fixtures.Land(100, "Blue")) && File.ReadAllBytes(Path.Combine(recovery, "original-1")).SequenceEqual(Fixtures.Directory("backup")), "Failed rollback retains both original backups");

    void Reject(string label, Action<Dictionary<string, byte[]>>? mutate = null,
        Func<Dictionary<string, byte[]>, IEnumerable<(string Name, byte[] Bytes, int? Attributes)>>? select = null,
        Action<string>? mutateZip = null, bool sync = true)
    {
        var entries = originalEntries.ToDictionary(p => p.Key, p => p.Value.ToArray());
        mutate?.Invoke(entries);
        if (sync) SyncManifest(entries);
        string path = Path.Combine(root, $"bad-{rejected++}.zip");
        WriteZip(path, select is null ? entries.Select(p => (p.Key, p.Value, (int?)null)) : select(entries));
        mutateZip?.Invoke(path);
        string destination = Folder("bad-target-" + rejected);
        File.WriteAllText(Path.Combine(destination, "sentinel"), "untouched");
        bool confirmed = false, failed = false;
        try { MapPackageService.PrepareImport(path, 100, _ => { confirmed = true; return true; }); }
        catch (InvalidDataException) { failed = true; }
        Check(failed && !confirmed && Directory.EnumerateFileSystemEntries(destination).Count() == 1 && File.ReadAllText(Path.Combine(destination, "sentinel")) == "untouched", label + " rejected before confirmation and target writes");
    }
    void BadManifest(string label, Action<JsonObject> mutate)
    {
        Reject(label, entries => { JsonObject manifest = JsonNode.Parse(entries["manifest.json"])!.AsObject(); mutate(manifest); entries["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString()); }, sync: false);
    }
    BadManifest("Unknown schema version", m => m["version"] = 2);
    BadManifest("Unknown format", m => m["format"] = "other");
    BadManifest("Missing manifest field", m => m.Remove("width"));
    BadManifest("Unexpected manifest field", m => m["unexpected"] = true);
    BadManifest("Invalid map number", m => m["sourceMapNumber"] = 1000);
    BadManifest("Mismatching manifest dimensions", m => m["width"] = 640);
    BadManifest("Mismatching entry path", m => m["land"]!["path"] = "Data/land02.dat");
    BadManifest("Wrong declared DAT length", m => m["land"]!["length"] = 2);
    BadManifest("Wrong DAT checksum", m => m["land"]!["sha256"] = new string('0', 64));
    BadManifest("Wrong DIR checksum", m => m["resources"]!["sha256"] = new string('0', 64));
    BadManifest("Unapproved included water", m => m["water"]!["included"] = true);
    BadManifest("Mismatching external water", m => m["water"]!["path"] = @"data\water\Blue");
    Reject("Duplicate JSON property", e => e["manifest.json"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(e["manifest.json"]).Replace("\"version\": 1", "\"version\": 1, \"version\": 1")), sync: false);
    Reject("Oversized manifest", e => e["manifest.json"] = new byte[MapPackageService.MaximumManifestBytes + 1], sync: false);
    Reject("Missing ZIP file", select: e => e.Where(p => p.Key != "Data/land01.dat").Select(p => (p.Key, p.Value, (int?)null)));
    Reject("Extra ZIP file", select: e => e.Select(p => (p.Key, p.Value, (int?)null)).Append(("extra.txt", new byte[1], null)));
    Reject("Case-insensitive ZIP duplicate", select: e => e.Where(p => p.Key != "Data/land01.dat").Select(p => (p.Key, p.Value, (int?)null)).Append(("MANIFEST.JSON", e["manifest.json"], null)));
    foreach (string unsafeName in new[] { "../land01.dat", "/land01.dat", "C:/land01.dat", @"Data\land01.dat", "Data/./land01.dat", "Data//land01.dat", "Data/CON.dat", "Data/land01.dat " })
        Reject("Unsafe ZIP path " + unsafeName, select: e => e.Select(p => (p.Key == "Data/land01.dat" ? unsafeName : p.Key, p.Value, (int?)null)));
    Reject("ZIP symbolic link", select: e => e.Select(p => (p.Key, p.Value, p.Key == "Data/land01.dat" ? (int?)unchecked((int)0xA1FF0000) : null)));
    Reject("ZIP reparse point", select: e => e.Select(p => (p.Key, p.Value, p.Key == "Data/land01.dat" ? (int?)0x400 : null)));
    Reject("ZIP directory attribute", select: e => e.Select(p => (p.Key, p.Value, p.Key == "Data/land01.dat" ? (int?)0x10 : null)));
    Reject("Truncated ZIP", mutateZip: p => { byte[] bytes = File.ReadAllBytes(p); File.WriteAllBytes(p, bytes[..^12]); });
    Reject("Unaccounted ZIP preamble", mutateZip: p => { byte[] bytes = File.ReadAllBytes(p); File.WriteAllBytes(p, new byte[1].Concat(bytes).ToArray()); });
    Reject("ZIP trailing bytes", mutateZip: p => { using var f = new FileStream(p, FileMode.Append); f.WriteByte(1); });
    Reject("ZIP CRC mismatch", mutateZip: p => Fixtures.ChangeZipField(p, 16, 14, 0x12345678));
    Reject("ZIP declared compression bomb", mutateZip: p => Fixtures.ChangeZipField(p, 24, 22, 32 * 1024 * 1024, entryIndex: 1));
    Reject("ZIP declared overlarge entry", mutateZip: p => Fixtures.ChangeZipField(p, 24, 22, 65 * 1024 * 1024, entryIndex: 1));

    Reject("ZIP unsupported compression method", mutateZip: p => Fixtures.ChangeZipShort(p, 10, 8, 99));
    Reject("ZIP encrypted entry flag", mutateZip: p => Fixtures.ChangeZipShort(p, 8, 6, 1));
    Reject("ZIP local header name mismatch", mutateZip: p => { byte[] b = File.ReadAllBytes(p); b[30] ^= 1; File.WriteAllBytes(p, b); });
    Reject("ZIP local header length mismatch", mutateZip: p => { byte[] b = File.ReadAllBytes(p); Fixtures.I32(b, 22, 1); File.WriteAllBytes(p, b); });
    Reject("ZIP central directory disk mismatch", mutateZip: p => { byte[] b = File.ReadAllBytes(p); b[^18] = 1; File.WriteAllBytes(p, b); });

    const string landKey = "Data/land01.dat", dirKey = "Data/Mfx/Tmap01/Level.Dir";
    void BadLand(string label, Action<byte[]> mutate) => Reject(label, e => mutate(e[landKey]));
    BadLand("Wrong DAT magic", b => b[0] = 0);
    BadLand("Wrong DAT length", b => Fixtures.I32(b, 4, 1));
    BadLand("Wrong map width", b => Fixtures.I32(b, 8, 1919));
    BadLand("Negative point count", b => Fixtures.I32(b, 20, -1));
    BadLand("Oversized point count", b => Fixtures.I32(b, 20, 4097));
    BadLand("Out-of-map spawn point", b => Fixtures.I32(b, 24, 1920));
    BadLand("Wrong terrain IMG magic", b => b[52] = 0);
    BadLand("Overlarge IMG length", b => Fixtures.I32(b, 56, int.MaxValue));
    Reject("Truncated DAT", e => e[landKey] = e[landKey][..^1]);
    Reject("DAT trailing bytes", e => { e[landKey] = e[landKey].Append((byte)0).ToArray(); Fixtures.I32(e[landKey], 4, e[landKey].Length); });
    Reject("Mismatching DAT numbered resource path", e => e[landKey] = Fixtures.Land(2));
    Reject("Unsafe DAT water path", e => e[landKey] = Fixtures.Land(1, "../Red"));
    Reject("Reserved DAT water path", e => e[landKey] = Fixtures.Land(1, "CON"));
    Reject("Absolute DAT water path", e => e[landKey] = Fixtures.Land(1, "Red", waterPath: @"C:\outside"));
    Reject("Wrong water root", e => e[landKey] = Fixtures.Land(1, "Red", waterPath: @"data\mfx\Tmap01"));
    Reject("Wrong DAT mask dimensions", e => e[landKey] = Fixtures.Land(1, maskWidth: 1919));
    Reject("Wrong DAT terrain depth", e => e[landKey] = Fixtures.Land(1, terrainDepth: 1));
    Reject("Non-ASCII DAT resource path", e => { byte[] b = e[landKey]; int name = MapPackageBinary.ValidateLand(b, 1).NameOffset; b[name + 1] = 255; });
    Reject("TXT alias outside the data region", e =>
    {
        e[dirKey] = Fixtures.Directory(aliasTextLength: 1);
        byte[] b = e[dirKey]; int table = Fixtures.R32(b, 8);
        int entry = Fixtures.FindEntry(b, "index.txt"); Fixtures.I32(b, entry + 4, table);
    });
    Reject("Negative TXT length", e =>
    {
        e[dirKey] = Fixtures.Directory(aliasTextLength: 1);
        Fixtures.I32(e[dirKey], Fixtures.FindEntry(e[dirKey], "index.txt") + 8, -1);
    });
    Reject("Oversized aggregate aliased TXT allocations", e => e[dirKey] = Fixtures.Directory(largeTextAliases: true));
    Reject("Wrong DIR magic", e => e[dirKey][0] = 0);
    Reject("Wrong DIR data end", e => Fixtures.I32(e[dirKey], 4, e[dirKey].Length + 1));
    Reject("Wrong DIR table offset", e => Fixtures.I32(e[dirKey], 8, int.MaxValue));
    Reject("Truncated DIR", e => e[dirKey] = e[dirKey][..^1]);
    Reject("DIR entry outside data region", e => { byte[] b = e[dirKey]; int table = Fixtures.R32(b, 8); Fixtures.I32(b, table + 4100 + 4, table); });
    Reject("DIR overlapping resource ranges", e => e[dirKey] = Fixtures.Directory(overlap: true));
    Reject("Duplicate DIR resource names", e => e[dirKey] = Fixtures.Directory(duplicate: true));
    foreach (string name in new[] { "../soil.img", "/soil.img", "data/mfx/tmap01/soil.img", "SOIL.IMG", "soil name.img", "soil.exe" })
        Reject("Unsupported DIR resource name " + name, e => e[dirKey] = Fixtures.Directory(firstName: name));
    Reject("Invalid DIR IMG pixels", e => e[dirKey] = Fixtures.Directory(firstImage: Fixtures.Image("soil", 2, 2, 8)[..^1]));
    Reject("Embedded IMG external path", e => e[dirKey] = Fixtures.Directory(firstImage: Fixtures.Image(@"data\mfx\Tmap01\soil", 2, 2, 8)));
    Reject("SPR huge canvas with empty frames", e => e[dirKey] = Fixtures.Directory(sprite: Fixtures.Sprite(8192, 2048, 255, empty: true)));
    Reject("SPR frame-derived canvas amplification", e => e[dirKey] = Fixtures.Directory(sprite: Fixtures.Sprite(0, 0, 255, empty: true, largeFrame: true)));
    Reject("Aliased off-canvas SPR frame allocation budget", e => e[dirKey] = Fixtures.Directory(sprite: Fixtures.AliasedNegativeSprite()));
    Reject("Aggregate decoded DIR pixel budget", e => e[dirKey] = Fixtures.Directory(extraImages: 3));

    string existingCancel = Folder("existing-cancel");
    WriteMap(existingCancel, 100, Fixtures.Land(100, "Blue"), Fixtures.Directory("cancelled original"));
    _ = MapPackageService.PrepareImport(zipPath, 100, _ => false);
    Check(File.ReadAllBytes(Dat(existingCancel, 100)).SequenceEqual(Fixtures.Land(100, "Blue"))
        && File.ReadAllBytes(Dir(existingCancel, 100)).SequenceEqual(Fixtures.Directory("cancelled original"))
        && !Directory.EnumerateDirectories(existingCancel, ".worms-map-*").Any(), "Overwrite cancellation preserves both originals and creates no stage");
    string descriptorZip = Path.Combine(root, "descriptor.zip");
    using (var memory = new MemoryStream())
    {
        using (var nonseek = new NonSeekableStream(memory))
        using (var archive = new ZipArchive(nonseek, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in originalEntries)
            {
                using Stream stream = archive.CreateEntry(entry.Key, CompressionLevel.Optimal).Open();
                stream.Write(entry.Value);
            }
        }
        File.WriteAllBytes(descriptorZip, memory.ToArray());
    }
    string descriptorTarget = Folder("descriptor-target");
    PreparedMapPackage streamed = MapPackageService.PrepareImport(descriptorZip, 2, _ => true)!;
    Check(streamed.LandBytes.SequenceEqual(Fixtures.Land(2)) && !Directory.EnumerateFileSystemEntries(descriptorTarget).Any(),
        "Valid streaming ZIP data descriptors are accepted and validated without target writes");

    byte[] savedZip = File.ReadAllBytes(zipPath);
    File.WriteAllBytes(Dat(source, 1), [1, 2, 3]);
    bool exportFailed = false;
    try { MapPackageService.Export(source, 1, zipPath); } catch (InvalidDataException) { exportFailed = true; }
    Check(exportFailed && File.ReadAllBytes(zipPath).SequenceEqual(savedZip) && !Directory.EnumerateFiles(root, ".worms-map-export-*").Any(), "Invalid-source export preserves an existing ZIP without leaked staging files");
    WriteMap(source, 1);
    foreach (string destination in new[] { Dat(source, 1), Dir(source, 1) })
    {
        byte[] before = File.ReadAllBytes(destination);
        bool rejectedAlias = false;
        try { MapPackageService.Export(source, 1, destination); } catch (InvalidDataException) { rejectedAlias = true; }
        Check(rejectedAlias && File.ReadAllBytes(destination).SequenceEqual(before), "Export refuses to replace either active map source");
    }
    foreach (bool land in new[] { true, false })
    {
        WriteMap(source, 1);
        using (var stream = new FileStream(land ? Dat(source, 1) : Dir(source, 1), FileMode.Open)) stream.SetLength((land ? MapPackageService.MaximumLandBytes : MapPackageService.MaximumDirectoryBytes) + 1L);
        bool rejectedSize = false;
        try { MapPackageService.Export(source, 1, zipPath); } catch (InvalidDataException) { rejectedSize = true; }
        Check(rejectedSize && File.ReadAllBytes(zipPath).SequenceEqual(savedZip), "Oversized source " + (land ? "DAT" : "DIR") + " rejected without truncating ZIP");
    }

    if (!OperatingSystem.IsWindows())
    {
        string outside = Folder("outside");
        File.WriteAllText(Path.Combine(outside, "land100.dat"), "outside original");
        foreach (string kind in new[] { "file", "tmap", "root", "broken" })
        {
            string destination = Folder("symlink-" + kind);
            if (kind == "file") File.CreateSymbolicLink(Dat(destination, 100), Path.Combine(outside, "land100.dat"));
            if (kind == "broken") File.CreateSymbolicLink(Dat(destination, 100), Path.Combine(outside, "missing.dat"));
            if (kind == "tmap") Directory.CreateSymbolicLink(Path.Combine(destination, "Mfx"), outside);
            if (kind == "root") { destination = Path.Combine(root, "linked-root"); Directory.CreateSymbolicLink(destination, outside); }
            bool rejectedLink = false;
            PreparedMapPackage linked = MapPackageService.PrepareImport(zipPath, 100, _ => true)!;
            try { MapSaveService.Save(linked, Dat(destination, 100), Dir(destination, 100)); } catch (InvalidDataException) { rejectedLink = true; }
            Check(rejectedLink && File.ReadAllText(Path.Combine(outside, "land100.dat")) == "outside original",
                "Explicit Save rejects destination " + kind + " symlink without touching its target");
        }
        string outLink = Path.Combine(root, "output-link.zip");
        File.CreateSymbolicLink(outLink, zipPath);
        bool rejectedSourceLink = false, confirmedSourceLink = false;
        try { MapPackageService.PrepareImport(outLink, 100, _ => { confirmedSourceLink = true; return true; }); }
        catch (InvalidDataException) { rejectedSourceLink = true; }
        Check(rejectedSourceLink && !confirmedSourceLink, "Import rejects a symbolic-link ZIP source before confirmation");
        WriteMap(source, 1);
        bool rejectedOutputLink = false;
        try { MapPackageService.Export(source, 1, outLink); } catch (InvalidDataException) { rejectedOutputLink = true; }
        Check(rejectedOutputLink && File.ReadAllBytes(zipPath).SequenceEqual(savedZip), "Export rejects symbolic-link output");
    }
    foreach (string culture in new[] { "en", "zh-CN" })
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        Check(!string.IsNullOrWhiteSpace(L10n.Package("ImportRecoveryRequired", "a", "b", "c")), "Package recovery diagnostic available for " + culture);
    }
    Console.WriteLine($"PASS {checks} package checks with real synthesized DAT/IMG/SPR/DIR binaries and ZIP I/O. Raw snapshot saves bypass fail-fast model serializers; no image rendering or Windows GUI is exercised.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine($"FAIL after {checks} checks: {ex}"); return 1; }
finally { Directory.Delete(root, recursive: true); }

sealed class InjectedOperations(int failure, bool afterWrite, bool failRestore = false) : IMapPackageFileOperations
{
    private int count;
    public void Replace(string staged, string destination)
    {
        bool fail = ++count == failure;
        if (fail && !afterWrite) throw new IOException("Injected pre-write failure");
        File.Move(staged, destination, overwrite: true);
        if (fail) throw new IOException("Injected partial failure");
    }
    public void Restore(string backup, string destination)
    {
        if (failRestore) throw new IOException("Injected restore failure");
        File.Copy(backup, destination, overwrite: true);
    }
}

static class Fixtures
{
    internal static void I32(byte[] b, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(offset), value);
    internal static int R32(byte[] b, int offset) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset));
    private static void Magic(BinaryWriter writer, string text) => writer.Write(Encoding.ASCII.GetBytes(text));
    private static void Name(BinaryWriter writer, string text) { Magic(writer, text); writer.Write((byte)0); }
    private static byte[] Finish(MemoryStream stream) { byte[] b = stream.ToArray(); I32(b, 4, b.Length); return b; }
    internal static byte[] Image(string name, int width, int height, int depth)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        Magic(w, "IMG\x1A"); w.Write(0); Name(w, name); w.Write((ushort)(depth == 8 ? 0x8008 : 1));
        if (depth == 8) { w.Write((ushort)128); for (int i = 0; i < 128 * 3; i++) w.Write((byte)(i * 17)); }
        w.Write((ushort)width); w.Write((ushort)height);
        int count = ((width * depth + 7) / 8) * height;
        for (int i = 0; i < count; i++) w.Write((byte)(depth == 8 ? (i * 13 + 7) % 128 : i % 2 == 0 ? 0x55 : 0xAA));
        return Finish(stream);
    }
    internal static byte[] Land(int number, string water = "Red", string? waterPath = null, int maskWidth = 1920, int terrainDepth = 8)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        Magic(w, "LND\x1A"); w.Write(0); w.Write(1920); w.Write(696); w.Write(1);
        w.Write(2); w.Write(100); w.Write(200); w.Write(1800); w.Write(500);
        w.Write(1); w.Write(700); w.Write(300);
        w.Write(Image("terrain", 1920, 696, terrainDepth)); w.Write(Image("collision", maskWidth, 696, 1));
        w.Write(Image("auxiliary", 1920, 696, 1)); w.Write(Image("preview", 240, 87, 1));
        foreach (string path in new[] { $@"data\mfx\Tmap{number:00}", waterPath ?? @"data\water\" + water })
        { byte[] b = Encoding.ASCII.GetBytes(path); w.Write((byte)b.Length); w.Write(b); }
        return Finish(stream);
    }
    internal static byte[] Sprite(int width = 4, int height = 3, int frames = 2, bool empty = false, bool largeFrame = false)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        Magic(w, "SPR\x1A"); w.Write(0); Name(w, "background"); w.Write((ushort)0x8008);
        w.Write((ushort)2); w.Write(new byte[] { 0, 0, 0, 255, 255, 255 });
        w.Write(120); w.Write((ushort)0); w.Write((ushort)width); w.Write((ushort)height); w.Write((ushort)frames);
        for (int i = 0; i < frames; i++)
        {
            w.Write(empty ? 0 : i * width * height);
            short edgeX = (short)(largeFrame ? 8192 : 0), edgeY = (short)(largeFrame ? 2048 : 0);
            w.Write(empty ? edgeX : (short)0); w.Write(empty ? edgeY : (short)0);
            w.Write(empty ? edgeX : (short)width); w.Write(empty ? edgeY : (short)height);
        }
        if (!empty) w.Write(Enumerable.Repeat((byte)1, width * height * frames).ToArray());
        return Finish(stream);
    }
    internal static byte[] AliasedNegativeSprite()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        Magic(w, "SPR\x1A"); w.Write(0); Name(w, "background"); w.Write((ushort)0x8008);
        w.Write((ushort)2); w.Write(new byte[] { 0, 0, 0, 255, 255, 255 });
        w.Write(120); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)255);
        for (int i = 0; i < 255; i++)
        {
            w.Write(0); w.Write((short)-256); w.Write((short)-1024); w.Write((short)0); w.Write((short)0);
        }
        w.Write(new byte[256 * 1024]);
        return Finish(stream);
    }
    internal static byte[] Directory(string text = "configuration preserved", bool overlap = false, bool duplicate = false,
        string firstName = "soil.img", byte[]? firstImage = null, byte[]? sprite = null, int extraImages = 0, int? aliasTextLength = null, bool largeTextAliases = false)
    {
        var resources = new List<(string Name, byte[] Bytes)>
        {
            (firstName, firstImage ?? Image("soil", 4, 4, 8)),
            (duplicate ? firstName : "empty.img", Image("", 0, 0, 8)),
            ("back.spr", sprite ?? Sprite()), ("settings.txt", Encoding.ASCII.GetBytes(text.PadRight(8)))
        };
        for (int i = 0; i < extraImages; i++) resources.Add(($"extra{i}.img", Image("", 4096, 4096, 8)));
        if (aliasTextLength.HasValue) resources.Add(("index.txt", new byte[aliasTextLength.Value]));
        if (largeTextAliases)
        {
            resources[0] = (firstName, Image("soil", 4096, 4096, 8));
            for (int i = 0; i < 4; i++) resources.Add(($"alias{i}.txt", Array.Empty<byte>()));
        }
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        Magic(w, "DIR\x1A"); w.Write(0); w.Write(0);
        var offsets = new List<int>();
        foreach (var resource in resources) { offsets.Add((int)stream.Position); w.Write(resource.Bytes); }
        int table = (int)stream.Position;
        w.Write(resources.Count); w.Write(new byte[4096]);
        for (int i = 0; i < resources.Count; i++)
        {
            long entryStart = stream.Position;
            bool textAlias = resources[i].Name == "index.txt" || (largeTextAliases && resources[i].Name.StartsWith("alias", StringComparison.Ordinal));
            int fileOffset = textAlias ? offsets[0] + 8 : overlap && i == 1 ? offsets[0] : offsets[i];
            int fileLength = largeTextAliases && textAlias ? resources[0].Bytes.Length - 8 : resources[i].Bytes.Length;
            w.Write(0); w.Write(fileOffset); w.Write(fileLength); Name(w, resources[i].Name);
            while ((stream.Position - entryStart) % 4 != 0) w.Write((byte)0);
        }
        byte[] bytes = Finish(stream); I32(bytes, 8, table); return bytes;
    }
    internal static int FindEntry(byte[] bytes, string name)
    {
        int offset = R32(bytes, 8) + 4100;
        while (offset + 13 < bytes.Length)
        {
            int end = Array.IndexOf(bytes, (byte)0, offset + 12);
            if (Encoding.ASCII.GetString(bytes, offset + 12, end - offset - 12) == name) return offset;
            offset += (end - offset + 1 + 3) & ~3;
        }
        throw new InvalidOperationException("Missing synthesized directory entry");
    }
    internal static void ChangeZipShort(string path, int centralField, int localField, ushort value)
    {
        byte[] b = File.ReadAllBytes(path);
        for (int i = 0; i <= b.Length - 46; i++)
        {
            if (R32(b, i) != 0x02014b50) continue;
            int local = R32(b, i + 42);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(i + centralField), value);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(local + localField), value);
            break;
        }
        File.WriteAllBytes(path, b);
    }
    internal static void ChangeZipField(string path, int centralField, int localField, uint value, int entryIndex = 0)
    {
        byte[] b = File.ReadAllBytes(path);
        for (int i = 0; i <= b.Length - 46; i++)
        {
            if (R32(b, i) != 0x02014b50) continue;
            if (entryIndex-- > 0) continue;
            int local = R32(b, i + 42);
            I32(b, i + centralField, unchecked((int)value)); I32(b, local + localField, unchecked((int)value));
            break;
        }
        File.WriteAllBytes(path, b);
    }
}

sealed class NonSeekableStream(Stream inner) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
