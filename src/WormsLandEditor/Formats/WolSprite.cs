using WormsLandEditor.Localization;
using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WormsLandEditor.Formats;

public sealed class WolSprite
{
    public const string Magic = "SPR\x1A";
    private const int MaxPaletteColors = 255;

    public string Name { get; set; } = string.Empty;
    public ushort Flags { get; set; }
    public List<Color> Palette { get; } = [];
    public byte[] BlockTable { get; set; } = [];
    public int Rate { get; set; }
    public ushort FieldA { get; set; }
    public ushort FieldB { get; set; }
    public ushort FieldC { get; set; }
    public List<WolSpriteFrame> Frames { get; } = [];

    public static WolSprite Read(ReadOnlySpan<byte> data)
    {
        int offset = 0;
        string magic = System.Text.Encoding.ASCII.GetString(BinaryUtil.ReadBytes(data, ref offset, 4));
        if (magic != Magic)
            throw new InvalidDataException(L10n.Core("ExpectedSpriteBlock"));

        int length = BinaryUtil.ReadInt32(data, ref offset);
        if (length < 8 || length > data.Length)
            throw new InvalidDataException(L10n.Core("InvalidSpriteLength"));

        data = data[..length];

        var sprite = new WolSprite
        {
            Name = BinaryUtil.ReadNullTerminatedAscii(data, ref offset),
            Flags = BinaryUtil.ReadUInt16(data, ref offset)
        };

        if (sprite.Flags != 0x8008 && sprite.Flags != 0xC008)
            throw new InvalidDataException(L10n.Core("UnsupportedSpriteFlags"));

        ushort colorCount = BinaryUtil.ReadUInt16(data, ref offset);
        if (colorCount == 0 || colorCount >= 256)
            throw new InvalidDataException(L10n.Core("InvalidSpritePaletteCount"));

        for (int i = 0; i < colorCount; i++)
        {
            byte r = BinaryUtil.ReadByte(data, ref offset);
            byte g = BinaryUtil.ReadByte(data, ref offset);
            byte b = BinaryUtil.ReadByte(data, ref offset);
            sprite.Palette.Add(Color.FromArgb(r, g, b));
        }

        if ((sprite.Flags & 0x4000) != 0)
        {
            int blockStart = offset;
            ushort blockCount = BinaryUtil.ReadUInt16(data, ref offset);
            if (blockCount * 12 > length - offset)
                throw new InvalidDataException(L10n.Core("InvalidSpriteBlockTable"));
            offset += blockCount * 12;
            sprite.BlockTable = data[blockStart..offset].ToArray();
        }

        sprite.Rate = BinaryUtil.ReadInt32(data, ref offset);
        sprite.FieldA = BinaryUtil.ReadUInt16(data, ref offset);
        sprite.FieldB = BinaryUtil.ReadUInt16(data, ref offset);
        sprite.FieldC = BinaryUtil.ReadUInt16(data, ref offset);
        ushort frameCount = BinaryUtil.ReadUInt16(data, ref offset);
        if (frameCount == 0)
            throw new InvalidDataException(L10n.Core("SpriteHasNoFrames"));

        int frameTable = offset;
        int pixelDataStart = frameTable + frameCount * 12;
        if (pixelDataStart > length)
            throw new InvalidDataException(L10n.Core("InvalidSpriteFrameTable"));

        for (int i = 0; i < frameCount; i++)
        {
            int frameOffset = frameTable + i * 12;
            int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(data[frameOffset..]);
            short left = BinaryPrimitives.ReadInt16LittleEndian(data[(frameOffset + 4)..]);
            short top = BinaryPrimitives.ReadInt16LittleEndian(data[(frameOffset + 6)..]);
            short right = BinaryPrimitives.ReadInt16LittleEndian(data[(frameOffset + 8)..]);
            short bottom = BinaryPrimitives.ReadInt16LittleEndian(data[(frameOffset + 10)..]);
            int width = right - left;
            int height = bottom - top;
            if (width < 0 || height < 0)
                throw new InvalidDataException(L10n.Core("InvalidSpriteFrameBounds"));

            long pixelLength = (long)width * height;
            if (dataOffset < 0 || dataOffset > length - pixelDataStart ||
                pixelLength > length - pixelDataStart - dataOffset)
                throw new InvalidDataException(L10n.Core("InvalidSpriteFramePixels"));

            int pixelOffset = pixelDataStart + dataOffset;
            sprite.Frames.Add(new WolSpriteFrame(left, top, right, bottom, data.Slice(pixelOffset, (int)pixelLength).ToArray()));
        }

        return sprite;
    }

