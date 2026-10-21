using System.Buffers.Binary;
using System.Text;
using WormsLandEditor.Localization;

namespace WormsLandEditor.Services;

/// <summary>
/// Bounded structural validation for package v1. Does not decode images or run the
/// editor's palette-changing serializers. Only the DAT's numbered NameA is rewritten.
/// </summary>
internal static class MapPackageBinary
{
    internal sealed record LandMetadata(int NameOffset, int NameLength, string ResourcePath, string WaterResourcePath);

    internal static LandMetadata ValidateLand(byte[] bytes, int mapNumber)
    {
        var reader = new Reader(bytes, "InvalidLand");
        reader.Magic("LND\x1A");
        reader.Require(reader.Int32() == bytes.Length);
        reader.Require(reader.Int32() == 1920 && reader.Int32() == 696);
        _ = reader.Int32(); // The client treats any nonzero flag as a closed map.
        for (int group = 0; group < 2; group++)
        {
            int count = reader.Int32();
            reader.Require(count is >= 0 and <= 4096);
            for (int i = 0; i < count; i++)
            {
                int x = reader.Int32(), y = reader.Int32();
                reader.Require(x is >= 0 and < 1920 && y is >= 0 and < 696);
            }
        }
        ValidateImage(reader, 1920, 696, 8);
        ValidateImage(reader, 1920, 696, 1);
        ValidateImage(reader, 1920, 696, 1);
        ValidateImage(reader, 240, 87, 1);
        int nameOffset = reader.Position;
        string resourcePath = reader.SizedAscii();
        int nameLength = reader.Position - nameOffset;
        string waterPath = reader.SizedAscii();
        reader.Require(reader.Position == bytes.Length);
        if (!resourcePath.Replace('\\', '/').Equals($"data/mfx/Tmap{mapNumber:00}", StringComparison.OrdinalIgnoreCase)
            || !IsWaterPath(waterPath))
            throw new InvalidDataException(L10n.Package("InvalidResourcePath"));
        return new(nameOffset, nameLength, resourcePath, waterPath);
    }

