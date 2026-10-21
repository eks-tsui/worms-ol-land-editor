using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using WormsLandEditor.Localization;

namespace WormsLandEditor.Services;

/// <summary>Strict, non-ZIP64 ZIP envelope checks before ZipArchive allocates its entry table.</summary>
internal static class MapPackageZip
{
    internal sealed record Entry(string Name, uint Crc, int Length, int CompressedLength, int LocalOffset, ushort Flags, ushort Method);
    private static readonly uint[] CrcTable = CreateCrcTable();

    internal static IReadOnlyDictionary<string, Entry> Inspect(FileStream file)
    {
        Require(file.Length is >= 22 and <= 97L * 1024 * 1024, "SizeLimit");
        int tailLength = (int)Math.Min(file.Length, 65557);
        byte[] tail = new byte[tailLength];
        file.Position = file.Length - tailLength;
        file.ReadExactly(tail);
        int end = -1;
        for (int i = tail.Length - 22; i >= 0; i--)
        {
            if (U32(tail, i) == 0x06054b50 && i + 22 + U16(tail, i + 20) == tail.Length) { end = i; break; }
        }
        Require(end >= 0);
        Require(U16(tail, end + 4) == 0 && U16(tail, end + 6) == 0
            && U16(tail, end + 8) == 3 && U16(tail, end + 10) == 3);
        uint centralSize = U32(tail, end + 12), centralOffset = U32(tail, end + 16);
        long endOffset = file.Length - tailLength + end;
        Require((long)centralOffset + centralSize == endOffset && centralSize <= 32 * 1024);
        file.Position = centralOffset;
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 3; i++)
        {
            byte[] header = Read(file, 46);
            Require(U32(header, 0) == 0x02014b50);
            ushort flags = U16(header, 8), method = U16(header, 10);
            Require(method is 0 or 8 && (flags & ~0x080E) == 0 && (method != 0 || (flags & 6) == 0));
            uint compressed = U32(header, 20), length = U32(header, 24), local = U32(header, 42);
            Require(length <= MapPackageService.MaximumDirectoryBytes && compressed <= 97 * 1024 * 1024
                && local < centralOffset, "SizeLimit");
            int nameLength = U16(header, 28), extraLength = U16(header, 30), commentLength = U16(header, 32);
            Require(nameLength is > 0 and <= 256 && extraLength <= 4096 && U16(header, 34) == 0);
            string name = Encoding.UTF8.GetString(Read(file, nameLength));
            uint attributes = U32(header, 38);
            uint unixType = (attributes >> 16) & 0xF000;
            Require(unixType is 0 or 0x8000 && (attributes & (0x400 | 0x10)) == 0, "UnsafeEntry", name);
            Require(!name.Contains('\\') && name.Split('/').All(MapPackageBinary.IsSafeComponent), "UnsafeEntry", name);
            Require(entries.TryAdd(name, new(name, U32(header, 16), (int)length, (int)compressed, (int)local, flags, method)), "UnsafeEntry", name);
            CheckExtra(Read(file, extraLength));
            _ = Read(file, commentLength);
        }
        Require(file.Position == endOffset);
        long next = 0;
        foreach (Entry entry in entries.Values.OrderBy(e => e.LocalOffset))
        {
            Require(entry.LocalOffset == next);
            file.Position = entry.LocalOffset;
            byte[] header = Read(file, 30);
            Require(U32(header, 0) == 0x04034b50 && U16(header, 6) == entry.Flags && U16(header, 8) == entry.Method);
            bool descriptor = (entry.Flags & 8) != 0;
            Require((U32(header, 14) == entry.Crc || (descriptor && U32(header, 14) == 0))
                && (U32(header, 18) == entry.CompressedLength || (descriptor && U32(header, 18) == 0))
                && (U32(header, 22) == entry.Length || (descriptor && U32(header, 22) == 0)));
            int nameLength = U16(header, 26), extraLength = U16(header, 28);
            Require(nameLength is > 0 and <= 256 && extraLength <= 4096);
            Require(Encoding.UTF8.GetString(Read(file, nameLength)) == entry.Name);
            CheckExtra(Read(file, extraLength));
            next = file.Position + entry.CompressedLength;
            Require(next <= centralOffset);
            if (descriptor)
            {
                file.Position = next;
                uint crc = U32(Read(file, 4), 0);
                if (crc == 0x08074b50) crc = U32(Read(file, 4), 0);
                byte[] sizes = Read(file, 8);
                Require(crc == entry.Crc && U32(sizes, 0) == entry.CompressedLength && U32(sizes, 4) == entry.Length);
                next = file.Position;
            }
        }
        Require(next == centralOffset);
        file.Position = 0;
        return entries;
    }

    internal static byte[] ReadBounded(ZipArchiveEntry archiveEntry, Entry entry, int maximum)
    {
        Require(archiveEntry.Length == entry.Length && archiveEntry.CompressedLength == entry.CompressedLength);
        Require(entry.Length > 0 && entry.Length <= maximum && entry.CompressedLength > 0, "SizeLimit");
        Require(entry.Length <= 1024 * 1024 || (long)entry.Length <= (long)entry.CompressedLength * 2000, "SizeLimit");
        byte[] bytes = new byte[entry.Length];
        using Stream stream = archiveEntry.Open();
        stream.ReadExactly(bytes);
        Require(stream.ReadByte() == -1);
        uint crc = uint.MaxValue;
        foreach (byte b in bytes) crc = CrcTable[(crc ^ b) & 255] ^ (crc >> 8);
        Require((crc ^ uint.MaxValue) == entry.Crc);
        return bytes;
    }

    private static void CheckExtra(byte[] bytes)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            Require(bytes.Length - offset >= 4);
            int id = U16(bytes, offset), count = U16(bytes, offset + 2);
            Require(id != 1 && count <= bytes.Length - offset - 4); // ZIP64 is outside package v1.
            offset += count + 4;
        }
    }

    private static byte[] Read(Stream stream, int count) { byte[] bytes = new byte[count]; stream.ReadExactly(bytes); return bytes; }
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static void Require(bool condition, string key = "InvalidPackage", params object?[] args)
    { if (!condition) throw new InvalidDataException(L10n.Package(key, args)); }
    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++) value = (value & 1) == 1 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