    public List<Bitmap> ToBitmaps()
    {
        var frames = new List<Bitmap>();
        foreach (WolSpriteFrame frame in Frames)
            frames.Add(IndexedPixelsToBitmap(frame.Pixels, frame.Width, frame.Height, Palette));

        return frames;
    }

    public List<Bitmap> ToCanvasBitmaps()
    {
        var frames = new List<Bitmap>();
        Size canvas = CanvasSize();
        foreach (WolSpriteFrame frame in Frames)
            frames.Add(IndexedPixelsToCanvasBitmap(frame, canvas.Width, canvas.Height, Palette));

        return frames;
    }

    public Size ReplacementImageSize()
    {
        int width = Math.Max(1, Frames.Select(frame => (int)frame.Right).DefaultIfEmpty(1).Max());
        int height = Math.Max(1, Frames.Select(frame => (int)frame.Bottom).DefaultIfEmpty(1).Max());
        return new Size(width, height);
    }

    public Size CanvasSize()
    {
        Size replacement = ReplacementImageSize();
        int width = Math.Max(replacement.Width, FieldB);
        int height = Math.Max(replacement.Height, FieldC);
        return new Size(Math.Max(1, width), Math.Max(1, height));
    }

    public void ValidateReplacementImageSize(string fileName, string resourceName)
    {
        Size expected = ReplacementImageSize();
        using Image image = Image.FromFile(fileName);
        if (image.Width == expected.Width && image.Height == expected.Height)
            return;

        throw new InvalidDataException(L10n.Core("ResourceSizeMismatch", resourceName, expected.Width, expected.Height, image.Width, image.Height));
    }

    public void ReplaceFromGif(string fileName, bool preserveCanvas = false, bool preservePalette = false)
    {
        List<Bitmap> gifFrames = LoadGifFrames(fileName);
        if (gifFrames.Count == 0)
            throw new InvalidDataException(L10n.Core("GifHasNoFrames"));
        if (gifFrames.Count >= 256)
            throw new InvalidDataException(L10n.Core("SpriteFrameCountLimit"));

        try
        {
            var palette = preservePalette && Palette.Count > 0
                ? Palette.ToList()
                : BuildPalette(gifFrames);
            var replacement = new List<WolSpriteFrame>();
            foreach (Bitmap bitmap in gifFrames)
            {
                Rectangle bounds = preserveCanvas
                    ? new Rectangle(0, 0, Math.Max(1, bitmap.Width), Math.Max(1, bitmap.Height))
                    : FindOpaqueBounds(bitmap);
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    bounds = new Rectangle(0, 0, 1, 1);

                byte[] pixels = QuantizeFrame(bitmap, bounds, palette);
                replacement.Add(new WolSpriteFrame(
                    checked((short)bounds.Left),
                    checked((short)bounds.Top),
                    checked((short)bounds.Right),
                    checked((short)bounds.Bottom),
                    pixels));
            }

            if (!preservePalette || Palette.Count == 0)
            {
                Palette.Clear();
                Palette.AddRange(palette);
            }

            Frames.Clear();
            Frames.AddRange(replacement);
            FieldB = checked((ushort)gifFrames.Max(frame => frame.Width));
            FieldC = checked((ushort)gifFrames.Max(frame => frame.Height));
        }
        finally
        {
            foreach (Bitmap frame in gifFrames)
                frame.Dispose();
        }
    }

