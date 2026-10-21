using WormsLandEditor.Formats;
using WormsLandEditor.Localization;

namespace WormsLandEditor.Commands;

internal static class InspectionCommands
{
    internal static void PrintPaletteReport(string path)
    {
        Console.WriteLine(path);
        if (Path.GetExtension(path).Equals(".dat", StringComparison.OrdinalIgnoreCase))
        {
            LandFile land = LandFile.Load(path);
            PrintImage(L10n.Cli("TerrainLabel"), land.Terrain);
            PrintImage(L10n.Cli("CollisionLabel"), land.CollisionMask);
            PrintImage(L10n.Cli("AuxiliaryLabel"), land.AuxiliaryMask);
            PrintImage(L10n.Cli("PreviewLabel"), land.PreviewMask);
            Console.WriteLine(L10n.Cli("PathsReport", land.NameA, land.NameB));
            return;
        }

        DirFile dir = DirFile.Load(path);
        bool hasParseErrors = false;
        foreach (DirResourceEntry entry in dir.Resources.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (entry.Name.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                {
                    int offset = 0;
                    WolImage image = WolImage.Read(entry.Data, ref offset);
                    PrintImage($"  {entry.Name}", image);
                }
                else if (entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
                {
                    WolSprite sprite = WolSprite.Read(entry.Data);
                    int maxIndex = sprite.Frames.SelectMany(f => f.Pixels).DefaultIfEmpty().Max();
                    int used = sprite.Frames.SelectMany(f => f.Pixels).Where(v => v != 0).Distinct().Count();
                    int uniquePalette = sprite.Palette.Select(color => color.ToArgb()).Distinct().Count();
                    Console.WriteLine(L10n.Cli("SpritePaletteReport", entry.Name, sprite.Frames.Count, sprite.Palette.Count, uniquePalette, used, maxIndex, entry.Data.Length));
                }
                else
                {
                    Console.WriteLine(L10n.Cli("ResourceBytesReport", entry.Name, entry.Data.Length));
                }
            }
            catch (Exception ex)
            {
                hasParseErrors = true;
                Console.Error.WriteLine(L10n.Cli("ResourceParseError", entry.Name, ex.GetType().Name, ex.Message, entry.Data.Length));
            }
        }

        if (hasParseErrors)
            throw new InvalidDataException(L10n.Cli("PaletteReportIncomplete", path));
    }

    internal static void PrintImage(string label, WolImage image)
    {
        int maxIndex = image.Pixels.DefaultIfEmpty().Max();
        int used = image.Pixels.Where(v => v != 0).Distinct().Count();
        int uniquePalette = image.Palette.Select(color => color.ToArgb()).Distinct().Count();
        Console.WriteLine(L10n.Cli("ImageReport", label, image.Width, image.Height, image.BitsPerPixel, image.Palette.Count, uniquePalette, used, maxIndex));
    }

    internal static void PrintMaskTerrainReport(string path)
    {
        LandFile land = LandFile.Load(path);
        long terrain0Mask0 = 0;
        long terrain0Mask1 = 0;
        long terrainSolidMask0 = 0;
        long terrainSolidMask1 = 0;
        long reversedTerrain0Mask0 = 0;
        long reversedTerrain0Mask1 = 0;
        long reversedTerrainSolidMask0 = 0;
        long reversedTerrainSolidMask1 = 0;
        long auxiliaryDiff = 0;
        long collisionPreviewSolidBlocks = 0;
        for (int y = 0; y < land.Height; y++)
        {
            for (int x = 0; x < land.Width; x++)
            {
                bool terrainSolid = land.Terrain.GetPixelIndex(x, y) != 0;
                int collision = land.CollisionMask.GetPixelIndex(x, y);
                int reversedX = (x & ~7) + (7 - (x & 7));
                int reversedCollision = land.CollisionMask.GetPixelIndex(reversedX, y);
                int auxiliary = land.AuxiliaryMask.GetPixelIndex(x, y);
                if (collision != auxiliary)
                    auxiliaryDiff++;

                if (terrainSolid)
                {
                    if (collision == 0)
                        terrainSolidMask0++;
                    else
                        terrainSolidMask1++;

                    if (reversedCollision == 0)
                        reversedTerrainSolidMask0++;
                    else
                        reversedTerrainSolidMask1++;
                }
                else
                {
                    if (collision == 0)
                        terrain0Mask0++;
                    else
                        terrain0Mask1++;

                    if (reversedCollision == 0)
                        reversedTerrain0Mask0++;
                    else
                        reversedTerrain0Mask1++;
                }
            }
        }

        for (int y = 0; y < land.PreviewMask.Height; y++)
        {
            for (int x = 0; x < land.PreviewMask.Width; x++)
            {
                if (land.PreviewMask.GetPixelIndex(x, y) != 0)
                    collisionPreviewSolidBlocks++;
            }
        }

        Console.WriteLine(path);
        Console.WriteLine(L10n.Cli("MaskEmptyEmpty", terrain0Mask0));
        Console.WriteLine(L10n.Cli("MaskEmptySolid", terrain0Mask1));
        Console.WriteLine(L10n.Cli("MaskSolidEmpty", terrainSolidMask0));
        Console.WriteLine(L10n.Cli("MaskSolidSolid", terrainSolidMask1));
        Console.WriteLine(L10n.Cli("ReversedMaskEmptyEmpty", reversedTerrain0Mask0));
        Console.WriteLine(L10n.Cli("ReversedMaskEmptySolid", reversedTerrain0Mask1));
        Console.WriteLine(L10n.Cli("ReversedMaskSolidEmpty", reversedTerrainSolidMask0));
        Console.WriteLine(L10n.Cli("ReversedMaskSolidSolid", reversedTerrainSolidMask1));
        Console.WriteLine(L10n.Cli("AuxiliaryDifference", auxiliaryDiff));
        Console.WriteLine(L10n.Cli("PreviewNonZero", collisionPreviewSolidBlocks));
        Console.WriteLine(L10n.Cli("CollisionFirstBytes", BitConverter.ToString(land.CollisionMask.Pixels.Take(32).ToArray())));
        Console.WriteLine(L10n.Cli("AuxiliaryFirstBytes", BitConverter.ToString(land.AuxiliaryMask.Pixels.Take(32).ToArray())));
        Console.WriteLine(L10n.Cli("PreviewFirstBytes", BitConverter.ToString(land.PreviewMask.Pixels.Take(32).ToArray())));
    }

    internal static Bitmap MaskToOpaqueBitmap(WolImage mask, Color solid, Color empty)
    {
        var bitmap = new Bitmap(mask.Width, mask.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < mask.Height; y++)
        {
            for (int x = 0; x < mask.Width; x++)
                bitmap.SetPixel(x, y, mask.GetPixelIndex(x, y) != 0 ? solid : empty);
        }

        return bitmap;
    }

    internal static void PrintSurfaceStats(WolImage mask)
    {
        int[] topByColumn = new int[mask.Width];
        Array.Fill(topByColumn, -1);
        for (int x = 0; x < mask.Width; x++)
        {
            for (int y = 0; y < mask.Height; y++)
            {
                if (mask.GetPixelIndex(x, y) == 0)
                    continue;

                topByColumn[x] = y;
                break;
            }
        }

        int columnsWithSurface = topByColumn.Count(y => y >= 0);
        int sameEightPixelBlocks = 0;
        int eightPixelBlocks = 0;
        for (int x = 0; x + 7 < topByColumn.Length; x += 8)
        {
            int first = topByColumn[x];
            if (first < 0)
                continue;

            eightPixelBlocks++;
            bool same = true;
            for (int xx = x + 1; xx < x + 8; xx++)
            {
                if (topByColumn[xx] == first)
                    continue;

                same = false;
                break;
            }

            if (same)
                sameEightPixelBlocks++;
        }

        int[] yRemainder = new int[8];
        foreach (int y in topByColumn)
        {
            if (y >= 0)
                yRemainder[y & 7]++;
        }

        Console.WriteLine(L10n.Cli("SurfaceReport", columnsWithSurface, mask.Width, sameEightPixelBlocks, eightPixelBlocks, string.Join(',', yRemainder)));
    }

    internal static void PrintPointsReport(string path)
    {
        LandFile land = LandFile.Load(path);
        Console.WriteLine(L10n.Cli("FileSummary", Path.GetFileName(path), DescribeLand(land)));
        for (int i = 0; i < land.PrimaryPoints.Count; i++)
        {
            LandPoint point = land.PrimaryPoints[i];
            int solidAt = IsSolidAt(land.CollisionMask, point.X, point.Y) ? 1 : 0;
            int solidBelow = -1;
            for (int y = point.Y; y < land.Height; y++)
            {
                if (IsSolidAt(land.CollisionMask, point.X, y))
                {
                    solidBelow = y;
                    break;
                }
            }

            int solidNearCount = 0;
            int solidAboveCount = 0;
            for (int y = Math.Max(0, point.Y - 32); y <= Math.Min(land.Height - 1, point.Y + 32); y++)
            {
                for (int x = Math.Max(0, point.X - 16); x <= Math.Min(land.Width - 1, point.X + 16); x++)
                {
                    if (!IsSolidAt(land.CollisionMask, x, y))
                        continue;

                    solidNearCount++;
                    if (y <= point.Y)
                        solidAboveCount++;
                }
            }

            Console.WriteLine(L10n.Cli("PointReport", i + 1, point.X, point.Y, solidAt, solidBelow, solidBelow < 0 ? -1 : solidBelow - point.Y, solidNearCount, solidAboveCount));
        }
    }

    private static bool IsSolidAt(WolImage mask, int x, int y)
    {
        if ((uint)x >= mask.Width || (uint)y >= mask.Height)
            return false;

        return mask.GetPixelIndex(x, y) != 0;
    }

    internal static void AddPaletteUnion(string path, HashSet<int> colors)
    {
        int before = colors.Count;
        Console.WriteLine(path);
        if (Path.GetExtension(path).Equals(".dat", StringComparison.OrdinalIgnoreCase))
        {
            LandFile land = LandFile.Load(path);
            AddColors(L10n.Cli("TerrainLabel"), land.Terrain.Palette, colors);
            Console.WriteLine(L10n.Cli("PalettePathTotal", colors.Count - before, colors.Count));
            return;
        }

        DirFile dir = DirFile.Load(path);
        foreach (DirResourceEntry entry in dir.Resources.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            List<Color> palette = PaletteUtil.ReadResourcePalette(entry);
            if (palette.Count > 0)
                AddColors($"  {entry.Name}", palette, colors);
        }

        Console.WriteLine(L10n.Cli("PalettePathTotal", colors.Count - before, colors.Count));
    }

    private static void AddColors(string label, IEnumerable<Color> palette, HashSet<int> colors)
    {
        int before = colors.Count;
        int count = 0;
        foreach (Color color in palette)
        {
            count++;
            colors.Add((color.R << 16) | (color.G << 8) | color.B);
        }

        Console.WriteLine(L10n.Cli("PaletteColors", label, count, colors.Count - before, colors.Count));
    }

    internal static void PrintSlotConflicts(string landPath, string dirPath)
    {
        LandFile land = LandFile.Load(landPath);
        DirFile dir = DirFile.Load(dirPath);
        Console.WriteLine($"{landPath}");
        Console.WriteLine($"{dirPath}");
        Console.WriteLine(L10n.Cli("LandPaletteSize", land.Terrain.Palette.Count));

        foreach (DirResourceEntry entry in dir.Resources.OrderBy(e => e.OriginalOffset))
        {
            List<Color> palette = PaletteUtil.ReadResourcePalette(entry);
            if (palette.Count == 0)
                continue;

            int compared = Math.Min(land.Terrain.Palette.Count, palette.Count);
            int conflicts = 0;
            int firstConflict = -1;
            for (int i = 0; i < compared; i++)
            {
                if (SameRgb(land.Terrain.Palette[i], palette[i]))
                    continue;

                conflicts++;
                if (firstConflict < 0)
                    firstConflict = i + 1;
            }

            string first = firstConflict < 0 ? "-" : firstConflict.ToString();
            Console.WriteLine(L10n.Cli("SlotConflictReport", entry.Name, palette.Count, compared, conflicts, first));
        }
    }

    internal static void PrintSpriteInfo(string dirPath, string name)
    {
        DirFile dir = DirFile.Load(dirPath);
        DirResourceEntry entry = dir.Find(name) ?? throw new InvalidOperationException(L10n.Cli("ResourceNotFound", dirPath, name));
        WolSprite sprite = WolSprite.Read(entry.Data);
        Console.WriteLine($"{dirPath}");
        int uniquePalette = sprite.Palette.Select(color => color.ToArgb()).Distinct().Count();
        long zeroPixels = 0;
        long nonZeroPixels = 0;
        int zeroFrames = 0;
        int minIndex = int.MaxValue;
        int maxIndex = 0;
        foreach (WolSpriteFrame frame in sprite.Frames)
        {
            int frameZeros = 0;
            foreach (byte pixel in frame.Pixels)
            {
                if (pixel == 0)
                {
                    zeroPixels++;
                    frameZeros++;
                    continue;
                }

                nonZeroPixels++;
                minIndex = Math.Min(minIndex, pixel);
                maxIndex = Math.Max(maxIndex, pixel);
            }

            if (frameZeros > 0)
                zeroFrames++;
        }

        string minIndexText = minIndex == int.MaxValue ? "-" : minIndex.ToString();
        Console.WriteLine(L10n.Cli("SpriteReport", name, sprite.Flags, sprite.Palette.Count, uniquePalette, sprite.BlockTable.Length, sprite.Rate, sprite.FieldA, sprite.FieldB, sprite.FieldC, sprite.Frames.Count, entry.Data.Length, zeroPixels, zeroFrames, nonZeroPixels, minIndexText, maxIndex));
        for (int i = 0; i < Math.Min(sprite.Frames.Count, 8); i++)
        {
            WolSpriteFrame frame = sprite.Frames[i];
            Console.WriteLine(L10n.Cli("SpriteFrameReport", i, frame.Left, frame.Top, frame.Right, frame.Bottom, frame.Width, frame.Height, frame.Pixels.Length));
        }
    }

    internal static void PrintLegacySpriteInfo(string dirPath, string name)
    {
        DirFile dir = DirFile.Load(dirPath);
        DirResourceEntry entry = dir.Find(name) ?? throw new InvalidOperationException(L10n.Cli("ResourceNotFound", dirPath, name));
        byte[] data = entry.Data;
        int length = BitConverter.ToInt32(data, 4);
        byte bpp = data[8];
        byte flags = data[9];
        int offset = 10;
        ushort colors = BitConverter.ToUInt16(data, offset);
        offset += 2 + colors * 3;
        Console.WriteLine($"{dirPath}");
        Console.WriteLine(L10n.Cli("LegacySpriteReport", name, data.Length, length, bpp, flags, colors, offset));
        if ((flags & 0x40) != 0)
        {
            ushort blockCount = BitConverter.ToUInt16(data, offset);
            Console.WriteLine(L10n.Cli("CompressedBlocks", blockCount));
            return;
        }

        ushort fieldA = BitConverter.ToUInt16(data, offset);
        ushort frameCount = BitConverter.ToUInt16(data, offset + 2);
        ushort rate = BitConverter.ToUInt16(data, offset + 4);
        ushort fieldB = BitConverter.ToUInt16(data, offset + 6);
        ushort fieldC = BitConverter.ToUInt16(data, offset + 8);
        int tableStart = offset + 14;
        int pixelStart = tableStart + frameCount * 12;
        Console.WriteLine(L10n.Cli("LegacySpriteFields", fieldA, frameCount, rate, fieldB, fieldC, tableStart, pixelStart));
        for (int i = 0; i < Math.Min(frameCount, (ushort)8); i++)
        {
            int e = tableStart + i * 12;
            int rawOffset = BitConverter.ToInt32(data, e);
            int leShift = rawOffset >> 8;
            int be = data[e] << 24 | data[e + 1] << 16 | data[e + 2] << 8 | data[e + 3];
            int beShift = be >> 8;
            short leftLe = BitConverter.ToInt16(data, e + 4);
            short topLe = BitConverter.ToInt16(data, e + 6);
            short rightLe = BitConverter.ToInt16(data, e + 8);
            short bottomLe = BitConverter.ToInt16(data, e + 10);
            short leftBe = (short)(data[e + 4] << 8 | data[e + 5]);
            short topBe = (short)(data[e + 6] << 8 | data[e + 7]);
            short rightBe = (short)(data[e + 8] << 8 | data[e + 9]);
            short bottomBe = (short)(data[e + 10] << 8 | data[e + 11]);
            Console.WriteLine(L10n.Cli("LegacyFrameBytes", i, rawOffset, leShift, beShift, leftLe, topLe, rightLe, bottomLe, leftBe, topBe, rightBe, bottomBe, BitConverter.ToString(data, e, 12)));
        }
    }

    internal static void PrintLegacySpritePixels(string dirPath, string name)
    {
        DirFile dir = DirFile.Load(dirPath);
        DirResourceEntry entry = dir.Find(name) ?? throw new InvalidOperationException(L10n.Cli("ResourceNotFound", dirPath, name));
        byte[] data = entry.Data;
        int offset = 10;
        ushort colors = BitConverter.ToUInt16(data, offset);
        offset += 2;
        var palette = new List<Color>();
        for (int i = 0; i < colors; i++)
        {
            palette.Add(Color.FromArgb(data[offset], data[offset + 1], data[offset + 2]));
            offset += 3;
        }

        ushort frameCount = BitConverter.ToUInt16(data, offset + 2);
        int tableStart = offset + 14;
        int pixelStart = tableStart + frameCount * 12;
        int dataOffset = BitConverter.ToInt32(data, tableStart) >> 8;
        short left = ReadBigEndianInt16(data, tableStart + 4);
        short top = ReadBigEndianInt16(data, tableStart + 6);
        short right = ReadBigEndianInt16(data, tableStart + 8);
        short bottom = ReadBigEndianInt16(data, tableStart + 10);
        int width = right - left;
        int height = bottom - top;
        var counts = new Dictionary<byte, int>();
        for (int i = 0; i < width * height; i++)
        {
            byte value = data[pixelStart + dataOffset + i];
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        Console.WriteLine($"{dirPath}");
        Console.WriteLine(L10n.Cli("LegacyFirstFrame", name, width, height, left, top, right, bottom, colors));
        foreach (var pair in counts.OrderByDescending(p => p.Value).Take(16))
        {
            string color = pair.Key > 0 && pair.Key <= palette.Count
                ? $"{palette[pair.Key - 1].R},{palette[pair.Key - 1].G},{palette[pair.Key - 1].B}"
                : L10n.Cli("TransparentOrOutside");
            Console.WriteLine(L10n.Cli("PixelIndexReport", pair.Key, pair.Value, color));
        }
    }

    private static bool SameRgb(Color left, Color right)
    {
        return left.R == right.R && left.G == right.G && left.B == right.B;
    }

    private static short ReadBigEndianInt16(byte[] data, int offset)
    {
        return unchecked((short)(data[offset] << 8 | data[offset + 1]));
    }

    internal static string DescribeLand(LandFile land) => L10n.Cli("LandSummary",
        land.Width, land.Height, L10n.Cli(land.IsClosedMap ? "Yes" : "No"),
        land.PrimaryPoints.Count, land.SecondaryPoints.Count,
        land.Terrain.BitsPerPixel, land.Terrain.Palette.Count,
        land.CollisionMask.Width, land.CollisionMask.Height,
        land.AuxiliaryMask.Width, land.AuxiliaryMask.Height,
        land.PreviewMask.Width, land.PreviewMask.Height);

    internal static void Inspect(string[] args)
    {
        Console.WriteLine(DescribeLand(LandFile.Load(args[1])));
        if (args.Length == 3)
        {
            DirFile dir = DirFile.Load(args[2]);
            Console.WriteLine(L10n.Cli("DirectorySummary", dir.DirectoryStart, dir.DataEnd,
                dir.DirectorySize, dir.EntryNames.Count == 0
                    ? L10n.Cli("NoNamedEntries") : string.Join(", ", dir.EntryNames)));
        }
    }

    internal static void PaletteUnion(string[] args)
    {
        var colors = new HashSet<int>();
        foreach (string path in args.Skip(1))
            AddPaletteUnion(path, colors);
        Console.WriteLine(L10n.Cli("PaletteUnionTotal", colors.Count));
    }
}
