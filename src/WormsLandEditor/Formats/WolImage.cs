using WormsLandEditor.Localization;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WormsLandEditor.Formats;

public sealed class WolImage
{
    public const string Magic = "IMG\x1A";
    private const int MaxClientPaletteColors = 255;
    private const int MaxTerrainPaletteColors = 255;
    private const byte SolidAlphaThreshold = 1;

    public string Name { get; set; } = string.Empty;
    public ushort Flags { get; set; }
    public ushort Width { get; set; }
    public ushort Height { get; set; }
    public List<Color> Palette { get; } = [];
    public byte[] Pixels { get; set; } = [];

    public int BitsPerPixel => Flags & 0xff;
    public bool HasPalette => (Flags & 0x8000) != 0;
    public int FileStride => ((Width * BitsPerPixel) + 7) / 8;

    public WolImage Clone()
    {
        var clone = new WolImage
        {
            Name = Name,
            Flags = Flags,
            Width = Width,
            Height = Height,
            Pixels = (byte[])Pixels.Clone()
        };
        clone.Palette.AddRange(Palette);
        return clone;
    }

    public static WolImage Create8Bit(int width, int height, IReadOnlyList<Color>? preferredPalette = null)
    {
        ValidateDimensions(width, height);
        var image = new WolImage
        {
            Flags = 0x8008,
            Width = checked((ushort)width),
            Height = checked((ushort)height),
            Pixels = new byte[width * height]
        };

        if (preferredPalette is { Count: > 0 })
            image.Palette.AddRange(preferredPalette);
        else
            image.Palette.AddRange(CreateDefaultPalette());

        return image;
    }

    public static WolImage Create1Bit(int width, int height)
    {
        ValidateDimensions(width, height);
        return new WolImage
        {
            Flags = 0x0001,
            Width = checked((ushort)width),
            Height = checked((ushort)height),
            Pixels = new byte[((width + 7) / 8) * height]
        };
    }

    public static WolImage Read(ReadOnlySpan<byte> data, ref int offset)
    {
        int start = offset;
        string magic = System.Text.Encoding.ASCII.GetString(BinaryUtil.ReadBytes(data, ref offset, 4));
        if (magic != Magic)
            throw new InvalidDataException(L10n.Core("ExpectedImgAtOffset", start, magic));

        int length = BinaryUtil.ReadInt32(data, ref offset);
        if (length < 8 || start > data.Length || length > data.Length - start)
            throw new InvalidDataException(L10n.Core("InvalidImgLength", length, start));

        // Do not let a malformed header consume bytes from the next block.
        data = data[..(start + length)];

        var image = new WolImage
        {
            Name = BinaryUtil.ReadNullTerminatedAscii(data, ref offset),
            Flags = BinaryUtil.ReadUInt16(data, ref offset)
        };

        if (image.BitsPerPixel is not (1 or 8))
            throw new InvalidDataException(L10n.Core("UnsupportedImgBitDepth", image.BitsPerPixel));

        if (image.HasPalette)
        {
            ushort count = BinaryUtil.ReadUInt16(data, ref offset);
            for (int i = 0; i < count; i++)
            {
                byte r = BinaryUtil.ReadByte(data, ref offset);
                byte g = BinaryUtil.ReadByte(data, ref offset);
                byte b = BinaryUtil.ReadByte(data, ref offset);
                image.Palette.Add(Color.FromArgb(r, g, b));
            }
        }

        image.Width = BinaryUtil.ReadUInt16(data, ref offset);
        image.Height = BinaryUtil.ReadUInt16(data, ref offset);

        long expectedBytes = (long)image.FileStride * image.Height;
        int remaining = start + length - offset;
        if (remaining != expectedBytes)
            throw new InvalidDataException(L10n.Core("ImgPixelLengthMismatch", image.Width, image.Height, image.BitsPerPixel, expectedBytes, remaining));

        image.Pixels = BinaryUtil.ReadBytes(data, ref offset, (int)expectedBytes);
        return image;
    }