    internal static byte[] RemapLand(byte[] bytes, LandMetadata metadata, int mapNumber)
    {
        // Preserve spelling and separator style; only the numbered suffix changes.
        int digitStart = metadata.ResourcePath.Length;
        while (digitStart > 0 && char.IsAsciiDigit(metadata.ResourcePath[digitStart - 1])) digitStart--;
        byte[] name = Encoding.ASCII.GetBytes(metadata.ResourcePath[..digitStart] + mapNumber.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
        byte[] mapped = new byte[checked(bytes.Length - metadata.NameLength + name.Length + 1)];
        bytes.AsSpan(0, metadata.NameOffset).CopyTo(mapped);
        mapped[metadata.NameOffset] = checked((byte)name.Length);
        name.CopyTo(mapped.AsSpan(metadata.NameOffset + 1));
        bytes.AsSpan(metadata.NameOffset + metadata.NameLength).CopyTo(mapped.AsSpan(metadata.NameOffset + name.Length + 1));
        BinaryPrimitives.WriteInt32LittleEndian(mapped.AsSpan(4), mapped.Length);
        _ = ValidateLand(mapped, mapNumber);
        return mapped;
    }

    internal static bool IsWaterPath(string value)
    {
        string normalized = value.Replace('\\', '/');
        string[] parts = normalized.Split('/');
        return parts.Length >= 3 && parts[0].Equals("data", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("water", StringComparison.OrdinalIgnoreCase)
            && parts.All(IsSafeComponent);
    }

    internal static bool IsSafeComponent(string value)
    {
        if (value.Length == 0 || value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ')
            || value.Any(c => c < 32 || c > 126 || "<>:\"/\\|?*".Contains(c))) return false;
        string stem = value.Split('.')[0];
        return !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    internal static void ValidateDirectory(byte[] bytes)
    {
        var reader = new Reader(bytes, "InvalidDirectory");
        reader.Magic("DIR\x1A");
        int dataEnd = reader.Int32(), directoryStart = reader.Int32();
        reader.Require(dataEnd == bytes.Length && directoryStart >= 12 && directoryStart <= dataEnd - 4100);
        reader.Require(dataEnd - directoryStart <= 1024 * 1024);
        // The original hash-table prefix is opaque to the editor. Preserve it;
        // validate every indexed name, block and range used by the editor below.
        reader.Position = directoryStart + 4100;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranges = new List<(int Start, int End)>();
        long decodedPixels = 0, indexedBytes = 0;
        while (reader.Position < dataEnd)
        {
            int entryStart = reader.Position;
            reader.Require(dataEnd - entryStart >= 16 && names.Count < 4096);
            _ = reader.Int32();
            int offset = reader.Int32(), length = reader.Int32();
            string name = reader.NullAscii(64);
            string normalized = name.Replace('\\', '/');
            reader.Require(name.Length >= 5 && name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.' or '/' or '\\')
                && normalized.Split('/').All(IsSafeComponent) && !normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase)
                && names.Add(normalized));
            string extension = Path.GetExtension(name).ToLowerInvariant();
            reader.Require(extension is ".img" or ".spr" or ".txt");
            bool text = extension == ".txt";
            reader.Require(offset >= 12 && length >= (text ? 0 : 8) && offset <= directoryStart && length <= directoryStart - offset);
            // Legacy TXT entries are opaque spans, not image blocks. A short
            // index.txt may alias bytes in an IMG. Do not resolve its name as a
            // path; bound the total copied bytes even when spans are shared.
            indexedBytes += length;
            reader.Require(indexedBytes <= 64L * 1024 * 1024);
            if (!text) ranges.Add((offset, offset + length));
            var block = new Reader(bytes.AsMemory(offset, length), "InvalidDirectory");
            if (extension == ".img")
            {
                decodedPixels += ValidateImage(block);
                block.Require(block.Position == length);
            }
            else if (extension == ".spr") decodedPixels += ValidateSprite(block);
            reader.Require(decodedPixels <= 32L * 1024 * 1024);
            int entrySize = (reader.Position - entryStart + 3) & ~3;
            reader.Require(entryStart <= dataEnd - entrySize);
            reader.Position = entryStart + entrySize;
        }
        reader.Require(names.Count > 0 && reader.Position == dataEnd);
        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int i = 1; i < ranges.Count; i++) reader.Require(ranges[i].Start >= ranges[i - 1].End);
    }

    private static long ValidateImage(Reader outer, int? requiredWidth = null, int? requiredHeight = null, int? requiredDepth = null)
    {
        int start = outer.Position;
        outer.Magic("IMG\x1A");
        int length = outer.Int32();
        outer.Require(length >= 15 && start <= outer.Length - length);
        var image = new Reader(outer.Bytes.Slice(start, length), outer.ErrorKey) { Position = 8 };
        ValidateEmbeddedName(image, image.NullAscii(255));
        int flags = image.UInt16(), depth = flags & 255;
        image.Require(depth is 1 or 8);
        if ((flags & 0x8000) != 0)
        {
            int count = image.UInt16();
            image.Require(count is > 0 and <= 256);
            image.Skip(count * 3);
        }
        int width = image.UInt16(), height = image.UInt16();
        image.Require(width <= 8192 && height <= 8192 && (long)width * height <= 16 * 1024 * 1024);
        // Empty resource IMG placeholders are supported by the editor; DAT images
        // still have their mandatory dimensions checked below.
        image.Require((requiredWidth is null || width == requiredWidth) && (requiredHeight is null || height == requiredHeight)
            && (requiredDepth is null || depth == requiredDepth));
        long pixels = ((width * (long)depth + 7) / 8) * height;
        image.Require(pixels == image.Length - image.Position);
        outer.Position = start + length;
        return (long)width * height;
    }

    private static void ValidateEmbeddedName(Reader reader, string name)
    {
        if (name.Length == 0) return;
        string normalized = name.Replace('\\', '/');
        reader.Require(normalized.Split('/').All(IsSafeComponent)
            && !normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase));
    }