    public void ReplaceFromPng(string fileName, bool preserveCanvas = false, bool preservePalette = false)
    {
        using var bitmap = new Bitmap(fileName);
        var palette = preservePalette && Palette.Count > 0
            ? Palette.ToList()
            : BuildPalette([bitmap]);
        Rectangle bounds = preserveCanvas
            ? new Rectangle(0, 0, Math.Max(1, bitmap.Width), Math.Max(1, bitmap.Height))
            : FindOpaqueBounds(bitmap);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            bounds = new Rectangle(0, 0, Math.Max(1, bitmap.Width), Math.Max(1, bitmap.Height));

        byte[] pixels = QuantizeFrame(bitmap, bounds, palette);
        if (!preservePalette || Palette.Count == 0)
        {
            Palette.Clear();
            Palette.AddRange(palette);
        }

        Frames.Clear();
        Frames.Add(new WolSpriteFrame(
            checked((short)bounds.Left),
            checked((short)bounds.Top),
            checked((short)bounds.Right),
            checked((short)bounds.Bottom),
            pixels));
        FieldB = checked((ushort)bitmap.Width);
        FieldC = checked((ushort)bitmap.Height);
    }

    public byte[] ToBytes()
    {
        if (Palette.Count == 0 || Palette.Count >= 256)
            throw new InvalidDataException(L10n.Core("SpritePaletteCountRange"));
        if (Frames.Count == 0 || Frames.Count >= 256)
            throw new InvalidDataException(L10n.Core("SpriteFrameCountRange"));

        using var stream = new MemoryStream();
        BinaryUtil.WriteAscii(stream, Magic, nullTerminate: false);
        BinaryUtil.WriteInt32(stream, 0);
        BinaryUtil.WriteAscii(stream, Name, nullTerminate: true);
        BinaryUtil.WriteUInt16(stream, Flags);
        BinaryUtil.WriteUInt16(stream, checked((ushort)Palette.Count));
        foreach (Color color in Palette)
        {
            stream.WriteByte(color.R);
            stream.WriteByte(color.G);
            stream.WriteByte(color.B);
        }

        if ((Flags & 0x4000) != 0)
            stream.Write(BlockTable);

        BinaryUtil.WriteInt32(stream, Rate);
        BinaryUtil.WriteUInt16(stream, FieldA);
        BinaryUtil.WriteUInt16(stream, FieldB);
        BinaryUtil.WriteUInt16(stream, FieldC);
        BinaryUtil.WriteUInt16(stream, checked((ushort)Frames.Count));

        long frameTable = stream.Position;
        for (int i = 0; i < Frames.Count * 12; i++)
            stream.WriteByte(0);

        int pixelOffset = 0;
        for (int i = 0; i < Frames.Count; i++)
        {
            WolSpriteFrame frame = Frames[i];
            long entry = frameTable + i * 12L;
            BinaryUtil.PatchInt32(stream, entry, pixelOffset);
            PatchInt16(stream, entry + 4, frame.Left);
            PatchInt16(stream, entry + 6, frame.Top);
            PatchInt16(stream, entry + 8, frame.Right);
            PatchInt16(stream, entry + 10, frame.Bottom);
            stream.Write(frame.Pixels);
            pixelOffset += frame.Pixels.Length;
        }

        BinaryUtil.PatchInt32(stream, 4, checked((int)stream.Length));
        return stream.ToArray();
    }

    public void RemapToPalette(IReadOnlyList<Color> targetPalette, int maxColors = MaxPaletteColors)
    {
        if (Palette.Count == 0 || targetPalette.Count == 0)
            return;

        maxColors = Math.Clamp(maxColors, 1, MaxPaletteColors);
        var normalized = targetPalette.Take(maxColors).ToList();
        if (normalized.Count == 0)
            return;

        byte[] remap = new byte[Palette.Count + 1];
        remap[0] = 0;
        for (int i = 1; i < remap.Length; i++)
            remap[i] = (byte)(FindNearestPaletteIndex(normalized, Palette[i - 1]) + 1);

        foreach (WolSpriteFrame frame in Frames)
        {
            for (int i = 0; i < frame.Pixels.Length; i++)
            {
                byte value = frame.Pixels[i];
                frame.Pixels[i] = value < remap.Length ? remap[value] : (byte)0;
            }
        }

        Palette.Clear();
        Palette.AddRange(normalized);
    }