    public void Write(Stream stream)
    {
        EnsureClientCompatiblePalette();

        long start = stream.Position;
        BinaryUtil.WriteAscii(stream, Magic, nullTerminate: false);
        BinaryUtil.WriteInt32(stream, 0);
        BinaryUtil.WriteAscii(stream, Name, nullTerminate: true);
        BinaryUtil.WriteUInt16(stream, Flags);

        if (HasPalette)
        {
            BinaryUtil.WriteUInt16(stream, checked((ushort)Palette.Count));
            foreach (Color color in Palette)
            {
                stream.WriteByte(color.R);
                stream.WriteByte(color.G);
                stream.WriteByte(color.B);
            }
        }

        BinaryUtil.WriteUInt16(stream, Width);
        BinaryUtil.WriteUInt16(stream, Height);
        stream.Write(Pixels);
        BinaryUtil.PatchInt32(stream, start + 4, checked((int)(stream.Position - start)));
    }

    public Bitmap ToBitmap(bool transparentZero = false)
    {
        ValidateDimensions(Width, Height);
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        Rectangle rect = new(0, 0, Width, Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int byteCount = Math.Abs(data.Stride) * Height;
            byte[] argb = new byte[byteCount];
            for (int y = 0; y < Height; y++)
            {
                int row = y * Math.Abs(data.Stride);
                if (BitsPerPixel == 8)
                {
                    int sourceRow = y * FileStride;
                    for (int x = 0; x < Width; x++)
                    {
                        byte value = Pixels[sourceRow + x];
                        Color color = value > 0 && Palette.Count >= value ? Palette[value - 1] : Color.Black;
                        int dest = row + x * 4;
                        argb[dest] = color.B;
                        argb[dest + 1] = color.G;
                        argb[dest + 2] = color.R;
                        argb[dest + 3] = transparentZero && value == 0 ? (byte)0 : (byte)255;
                    }
                }
                else
                {
                    for (int x = 0; x < Width; x++)
                    {
                        byte value = GetPixelIndex(x, y);
                        int dest = row + x * 4;
                        if (value == 0)
                        {
                            argb[dest + 3] = 0;
                        }
                        else
                        {
                            argb[dest] = 255;
                            argb[dest + 1] = 255;
                            argb[dest + 2] = 255;
                            argb[dest + 3] = 210;
                        }
                    }
                }
            }

            Marshal.Copy(argb, 0, data.Scan0, argb.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    internal static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > ushort.MaxValue || height > ushort.MaxValue ||
            (long)width * height > int.MaxValue / 4)
        {
            throw new InvalidDataException(L10n.Core("InvalidImageDimensions", width, height));
        }
    }

    public Bitmap ToMaskBitmap(Color color)
    {
        if (BitsPerPixel != 1)
            return ToBitmap(transparentZero: true);

        ValidateDimensions(Width, Height);
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        Rectangle rect = new(0, 0, Width, Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] argb = new byte[stride * Height];
            for (int y = 0; y < Height; y++)
            {
                int row = y * stride;
                int sourceRow = y * FileStride;
                for (int x = 0; x < Width; x++)
                {
                    int source = sourceRow + (x >> 3);
                    int bit = x & 7;
                    if (((Pixels[source] >> bit) & 1) == 0)
                        continue;

                    int dest = row + x * 4;
                    argb[dest] = color.B;
                    argb[dest + 1] = color.G;
                    argb[dest + 2] = color.R;
                    argb[dest + 3] = color.A;
                }
            }

            Marshal.Copy(argb, 0, data.Scan0, argb.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    public byte GetPixelIndex(int x, int y)
    {
        if ((uint)x >= Width || (uint)y >= Height)
            return 0;

        if (BitsPerPixel == 8)
            return Pixels[y * FileStride + x];

        int byteIndex = y * FileStride + (x >> 3);
        int bit = x & 7;
        return (byte)((Pixels[byteIndex] >> bit) & 1);
    }

    public void SetPixelIndex(int x, int y, byte value)
    {
        if ((uint)x >= Width || (uint)y >= Height)
            return;

        if (BitsPerPixel == 8)
        {
            Pixels[y * FileStride + x] = value;
            return;
        }

        int byteIndex = y * FileStride + (x >> 3);
        int bit = x & 7;
        byte mask = (byte)(1 << bit);
        Pixels[byteIndex] = value == 0 ? (byte)(Pixels[byteIndex] & ~mask) : (byte)(Pixels[byteIndex] | mask);
    }

    public void DrawCircle(int cx, int cy, int radius, byte value)
    {
        int r2 = radius * radius;
        for (int y = cy - radius; y <= cy + radius; y++)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                int dx = x - cx;
                int dy = y - cy;
                if (dx * dx + dy * dy <= r2)
                    SetPixelIndex(x, y, value);
            }
        }
    }

    public void ReplaceFromBitmap(Bitmap source)
    {
        using Bitmap scaled = ConvertTo32Bpp(source, Width, Height);
        byte[] argb = ReadArgb(scaled);
        if (BitsPerPixel == 1)
        {
            Array.Clear(Pixels);
            for (int y = 0; y < Height; y++)
            {
                int row = y * scaled.Width * 4;
                for (int x = 0; x < Width; x++)
                {
                    int p = row + x * 4;
                    byte b = argb[p];
                    byte g = argb[p + 1];
                    byte r = argb[p + 2];
                    byte a = argb[p + 3];
                    byte value = a < 96 || Brightness(r, g, b) < 0.45f ? (byte)0 : (byte)1;
                    SetPixelIndex(x, y, value);
                }
            }
            return;
        }

        RebuildPaletteFromArgb(argb);

        int[] paletteLookup = BuildPaletteLookup();
        for (int y = 0; y < Height; y++)
        {
            int row = y * scaled.Width * 4;
            int destRow = y * FileStride;
            for (int x = 0; x < Width; x++)
            {
                int p = row + x * 4;
                byte b = argb[p];
                byte g = argb[p + 1];
                byte r = argb[p + 2];
                byte a = argb[p + 3];
                if (a < 96)
                {
                    Pixels[destRow + x] = 0;
                    continue;
                }

                int key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                Pixels[destRow + x] = (byte)paletteLookup[key];
            }
        }
    }

    public void ReplaceMaskFromBitmap(Bitmap source)
    {
        if (BitsPerPixel != 1)
            throw new InvalidOperationException(L10n.Core("TargetNotOneBitMask"));

        using Bitmap scaled = ConvertTo32Bpp(source, Width, Height);
        byte[] argb = ReadArgb(scaled);
        Array.Clear(Pixels);
        for (int y = 0; y < Height; y++)
        {
            int row = y * scaled.Width * 4;
            for (int x = 0; x < Width; x++)
            {
                int p = row + x * 4;
                byte b = argb[p];
                byte g = argb[p + 1];
                byte r = argb[p + 2];
                byte a = argb[p + 3];
                bool solid = a >= 96 && Brightness(r, g, b) >= 0.08f;
                SetPixelIndex(x, y, solid ? (byte)1 : (byte)0);
            }
        }
    }

    public void ReplaceTerrainAndMasksFromBitmap(Bitmap source, WolImage collisionMask, WolImage auxiliaryMask, WolImage previewMask)
    {
        if (BitsPerPixel != 8)
            throw new InvalidOperationException(L10n.Core("TerrainNotEightBit"));
        if (collisionMask.BitsPerPixel != 1 || auxiliaryMask.BitsPerPixel != 1 || previewMask.BitsPerPixel != 1)
            throw new InvalidOperationException(L10n.Core("AutoMaskNotOneBit"));

        using Bitmap scaled = ConvertTo32Bpp(source, Width, Height);
        byte[] argb = ReadArgb(scaled);
        RebuildPaletteFromArgb(argb, MaxTerrainPaletteColors);
        int[] paletteLookup = BuildPaletteLookup();
        Array.Clear(collisionMask.Pixels);
        Array.Clear(auxiliaryMask.Pixels);
        Array.Clear(previewMask.Pixels);
        bool[] solidPixels = new bool[Width * Height];

        for (int y = 0; y < Height; y++)
        {
            int row = y * scaled.Width * 4;
            int destRow = y * FileStride;
            for (int x = 0; x < Width; x++)
            {
                int p = row + x * 4;
                byte b = argb[p];
                byte g = argb[p + 1];
                byte r = argb[p + 2];
                byte a = argb[p + 3];
                if (a == 0)
                {
                    Pixels[destRow + x] = 0;
                }
                else
                {
                    int key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                    Pixels[destRow + x] = (byte)paletteLookup[key];
                }

                solidPixels[y * Width + x] = a >= SolidAlphaThreshold;
            }
        }

        ReplaceMasksFromSolidMap(collisionMask, auxiliaryMask, previewMask, solidPixels, Width, Height, cleanEdges: false, fillPreview: false);
        // This preview format requires the low-resolution mask to remain empty.
    }

    public static void ReplaceMasksFromIndexedTerrain(WolImage terrain, WolImage collisionMask, WolImage auxiliaryMask, WolImage previewMask)
    {
        if (collisionMask.BitsPerPixel != 1 || auxiliaryMask.BitsPerPixel != 1 || previewMask.BitsPerPixel != 1)
            throw new InvalidOperationException(L10n.Core("AutoMaskNotOneBit"));

        ValidateDimensions(terrain.Width, terrain.Height);
        bool[] solidPixels = new bool[terrain.Width * terrain.Height];
        for (int y = 0; y < terrain.Height; y++)
        {
            for (int x = 0; x < terrain.Width; x++)
                solidPixels[y * terrain.Width + x] = terrain.GetPixelIndex(x, y) != 0;
        }

        ReplaceMasksFromSolidMap(collisionMask, auxiliaryMask, previewMask, solidPixels, terrain.Width, terrain.Height, cleanEdges: false, fillPreview: false);
    }

    public static void NormalizeMasks(WolImage collisionMask, WolImage auxiliaryMask, WolImage previewMask)
    {
        if (collisionMask.BitsPerPixel != 1 || auxiliaryMask.BitsPerPixel != 1 || previewMask.BitsPerPixel != 1)
            throw new InvalidOperationException(L10n.Core("MaskNotOneBit"));

        ValidateDimensions(collisionMask.Width, collisionMask.Height);
        bool[] solidPixels = new bool[collisionMask.Width * collisionMask.Height];
        for (int y = 0; y < collisionMask.Height; y++)
        {
            for (int x = 0; x < collisionMask.Width; x++)
                solidPixels[y * collisionMask.Width + x] = collisionMask.GetPixelIndex(x, y) != 0;
        }

        ReplaceMasksFromSolidMap(collisionMask, auxiliaryMask, previewMask, solidPixels, collisionMask.Width, collisionMask.Height, cleanEdges: false, fillPreview: false);
    }

    public void RemapToPalette(IReadOnlyList<Color> targetPalette, int maxColors = MaxClientPaletteColors)
    {
        if (BitsPerPixel != 8 || !HasPalette || targetPalette.Count == 0)
            return;

        maxColors = Math.Clamp(maxColors, 1, MaxClientPaletteColors);
        var normalized = targetPalette
            .Take(maxColors)
            .ToList();
        if (normalized.Count == 0)
            return;

        byte[] remap = new byte[Palette.Count + 1];
        remap[0] = 0;
        for (int i = 1; i < remap.Length; i++)
            remap[i] = (byte)(FindNearestPaletteIndex(normalized, Palette[i - 1]) + 1);

        for (int i = 0; i < Pixels.Length; i++)
        {
            byte value = Pixels[i];
            Pixels[i] = value < remap.Length ? remap[value] : (byte)0;
        }

        Palette.Clear();
        Palette.AddRange(normalized);
    }

    public void RemapToBestPaletteSubset(IReadOnlyList<Color> allowedPalette, int maxColors = MaxClientPaletteColors)
    {
        if (BitsPerPixel != 8 || !HasPalette || allowedPalette.Count == 0)
            return;

        maxColors = Math.Clamp(maxColors, 1, MaxClientPaletteColors);
        var allowed = allowedPalette.Take(MaxClientPaletteColors).ToList();
        if (allowed.Count <= maxColors)
        {
            RemapToPalette(allowed, maxColors);
            return;
        }

        var weighted = new Dictionary<int, long>();
        foreach (byte value in Pixels)
        {
            if (value == 0 || value > Palette.Count)
                continue;

            int allowedIndex = FindNearestPaletteIndex(allowed, Palette[value - 1]);
            weighted.TryGetValue(allowedIndex, out long count);
            weighted[allowedIndex] = count + 1;
        }

        if (weighted.Count == 0)
        {
            RemapToPalette(allowed, maxColors);
            return;
        }

        var selectedIndexes = weighted
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(maxColors)
            .Select(pair => pair.Key)
            .Order()
            .ToList();

        for (int i = 0; selectedIndexes.Count < maxColors && i < allowed.Count; i++)
        {
            if (!selectedIndexes.Contains(i))
                selectedIndexes.Add(i);
        }

        selectedIndexes.Sort();
        var selected = selectedIndexes.Select(index => allowed[index]).ToList();
        RemapToPalette(selected, maxColors);
    }

    public void ClampPalette(int maxColors)
    {
        if (BitsPerPixel != 8 || !HasPalette || Palette.Count <= maxColors)
            return;

        RemapToPalette(Palette.Take(maxColors).ToList(), maxColors);
    }

    private byte FindNearestPaletteIndex(byte r, byte g, byte b)
    {
        return FindNearestPaletteIndex(Palette, Color.FromArgb(r, g, b));
    }

    private void RebuildPaletteFromArgb(byte[] argb, int maxColors = MaxClientPaletteColors)
    {
        if (BitsPerPixel != 8)
            return;

        maxColors = Math.Clamp(maxColors, 1, MaxClientPaletteColors);
        var bins = new Dictionary<int, QuantizedColor>(4096);
        for (int p = 0; p + 3 < argb.Length; p += 4)
        {
            byte b = argb[p];
            byte g = argb[p + 1];
            byte r = argb[p + 2];
            byte a = argb[p + 3];
            if (a < 96)
                continue;

            int r5 = r >> 3;
            int g5 = g >> 3;
            int b5 = b >> 3;
            int key = (r5 << 10) | (g5 << 5) | b5;
            if (!bins.TryGetValue(key, out QuantizedColor? color))
            {
                color = new QuantizedColor(r5, g5, b5);
                bins.Add(key, color);
            }

            color.Add(r, g, b);
        }

        Palette.Clear();
        if (bins.Count == 0)
        {
            Palette.Add(Color.Black);
            return;
        }

        var boxes = new List<ColorBox> { new(bins.Values.ToList()) };
        while (boxes.Count < maxColors)
        {
            int splitIndex = -1;
            long bestScore = -1;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Colors.Count <= 1)
                    continue;

                long score = boxes[i].SplitScore;
                if (score > bestScore)
                {
                    bestScore = score;
                    splitIndex = i;
                }
            }

            if (splitIndex < 0)
                break;

            ColorBox box = boxes[splitIndex];
            boxes.RemoveAt(splitIndex);
            (ColorBox left, ColorBox right) = box.Split();
            boxes.Add(left);
            boxes.Add(right);
        }

