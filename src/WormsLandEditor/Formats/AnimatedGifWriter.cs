using WormsLandEditor.Localization;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WormsLandEditor.Formats;

public static class AnimatedGifWriter
{
    private const int PaletteSize = 256;
    private const byte TransparentIndex = 0;

    public static void Save(string path, IReadOnlyList<Bitmap> frames, int delayMilliseconds)
    {
        if (frames.Count == 0)
            throw new InvalidDataException(L10n.Core("NoGifFramesToExport"));

        GifPalette palette = GifPalette.Build(frames);
        ImageCodecInfo encoder = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(codec => string.Equals(codec.MimeType, "image/gif", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(L10n.Core("GifEncoderUnavailable"));

        using var first = CreateIndexedFrame(frames[0], palette);
        SetFrameDelay(first, frames.Count, delayMilliseconds);

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.MultiFrame);
        first.Save(path, encoder, parameters);

        parameters.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.FrameDimensionTime);
        for (int i = 1; i < frames.Count; i++)
        {
            using Bitmap frame = CreateIndexedFrame(frames[i], palette);
            first.SaveAdd(frame, parameters);
        }

        parameters.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.Flush);
        first.SaveAdd(parameters);
        PatchGraphicControlExtensions(path);
    }

    private static Bitmap CreateIndexedFrame(Bitmap source, GifPalette palette)
    {
        var indexed = new Bitmap(source.Width, source.Height, PixelFormat.Format8bppIndexed);
        ColorPalette colorPalette = indexed.Palette;
        for (int i = 0; i < PaletteSize; i++)
            colorPalette.Entries[i] = palette.Colors[i];
        indexed.Palette = colorPalette;

        Rectangle rect = new(0, 0, source.Width, source.Height);
        BitmapData data = indexed.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] pixels = new byte[stride * source.Height];
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                    pixels[y * stride + x] = palette.GetIndex(source.GetPixel(x, y));
            }

            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            indexed.UnlockBits(data);
        }

        return indexed;
    }

    private static void SetFrameDelay(Image image, int frameCount, int delayMilliseconds)
    {
        int delay = Math.Max(1, delayMilliseconds / 10);
        byte[] delays = new byte[frameCount * 4];
        for (int i = 0; i < frameCount; i++)
            BitConverter.GetBytes(delay).CopyTo(delays, i * 4);

        PropertyItem item = (PropertyItem)RuntimeHelpers.GetUninitializedObject(typeof(PropertyItem));
        item.Id = 0x5100;
        item.Type = 4;
        item.Len = delays.Length;
        item.Value = delays;
        image.SetPropertyItem(item);
    }

    private static void PatchGraphicControlExtensions(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        for (int i = 0; i + 7 < data.Length; i++)
        {
            if (data[i] != 0x21 || data[i + 1] != 0xF9 || data[i + 2] != 0x04)
                continue;

            data[i + 3] = (byte)((data[i + 3] & 0xE0) | 0x09);
            data[i + 6] = TransparentIndex;
        }

        if (data.Length > 11 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F')
            data[11] = TransparentIndex;

        File.WriteAllBytes(path, data);
    }

    private sealed class GifPalette
    {
        private readonly Dictionary<int, byte> _exactIndexes;
        private readonly bool _useColorCube;

        private GifPalette(Color[] colors, Dictionary<int, byte> exactIndexes, bool useColorCube)
        {
            Colors = colors;
            _exactIndexes = exactIndexes;
            _useColorCube = useColorCube;
        }

        public Color[] Colors { get; }

        public static GifPalette Build(IReadOnlyList<Bitmap> frames)
        {
            var usedRgb = new HashSet<int>();
            foreach (Bitmap frame in frames)
            {
                for (int y = 0; y < frame.Height; y++)
                {
                    for (int x = 0; x < frame.Width; x++)
                    {
                        Color color = frame.GetPixel(x, y);
                        if (color.A >= 96)
                            usedRgb.Add(RgbKey(color));
                    }
                }
            }

            Color transparent = FindUnusedTransparentColor(usedRgb);
            var colors = new List<Color> { transparent };
            var indexes = new Dictionary<int, byte>();

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
                        int key = RgbKey(opaque);
                        if (indexes.ContainsKey(key))
                            continue;

                        if (colors.Count >= PaletteSize)
                            return BuildColorCubePalette();

                        indexes[key] = (byte)colors.Count;
                        colors.Add(opaque);
                    }
                }
            }

            while (colors.Count < PaletteSize)
                colors.Add(Color.Black);

            return new GifPalette(colors.ToArray(), indexes, useColorCube: false);
        }

        public byte GetIndex(Color color)
        {
            if (color.A < 96)
                return TransparentIndex;

            if (_useColorCube)
            {
                int r = color.R * 5 / 255;
                int g = color.G * 5 / 255;
                int b = color.B * 5 / 255;
                return checked((byte)(1 + r * 36 + g * 6 + b));
            }

            return _exactIndexes.TryGetValue(RgbKey(color), out byte index) ? index : TransparentIndex;
        }

        private static GifPalette BuildColorCubePalette()
        {
            var colors = new Color[PaletteSize];
            var indexes = new Dictionary<int, byte>();
            colors[TransparentIndex] = Color.FromArgb(0, 1, 2, 3);

            int index = 1;
            for (int r = 0; r < 6; r++)
            {
                for (int g = 0; g < 6; g++)
                {
                    for (int b = 0; b < 6; b++)
                    {
                        Color color = Color.FromArgb(r * 51, g * 51, b * 51);
                        colors[index] = color;
                        indexes[RgbKey(color)] = (byte)index;
                        index++;
                    }
                }
            }

            for (; index < PaletteSize; index++)
                colors[index] = Color.Black;

            return new GifPalette(colors, indexes, useColorCube: true);
        }

        private static int RgbKey(Color color)
        {
            return color.R << 16 | color.G << 8 | color.B;
        }

        private static Color FindUnusedTransparentColor(HashSet<int> usedRgb)
        {
            ReadOnlySpan<Color> candidates =
            [
                Color.FromArgb(0, 1, 2, 3),
                Color.FromArgb(0, 255, 0, 255),
                Color.FromArgb(0, 0, 255, 255),
                Color.FromArgb(0, 255, 255, 0)
            ];

            foreach (Color candidate in candidates)
            {
                if (!usedRgb.Contains(RgbKey(candidate)))
                    return candidate;
            }

            for (int r = 1; r < 256; r += 17)
            {
                for (int g = 2; g < 256; g += 17)
                {
                    for (int b = 3; b < 256; b += 17)
                    {
                        Color candidate = Color.FromArgb(0, r, g, b);
                        if (!usedRgb.Contains(RgbKey(candidate)))
                            return candidate;
                    }
                }
            }

            return Color.FromArgb(0, 1, 2, 3);
        }
    }
}
