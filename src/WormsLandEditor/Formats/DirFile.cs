using WormsLandEditor.Localization;
using System.Text;

namespace WormsLandEditor.Formats;

public sealed class DirFile
{
    private const int HeaderSize = 12;
    private const int BucketCount = 1024;
    private const int DirectoryPrefixSize = 4 + BucketCount * 4;

    private byte[] _directoryBytes = [];
    private string? _unsafeDirectoryEntry;

    public string? SourcePath { get; private set; }
    public int DirectoryStart { get; private set; }
    public int DataEnd { get; private set; }
    public int DirectorySize => _directoryBytes.Length;
    public List<DirResourceEntry> Resources { get; } = [];
    public List<string> EntryNames { get; } = [];

    public static DirFile Load(string path) => Load(File.ReadAllBytes(path), path);

    public static DirFile Load(ReadOnlySpan<byte> bytes, string? sourcePath = null)
    {
        int offset = 0;
        string magic = Encoding.ASCII.GetString(BinaryUtil.ReadBytes(bytes, ref offset, 4));
        if (magic != "DIR\x1A")
            throw new InvalidDataException(L10n.Core("InvalidDirMagic"));

        var dir = new DirFile
        {
            SourcePath = sourcePath,
            DataEnd = BinaryUtil.ReadInt32(bytes, ref offset),
            DirectoryStart = BinaryUtil.ReadInt32(bytes, ref offset)
        };

        if (dir.DirectoryStart < HeaderSize || dir.DataEnd > bytes.Length || dir.DirectoryStart > dir.DataEnd)
            throw new InvalidDataException(L10n.Core("InvalidDirDataRange"));

        dir._directoryBytes = bytes[dir.DirectoryStart..dir.DataEnd].ToArray();
        dir.ParseDirectoryEntries(bytes);
        return dir;
    }

