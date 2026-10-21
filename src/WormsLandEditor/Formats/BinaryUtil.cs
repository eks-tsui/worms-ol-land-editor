using WormsLandEditor.Localization;
using System.Buffers.Binary;
using System.Text;

namespace WormsLandEditor.Formats;

internal static class BinaryUtil
{
    public static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        Ensure(data, offset, 4);
        int value = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
        offset += 4;
        return value;
    }

    public static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        Ensure(data, offset, 4);
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
        return value;
    }

    public static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        Ensure(data, offset, 2);
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;
        return value;
    }

    public static byte ReadByte(ReadOnlySpan<byte> data, ref int offset)
    {
        Ensure(data, offset, 1);
        return data[offset++];
    }

    public static byte[] ReadBytes(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        Ensure(data, offset, count);
        byte[] bytes = data.Slice(offset, count).ToArray();
        offset += count;
        return bytes;
    }

    public static string ReadNullTerminatedAscii(ReadOnlySpan<byte> data, ref int offset)
    {
        Ensure(data, offset, 0);
        int end = offset;
        while (end < data.Length && data[end] != 0)
            end++;

        if (end >= data.Length)
            throw new InvalidDataException(L10n.Core("MissingNullTerminator"));

        string value = Encoding.ASCII.GetString(data.Slice(offset, end - offset));
        offset = end + 1;
        return value;
    }

    public static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    public static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    public static void WriteAscii(Stream stream, string value, bool nullTerminate)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
        if (nullTerminate)
            stream.WriteByte(0);
    }

    public static void PatchInt32(Stream stream, long position, int value)
    {
        long old = stream.Position;
        stream.Position = position;
        WriteInt32(stream, value);
        stream.Position = old;
    }

    internal static void Ensure(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length || count > data.Length - offset)
            throw new EndOfStreamException(L10n.Core("UnexpectedEndOfFile"));
    }
}
