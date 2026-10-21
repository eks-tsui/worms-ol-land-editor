using System.Buffers.Binary;
using System.Text;

namespace WormsLandEditor.Formats;

public static class PaletteUtil
{
    public static void RemapTerrainResources(LandFile land, DirFile resources)
    {
        List<Color> palette = BuildTerrainOnlyPalette(land.Terrain, LandFile.MaximumTerrainColors);
        resources.RemapTerrainPaletteResources(palette);
        land.Terrain.RemapToPalette(palette, palette.Count);
    }

    public static List<Color> BuildGameplayPalette(LandFile land, DirFile resources, DirFile? water, int maxColors = 224)
    {
        maxColors = Math.Clamp(maxColors, 1, 255);
        var colors = new List<Color>();
        var seen = new HashSet<int>();

        if (water is not null)
        {
            foreach (DirResourceEntry entry in water.Resources.OrderBy(e => e.OriginalOffset))
            foreach (Color color in ReadResourcePalette(entry))
                AddUnique(colors, seen, color, maxColors);
        }

        var weighted = new Dictionary<int, WeightedColor>();
        AccumulateImageColors(land.Terrain, weighted, weightMultiplier: 4);
        AccumulateDirectoryColors(resources, weighted);

        foreach (WeightedColor color in weighted.Values.OrderByDescending(c => c.Weight))
        {
            if (colors.Count >= maxColors)
                break;

            AddUnique(colors, seen, color.ToColor(), maxColors);
        }

        if (colors.Count == 0)
            colors.Add(Color.Black);

        return colors;
    }

    public static List<Color> BuildSharedPalette(params DirFile?[] directories)
    {
        var colors = new List<Color>();
        var seen = new HashSet<int>();

        foreach (DirFile? directory in directories)
        {
            if (directory is null)
                continue;

            foreach (DirResourceEntry entry in directory.Resources.OrderBy(e => e.OriginalOffset))
            {
                foreach (Color color in ReadResourcePalette(entry))
                {
                    int key = (color.R << 16) | (color.G << 8) | color.B;
                    if (seen.Add(key))
                        colors.Add(color);
                }
            }
        }

        return colors.Take(255).ToList();
    }

    public static List<Color> BuildTerrainPaletteWithBudget(WolImage terrain, DirFile resources, DirFile? water, int maxTotalColors = 255)
    {
        maxTotalColors = Math.Clamp(maxTotalColors, 1, 255);

        var colors = new List<Color>();
        var seen = new HashSet<int>();

        AddVisibleDirectoryColors(colors, seen, resources, maxTotalColors);
        if (water is not null)
            AddVisibleDirectoryColors(colors, seen, water, maxTotalColors);

        var weighted = new Dictionary<int, WeightedColor>();
        AccumulateImageColors(terrain, weighted, weightMultiplier: 4);
        foreach (WeightedColor color in weighted.Values.OrderByDescending(c => c.Weight))
        {
            if (colors.Count >= maxTotalColors)
                break;

            AddUnique(colors, seen, color.ToColor(), maxTotalColors);
        }

        if (colors.Count == 0)
            colors.Add(Color.Black);

        return colors;
    }

    public static List<Color> BuildLandCompatiblePalette(WolImage terrain, DirFile resources, int maxLandColors = 96)
    {
        maxLandColors = Math.Clamp(maxLandColors, 1, 255);

        var colors = new List<Color>();
        var seen = new HashSet<int>();

        AddVisibleDirectoryColors(colors, seen, resources, maxLandColors);

        var weighted = new Dictionary<int, WeightedColor>();
        AccumulateImageColors(terrain, weighted, weightMultiplier: 4);
        foreach (WeightedColor color in weighted.Values.OrderByDescending(c => c.Weight))
        {
            if (colors.Count >= maxLandColors)
                break;

            AddUnique(colors, seen, color.ToColor(), maxLandColors);
        }

        if (colors.Count == 0)
            colors.Add(Color.Black);

        return colors;
    }

