using WormsLandEditor.Formats;
using WormsLandEditor.Localization;

namespace WormsLandEditor.Commands;

internal static class MapCommands
{
    internal static void Roundtrip(string inputPath, string outputPath)
    {
        Save(LandFile.Load(inputPath), outputPath);
    }

    internal static void FromPng(string pngPath, string outputPath)
    {
        using var bitmap = new Bitmap(pngPath);
        ValidateMapBitmapSize(bitmap);
        LandFile land = LandFile.CreateFromBitmap(bitmap, template: null,
            name: Path.GetFileNameWithoutExtension(pngPath));
        Console.Error.WriteLine(L10n.Cli("FromPngScope"));
        Save(land, outputPath);
    }

    internal static void ReplacePng(string inputPath, string pngPath, string outputPath)
    {
        LandFile land = LandFile.Load(inputPath);
        using var bitmap = new Bitmap(pngPath);
        ValidateMapBitmapSize(bitmap);
        land.ReplacePng(bitmap);
        Save(land, outputPath);
    }

    internal static void ExportPng(string inputPath, string outputPath)
    {
        LandFile land = LandFile.Load(inputPath);
        using Bitmap bitmap = land.Terrain.ToBitmap(transparentZero: true);
        CommandLine.ReportWrites(outputPath);
        bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine(L10n.Cli("FileWritten", outputPath));
    }

    internal static void RepairSpawns(string inputPath, string outputPath)
    {
        LandFile land = LandFile.Load(inputPath);
        land.PrepareSpawnPointsForExport();
        Save(land, outputPath);
        InspectionCommands.PrintPointsReport(outputPath);
    }

    internal static void NormalizeMasks(string inputPath, string outputPath)
    {
        LandFile land = LandFile.Load(inputPath);
        WolImage.ReplaceMasksFromIndexedTerrain(land.Terrain, land.CollisionMask,
            land.AuxiliaryMask, land.PreviewMask);
        Save(land, outputPath);
    }

    internal static void SetClosedMap(string inputPath, string outputPath, string value)
    {
        bool closed = value switch
        {
            "1" or "true" or "yes" => true,
            "0" or "false" or "no" => false,
            _ => throw new CommandLine.UsageException(L10n.Cli("InvalidClosedMapValue", value))
        };
        LandFile land = LandFile.Load(inputPath);
        land.IsClosedMap = closed;
        Save(land, outputPath);
    }

    internal static void RemapPalette(string[] args)
    {
        // The old fifth argument was never read. Accept it only for compatibility.
        if (args.Length == 5)
            Console.Error.WriteLine(L10n.Cli("IgnoredWaterArgument", args[4]));
        LandFile land = LandFile.Load(args[1]);
        DirFile level = DirFile.Load(args[3]);
        PaletteUtil.RemapTerrainResources(land, level);
        CommandLine.ReportWrites(args[2], ResourceCommands.OutputPath(args[3]));
        land.Save(args[2]);
        Console.WriteLine(L10n.Cli("FileWritten", args[2]));
        level.SaveTo(ResourceCommands.OutputDirectory(args[3]));
        Console.WriteLine(L10n.Cli("FileWritten", ResourceCommands.OutputPath(args[3])));
        Console.WriteLine(InspectionCommands.DescribeLand(LandFile.Load(args[2])));
    }