        foreach (ColorBox box in boxes.OrderByDescending(b => b.TotalCount))
            Palette.Add(box.AverageColor());
    }

    private int[] BuildPaletteLookup()
    {
        int[] lookup = new int[32 * 32 * 32];
        for (int r5 = 0; r5 < 32; r5++)
        {
            byte r = (byte)((r5 << 3) | (r5 >> 2));
            for (int g5 = 0; g5 < 32; g5++)
            {
                byte g = (byte)((g5 << 3) | (g5 >> 2));
                for (int b5 = 0; b5 < 32; b5++)
                {
                    byte b = (byte)((b5 << 3) | (b5 >> 2));
                    int key = (r5 << 10) | (g5 << 5) | b5;
                    lookup[key] = FindNearestPaletteIndex(r, g, b) + 1;
                }
            }
        }

        return lookup;
    }

    private static Bitmap ConvertTo32Bpp(Bitmap source, int width, int height)
    {
        ValidateDimensions(width, height);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return bitmap;
    }

    private static byte[] ReadArgb(Bitmap bitmap)
    {
        Rectangle rect = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] packed = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, packed, 0, packed.Length);
            if (stride == bitmap.Width * 4)
                return packed;

            byte[] compact = new byte[bitmap.Width * bitmap.Height * 4];
            for (int y = 0; y < bitmap.Height; y++)
                Buffer.BlockCopy(packed, y * stride, compact, y * bitmap.Width * 4, bitmap.Width * 4);
            return compact;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static float Brightness(byte r, byte g, byte b)
    {
        return (MathF.Max(r, MathF.Max(g, b)) + MathF.Min(r, MathF.Min(g, b))) / 510f;
    }

    private static void SetMaskBit(WolImage image, int x, int y)
    {
        int byteIndex = y * image.FileStride + (x >> 3);
        int bit = x & 7;
        image.Pixels[byteIndex] |= (byte)(1 << bit);
    }

    private static void ReplaceMasksFromSolidMap(
        WolImage collisionMask,
        WolImage auxiliaryMask,
        WolImage previewMask,
        bool[] solidPixels,
        int width,
        int height,
        bool cleanEdges,
        bool fillPreview)
    {
        if (collisionMask.Width != width || collisionMask.Height != height ||
            auxiliaryMask.Width != width || auxiliaryMask.Height != height)
        {
            throw new InvalidOperationException(L10n.Core("MaskTerrainSizeMismatch"));
        }

        bool[] normalized = cleanEdges ? CleanSolidMask(solidPixels, width, height) : solidPixels;
        Array.Clear(collisionMask.Pixels);
        Array.Clear(auxiliaryMask.Pixels);
        Array.Clear(previewMask.Pixels);
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (!normalized[row + x])
                    continue;

                SetMaskBit(collisionMask, x, y);
                SetMaskBit(auxiliaryMask, x, y);
            }
        }

        if (!fillPreview)
            return;

        for (int y = 0; y < previewMask.Height; y++)
        {
            for (int x = 0; x < previewMask.Width; x++)
            {
                bool solid = false;
                for (int yy = y * 8; yy < Math.Min(height, y * 8 + 8) && !solid; yy++)
                {
                    int row = yy * width;
                    for (int xx = x * 8; xx < Math.Min(width, x * 8 + 8); xx++)
                    {
                        if (!normalized[row + xx])
                            continue;

                        solid = true;
                        break;
                    }
                }

                if (solid)
                    SetMaskBit(previewMask, x, y);
            }
        }
    }

    private static bool[] CleanSolidMask(bool[] source, int width, int height)
    {
        bool[] cleaned = new bool[source.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                int neighbors = CountSolidNeighbors(source, width, height, x, y);
                cleaned[index] = source[index]
                    ? neighbors >= 2 || HasOrthogonalSolidNeighbor(source, width, height, x, y)
                    : neighbors >= 7;
            }
        }

        return cleaned;
    }

    private static int CountSolidNeighbors(bool[] source, int width, int height, int x, int y)
    {
        int count = 0;
        for (int yy = Math.Max(0, y - 1); yy <= Math.Min(height - 1, y + 1); yy++)
        {
            int row = yy * width;
            for (int xx = Math.Max(0, x - 1); xx <= Math.Min(width - 1, x + 1); xx++)
            {
                if (xx == x && yy == y)
                    continue;

                if (source[row + xx])
                    count++;
            }
        }

        return count;
    }

    private static bool HasOrthogonalSolidNeighbor(bool[] source, int width, int height, int x, int y)
    {
        return (x > 0 && source[y * width + x - 1]) ||
               (x + 1 < width && source[y * width + x + 1]) ||
               (y > 0 && source[(y - 1) * width + x]) ||
               (y + 1 < height && source[(y + 1) * width + x]);
    }

    private static List<Color> CreateDefaultPalette()
    {
        var colors = new List<Color> { Color.Black };
        int[] steps = [0, 51, 102, 153, 204, 255];
        foreach (int r in steps)
        foreach (int g in steps)
        foreach (int b in steps)
            colors.Add(Color.FromArgb(r, g, b));

        for (int i = 0; colors.Count < MaxClientPaletteColors; i++)
        {
            int v = Math.Min(255, i * 255 / 38);
            colors.Add(Color.FromArgb(v, v, v));
        }

        return colors.Take(MaxClientPaletteColors).ToList();
    }

    private void EnsureClientCompatiblePalette()
    {
        if (!HasPalette || Palette.Count <= MaxClientPaletteColors)
            return;

        var trimmed = Palette.Take(MaxClientPaletteColors).ToList();
        byte[] remap = new byte[Palette.Count + 1];
        remap[0] = 0;
        for (int i = 1; i < remap.Length; i++)
        {
            remap[i] = i <= MaxClientPaletteColors
                ? (byte)i
                : (byte)(FindNearestPaletteIndex(trimmed, Palette[i - 1]) + 1);
        }

        for (int i = 0; i < Pixels.Length; i++)
        {
            byte value = Pixels[i];
            Pixels[i] = value < remap.Length ? remap[value] : (byte)0;
        }

        Palette.Clear();
        Palette.AddRange(trimmed);
    }

    private static byte FindNearestPaletteIndex(IReadOnlyList<Color> palette, Color target)
    {
        int best = 0;
        int bestScore = int.MaxValue;
        for (int i = 0; i < palette.Count; i++)
        {
            Color candidate = palette[i];
            int dr = target.R - candidate.R;
            int dg = target.G - candidate.G;
            int db = target.B - candidate.B;
            int score = dr * dr + dg * dg + db * db;
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return (byte)best;
    }

    private sealed class QuantizedColor
    {
        public QuantizedColor(int r5, int g5, int b5)
        {
            R5 = r5;
            G5 = g5;
            B5 = b5;
        }

        public int R5 { get; }
        public int G5 { get; }
        public int B5 { get; }
        public int Count { get; private set; }
        public long RSum { get; private set; }
        public long GSum { get; private set; }
        public long BSum { get; private set; }

        public void Add(byte r, byte g, byte b)
        {
            Count++;
            RSum += r;
            GSum += g;
            BSum += b;
        }
    }

    private sealed class ColorBox
    {
        public ColorBox(List<QuantizedColor> colors)
        {
            Colors = colors;
            TotalCount = colors.Sum(c => c.Count);
            MinR = colors.Min(c => c.R5);
            MaxR = colors.Max(c => c.R5);
            MinG = colors.Min(c => c.G5);
            MaxG = colors.Max(c => c.G5);
            MinB = colors.Min(c => c.B5);
            MaxB = colors.Max(c => c.B5);
        }

        public List<QuantizedColor> Colors { get; }
        public long TotalCount { get; }
        private int MinR { get; }
        private int MaxR { get; }
        private int MinG { get; }
        private int MaxG { get; }
        private int MinB { get; }
        private int MaxB { get; }
        public long SplitScore => TotalCount * Math.Max(MaxR - MinR, Math.Max(MaxG - MinG, MaxB - MinB));

        public (ColorBox Left, ColorBox Right) Split()
        {
            int rRange = MaxR - MinR;
            int gRange = MaxG - MinG;
            int bRange = MaxB - MinB;
            IOrderedEnumerable<QuantizedColor> ordered = rRange >= gRange && rRange >= bRange
                ? Colors.OrderBy(c => c.R5)
                : gRange >= bRange ? Colors.OrderBy(c => c.G5) : Colors.OrderBy(c => c.B5);

            var sorted = ordered.ToList();
            long half = Math.Max(1, TotalCount / 2);
            long seen = 0;
            int split = 1;
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                seen += sorted[i].Count;
                if (seen >= half)
                {
                    split = i + 1;
                    break;
                }
            }

            split = Math.Clamp(split, 1, sorted.Count - 1);
            return (new ColorBox(sorted[..split]), new ColorBox(sorted[split..]));
        }

        public Color AverageColor()
        {
            if (TotalCount <= 0)
                return Color.Black;

            long r = 0;
            long g = 0;
            long b = 0;
            foreach (QuantizedColor color in Colors)
            {
                r += color.RSum;
                g += color.GSum;
                b += color.BSum;
            }

            return Color.FromArgb(
                (int)Math.Clamp(r / TotalCount, 0, 255),
                (int)Math.Clamp(g / TotalCount, 0, 255),
                (int)Math.Clamp(b / TotalCount, 0, 255));
        }
    }
}