    public static List<Color> BuildTerrainOnlyPalette(WolImage terrain, int maxColors = LandFile.MaximumTerrainColors)
    {
        maxColors = Math.Clamp(maxColors, 1, 255);

        var colors = new List<Color>();
        var seen = new HashSet<int>();
        var weighted = new Dictionary<int, WeightedColor>();
        AccumulateImageColors(terrain, weighted, weightMultiplier: 4);

        foreach (WeightedColor color in weighted.Values.OrderByDescending(c => c.Weight))
        {
            if (colors.Count >= maxColors)
                break;

            AddUnique(colors, seen, color.ToColor(), maxColors);
        }

        if (colors.Count == 0)
            colors.Add(Color.Black);

        return colors;
    }

    public static List<Color> BuildResourceOnlyPalette(DirFile resources, int maxColors = 128)
    {
        maxColors = Math.Clamp(maxColors, 1, 255);

        var colors = new List<Color>();
        var seen = new HashSet<int>();
        var weighted = new Dictionary<int, WeightedColor>();
        AccumulateDirectoryColors(resources, weighted);

        foreach (WeightedColor color in weighted.Values.OrderByDescending(c => c.Weight))
        {
            if (colors.Count >= maxColors)
                break;

            AddUnique(colors, seen, color.ToColor(), maxColors);
        }

        foreach (Color color in BuildSharedPalette(resources))
        {
            if (colors.Count >= maxColors)
                break;

            AddUnique(colors, seen, color, maxColors);
        }

        if (colors.Count == 0)
            colors.Add(Color.Black);

        return colors;
    }