    internal static void ImportLegacy(string[] args)
    {
        string kindText = args[1].ToLowerInvariant();
        // Validate every explicit option before the importer can read or write data.
        if (kindText is not ("auto" or "underwater" or "wa" or "armageddon" or "wwp"))
            throw new CommandLine.UsageException(L10n.Cli("InvalidLegacyKind", args[1]));
        int mapNumber = args.Length >= 7
            ? CommandLine.ParseInteger(args[6], LandFile.MinimumMapNumber, LandFile.MaximumMapNumber, "MapNumber")
            : 45;
        string waterPath = args.Length == 8 ? args[7] : @"data\water\Red";
        if (string.IsNullOrWhiteSpace(waterPath) || waterPath.Length > 127 ||
            waterPath.Any(c => c == '\0' || c > 127))
            throw new CommandLine.UsageException(L10n.Cli("InvalidWaterPath"));
        if (args.Length < 8)
            Console.Error.WriteLine(L10n.Cli("LegacyImportDefaults", mapNumber, waterPath));

        LegacyMapKind kind = kindText switch
        {
            "auto" => LegacyMapImporter.DetectKind(args[2]),
            "underwater" => LegacyMapKind.Underwater,
            "wa" or "armageddon" => LegacyMapKind.Armageddon,
            "wwp" => LegacyMapKind.WorldParty,
            _ => throw new InvalidOperationException()
        };
        LegacyMapImportResult result = LegacyMapImporter.Import(kind, args[2], args[3], mapNumber, waterPath);
        CommandLine.ReportWrites(args[4], Path.Combine(args[5], "Level.Dir"));
        result.Land.Save(args[4]);
        Console.WriteLine(L10n.Cli("FileWritten", args[4]));
        result.Resources.SaveTo(args[5]);
        Console.WriteLine(L10n.Cli("FileWritten", Path.Combine(args[5], "Level.Dir")));
        Console.WriteLine(L10n.Cli("ImportedSource", args[2], kind));
        Console.WriteLine(L10n.Cli("PathsReport", result.Land.NameA, result.Land.NameB));
        Console.WriteLine(InspectionCommands.DescribeLand(LandFile.Load(args[4])));
        InspectionCommands.PrintPaletteReport(Path.Combine(args[5], "Level.Dir"));
    }

    private static void Save(LandFile land, string outputPath)
    {
        CommandLine.ReportWrites(outputPath);
        land.Save(outputPath);
        Console.WriteLine(L10n.Cli("FileWritten", outputPath));
        Console.WriteLine(InspectionCommands.DescribeLand(LandFile.Load(outputPath)));
    }

    internal static void ExportLandDebug(string inputDat, string outputDirectory)
    {
        CommandLine.ReportWrites(new[] { "terrain.png", "collision.png", "auxiliary.png", "preview.png", "terrain_collision_overlay.png" }
            .Select(name => Path.Combine(outputDirectory, name)).ToArray());
        Directory.CreateDirectory(outputDirectory);
        LandFile land = LandFile.Load(inputDat);
        using Bitmap terrain = land.Terrain.ToBitmap();
        string terrainPath = Path.Combine(outputDirectory, "terrain.png");
        terrain.Save(terrainPath, System.Drawing.Imaging.ImageFormat.Png);

        string collisionPath = Path.Combine(outputDirectory, "collision.png");
        using (Bitmap collision = InspectionCommands.MaskToOpaqueBitmap(land.CollisionMask, Color.Black, Color.White))
            collision.Save(collisionPath, System.Drawing.Imaging.ImageFormat.Png);

        string auxiliaryPath = Path.Combine(outputDirectory, "auxiliary.png");
        using (Bitmap auxiliary = InspectionCommands.MaskToOpaqueBitmap(land.AuxiliaryMask, Color.Black, Color.White))
            auxiliary.Save(auxiliaryPath, System.Drawing.Imaging.ImageFormat.Png);

        string previewPath = Path.Combine(outputDirectory, "preview.png");
        using (Bitmap preview = InspectionCommands.MaskToOpaqueBitmap(land.PreviewMask, Color.Black, Color.White))
            preview.Save(previewPath, System.Drawing.Imaging.ImageFormat.Png);

        string overlayPath = Path.Combine(outputDirectory, "terrain_collision_overlay.png");
        using (Bitmap overlay = new(land.Width, land.Height))
        using (Graphics graphics = Graphics.FromImage(overlay))
        using (Bitmap mask = land.CollisionMask.ToMaskBitmap(Color.FromArgb(120, 255, 0, 0)))
        {
            graphics.DrawImageUnscaled(terrain, 0, 0);
            graphics.DrawImageUnscaled(mask, 0, 0);
            overlay.Save(overlayPath, System.Drawing.Imaging.ImageFormat.Png);
        }

        InspectionCommands.PrintSurfaceStats(land.CollisionMask);
        Console.WriteLine(terrainPath);
        Console.WriteLine(collisionPath);
        Console.WriteLine(auxiliaryPath);
        Console.WriteLine(previewPath);
        Console.WriteLine(overlayPath);
    }

    private static void ValidateMapBitmapSize(Bitmap bitmap)
    {
        if (bitmap.Width == LandFile.ExpectedWidth && bitmap.Height == LandFile.ExpectedHeight)
            return;

        throw new InvalidDataException(L10n.Cli("InvalidMapSize", LandFile.ExpectedWidth, LandFile.ExpectedHeight, bitmap.Width, bitmap.Height));
    }
}