    public void RemapToBestPaletteSubset(IReadOnlyList<Color> allowedPalette, int maxColors = MaxPaletteColors)
    {
        if (Palette.Count == 0 || allowedPalette.Count == 0)
            return;

        maxColors = Math.Clamp(maxColors, 1, MaxPaletteColors);
        var allowed = allowedPalette.Take(MaxPaletteColors).ToList();
        if (allowed.Count <= maxColors)
        {
            RemapToPalette(allowed, maxColors);
            return;
        }

        var weighted = new Dictionary<int, long>();
        foreach (WolSpriteFrame frame in Frames)
        {
            foreach (byte value in frame.Pixels)
            {
                if (value == 0 || value > Palette.Count)
                    continue;

                int allowedIndex = FindNearestPaletteIndex(allowed, Palette[value - 1]);
                weighted.TryGetValue(allowedIndex, out long count);
                weighted[allowedIndex] = count + 1;
            }
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
        if (Palette.Count <= maxColors)
            return;

        RemapToPalette(Palette.Take(maxColors).ToList(), maxColors);
    }

    public void EnsurePaletteSize(int colorCount)
    {
        if (colorCount <= 0 || colorCount >= 256)
            throw new InvalidDataException(L10n.Core("SpritePaletteCountRange"));

        if (Palette.Count > colorCount)
        {
            RemapToPalette(Palette.Take(colorCount).ToList(), colorCount);
            return;
        }

        var seen = new HashSet<int>();
        for (int i = 0; i < Palette.Count; i++)
        {
            if (seen.Add(Palette[i].ToArgb()))
                continue;

            Palette[i] = NextUnusedPalettePadding(seen);
            seen.Add(Palette[i].ToArgb());
        }

        while (Palette.Count < colorCount)
        {
            Color padding = NextUnusedPalettePadding(seen);
            Palette.Add(padding);
            seen.Add(padding.ToArgb());
        }
    }

    private static Color NextUnusedPalettePadding(HashSet<int> seen)
    {
        for (int i = 0; i < 4096; i++)
        {
            int r = 8 + i % 16 * 15;
            int g = 8 + i / 16 % 16 * 15;
            int b = 8 + i / 256 % 16 * 15;
            Color color = Color.FromArgb(r, g, b);
            if (!seen.Contains(color.ToArgb()))
                return color;
        }

        throw new InvalidDataException(L10n.Core("SpritePaddingColorUnavailable"));
    }

    public void ClampCanvasSize(int maxWidth, int maxHeight)
    {
        int width = Frames.Select(frame => (int)frame.Right).DefaultIfEmpty(1).Max();
        int height = Frames.Select(frame => (int)frame.Bottom).DefaultIfEmpty(1).Max();
        FieldB = checked((ushort)Math.Clamp(width, 1, maxWidth));
        FieldC = checked((ushort)Math.Clamp(height, 1, maxHeight));
    }

    public void SetAnimationRate(int rate)
    {
        Rate = rate;
    }

    private static List<Bitmap> LoadGifFrames(string fileName)
    {
        using Image image = Image.FromFile(fileName);
        WolImage.ValidateDimensions(image.Width, image.Height);
        if (image.FrameDimensionsList.Length == 0)
            throw new InvalidDataException(L10n.Core("GifHasNoFrames"));
        FrameDimension dimension = new(image.FrameDimensionsList[0]);
        int count = image.GetFrameCount(dimension);
        if (count == 0)
            throw new InvalidDataException(L10n.Core("GifHasNoFrames"));
        if (count >= 256)
            throw new InvalidDataException(L10n.Core("SpriteFrameCountLimit"));
        var frames = new List<Bitmap>(count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                image.SelectActiveFrame(dimension, i);
                var frame = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
                frames.Add(frame);
                using Graphics graphics = Graphics.FromImage(frame);
                graphics.DrawImage(image, 0, 0, image.Width, image.Height);
            }

            return frames;
        }
        catch
        {
            foreach (Bitmap frame in frames)
                frame.Dispose();
            throw;
        }
    }