    public static DirFile LoadTemplate(string? gameDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDataDirectory))
            throw new InvalidOperationException(L10n.Core("TemplateDataDirectoryRequired"));

        string path = Path.Combine(gameDataDirectory, "Mfx", "Tmap01", "Level.Dir");
        if (!File.Exists(path))
            throw new FileNotFoundException(L10n.Core("TemplateFileNotFound", path), path);

        return Load(path);
    }

    public DirResourceEntry? Find(string name)
    {
        return Resources.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public Size? GetResourceDimensions(string name)
    {
        DirResourceEntry? entry = Find(name);
        if (entry is null)
            return null;

        if (entry.Name.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
        {
            int imageOffset = 0;
            WolImage image = WolImage.Read(entry.Data, ref imageOffset);
            return new Size(image.Width, image.Height);
        }

        if (entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
            return WolSprite.Read(entry.Data).CanvasSize();

        return null;
    }

    public void ReplaceResourceFromFile(string name, string fileName)
    {
        DirResourceEntry entry = Find(name) ?? throw new InvalidOperationException(L10n.Core("ResourceNotFound", name));
        if (entry.Name.EndsWith(".img", StringComparison.OrdinalIgnoreCase) &&
            Path.GetExtension(fileName).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            int imageOffset = 0;
            WolImage image = WolImage.Read(entry.Data, ref imageOffset);
            using var bitmap = new Bitmap(fileName);
            ValidateBitmapSize(bitmap.Size, image.Width, image.Height, entry.Name);
            image.ReplaceFromBitmap(bitmap);
            using var stream = new MemoryStream();
            image.Write(stream);
            entry.Data = stream.ToArray();
            return;
        }

        if (entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
        {
            WolSprite sprite = WolSprite.Read(entry.Data);
            string extension = Path.GetExtension(fileName);
            bool preserveCanvas =
                entry.Name.Equals("back.spr", StringComparison.OrdinalIgnoreCase) ||
                entry.Name.Equals("debris.spr", StringComparison.OrdinalIgnoreCase);
            bool preservePalette = true;
            if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            {
                ValidateSpriteReplacementImageSize(sprite, fileName, entry.Name);
                sprite.ReplaceFromGif(fileName, preserveCanvas, preservePalette);
            }
            else if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                ValidateSpriteReplacementImageSize(sprite, fileName, entry.Name);
                sprite.ReplaceFromPng(fileName, preserveCanvas, preservePalette);
            }
            else
                throw new InvalidDataException(L10n.Core("SpriteImportFormats"));

            NormalizeSpriteResource(entry.Name, sprite);
            entry.Data = sprite.ToBytes();
            return;
        }

        throw new InvalidDataException(L10n.Core("ResourceImportFormats"));
    }

    private static void NormalizeSpriteResource(string name, WolSprite sprite)
    {
        if (!name.Equals("debris.spr", StringComparison.OrdinalIgnoreCase))
            return;

        sprite.EnsurePaletteSize(PreferredPaletteCount(name, sprite.Palette.Count));
        sprite.SetAnimationRate(0);
        sprite.FieldB = checked((ushort)Math.Max((int)sprite.FieldB, 60));
        sprite.FieldC = checked((ushort)Math.Max((int)sprite.FieldC, 60));
    }

    public void ReplaceResourceBytes(string name, byte[] data)
    {
        DirResourceEntry entry = Find(name) ?? throw new InvalidOperationException(L10n.Core("ResourceNotFound", name));
        if (data.Length < 8)
            throw new InvalidDataException(L10n.Core("ResourceBlockTooShort", name));

        entry.Data = data;
        NormalizeResourceEntry(entry);
    }

    public void ClearResourceImage(string name)
    {
        DirResourceEntry entry = Find(name) ?? throw new InvalidOperationException(L10n.Core("ResourceNotFound", name));
        if (entry.Name.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
        {
            int offset = 0;
            WolImage image = WolImage.Read(entry.Data, ref offset);
            Array.Clear(image.Pixels);
            using var stream = new MemoryStream();
            image.Write(stream);
            entry.Data = stream.ToArray();
            return;
        }

        if (entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
        {
            WolSprite sprite = WolSprite.Read(entry.Data);
            foreach (WolSpriteFrame frame in sprite.Frames)
                Array.Clear(frame.Pixels);

            NormalizeSpriteResource(entry.Name, sprite);
            entry.Data = sprite.ToBytes();
            return;
        }

        throw new InvalidDataException(L10n.Core("ResourceCannotBeCleared", name));
    }

    public void NormalizeResourceFormats()
    {
        foreach (DirResourceEntry entry in Resources)
            NormalizeResourceEntry(entry);
    }

    private static void NormalizeResourceEntry(DirResourceEntry entry)
    {
        if (!entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
            return;

        WolSprite sprite = WolSprite.Read(entry.Data);
        NormalizeSpriteResource(entry.Name, sprite);
        entry.Data = sprite.ToBytes();
    }

    public void RemapPalettedResources(IReadOnlyList<Color> sharedPalette, int maxColors)
    {
        if (sharedPalette.Count == 0)
            return;

        foreach (DirResourceEntry entry in Resources)
        {
            if (entry.Name.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
            {
                int offset = 0;
                WolImage image = WolImage.Read(entry.Data, ref offset);
                image.RemapToPalette(sharedPalette, maxColors);
                using var stream = new MemoryStream();
                image.Write(stream);
                entry.Data = stream.ToArray();
                continue;
            }

            if (entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
            {
                WolSprite sprite = WolSprite.Read(entry.Data);
                sprite.RemapToPalette(sharedPalette, maxColors);
                entry.Data = sprite.ToBytes();
            }
        }
    }

    public void RemapPalettedResourcesToBestPaletteSubset(IReadOnlyList<Color> sharedPalette)
    {
        if (sharedPalette.Count == 0)
            return;

        foreach (DirResourceEntry entry in Resources)
            RemapEntryToBestPaletteSubset(entry, sharedPalette);
    }

    public void RemapNonTerrainPalettedResourcesToBestPaletteSubset(IReadOnlyList<Color> sharedPalette)
    {
        if (sharedPalette.Count == 0)
            return;

        foreach (DirResourceEntry entry in Resources)
        {
            if (UsesTerrainPalette(entry.Name))
                continue;

            RemapEntryToBestPaletteSubset(entry, sharedPalette);
        }
    }

    public void RemapTerrainPaletteResources(IReadOnlyList<Color> terrainPalette)
    {
        if (terrainPalette.Count == 0)
            return;

        foreach (DirResourceEntry entry in Resources)
        {
            if (!UsesTerrainPalette(entry.Name))
                continue;

            if (entry.Name.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
            {
                int offset = 0;
                WolImage image = WolImage.Read(entry.Data, ref offset);
                image.RemapToPalette(terrainPalette, terrainPalette.Count);
                using var stream = new MemoryStream();
                image.Write(stream);
                entry.Data = stream.ToArray();
                continue;
            }

            if (entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
            {
                WolSprite sprite = WolSprite.Read(entry.Data);
                sprite.RemapToPalette(terrainPalette, terrainPalette.Count);
                entry.Data = sprite.ToBytes();
            }
        }
    }

    private static int PreferredPaletteCount(string name, int fallback)
    {
        return name.ToLowerInvariant() switch
        {
            "back.spr" => 96,
            "debris.spr" => 80,
            "gradient.img" => 16,
            "bridge-l.img" or "bridge-r.img" or "bridge.img" or "grass.img" or "soil.img" or "text.img" => 96,
            _ => fallback
        };
    }

    private static void RemapEntryToBestPaletteSubset(DirResourceEntry entry, IReadOnlyList<Color> sharedPalette)
    {
        if (entry.Name.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
        {
            int offset = 0;
            WolImage image = WolImage.Read(entry.Data, ref offset);
            int maxColors = PreferredPaletteCount(entry.Name, image.HasPalette && image.Palette.Count > 0 ? image.Palette.Count : sharedPalette.Count);
            image.RemapToBestPaletteSubset(sharedPalette, maxColors);
            using var stream = new MemoryStream();
            image.Write(stream);
            entry.Data = stream.ToArray();
            return;
        }

        if (entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
        {
            WolSprite sprite = WolSprite.Read(entry.Data);
            int maxColors = PreferredPaletteCount(entry.Name, sprite.Palette.Count > 0 ? sprite.Palette.Count : sharedPalette.Count);
            sprite.RemapToBestPaletteSubset(sharedPalette, maxColors);
            entry.Data = sprite.ToBytes();
        }
    }

    private static bool UsesTerrainPalette(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "back.spr" or "bridge-l.img" or "bridge-r.img" or "bridge.img" or "grass.img" or "soil.img" or "text.img" => true,
            _ => false
        };
    }

    private static void ValidateBitmapSize(Size actual, int expectedWidth, int expectedHeight, string name)
    {
        if (actual.Width == expectedWidth && actual.Height == expectedHeight)
            return;

        throw new InvalidDataException(L10n.Core("ResourceSizeMismatch", name, expectedWidth, expectedHeight, actual.Width, actual.Height));
    }

    private static void ValidateSpriteReplacementImageSize(WolSprite sprite, string fileName, string resourceName)
    {
        using Image image = Image.FromFile(fileName);
        if (resourceName.Equals("debris.spr", StringComparison.OrdinalIgnoreCase))
        {
            if (image.Width <= 80 && image.Height <= 80)
                return;

            throw new InvalidDataException(L10n.Core("DebrisSizeExceeded", resourceName, image.Width, image.Height));
        }

        if (resourceName.Equals("back.spr", StringComparison.OrdinalIgnoreCase))
        {
            if (image.Width == 640)
                return;

            throw new InvalidDataException(L10n.Core("BackgroundWidthMismatch", resourceName, image.Width, image.Height));
        }

        Size expected = sprite.ReplacementImageSize();
        if (image.Width == expected.Width && image.Height == expected.Height)
            return;

        throw new InvalidDataException(L10n.Core("ResourceSizeMismatch", resourceName, expected.Width, expected.Height, image.Width, image.Height));
    }

    public Bitmap? CreatePreview(string name)
    {
        return CreatePreviewFrames(name).FirstOrDefault();
    }

    public List<Bitmap> CreatePreviewFrames(string name, bool preserveSpriteCanvas = false)
    {
        DirResourceEntry? entry = Find(name);
        if (entry is null)
            return [];

        if (entry.Name.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
            return [CreateImgPreview(entry.Data)];

        if (entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
            return CreateSprPreview(entry.Data, preserveSpriteCanvas);

        return [];
    }

    public void SaveTo(string outputDirectory)
    {
        if (_directoryBytes.Length < DirectoryPrefixSize)
            throw new InvalidDataException(L10n.Core("IncompleteDirTable"));
        if (_unsafeDirectoryEntry is not null)
            throw new InvalidDataException(L10n.Core("UnsafeDirRewrite", _unsafeDirectoryEntry));

        NormalizeResourceFormats();
        Directory.CreateDirectory(outputDirectory);

        byte[] directory = (byte[])_directoryBytes.Clone();
        using var stream = new MemoryStream();
        BinaryUtil.WriteAscii(stream, "DIR\x1A", nullTerminate: false);
        BinaryUtil.WriteInt32(stream, 0);
        BinaryUtil.WriteInt32(stream, 0);

        int dataOffset = HeaderSize;
        foreach (DirResourceEntry entry in Resources.OrderBy(r => r.OriginalOffset))
        {
            if (entry.Data.Length < 8 && !entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L10n.Core("ResourceBlockTooShort", entry.Name));

            BinaryPrimitivesWriteInt32(directory, entry.DirectoryEntryOffset + 4, dataOffset);
            BinaryPrimitivesWriteInt32(directory, entry.DirectoryEntryOffset + 8, entry.Data.Length);
            stream.Write(entry.Data);
            dataOffset = checked(dataOffset + entry.Data.Length);
        }

        int directoryStart = checked((int)stream.Position);
        stream.Write(directory);
        int dataEnd = checked((int)stream.Position);
        BinaryUtil.PatchInt32(stream, 4, dataEnd);
        BinaryUtil.PatchInt32(stream, 8, directoryStart);

        string outputPath = Path.Combine(outputDirectory, "Level.Dir");
        File.WriteAllBytes(outputPath, stream.ToArray());
        SourcePath = outputPath;
        DirectoryStart = directoryStart;
        DataEnd = dataEnd;
        _directoryBytes = directory;
    }

    public string Describe()
    {
        string names = EntryNames.Count == 0 ? L10n.Core("NoNamedDirEntries") : string.Join(", ", EntryNames);
        return L10n.Core("DirDescription", DirectoryStart, DataEnd, DirectorySize, names);
    }

    private void ParseDirectoryEntries(ReadOnlySpan<byte> fileBytes)
    {
        Resources.Clear();
        EntryNames.Clear();
        _unsafeDirectoryEntry = null;

        if (_directoryBytes.Length < DirectoryPrefixSize)
            throw new InvalidDataException(L10n.Core("DirTableTooSmall"));

        var seenBinaryOffsets = new HashSet<int>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var binaryRanges = new List<(int Start, int End, string Name)>();
        long copiedBytes = 0;
        for (int entryOffset = DirectoryPrefixSize; entryOffset + 13 < _directoryBytes.Length;)
        {
            int fileOffset = BitConverter.ToInt32(_directoryBytes, entryOffset + 4);
            int length = BitConverter.ToInt32(_directoryBytes, entryOffset + 8);
            int nameOffset = entryOffset + 12;
            int nameEnd = Array.IndexOf(_directoryBytes, (byte)0, nameOffset);
            if (nameEnd < 0)
            {
                _unsafeDirectoryEntry ??= L10n.Core("UnterminatedDirEntry");
                break;
            }

            string name = Encoding.ASCII.GetString(_directoryBytes, nameOffset, nameEnd - nameOffset);
            int entrySize = Align4(12 + name.Length + 1);
            bool text = name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
            if (IsResourceEntryName(name) &&
                fileOffset >= HeaderSize &&
                length >= (text ? 0 : 8) &&
                fileOffset <= DirectoryStart &&
                length <= DirectoryStart - fileOffset &&
                length <= 64L * 1024 * 1024 - copiedBytes &&
                seenNames.Add(name.Replace('\\', '/')) &&
                (text || seenBinaryOffsets.Add(fileOffset)))
            {
                // TXT records can be short aliases into another resource. Copy
                // only their bounded bytes; never interpret them as IMG/SPR.
                copiedBytes += length;
                if (!text) binaryRanges.Add((fileOffset, fileOffset + length, name));
                Resources.Add(new DirResourceEntry(name, fileOffset, length, entryOffset, fileBytes[fileOffset..(fileOffset + length)].ToArray()));
                EntryNames.Add(name);
            }
            else
            {
                // Retain read-only access, but never rewrite an archive whose data
                // cannot be represented by the supported resource entries.
                _unsafeDirectoryEntry ??= name;
            }

            entryOffset += Math.Max(entrySize, 16);
        }
        binaryRanges.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int i = 1; i < binaryRanges.Count; i++)
        {
            if (binaryRanges[i].Start < binaryRanges[i - 1].End)
                _unsafeDirectoryEntry ??= binaryRanges[i].Name;
        }
    }

    private static Bitmap CreateImgPreview(byte[] data)
    {
        int offset = 0;
        return WolImage.Read(data, ref offset).ToBitmap(transparentZero: true);
    }

    private static List<Bitmap> CreateSprPreview(byte[] data, bool preserveCanvas)
    {
        WolSprite sprite = WolSprite.Read(data);
        return preserveCanvas ? sprite.ToCanvasBitmaps() : sprite.ToBitmaps();
    }

    private static bool IsResourceEntryName(string value)
    {
        if (value.Length is < 5 or > 64)
            return false;

        string extension = Path.GetExtension(value);
        if (!extension.Equals(".img", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".spr", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.' or '/' or '\\');
    }

    private static int Align4(int value)
    {
        return (value + 3) & ~3;
    }

    private static void BinaryPrimitivesWriteInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }
}

public sealed class DirResourceEntry
{
    public DirResourceEntry(string name, int originalOffset, int length, int directoryEntryOffset, byte[] data)
    {
        Name = name;
        OriginalOffset = originalOffset;
        Length = length;
        DirectoryEntryOffset = directoryEntryOffset;
        Data = data;
    }

    public string Name { get; }
    public int OriginalOffset { get; }
    public int Length { get; }
    public int DirectoryEntryOffset { get; }
    public byte[] Data { get; set; }
}