    private static void AddVisibleDirectoryColors(List<Color> colors, HashSet<int> seen, DirFile directory, int maxColors)
    {
        foreach (DirResourceEntry entry in directory.Resources.OrderBy(e => e.OriginalOffset))
        {
            int before = colors.Count;
            try
            {
                if (entry.Name.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                {
                    int offset = 0;
                    WolImage image = WolImage.Read(entry.Data, ref offset);
                    AddVisibleImageColors(colors, seen, image, maxColors);
                }
                else if (entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
                {
                    AddVisibleSpriteColors(colors, seen, WolSprite.Read(entry.Data), maxColors);
                }
            }
            catch
            {
            }

            if (colors.Count != before)
                continue;

            foreach (Color color in ReadResourcePalette(entry))
                AddUnique(colors, seen, color, maxColors);
        }
    }

    private static void AddVisibleImageColors(List<Color> colors, HashSet<int> seen, WolImage image, int maxColors)
    {
        if (!image.HasPalette || image.Palette.Count == 0)
            return;

        foreach (byte value in image.Pixels)
        {
            if (value == 0 || value > image.Palette.Count)
                continue;

            AddUnique(colors, seen, image.Palette[value - 1], maxColors);
        }
    }

    private static void AddVisibleSpriteColors(List<Color> colors, HashSet<int> seen, WolSprite sprite, int maxColors)
    {
        if (sprite.Palette.Count == 0)
            return;

        foreach (WolSpriteFrame frame in sprite.Frames)
        foreach (byte value in frame.Pixels)
        {
            if (value == 0 || value > sprite.Palette.Count)
                continue;

            AddUnique(colors, seen, sprite.Palette[value - 1], maxColors);
        }
    }

    private static void AccumulateDirectoryColors(DirFile directory, Dictionary<int, WeightedColor> weighted)
    {
        foreach (DirResourceEntry entry in directory.Resources)
        {
            try
            {
                if (entry.Name.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                {
                    int offset = 0;
                    AccumulateImageColors(WolImage.Read(entry.Data, ref offset), weighted, weightMultiplier: 1);
                }
                else if (entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
                {
                    AccumulateSpriteColors(WolSprite.Read(entry.Data), weighted);
                }
            }
            catch
            {
            }
        }
    }

    private static void AccumulateImageColors(WolImage image, Dictionary<int, WeightedColor> weighted, int weightMultiplier)
    {
        if (!image.HasPalette || image.Palette.Count == 0)
            return;

        foreach (byte value in image.Pixels)
        {
            if (value == 0 || value > image.Palette.Count)
                continue;

            AddWeighted(weighted, image.Palette[value - 1], weightMultiplier);
        }
    }

    private static void AccumulateSpriteColors(WolSprite sprite, Dictionary<int, WeightedColor> weighted)
    {
        if (sprite.Palette.Count == 0)
            return;

        foreach (WolSpriteFrame frame in sprite.Frames)
        foreach (byte value in frame.Pixels)
        {
            if (value == 0 || value > sprite.Palette.Count)
                continue;

            AddWeighted(weighted, sprite.Palette[value - 1], 1);
        }
    }

    private static void AddWeighted(Dictionary<int, WeightedColor> weighted, Color color, int weight)
    {
        int key = ColorKey(color);
        if (!weighted.TryGetValue(key, out WeightedColor? existing))
        {
            existing = new WeightedColor(color);
            weighted.Add(key, existing);
        }

        existing.Weight += weight;
    }

    private static bool AddUnique(List<Color> colors, HashSet<int> seen, Color color, int maxColors)
    {
        if (colors.Count >= maxColors)
            return false;

        if (!seen.Add(ColorKey(color)))
            return false;

        colors.Add(color);
        return true;
    }

    private static int ColorKey(Color color) => (color.R << 16) | (color.G << 8) | color.B;

    public static List<Color> ReadResourcePalette(DirResourceEntry entry)
    {
        ReadOnlySpan<byte> data = entry.Data;
        if (entry.Name.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
            return ReadImgPalette(data);

        if (entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
            return ReadSprPalette(data);

        return [];
    }

    private static List<Color> ReadImgPalette(ReadOnlySpan<byte> data)
    {
        int offset = 0;
        if (ReadAscii(data, ref offset, 4) != "IMG\x1A")
            return [];

        _ = ReadInt32(data, ref offset);
        SkipNullTerminatedAscii(data, ref offset);
        ushort flags = ReadUInt16(data, ref offset);
        if ((flags & 0x8000) == 0)
            return [];

        return ReadPalette(data, ref offset);
    }

    private static List<Color> ReadSprPalette(ReadOnlySpan<byte> data)
    {
        int offset = 0;
        if (ReadAscii(data, ref offset, 4) != "SPR\x1A")
            return [];

        _ = ReadInt32(data, ref offset);
        SkipNullTerminatedAscii(data, ref offset);
        _ = ReadUInt16(data, ref offset);
        return ReadPalette(data, ref offset);
    }

    private static List<Color> ReadPalette(ReadOnlySpan<byte> data, ref int offset)
    {
        ushort count = ReadUInt16(data, ref offset);
        var palette = new List<Color>(count);
        for (int i = 0; i < count && offset + 3 <= data.Length; i++)
        {
            byte r = data[offset++];
            byte g = data[offset++];
            byte b = data[offset++];
            palette.Add(Color.FromArgb(r, g, b));
        }

        return palette;
    }

    private static string ReadAscii(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        string value = Encoding.ASCII.GetString(data.Slice(offset, count));
        offset += count;
        return value;
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        int value = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
        offset += 4;
        return value;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;
        return value;
    }

    private static void SkipNullTerminatedAscii(ReadOnlySpan<byte> data, ref int offset)
    {
        while (offset < data.Length && data[offset] != 0)
            offset++;

        if (offset < data.Length)
            offset++;
    }

    private sealed class WeightedColor
    {
        private readonly Color _color;

        public WeightedColor(Color color)
        {
            _color = color;
        }

        public long Weight { get; set; }

        public Color ToColor() => _color;
    }
}