    private static List<Color> BuildPalette(IEnumerable<Bitmap> frames)
    {
        var palette = new List<Color>();
        var seen = new HashSet<int>();
        foreach (Bitmap frame in frames)
        {
            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++)
                {
                    Color color = frame.GetPixel(x, y);
                    if (color.A < 96)
                        continue;

                    Color opaque = Color.FromArgb(color.R, color.G, color.B);
                    if (seen.Add(opaque.ToArgb()))
                    {
                        palette.Add(opaque);
                        if (palette.Count >= MaxPaletteColors)
                            return palette;
                    }
                }
            }
        }

        if (palette.Count == 0)
            palette.Add(Color.Black);

        return palette;
    }

    private static Rectangle FindOpaqueBounds(Bitmap bitmap)
    {
        int left = bitmap.Width;
        int top = bitmap.Height;
        int right = -1;
        int bottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A < 96)
                    continue;

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return right < left || bottom < top ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static byte[] QuantizeFrame(Bitmap bitmap, Rectangle bounds, IReadOnlyList<Color> palette)
    {
        byte[] pixels = new byte[bounds.Width * bounds.Height];
        for (int y = 0; y < bounds.Height; y++)
        {
            for (int x = 0; x < bounds.Width; x++)
            {
                Color color = bitmap.GetPixel(bounds.Left + x, bounds.Top + y);
                pixels[y * bounds.Width + x] = color.A < 96 ? (byte)0 : (byte)(FindNearestPaletteIndex(palette, color) + 1);
            }
        }

        return pixels;
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

    private static Bitmap IndexedPixelsToBitmap(ReadOnlySpan<byte> pixels, int width, int height, IReadOnlyList<Color> palette)
    {
        WolImage.ValidateDimensions(Math.Max(1, width), Math.Max(1, height));
        var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
        if (width <= 0 || height <= 0)
            return bitmap;

        Rectangle rect = new(0, 0, width, height);
        BitmapData locked = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(locked.Stride);
            byte[] argb = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                int sourceRow = y * width;
                int destRow = y * stride;
                for (int x = 0; x < width; x++)
                {
                    byte index = pixels[sourceRow + x];
                    Color color = index > 0 && palette.Count >= index ? palette[index - 1] : Color.Transparent;
                    int dest = destRow + x * 4;
                    argb[dest] = color.B;
                    argb[dest + 1] = color.G;
                    argb[dest + 2] = color.R;
                    argb[dest + 3] = index == 0 ? (byte)0 : (byte)255;
                }
            }

            Marshal.Copy(argb, 0, locked.Scan0, argb.Length);
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }

        return bitmap;
    }

    private static Bitmap IndexedPixelsToCanvasBitmap(WolSpriteFrame frame, int canvasWidth, int canvasHeight, IReadOnlyList<Color> palette)
    {
        int width = Math.Max(1, canvasWidth);
        int height = Math.Max(1, canvasHeight);
        WolImage.ValidateDimensions(width, height);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        if (frame.Width <= 0 || frame.Height <= 0)
            return bitmap;

        Rectangle rect = new(0, 0, width, height);
        BitmapData locked = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(locked.Stride);
            byte[] argb = new byte[stride * height];
            for (int y = 0; y < frame.Height; y++)
            {
                int destY = frame.Top + y;
                if (destY < 0 || destY >= height)
                    continue;

                for (int x = 0; x < frame.Width; x++)
                {
                    int destX = frame.Left + x;
                    if (destX < 0 || destX >= width)
                        continue;

                    byte index = frame.Pixels[y * frame.Width + x];
                    Color color = index > 0 && palette.Count >= index ? palette[index - 1] : Color.Transparent;
                    int dest = destY * stride + destX * 4;
                    argb[dest] = color.B;
                    argb[dest + 1] = color.G;
                    argb[dest + 2] = color.R;
                    argb[dest + 3] = index == 0 ? (byte)0 : (byte)255;
                }
            }

            Marshal.Copy(argb, 0, locked.Scan0, argb.Length);
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }

        return bitmap;
    }

    private static void PatchInt16(Stream stream, long position, short value)
    {
        long old = stream.Position;
        stream.Position = position;
        Span<byte> buffer = stackalloc byte[2];
        buffer[0] = (byte)value;
        buffer[1] = (byte)(value >> 8);
        stream.Write(buffer);
        stream.Position = old;
    }
}

public sealed class WolSpriteFrame
{
    public WolSpriteFrame(short left, short top, short right, short bottom, byte[] pixels)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
        Pixels = pixels;
    }

    public short Left { get; }
    public short Top { get; }
    public short Right { get; }
    public short Bottom { get; }
    public byte[] Pixels { get; }
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
