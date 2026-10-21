using WormsLandEditor.Localization;
using System.Text;

namespace WormsLandEditor.Formats;

public enum LegacyMapKind
{
    WorldParty,
    Underwater,
    Armageddon
}

public sealed class LegacyMapImportResult
{
    public required LandFile Land { get; init; }
    public required DirFile Resources { get; init; }
    public required string SourceDescription { get; init; }
}

public static class LegacyMapImporter
{
    private static readonly string[] RequiredResources = ["gradient.img", "back.spr", "debris.spr", "soil.img", "grass.img"];
    private static readonly Dictionary<string, string> WorldPartyImageToTheme = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ARTCLASS"] = "Art",
        ["BEACH"] = "Beach_Legacy",
        ["CANYONS"] = "Desert",
        ["CITY"] = "Urban",
        ["DESERT"] = "Desert",
        ["DINO"] = "Dinos",
        ["FARMYARD"] = "Farm_Legacy",
        ["FRUIT"] = "Fruit",
        ["FRUITY2"] = "Fruit",
        ["MARS"] = "Space",
        ["NEWYORK"] = "Manhattan",
        ["POLAR"] = "Snow",
        ["SNOW"] = "Snow",
        ["SPACE"] = "Space",
        ["T17SPACE"] = "Space",
        ["TECH"] = "Tools",
        ["TREASURE"] = "Pirate",
        ["WOODS"] = "Forest"
    };

    private static readonly Dictionary<string, string> WorldPartyThemeToImage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Art"] = "ARTCLASS",
        ["Beach_Legacy"] = "BEACH",
        ["Desert"] = "DESERT",
        ["Desert_Legacy"] = "DESERT",
        ["Dinos"] = "DINO",
        ["Farm_Legacy"] = "FARMYARD",
        ["Forest"] = "WOODS",
        ["Forest_Legacy"] = "WOODS",
        ["Fruit"] = "FRUIT",
        ["Manhattan"] = "NEWYORK",
        ["Pirate"] = "TREASURE",
        ["Snow"] = "SNOW",
        ["Space"] = "SPACE",
        ["Tools"] = "TECH",
        ["Urban"] = "CITY"
    };

    public static LegacyMapImportResult Import(LegacyMapKind kind, string selectedPath, string templateDataDirectory, int mapNumber, string waterResourcePath)
    {
        ILegacyMapAdapter adapter = CreateAdapter(kind);
        string sourceData = adapter.FindSourceDataDirectory(selectedPath);
        string? sourceLand = adapter.ResolveSourceLand(selectedPath, sourceData);
        string? sourceLevelDir = adapter.ResolveSourceLevelDir(selectedPath, sourceData, sourceLand);
        LandFile land = adapter.ImportLand(sourceLand, mapNumber, waterResourcePath);

        DirFile resources = DirFile.LoadTemplate(templateDataDirectory);
        if (sourceLevelDir is not null)
        {
            Dictionary<string, byte[]> converted = ConvertLegacyLevelDir(adapter, sourceLevelDir);
            foreach (string name in RequiredResources)
            {
                if (!converted.TryGetValue(name, out byte[]? data))
                    continue;

                if (IsEmptyImgResource(data))
                    continue;

                if (!ShouldUseSourceResource(kind, name))
                    continue;

                data = RemapToTemplateResourcePalette(name, data, resources);
                resources.ReplaceResourceBytes(name, data);
            }

        }

        PaletteUtil.RemapTerrainResources(land, resources);

        return new LegacyMapImportResult
        {
            Land = land,
            Resources = resources,
            SourceDescription = DescribeSources(sourceLand, sourceLevelDir)
        };
    }

    public static LegacyMapImportResult ImportAuto(string selectedPath, string templateDataDirectory, int mapNumber, string waterResourcePath)
    {
        LegacyMapKind kind = DetectKind(selectedPath);
        return Import(kind, selectedPath, templateDataDirectory, mapNumber, waterResourcePath);
    }

    public static LegacyMapKind DetectKind(string selectedPath)
    {
        string? sourceData = TryFindSourceDataDirectory(selectedPath);
        string fileName = File.Exists(selectedPath) ? Path.GetFileName(selectedPath) : string.Empty;
        string extension = Path.GetExtension(selectedPath);

        if (File.Exists(selectedPath))
        {
            byte[] data = File.ReadAllBytes(selectedPath);
            if (data.Length >= 4 && data.AsSpan(0, 4).SequenceEqual("LND\x1B"u8))
                return LegacyMapKind.Underwater;

            if (fileName.StartsWith("land", StringComparison.OrdinalIgnoreCase) &&
                extension.Equals(".dat", StringComparison.OrdinalIgnoreCase))
            {
                return LegacyMapKind.Underwater;
            }
        }

        if (sourceData is not null)
        {
            if (Directory.Exists(Path.Combine(sourceData, "Map")) &&
                Directory.Exists(Path.Combine(sourceData, "Mfx")))
            {
                return LegacyMapKind.Underwater;
            }

            if (LooksLikeArmageddonPath(selectedPath, sourceData))
                return LegacyMapKind.Armageddon;

            if (Directory.Exists(Path.Combine(sourceData, "Level")) ||
                Directory.Exists(Path.Combine(sourceData, "Custom")) ||
                Directory.Exists(Path.Combine(sourceData, "Bitmaps")) ||
                Directory.Exists(Path.Combine(sourceData, "Image")))
            {
                return LegacyMapKind.WorldParty;
            }
        }

        if (extension.Equals(".wam", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".img", StringComparison.OrdinalIgnoreCase))
        {
            return LooksLikeArmageddonPath(selectedPath, sourceData) ? LegacyMapKind.Armageddon : LegacyMapKind.WorldParty;
        }

        throw new InvalidDataException(L10n.Core("LegacySourceUnrecognized"));
    }

    private static ILegacyMapAdapter CreateAdapter(LegacyMapKind kind)
    {
        return kind switch
        {
            LegacyMapKind.WorldParty => new WorldPartyLegacyMapAdapter(),
            LegacyMapKind.Underwater => new UnderwaterLegacyMapAdapter(),
            LegacyMapKind.Armageddon => new ArmageddonLegacyMapAdapter(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), L10n.Core("UnknownLegacyMapKind", kind))
        };
    }

    private interface ILegacyMapAdapter
    {
        LegacyMapKind Kind { get; }
        string FindSourceDataDirectory(string selectedPath);
        string? ResolveSourceLand(string selectedPath, string sourceData);
        string? ResolveSourceLevelDir(string selectedPath, string sourceData, string? sourceLand);
        LandFile ImportLand(string? sourceLand, int mapNumber, string waterResourcePath);
        byte[] ConvertResource(string name, byte[] data);
    }

    private sealed class UnderwaterLegacyMapAdapter : ILegacyMapAdapter
    {
        public LegacyMapKind Kind => LegacyMapKind.Underwater;

        public string FindSourceDataDirectory(string selectedPath) => LegacyMapImporter.FindSourceDataDirectory(Kind, selectedPath);

        public string? ResolveSourceLand(string selectedPath, string sourceData)
        {
            return LegacyMapImporter.ResolveSourceLand(Kind, selectedPath, sourceData);
        }

        public string? ResolveSourceLevelDir(string selectedPath, string sourceData, string? sourceLand)
        {
            return LegacyMapImporter.ResolveSourceLevelDir(Kind, selectedPath, sourceData, sourceLand);
        }

        public LandFile ImportLand(string? sourceLand, int mapNumber, string waterResourcePath)
        {
            return ImportUnderwaterLand(sourceLand ?? throw new InvalidDataException(L10n.Core("UnderwaterSourceRequired")), mapNumber, waterResourcePath);
        }

        public byte[] ConvertResource(string name, byte[] data)
        {
            return ConvertLegacyResource(Kind, name, data, LegacyCompression.WwpLz);
        }
    }

    private sealed class WorldPartyLegacyMapAdapter : ILegacyMapAdapter
    {
        public LegacyMapKind Kind => LegacyMapKind.WorldParty;

        public string FindSourceDataDirectory(string selectedPath) => LegacyMapImporter.FindSourceDataDirectory(Kind, selectedPath);

        public string? ResolveSourceLand(string selectedPath, string sourceData)
        {
            return LegacyMapImporter.ResolveSourceLand(Kind, selectedPath, sourceData);
        }

        public string? ResolveSourceLevelDir(string selectedPath, string sourceData, string? sourceLand)
        {
            return LegacyMapImporter.ResolveSourceLevelDir(Kind, selectedPath, sourceData, sourceLand);
        }

        public LandFile ImportLand(string? sourceLand, int mapNumber, string waterResourcePath)
        {
            return sourceLand is null
                ? CreateEmptyWorldPartyLand(mapNumber, waterResourcePath)
                : ImportWorldPartyLand(sourceLand, mapNumber, waterResourcePath);
        }

        public byte[] ConvertResource(string name, byte[] data)
        {
            return ConvertLegacyResource(Kind, name, data, LegacyCompression.WwpLz);
        }
    }

    private sealed class ArmageddonLegacyMapAdapter : ILegacyMapAdapter
    {
        public LegacyMapKind Kind => LegacyMapKind.Armageddon;

        public string FindSourceDataDirectory(string selectedPath) => LegacyMapImporter.FindSourceDataDirectory(Kind, selectedPath);

        public string? ResolveSourceLand(string selectedPath, string sourceData)
        {
            return LegacyMapImporter.ResolveSourceLand(Kind, selectedPath, sourceData);
        }

        public string? ResolveSourceLevelDir(string selectedPath, string sourceData, string? sourceLand)
        {
            return LegacyMapImporter.ResolveSourceLevelDir(Kind, selectedPath, sourceData, sourceLand);
        }

        public LandFile ImportLand(string? sourceLand, int mapNumber, string waterResourcePath)
        {
            return sourceLand is null
                ? CreateEmptyWorldPartyLand(mapNumber, waterResourcePath)
                : ImportWorldPartyLand(sourceLand, mapNumber, waterResourcePath);
        }

        public byte[] ConvertResource(string name, byte[] data)
        {
            return ConvertLegacyResource(Kind, name, data, LegacyCompression.WwpLz);
        }
    }

    private static string FindSourceDataDirectory(LegacyMapKind kind, string selectedPath)
    {
        string? directory = TryFindSourceDataDirectory(selectedPath);
        if (directory is not null)
        {
            if (kind == LegacyMapKind.Underwater &&
                Directory.Exists(Path.Combine(directory, "Mfx")))
            {
                return directory;
            }

            if ((kind == LegacyMapKind.WorldParty || kind == LegacyMapKind.Armageddon) &&
                (Directory.Exists(Path.Combine(directory, "Level")) ||
                 Directory.Exists(Path.Combine(directory, "Custom")) ||
                 Directory.Exists(Path.Combine(directory, "Bitmaps")) ||
                 Directory.Exists(Path.Combine(directory, "Image"))))
            {
                return directory;
            }
        }

        throw new InvalidDataException(L10n.Core("LegacyDataDirectoryNotFound"));
    }

    private static string? TryFindSourceDataDirectory(string selectedPath)
    {
        string? directory = File.Exists(selectedPath) ? Path.GetDirectoryName(selectedPath) : selectedPath;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            string name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (name.Equals("data", StringComparison.OrdinalIgnoreCase) &&
                (Directory.Exists(Path.Combine(directory, "Mfx")) ||
                 Directory.Exists(Path.Combine(directory, "Map")) ||
                 Directory.Exists(Path.Combine(directory, "Level")) ||
                 Directory.Exists(Path.Combine(directory, "Custom")) ||
                 Directory.Exists(Path.Combine(directory, "Bitmaps")) ||
                 Directory.Exists(Path.Combine(directory, "Image"))))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static bool LooksLikeArmageddonPath(string selectedPath, string? sourceData)
    {
        string text = $"{selectedPath}|{sourceData}".Replace('/', '\\');
        return text.Contains("Worms Armageddon", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Armageddon", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("\\WA\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveSourceLand(LegacyMapKind kind, string selectedPath, string sourceData)
    {
        if (File.Exists(selectedPath))
        {
            string name = Path.GetFileName(selectedPath);
            if (kind == LegacyMapKind.Underwater && name.StartsWith("land", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                return selectedPath;

            if ((kind == LegacyMapKind.WorldParty || kind == LegacyMapKind.Armageddon) && IsWorldPartyMapFile(selectedPath) && !name.Equals("Level.dir", StringComparison.OrdinalIgnoreCase))
                return selectedPath;
        }

        if (kind == LegacyMapKind.Underwater)
        {
            int? number = TryMapNumberFromPath(selectedPath);
            if (number.HasValue)
            {
                string land = Path.Combine(sourceData, "Map", $"land{number.Value:000}.dat");
                if (File.Exists(land))
                    return land;
            }
        }

        if ((kind == LegacyMapKind.WorldParty || kind == LegacyMapKind.Armageddon) && File.Exists(selectedPath) && Path.GetFileName(selectedPath).Equals("Level.dir", StringComparison.OrdinalIgnoreCase))
            return ResolveWorldPartyLandFromTheme(selectedPath, sourceData);

        return null;
    }

    private static string? ResolveSourceLevelDir(LegacyMapKind kind, string selectedPath, string sourceData, string? sourceLand)
    {
        if (File.Exists(selectedPath) && Path.GetFileName(selectedPath).Equals("Level.dir", StringComparison.OrdinalIgnoreCase))
            return selectedPath;

        if (kind == LegacyMapKind.Underwater)
        {
            int? number = TryMapNumberFromPath(sourceLand ?? selectedPath);
            if (number.HasValue)
            {
                string path = Path.Combine(sourceData, "Mfx", $"Tmap{number.Value:00}", "Level.dir");
                if (File.Exists(path))
                    return path;
            }
        }

        if (kind == LegacyMapKind.WorldParty || kind == LegacyMapKind.Armageddon)
        {
            string? level = ResolveWorldPartyLevelDir(selectedPath, sourceData);
            if (level is not null)
                return level;
        }

        if (kind == LegacyMapKind.WorldParty || kind == LegacyMapKind.Armageddon)
            return null;

        throw new FileNotFoundException(L10n.Core("LegacyLevelDirNotFound"));
    }

    private static string DescribeSources(string? sourceLand, string? sourceLevelDir)
    {
        return (sourceLand, sourceLevelDir) switch
        {
            ({ } land, { } level) => $"{Path.GetFileName(land)} + {level}",
            ({ } land, null) => L10n.Core("LegacyLandWithTemplateDescription", Path.GetFileName(land)),
            (null, { } level) => L10n.Core("LegacyEmptyLandWithResourcesDescription", level),
            _ => L10n.Core("LegacyEmptyLandWithTemplateDescription")
        };
    }

    private static bool IsWorldPartyMapFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".img", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".dat", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".wam", StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryMapNumberFromPath(string path)
    {
        foreach (string part in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Reverse())
        {
            string name = Path.GetFileNameWithoutExtension(part);
            if (!name.StartsWith("land", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("Tmap", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string digits = new(name.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out int value))
                return value;
        }

        return null;
    }

    private static LandFile ImportUnderwaterLand(string path, int mapNumber, string waterResourcePath)
    {
        byte[] data = File.ReadAllBytes(path);
        BinaryUtil.Ensure(data, 0, 28);
        if (!data.AsSpan(0, 4).SequenceEqual("LND\x1B"u8))
            throw new InvalidDataException(L10n.Core("InvalidUnderwaterLandMagic"));

        int width = ReadInt32(data, 8);
        int height = ReadInt32(data, 12);
        WolImage.ValidateDimensions(width, height);
        int unknown = ReadInt32(data, 16);
        int offset = 28;
        List<LandPoint> points = ReadPoints(data, ref offset);
        BinaryUtil.Ensure(data, offset, 4);
        if (!data.AsSpan(offset, 4).SequenceEqual("IMG\x1A"u8))
            throw new InvalidDataException(L10n.Core("UnderwaterTerrainBlockMissing"));

        WolImage terrain = DecodeLegacyImg(ReadLegacyBlock(data, ref offset), LegacyCompression.BmpPack);
        WolImage sourceCollision = DecodeLegacyImg(ReadLegacyBlock(data, ref offset), LegacyCompression.BmpPack);
        WolImage sourceAuxiliary = DecodeLegacyImg(ReadLegacyBlock(data, ref offset), LegacyCompression.BmpPack);
        WolImage collision = ToOneBitMask(sourceCollision);
        WolImage auxiliary = ToOneBitMask(sourceAuxiliary);
        WolImage preview = WolImage.Create1Bit(collision.Width / 8, collision.Height / 8);
        WolImage.NormalizeMasks(collision, auxiliary, preview);

        var land = new LandFile
        {
            Width = width,
            Height = height,
            ClosedMapFlag = unknown,
            Terrain = terrain,
            CollisionMask = collision,
            AuxiliaryMask = auxiliary,
            PreviewMask = preview,
            SourcePath = path
        };
        land.PrimaryPoints.AddRange(points);
        if (land.PrimaryPoints.Count == 0)
            land.GenerateImportSpawnPointsAcrossMap();

        land.SetClientResourcePaths(mapNumber, waterResourcePath);
        return land;
    }

    private static LandFile ImportWorldPartyLand(string path, int mapNumber, string waterResourcePath)
    {
        string? wamPath = null;
        if (Path.GetExtension(path).Equals(".wam", StringComparison.OrdinalIgnoreCase))
        {
            wamPath = path;
            string imagePath = Path.ChangeExtension(path, ".img");
            if (!File.Exists(imagePath))
                imagePath = Path.ChangeExtension(path, ".IMG");

            if (!File.Exists(imagePath))
                throw new FileNotFoundException(L10n.Core("MatchingLegacyImgNotFound", Path.GetFileName(path)));

            path = imagePath;
        }
        else
        {
            wamPath = FindSiblingWorldPartyWam(path);
        }

        byte[] data = File.ReadAllBytes(path);
        WolImage terrain;
        List<LandPoint> points = wamPath is null ? [] : ReadWorldPartyWamSpawnPoints(wamPath);
        if (data.Length >= 4 && data.AsSpan(0, 4).SequenceEqual("IMG\x1A"u8))
        {
            terrain = DecodeLegacyImg(data, LegacyCompression.WwpLz);
        }
        else if (data.Length >= 4 && data.AsSpan(0, 4).SequenceEqual("LND\x1B"u8))
        {
            return ImportUnderwaterLand(path, mapNumber, waterResourcePath);
        }
        else
        {
            throw new InvalidDataException(L10n.Core("WorldPartyImportFormats"));
        }

        if (terrain.Width != LandFile.ExpectedWidth || terrain.Height != LandFile.ExpectedHeight)
            throw new InvalidDataException(L10n.Core("WorldPartyLandSizeMismatch", terrain.Width, terrain.Height, LandFile.ExpectedWidth, LandFile.ExpectedHeight));

        WolImage collision = WolImage.Create1Bit(terrain.Width, terrain.Height);
        WolImage auxiliary = WolImage.Create1Bit(terrain.Width, terrain.Height);
        WolImage preview = WolImage.Create1Bit(terrain.Width / 8, terrain.Height / 8);
        WolImage.ReplaceMasksFromIndexedTerrain(terrain, collision, auxiliary, preview);

        var land = new LandFile
        {
            Width = terrain.Width,
            Height = terrain.Height,
            ClosedMapFlag = 0,
            Terrain = terrain,
            CollisionMask = collision,
            AuxiliaryMask = auxiliary,
            PreviewMask = preview,
            SourcePath = path
        };
        land.PrimaryPoints.AddRange(points);
        if (land.PrimaryPoints.Count == 0)
            land.GenerateImportSpawnPointsAcrossMap();

        land.SetClientResourcePaths(mapNumber, waterResourcePath);
        return land;
    }

    private static string? FindSiblingWorldPartyWam(string imagePath)
    {
        string? directory = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        string name = Path.GetFileNameWithoutExtension(imagePath);
        string wam = Path.Combine(directory, $"{name}.wam");
        if (File.Exists(wam))
            return wam;

        wam = Path.Combine(directory, $"{name}.WAM");
        return File.Exists(wam) ? wam : null;
    }

    private static List<LandPoint> ReadWorldPartyWamSpawnPoints(string path)
    {
        var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string section = string.Empty;
        foreach (string rawLine in File.ReadLines(path, Encoding.Default))
        {
            string line = rawLine.Trim();
            int comment = line.IndexOf(';');
            if (comment >= 0)
                line = line[..comment].Trim();

            if (line.Length > 2 && line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            string key = line[..equals].Trim();
            string valueText = line[(equals + 1)..].Trim();
            if (int.TryParse(valueText, out int value))
                values[$"{section}.{key}"] = value;
        }

        var points = new List<LandPoint>();
        foreach (var pair in values)
        {
            if (!pair.Key.EndsWith("_XPosition", StringComparison.OrdinalIgnoreCase))
                continue;

            string prefix = pair.Key[..^"_XPosition".Length];
            int keyStart = prefix.LastIndexOf('.') + 1;
            if (!prefix[keyStart..].StartsWith("Worm", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!values.TryGetValue($"{prefix}_PlaceWorm", out int place) || place == 0)
                continue;

            if (!values.TryGetValue($"{prefix}_YPosition", out int y))
                continue;

            points.Add(new LandPoint(pair.Value, y));
        }

        return points
            .Distinct()
            .Take(LandFile.MaximumSpawnPoints)
            .ToList();
    }

    private static LandFile CreateEmptyWorldPartyLand(int mapNumber, string waterResourcePath)
    {
        var land = new LandFile
        {
            Width = LandFile.ExpectedWidth,
            Height = LandFile.ExpectedHeight,
            ClosedMapFlag = 0,
            Terrain = WolImage.Create8Bit(LandFile.ExpectedWidth, LandFile.ExpectedHeight),
            CollisionMask = WolImage.Create1Bit(LandFile.ExpectedWidth, LandFile.ExpectedHeight),
            AuxiliaryMask = WolImage.Create1Bit(LandFile.ExpectedWidth, LandFile.ExpectedHeight),
            PreviewMask = WolImage.Create1Bit(LandFile.ExpectedWidth / 8, LandFile.ExpectedHeight / 8)
        };
        land.SetClientResourcePaths(mapNumber, waterResourcePath);
        return land;
    }

    private static string? ResolveWorldPartyLevelDir(string selectedPath, string sourceData)
    {
        if (File.Exists(selectedPath) && Path.GetFileName(selectedPath).Equals("Level.dir", StringComparison.OrdinalIgnoreCase))
            return selectedPath;

        if (File.Exists(selectedPath) && IsWorldPartyMapFile(selectedPath))
        {
            string imageName = Path.GetFileNameWithoutExtension(selectedPath);
            if (WorldPartyImageToTheme.TryGetValue(imageName, out string? mapped))
            {
                string mappedPath = Path.Combine(sourceData, "Level", mapped, "Level.dir");
                if (File.Exists(mappedPath))
                    return mappedPath;

                mappedPath = Path.Combine(sourceData, "Custom", mapped, "Level.dir");
                if (File.Exists(mappedPath))
                    return mappedPath;
            }

            string normalizedImage = NormalizeKey(imageName);
            foreach (string root in new[] { Path.Combine(sourceData, "Level"), Path.Combine(sourceData, "Custom") })
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (string directory in Directory.EnumerateDirectories(root))
                {
                    string theme = Path.GetFileName(directory);
                    if (!NormalizeKey(theme).Contains(normalizedImage, StringComparison.OrdinalIgnoreCase) &&
                        !normalizedImage.Contains(NormalizeKey(theme), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string candidate = Path.Combine(directory, "Level.dir");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        string? directoryPath = File.Exists(selectedPath) ? Path.GetDirectoryName(selectedPath) : selectedPath;
        while (!string.IsNullOrWhiteSpace(directoryPath) && !directoryPath.Equals(sourceData, StringComparison.OrdinalIgnoreCase))
        {
            string path = Path.Combine(directoryPath, "Level.dir");
            if (File.Exists(path))
                return path;

            directoryPath = Path.GetDirectoryName(directoryPath);
        }

        return null;
    }

    private static string? ResolveWorldPartyLandFromTheme(string selectedPath, string sourceData)
    {
        string? directory = File.Exists(selectedPath) ? Path.GetDirectoryName(selectedPath) : selectedPath;
        string theme = string.IsNullOrWhiteSpace(directory) ? string.Empty : Path.GetFileName(directory);
        if (!string.IsNullOrWhiteSpace(theme) && WorldPartyThemeToImage.TryGetValue(theme, out string? imageName))
        {
            string? mapped = FindWorldPartyImage(sourceData, imageName);
            if (mapped is not null)
                return mapped;
        }

        string normalizedTheme = NormalizeKey(theme);
        if (!string.IsNullOrWhiteSpace(normalizedTheme))
        {
            foreach (string root in new[] { Path.Combine(sourceData, "Bitmaps"), Path.Combine(sourceData, "Image") })
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (string image in Directory.EnumerateFiles(root, "*.img"))
                {
                    string normalizedImage = NormalizeKey(Path.GetFileNameWithoutExtension(image));
                    if (normalizedImage.Contains(normalizedTheme, StringComparison.OrdinalIgnoreCase) ||
                        normalizedTheme.Contains(normalizedImage, StringComparison.OrdinalIgnoreCase))
                    {
                        return image;
                    }
                }
            }
        }

        return null;
    }

    private static string? FindWorldPartyImage(string sourceData, string imageName)
    {
        foreach (string root in new[] { Path.Combine(sourceData, "Bitmaps"), Path.Combine(sourceData, "Image") })
        {
            string path = Path.Combine(root, $"{imageName}.IMG");
            if (File.Exists(path))
                return path;

            path = Path.Combine(root, $"{imageName}.img");
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static string NormalizeKey(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }

    private static Dictionary<string, byte[]> ConvertLegacyLevelDir(ILegacyMapAdapter adapter, string path)
    {
        byte[] data = File.ReadAllBytes(path);
        Dictionary<string, byte[]> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (LegacyDirEntry entry in ParseLegacyDir(data))
        {
            if (!RequiredResources.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            result[entry.Name] = adapter.ConvertResource(entry.Name, entry.Data);
        }

        return result;
    }

    private static byte[] ConvertLegacyResource(LegacyMapKind kind, string name, byte[] data, LegacyCompression imageCompression)
    {
        string internalName = name.ToLowerInvariant() switch
        {
            "back.spr" => "BackgroundLayer",
            "debris.spr" => "BackgroundDebris",
            "gradient.img" => "BackgroundGradient",
            "grass.img" => "LandGrass",
            "soil.img" => "LandSoil",
            _ => Path.GetFileNameWithoutExtension(name)
        };

        if (data.AsSpan(0, 4).SequenceEqual("IMG\x1A"u8))
        {
            WolImage image = DecodeLegacyImg(data, imageCompression);
            image.Name = internalName;
            image.ClampPalette(name.Equals("gradient.img", StringComparison.OrdinalIgnoreCase) ? 16 : 96);
            using var stream = new MemoryStream();
            image.Write(stream);
            return stream.ToArray();
        }

        if (data.AsSpan(0, 4).SequenceEqual("SPR\x1A"u8))
        {
            byte[] converted = (kind == LegacyMapKind.WorldParty || kind == LegacyMapKind.Armageddon) ? ConvertWorldPartySpr(data, internalName) : ConvertLegacySpr(data, internalName);
            WolSprite sprite = WolSprite.Read(converted);
            sprite.ClampPalette(96);
            return sprite.ToBytes();
        }

        return data;
    }

    private static bool IsEmptyImgResource(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 4).SequenceEqual("IMG\x1A"u8))
            return false;

        int offset = 0;
        WolImage image = WolImage.Read(data, ref offset);
        return image.BitsPerPixel == 8 && image.Pixels.All(pixel => pixel == 0);
    }

    private static bool ShouldUseSourceResource(LegacyMapKind kind, string name)
    {
        return kind is LegacyMapKind.Underwater or LegacyMapKind.WorldParty or LegacyMapKind.Armageddon;
    }

    private static byte[] RemapToTemplateResourcePalette(string name, byte[] data, DirFile templateResources)
    {
        DirResourceEntry? template = templateResources.Find(name);
        if (template is null)
            return data;

        if (data.AsSpan(0, 4).SequenceEqual("IMG\x1A"u8) &&
            template.Data.AsSpan(0, 4).SequenceEqual("IMG\x1A"u8))
        {
            int sourceOffset = 0;
            WolImage source = WolImage.Read(data, ref sourceOffset);
            int templateOffset = 0;
            WolImage target = WolImage.Read(template.Data, ref templateOffset);
            if (!target.HasPalette || target.Palette.Count == 0)
                return data;

            source.RemapToPalette(target.Palette, target.Palette.Count);
            using var stream = new MemoryStream();
            source.Write(stream);
            return stream.ToArray();
        }

        if (data.AsSpan(0, 4).SequenceEqual("SPR\x1A"u8) &&
            template.Data.AsSpan(0, 4).SequenceEqual("SPR\x1A"u8))
        {
            WolSprite source = WolSprite.Read(data);
            WolSprite target = WolSprite.Read(template.Data);
            if (target.Palette.Count == 0)
                return data;

            source.RemapToPalette(target.Palette, target.Palette.Count);
            return source.ToBytes();
        }

        return data;
    }

    private static byte[] ConvertLegacySpr(byte[] data, string internalName)
    {
        try
        {
            return ConvertLegacySprStrict(data, internalName);
        }
        catch
        {
            return CreateFallbackSprite(data, internalName);
        }
    }

    private static byte[] ConvertWorldPartySpr(byte[] data, string internalName)
    {
        int length = ReadInt32(data, 4);
        ValidateLegacyBlockLength(data, length);
        data = data[..length];
        byte bpp = data[8];
        byte flags = data[9];
        if (bpp != 8 || (flags & 0x80) == 0)
            throw new InvalidDataException(L10n.Core("UnsupportedWwpSpriteHeader"));

        int offset = 10;
        ushort colors = ReadUInt16(data, offset);
        offset += 2;
        BinaryUtil.Ensure(data, offset, colors * 3);
        byte[] palette = data.AsSpan(offset, colors * 3).ToArray();
        offset += colors * 3;

        int rate = ReadInt32(data, offset);
        ushort fieldA = ReadUInt16(data, offset + 4);
        ushort fieldB = ReadUInt16(data, offset + 6);
        ushort fieldC = ReadUInt16(data, offset + 8);
        ushort frameCount = ReadUInt16(data, offset + 10);
        int tableStart = offset + 12;
        int pixelStart = tableStart + frameCount * 12;
        if (frameCount == 0 || length > data.Length || pixelStart > length)
            throw new InvalidDataException(L10n.Core("InvalidWwpSpriteFrameTable"));

        var frames = new List<(int DataOffset, short Left, short Top, short Right, short Bottom, int Length)>();
        for (int i = 0; i < frameCount; i++)
        {
            int entry = tableStart + i * 12;
            int dataOffset = ReadInt32(data, entry);
            short left = checked((short)ReadUInt16(data, entry + 4));
            short top = checked((short)ReadUInt16(data, entry + 6));
            short right = checked((short)ReadUInt16(data, entry + 8));
            short bottom = checked((short)ReadUInt16(data, entry + 10));
            if (right < left || bottom < top)
                throw new InvalidDataException(L10n.Core("InvalidWwpSpriteFrameBounds"));

            long pixelLength = (long)(right - left) * (bottom - top);
            if (dataOffset < 0 || (long)pixelStart + dataOffset + pixelLength > length)
                throw new InvalidDataException(L10n.Core("InvalidWwpSpriteFramePixels"));

            frames.Add(((int)dataOffset, left, top, right, bottom, (int)pixelLength));
        }

        return WriteOnlineSprite(internalName, colors, palette, rate, fieldA, fieldB, fieldC, frameCount, frames, data, pixelStart);
    }

    private static byte[] CreateFallbackSprite(byte[] legacyData, string internalName)
    {
        int width = internalName.Equals("BackgroundLayer", StringComparison.OrdinalIgnoreCase) ? 640 : 1;
        int height = internalName.Equals("BackgroundLayer", StringComparison.OrdinalIgnoreCase) ? 160 : 1;
        int offset = 10;
        ushort colors = legacyData.Length >= 12 ? ReadUInt16(legacyData, offset) : (ushort)1;
        offset += 2;
        byte[] palette = colors > 0 && offset + colors * 3 <= legacyData.Length
            ? legacyData.AsSpan(offset, colors * 3).ToArray()
            : [0, 0, 0];
        if (colors == 0)
            colors = 1;

        using var stream = new MemoryStream();
        BinaryUtil.WriteAscii(stream, "SPR\x1A", nullTerminate: false);
        BinaryUtil.WriteInt32(stream, 0);
        BinaryUtil.WriteAscii(stream, internalName, nullTerminate: true);
        BinaryUtil.WriteUInt16(stream, 0x8008);
        BinaryUtil.WriteUInt16(stream, colors);
        stream.Write(palette, 0, colors * 3);
        BinaryUtil.WriteInt32(stream, 0);
        BinaryUtil.WriteUInt16(stream, 0);
        BinaryUtil.WriteUInt16(stream, checked((ushort)width));
        BinaryUtil.WriteUInt16(stream, checked((ushort)height));
        BinaryUtil.WriteUInt16(stream, 1);
        BinaryUtil.WriteInt32(stream, 0);
        BinaryUtil.WriteUInt16(stream, 0);
        BinaryUtil.WriteUInt16(stream, 0);
        BinaryUtil.WriteUInt16(stream, checked((ushort)width));
        BinaryUtil.WriteUInt16(stream, checked((ushort)height));
        stream.Write(new byte[width * height]);
        BinaryUtil.PatchInt32(stream, 4, checked((int)stream.Length));
        return stream.ToArray();
    }

    private static byte[] ConvertLegacySprStrict(byte[] data, string internalName)
    {
        int length = ReadInt32(data, 4);
        ValidateLegacyBlockLength(data, length);
        data = data[..length];
        byte bpp = data[8];
        byte flags = data[9];
        if (bpp != 8)
            throw new InvalidDataException(L10n.Core("UnsupportedSpriteBitDepth"));

        int offset = 10;
        ushort colors = ReadUInt16(data, offset);
        offset += 2;
        BinaryUtil.Ensure(data, offset, colors * 3);
        byte[] palette = data.AsSpan(offset, colors * 3).ToArray();
        offset += colors * 3;
        if ((flags & 0x40) == 0)
        {
            if (TryConvertLegacyFrameEndSpriteLayout(data, internalName, length, colors, palette, offset, out byte[] frameEndConverted))
                return frameEndConverted;

            if (TryConvertPaletteSpriteLayout(data, internalName, length, colors, palette, offset, out byte[] converted))
                return converted;

            return ConvertUncompressedLegacySpr(data, internalName, length, colors, palette, offset);
        }

        ushort blockCount = ReadUInt16(data, offset);
        offset += 2 + blockCount * 10;
        int rate = ReadInt32(data, offset);
        ushort fieldA = ReadUInt16(data, offset + 4);
        ushort fieldB = ReadUInt16(data, offset + 8);
        ushort fieldC = ReadUInt16(data, offset + 10);
        ushort frameCount = ReadUInt16(data, offset + 12);
        if (frameCount != 1)
            throw new InvalidDataException(L10n.Core("UnsupportedCompressedSpriteFrames"));

        int pixelStart = offset + 27;
        var frames = new List<(int DataOffset, short Left, short Top, short Right, short Bottom, int Length)>();
        if (fieldB > short.MaxValue || fieldC > short.MaxValue)
            throw new InvalidDataException(L10n.Core("InvalidLegacySpriteFrameBounds"));
        int expectedPixels = fieldB * fieldC;
        frames.Add((0, 0, 0, checked((short)fieldB), checked((short)fieldC), expectedPixels));
        BinaryUtil.Ensure(data, pixelStart, length - pixelStart);
        byte[] pixels = BmpPackDecompress(data.AsSpan(pixelStart, length - pixelStart), expectedPixels);
        return WriteOnlineSprite(internalName, colors, palette, rate, fieldA, fieldB, fieldC, frameCount, frames, pixels, 0);
    }

    private static bool TryConvertLegacyFrameEndSpriteLayout(
        byte[] data,
        string internalName,
        int length,
        ushort colors,
        byte[] palette,
        int offset,
        out byte[] converted)
    {
        converted = [];
        if (offset + 19 > length)
            return false;

        int rate = ReadInt32(data, offset);
        ushort fieldA = ReadUInt16(data, offset + 4);
        ushort fieldB = ReadUInt16(data, offset + 6);
        ushort fieldC = ReadUInt16(data, offset + 8);
        ushort frameCount = ReadUInt16(data, offset + 10);
        int tableStart = offset + 19;
        int pixelStart = tableStart + (frameCount - 1) * 12 + 8;
        if (frameCount == 0 || pixelStart > length)
            return false;

        var frames = new List<(int DataOffset, short Left, short Top, short Right, short Bottom, int Length)>();
        int previousEndOffset = 0;
        for (int i = 0; i < frameCount; i++)
        {
            int entry = tableStart + i * 12;
            short left = checked((short)ReadUInt16(data, entry));
            short top = checked((short)ReadUInt16(data, entry + 2));
            short right = checked((short)ReadUInt16(data, entry + 4));
            short bottom = checked((short)ReadUInt16(data, entry + 6));
            int endOffset = i == frameCount - 1 ? length - pixelStart : ReadInt32(data, entry + 8);
            if (right < left || bottom < top || endOffset < previousEndOffset)
                return false;

            long pixelLength = (long)(right - left) * (bottom - top);
            long dataOffset = endOffset - pixelLength;
            if (dataOffset < 0 || dataOffset < previousEndOffset || (long)pixelStart + endOffset > length)
                return false;

            frames.Add(((int)dataOffset, left, top, right, bottom, (int)pixelLength));
            previousEndOffset = endOffset;
        }

        if (frames.All(frame => frame.Length == 0))
            return false;

        converted = WriteOnlineSprite(internalName, colors, palette, rate, fieldA, fieldB, fieldC, frameCount, frames, data, pixelStart);
        return true;
    }

    private static bool TryConvertPaletteSpriteLayout(
        byte[] data,
        string internalName,
        int length,
        ushort colors,
        byte[] palette,
        int offset,
        out byte[] converted)
    {
        converted = [];
        if (offset + 12 > length)
            return false;

        int rate = ReadInt32(data, offset);
        ushort fieldA = ReadUInt16(data, offset + 4);
        ushort fieldB = ReadUInt16(data, offset + 6);
        ushort fieldC = ReadUInt16(data, offset + 8);
        ushort frameCount = ReadUInt16(data, offset + 10);
        int tableStart = offset + 12;
        int pixelStart = tableStart + frameCount * 12;
        if (frameCount == 0 || pixelStart > length)
            return false;

        var frames = new List<(int DataOffset, short Left, short Top, short Right, short Bottom, int Length)>();
        for (int i = 0; i < frameCount; i++)
        {
            int entry = tableStart + i * 12;
            int dataOffset = ReadInt32(data, entry);
            short left = checked((short)ReadUInt16(data, entry + 4));
            short top = checked((short)ReadUInt16(data, entry + 6));
            short right = checked((short)ReadUInt16(data, entry + 8));
            short bottom = checked((short)ReadUInt16(data, entry + 10));
            if (right < left || bottom < top)
                return false;

            long pixelLength = (long)(right - left) * (bottom - top);
            if (dataOffset < 0 || (long)pixelStart + dataOffset + pixelLength > length)
                return false;

            frames.Add(((int)dataOffset, left, top, right, bottom, (int)pixelLength));
        }

        if (frames.All(frame => frame.Length == 0))
            return false;

        converted = WriteOnlineSprite(internalName, colors, palette, rate, fieldA, fieldB, fieldC, frameCount, frames, data, pixelStart);
        return true;
    }

    private static byte[] ConvertUncompressedLegacySpr(byte[] data, string internalName, int length, ushort colors, byte[] palette, int offset)
    {
        ushort fieldA = ReadUInt16(data, offset);
        ushort frameCount = ReadUInt16(data, offset + 2);
        int rate = ReadUInt16(data, offset + 4);
        ushort fieldB = ReadUInt16(data, offset + 6);
        ushort fieldC = ReadUInt16(data, offset + 8);
        int tableStart = offset + 14;
        int pixelStart = tableStart + frameCount * 12;
        if (frameCount == 0 || pixelStart > length)
            throw new InvalidDataException(L10n.Core("InvalidLegacySpriteFrameTable"));

        var frames = new List<(int DataOffset, short Left, short Top, short Right, short Bottom, int Length)>();
        for (int i = 0; i < frameCount; i++)
        {
            int entry = tableStart + i * 12;
            int dataOffset = ReadInt32(data, entry) >> 8;
            short left = ReadBigInt16(data, entry + 4);
            short top = ReadBigInt16(data, entry + 6);
            short right = ReadBigInt16(data, entry + 8);
            short bottom = ReadBigInt16(data, entry + 10);
            if (right < left || bottom < top)
                throw new InvalidDataException(L10n.Core("InvalidLegacySpriteFrameBounds"));

            long pixelLength = (long)(right - left) * (bottom - top);
            if (dataOffset < 0 || (long)pixelStart + dataOffset + pixelLength > length)
                throw new InvalidDataException(L10n.Core("InvalidLegacySpriteFramePixels"));

            frames.Add(((int)dataOffset, left, top, right, bottom, (int)pixelLength));
        }

        return WriteOnlineSprite(internalName, colors, palette, rate, fieldA, fieldB, fieldC, frameCount, frames, data, pixelStart);
    }

    private static byte[] WriteOnlineSprite(
        string internalName,
        ushort colors,
        byte[] palette,
        int rate,
        ushort fieldA,
        ushort fieldB,
        ushort fieldC,
        ushort frameCount,
        List<(int DataOffset, short Left, short Top, short Right, short Bottom, int Length)> frames,
        byte[] pixelSource,
        int pixelStart)
    {
        using var stream = new MemoryStream();
        BinaryUtil.WriteAscii(stream, "SPR\x1A", nullTerminate: false);
        BinaryUtil.WriteInt32(stream, 0);
        BinaryUtil.WriteAscii(stream, internalName, nullTerminate: true);
        BinaryUtil.WriteUInt16(stream, 0x8008);
        BinaryUtil.WriteUInt16(stream, colors);
        stream.Write(palette);
        BinaryUtil.WriteInt32(stream, rate);
        BinaryUtil.WriteUInt16(stream, frameCount > 1 && fieldA == 0 ? (ushort)1 : fieldA);
        BinaryUtil.WriteUInt16(stream, fieldB);
        BinaryUtil.WriteUInt16(stream, fieldC);
        BinaryUtil.WriteUInt16(stream, frameCount);
        long newTable = stream.Position;
        stream.Write(new byte[frameCount * 12]);
        int newPixelOffset = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            long entry = newTable + i * 12L;
            PatchInt32(stream, entry, newPixelOffset);
            PatchInt16(stream, entry + 4, frame.Left);
            PatchInt16(stream, entry + 6, frame.Top);
            PatchInt16(stream, entry + 8, frame.Right);
            PatchInt16(stream, entry + 10, frame.Bottom);
            stream.Write(pixelSource, pixelStart + frame.DataOffset, frame.Length);
            newPixelOffset += frame.Length;
        }

        BinaryUtil.PatchInt32(stream, 4, checked((int)stream.Length));
        return stream.ToArray();
    }

    private static WolImage DecodeLegacyImg(byte[] block, LegacyCompression compression)
    {
        BinaryUtil.Ensure(block, 0, 10);
        if (!block.AsSpan(0, 4).SequenceEqual("IMG\x1A"u8))
            throw new InvalidDataException(L10n.Core("ExpectedImgBlock"));

        int length = ReadInt32(block, 4);
        ValidateLegacyBlockLength(block, length);
        block = block[..length];
        byte bpp = block[8];
        if (bpp is not (1 or 8))
            throw new InvalidDataException(L10n.Core("UnsupportedImgBitDepth", bpp));
        byte flags = block[9];
        int offset = 10;
        var palette = new List<Color>();
        if ((flags & 0x80) != 0)
        {
            ushort colors = ReadUInt16(block, offset);
            offset += 2;
            BinaryUtil.Ensure(block, offset, colors * 3);
            for (int i = 0; i < colors; i++)
            {
                palette.Add(Color.FromArgb(block[offset], block[offset + 1], block[offset + 2]));
                offset += 3;
            }
        }

        ushort width = ReadUInt16(block, offset);
        ushort height = ReadUInt16(block, offset + 2);
        // Zero-sized resource placeholders remain valid and are ignored by import.
        if (width > 0 && height > 0)
            WolImage.ValidateDimensions(width, height);
        offset = Align4(offset + 4);
        int expected = ((width * bpp + 7) / 8) * height;
        BinaryUtil.Ensure(block, offset, length - offset);
        ReadOnlySpan<byte> payload = block.AsSpan(offset, length - offset);
        if ((flags & 0x40) == 0)
            BinaryUtil.Ensure(payload, 0, expected);
        byte[] pixels = (flags & 0x40) == 0
            ? payload[..expected].ToArray()
            : compression == LegacyCompression.BmpPack ? BmpPackDecompress(payload, expected) : WwpDecompress(payload, expected);

        var image = new WolImage
        {
            Flags = (ushort)(((palette.Count > 0) ? 0x8000 : 0) | bpp),
            Width = width,
            Height = height,
            Pixels = pixels
        };
        image.Palette.AddRange(palette);
        return image;
    }

    private static List<LegacyDirEntry> ParseLegacyDir(byte[] data)
    {
        BinaryUtil.Ensure(data, 0, 12);
        if (!data.AsSpan(0, 4).SequenceEqual("DIR\x1A"u8))
            throw new InvalidDataException(L10n.Core("InvalidDirMagic"));

        int dataEnd = ReadInt32(data, 4);
        int directoryStart = ReadInt32(data, 8);
        if (directoryStart < 12 || dataEnd > data.Length || directoryStart > dataEnd)
            throw new InvalidDataException(L10n.Core("InvalidDirDataRange"));
        byte[] directory = data.AsSpan(directoryStart, dataEnd - directoryStart).ToArray();
        if (directory.Length < 4 + 1024 * 4)
            throw new InvalidDataException(L10n.Core("DirTableTooSmall"));
        var entries = new List<LegacyDirEntry>();
        var seen = new HashSet<int>();
        for (int offset = 4 + 1024 * 4; offset + 13 < directory.Length;)
        {
            int fileOffset = ReadInt32(directory, offset + 4);
            int length = ReadInt32(directory, offset + 8);
            int nameStart = offset + 12;
            int nameEnd = Array.IndexOf(directory, (byte)0, nameStart);
            if (nameEnd < 0)
                break;

            string name = Encoding.ASCII.GetString(directory, nameStart, nameEnd - nameStart);
            int entrySize = Align4(12 + name.Length + 1);
            if (!string.IsNullOrWhiteSpace(name) && fileOffset >= 12 && length >= 8 &&
                fileOffset <= directoryStart && length <= directoryStart - fileOffset && seen.Add(fileOffset))
                entries.Add(new LegacyDirEntry(name, data.AsSpan(fileOffset, length).ToArray()));

            offset += Math.Max(entrySize, 16);
        }

        return entries;
    }

    private static List<LandPoint> ReadPoints(byte[] data, ref int offset)
    {
        int count = ReadInt32(data, offset);
        offset += 4;
        if (count < 0 || count > (data.Length - offset) / 8)
            throw new InvalidDataException(L10n.Core("InvalidPointCount", count));
        var points = new List<LandPoint>();
        for (int i = 0; i < count; i++)
        {
            points.Add(new LandPoint(ReadInt32(data, offset), ReadInt32(data, offset + 4)));
            offset += 8;
        }

        return points;
    }

    private static WolImage ToOneBitMask(WolImage source)
    {
        if (source.BitsPerPixel == 1)
            return source;

        WolImage mask = WolImage.Create1Bit(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                if (source.GetPixelIndex(x, y) != 0)
                    mask.SetPixelIndex(x, y, 1);
            }
        }

        return mask;
    }

    private static byte[] WwpDecompress(ReadOnlySpan<byte> data, int expected)
    {
        var output = new List<byte>(expected);
        int pos = 0;
        while (pos < data.Length && output.Count < expected)
        {
            byte value = data[pos++];
            if ((value & 0x80) == 0)
            {
                output.Add(value);
                continue;
            }

            BinaryUtil.Ensure(data, pos, 1);
            int offset = ((value & 0x07) << 8) | data[pos++];
            int count;
            if ((value & 0x78) == 0)
            {
                if (offset == 0)
                    break;
                BinaryUtil.Ensure(data, pos, 1);
                count = data[pos++] + 18;
            }
            else
            {
                offset += 1;
                count = ((value >> 3) & 0x0F) + 2;
            }

            if (offset <= 0 || offset > output.Count)
                throw new InvalidDataException(L10n.Core("InvalidWwpLzOffset"));
            if (count > expected - output.Count)
                throw new InvalidDataException(L10n.Core("CompressedDataTooLong", expected));

            int source = output.Count - offset;
            for (int i = 0; i < count; i++)
                output.Add(output[source + i]);
        }

        if (output.Count != expected)
            throw new InvalidDataException(L10n.Core("WwpDecompressedLengthMismatch", output.Count, expected));

        return output.ToArray();
    }

    private static byte[] BmpPackDecompress(ReadOnlySpan<byte> data, int expected)
    {
        var output = new List<byte>(expected);
        int pos = 0;
        while (pos < data.Length && output.Count < expected)
        {
            byte control = data[pos++];
            if (control != 0xFF)
            {
                BinaryUtil.Ensure(data, pos, control);
                if (control > expected - output.Count)
                    throw new InvalidDataException(L10n.Core("CompressedDataTooLong", expected));
                for (int i = 0; i < control; i++)
                    output.Add(data[pos++]);
                continue;
            }

            BinaryUtil.Ensure(data, pos, 3);
            int offset = ((data[pos] & 0x0F) << 8) | data[pos + 1];
            pos += 2;
            int count = data[pos++];
            if (count == 0)
                break;
            if (offset <= 0)
                throw new InvalidDataException(L10n.Core("InvalidBmpPackOffset"));
            if (count > expected - output.Count)
                throw new InvalidDataException(L10n.Core("CompressedDataTooLong", expected));

            int source = output.Count - offset;
            for (int i = 0; i < count; i++)
            {
                int index = source + i;
                output.Add(index < 0 ? (byte)0 : output[index]);
            }
        }

        while (output.Count < expected)
            output.Add(0);

        if (output.Count != expected)
            throw new InvalidDataException(L10n.Core("BmpPackDecompressedLengthMismatch", output.Count, expected));

        return output.ToArray();
    }

    private static int Align4(int value) => (value + 3) & ~3;
    private static int ReadInt32(byte[] data, int offset) => BinaryUtil.ReadInt32(data, ref offset);
    private static short ReadBigInt16(byte[] data, int offset)
    {
        BinaryUtil.Ensure(data, offset, 2);
        return unchecked((short)((data[offset] << 8) | data[offset + 1]));
    }
    private static ushort ReadUInt16(byte[] data, int offset) => BinaryUtil.ReadUInt16(data, ref offset);

    private static void ValidateLegacyBlockLength(byte[] data, int length)
    {
        if (length < 10 || length > data.Length)
            throw new InvalidDataException(L10n.Core("InvalidLegacyBlockLength", length, 0));
    }

    private static byte[] ReadLegacyBlock(byte[] data, ref int offset)
    {
        BinaryUtil.Ensure(data, offset, 8);
        int length = ReadInt32(data, offset + 4);
        if (length < 8 || length > data.Length - offset)
            throw new InvalidDataException(L10n.Core("InvalidLegacyBlockLength", length, offset));

        return BinaryUtil.ReadBytes(data, ref offset, length);
    }

    private static void PatchInt32(Stream stream, long position, int value)
    {
        long old = stream.Position;
        stream.Position = position;
        BinaryUtil.WriteInt32(stream, value);
        stream.Position = old;
    }

    private static void PatchInt16(Stream stream, long position, short value)
    {
        long old = stream.Position;
        stream.Position = position;
        BinaryUtil.WriteUInt16(stream, unchecked((ushort)value));
        stream.Position = old;
    }

    private enum LegacyCompression
    {
        WwpLz,
        BmpPack
    }

    private sealed record LegacyDirEntry(string Name, byte[] Data);
}