    private static long ValidateSprite(Reader sprite)
    {
        sprite.Magic("SPR\x1A");
        sprite.Require(sprite.Int32() == sprite.Length);
        ValidateEmbeddedName(sprite, sprite.NullAscii(255));
        int flags = sprite.UInt16();
        sprite.Require(flags is 0x8008 or 0xC008);
        int colors = sprite.UInt16();
        sprite.Require(colors is > 0 and < 256);
        sprite.Skip(colors * 3);
        if ((flags & 0x4000) != 0) sprite.Skip(sprite.UInt16() * 12);
        _ = sprite.Int32();
        _ = sprite.UInt16();
        int canvasWidth = sprite.UInt16(), canvasHeight = sprite.UInt16();
        sprite.Require(canvasWidth <= 8192 && canvasHeight <= 8192 && (long)canvasWidth * canvasHeight <= 16 * 1024 * 1024);
        int frames = sprite.UInt16();
        sprite.Require(frames is > 0 and <= 255);
        int pixelStart = sprite.Position + frames * 12;
        sprite.Require(pixelStart <= sprite.Length);
        long totalPixels = 0;
        int effectiveWidth = Math.Max(1, canvasWidth), effectiveHeight = Math.Max(1, canvasHeight);
        for (int i = 0; i < frames; i++)
        {
            int offset = sprite.Int32(), left = sprite.Int16(), top = sprite.Int16(), right = sprite.Int16(), bottom = sprite.Int16();
            int width = right - left, height = bottom - top;
            sprite.Require(width is >= 0 and <= 8192 && height is >= 0 and <= 8192);
            long pixels = (long)width * height;
            totalPixels += pixels;
            sprite.Require(totalPixels <= 64 * 1024 * 1024 && offset >= 0 && offset <= sprite.Length - pixelStart
                && pixels <= sprite.Length - pixelStart - offset);
            sprite.Require(right <= 8192 && bottom <= 8192);
            effectiveWidth = Math.Max(effectiveWidth, right);
            effectiveHeight = Math.Max(effectiveHeight, bottom);
        }
        long decodedPixels = (long)effectiveWidth * effectiveHeight * frames;
        sprite.Require(decodedPixels <= 16L * 1024 * 1024);
        // Frame ranges may legitimately alias the same stored pixels. WolSprite.Read
        // still allocates each frame separately, including off-canvas frames.
        return Math.Max(totalPixels, decodedPixels);
    }

    private sealed class Reader(ReadOnlyMemory<byte> bytes, string errorKey)
    {
        public ReadOnlyMemory<byte> Bytes { get; } = bytes;
        public string ErrorKey { get; } = errorKey;
        public int Position { get; set; }
        public int Length => Bytes.Length;
        public void Require(bool condition) { if (!condition) throw new InvalidDataException(L10n.Package(ErrorKey)); }
        public void Skip(int count) { Require(count >= 0 && Position >= 0 && Position <= Length - count); Position += count; }
        public int Int32() { int start = Position; Skip(4); return BinaryPrimitives.ReadInt32LittleEndian(Bytes.Span[start..]); }
        public int UInt16() { int start = Position; Skip(2); return BinaryPrimitives.ReadUInt16LittleEndian(Bytes.Span[start..]); }
        public int Int16() { int start = Position; Skip(2); return BinaryPrimitives.ReadInt16LittleEndian(Bytes.Span[start..]); }
        public void Magic(string value) { int start = Position; Skip(4); Require(Bytes.Span.Slice(start, 4).SequenceEqual(Encoding.ASCII.GetBytes(value))); }
        public string SizedAscii() { int start = Position; Skip(1); int count = Bytes.Span[start]; Require(count is > 0 and < 128); return Ascii(count); }
        public string NullAscii(int maximum)
        {
            Require(Position >= 0 && Position <= Length);
            int end = Bytes.Span[Position..].IndexOf((byte)0);
            Require(end >= 0 && end <= maximum);
            string value = Ascii(end); Skip(1); return value;
        }
        private string Ascii(int count)
        {
            int start = Position; Skip(count);
            foreach (byte value in Bytes.Span.Slice(start, count)) Require(value is >= 32 and <= 126);
            return Encoding.ASCII.GetString(Bytes.Span.Slice(start, count));
        }
    }
}
