using WormsLandEditor.Localization;
using System.Text;

namespace WormsLandEditor.Formats;

public sealed class LandFile
{
    public const int ExpectedWidth = 1920;
    public const int ExpectedHeight = 696;
    public const int MaximumTerrainColors = 96;
    public const int MinimumMapNumber = 1;
    public const int MaximumMapNumber = 999;
    public const int MinimumSpawnPoints = 12;
    public const int MaximumSpawnPoints = 32;
    public const string Magic = "LND\x1A";
    private const int SpawnClearance = 70;
    private const int SpawnMinimumDrop = 12;
    private const int SpawnMaximumDrop = 110;
    private const int SpawnMinimumDistance = 56;
    private const int SpawnSupportHalfWidth = 24;

    public string? SourcePath { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int ClosedMapFlag { get; set; }
    public bool IsClosedMap
    {
        get => ClosedMapFlag != 0;
        set => ClosedMapFlag = value ? 1 : 0;
    }
    public List<LandPoint> PrimaryPoints { get; } = [];
    public List<LandPoint> SecondaryPoints { get; } = [];
    public WolImage Terrain { get; set; } = new();
    public WolImage CollisionMask { get; set; } = new();
    public WolImage AuxiliaryMask { get; set; } = new();
    public WolImage PreviewMask { get; set; } = new();
    public string NameA { get; set; } = "CustomMap";
    public string NameB { get; set; } = "CustomMap";

    public static LandFile Load(string path) => Load(File.ReadAllBytes(path), path);

    public static LandFile Load(ReadOnlySpan<byte> bytes, string? sourcePath = null)
    {
        int offset = 0;
        string magic = Encoding.ASCII.GetString(BinaryUtil.ReadBytes(bytes, ref offset, 4));
        if (magic != Magic)
            throw new InvalidDataException(L10n.Core("InvalidLandMagic"));

        int fileLength = BinaryUtil.ReadInt32(bytes, ref offset);
        if (fileLength != bytes.Length)
            throw new InvalidDataException(L10n.Core("LandLengthMismatch", fileLength, bytes.Length));

        var land = new LandFile
        {
            SourcePath = sourcePath,
            Width = BinaryUtil.ReadInt32(bytes, ref offset),
            Height = BinaryUtil.ReadInt32(bytes, ref offset),
            ClosedMapFlag = BinaryUtil.ReadInt32(bytes, ref offset)
        };

        int primaryCount = BinaryUtil.ReadInt32(bytes, ref offset);
        ReadPoints(bytes, ref offset, primaryCount, land.PrimaryPoints);

        int secondaryCount = BinaryUtil.ReadInt32(bytes, ref offset);
        ReadPoints(bytes, ref offset, secondaryCount, land.SecondaryPoints);

        land.Terrain = WolImage.Read(bytes, ref offset);
        land.CollisionMask = WolImage.Read(bytes, ref offset);
        land.AuxiliaryMask = WolImage.Read(bytes, ref offset);
        land.PreviewMask = WolImage.Read(bytes, ref offset);
        land.NameA = ReadSizedAscii(bytes, ref offset);
        land.NameB = ReadSizedAscii(bytes, ref offset);

        if (offset != bytes.Length)
            throw new InvalidDataException(L10n.Core("LandTrailingBytes", bytes.Length - offset));

        return land;
    }

    public static LandFile CreateFromBitmap(Bitmap bitmap, LandFile? template = null, string name = "CustomMap")
    {
        var land = new LandFile
        {
            Width = ExpectedWidth,
            Height = ExpectedHeight,
            ClosedMapFlag = template?.ClosedMapFlag ?? 0,
            NameA = name,
            NameB = name
        };

        if (template is not null)
        {
            land.PrimaryPoints.AddRange(template.PrimaryPoints);
            land.SecondaryPoints.AddRange(template.SecondaryPoints);
            land.Terrain = template.Terrain.Clone();
            land.CollisionMask = template.CollisionMask.Clone();
            land.AuxiliaryMask = template.AuxiliaryMask.Clone();
            land.PreviewMask = template.PreviewMask.Clone();
        }
        else
        {
            land.Terrain = WolImage.Create8Bit(ExpectedWidth, ExpectedHeight);
            land.CollisionMask = WolImage.Create1Bit(ExpectedWidth, ExpectedHeight);
            land.AuxiliaryMask = WolImage.Create1Bit(ExpectedWidth, ExpectedHeight);
            land.PreviewMask = WolImage.Create1Bit(ExpectedWidth / 8, ExpectedHeight / 8);
        }

        land.ReplacePng(bitmap);
        return land;
    }

    public void ReplacePng(Bitmap bitmap)
    {
        Terrain.ReplaceTerrainAndMasksFromBitmap(bitmap, CollisionMask, AuxiliaryMask, PreviewMask);
    }

    public void SetClientResourcePaths(int mapNumber, string waterResourcePath)
    {
        if (mapNumber is < MinimumMapNumber or > MaximumMapNumber)
            throw new ArgumentOutOfRangeException(nameof(mapNumber), L10n.Core("MapNumberOutOfRange", MinimumMapNumber, MaximumMapNumber));

        NameA = $@"data\mfx\Tmap{mapNumber:00}";
        NameB = string.IsNullOrWhiteSpace(waterResourcePath) ? @"data\water\Red" : waterResourcePath;
    }

    public void PrepareSpawnPointsForExport()
    {
        EnsureMinimumSpawnPoints();
    }

    public void EnsureMinimumSpawnPoints(int minimum = MinimumSpawnPoints)
    {
        var prepared = new List<LandPoint>();
        foreach (LandPoint point in PrimaryPoints)
            AddSpawnPoint(prepared, NormalizeSpawnPoint(point));

        for (int x = 48; x < ExpectedWidth && prepared.Count < minimum; x += 72)
        {
            LandPoint? point = FindSafeSpawnAtColumn(x, preferredY: null);
            if (point.HasValue)
                AddSpawnPoint(prepared, point.Value);
        }

        for (int x = 96; x < ExpectedWidth && prepared.Count < minimum; x += 48)
        {
            LandPoint? point = FindSafeSpawnAtColumn(x, preferredY: null);
            if (point.HasValue)
                AddSpawnPoint(prepared, point.Value);
        }

        PrimaryPoints.Clear();
        PrimaryPoints.AddRange(prepared.Take(MaximumSpawnPoints));
    }

    public void GenerateImportSpawnPointsAcrossMap()
    {
        var generated = new List<LandPoint>();
        for (int i = 0; i < MaximumSpawnPoints; i++)
        {
            int x = 48 + i * (ExpectedWidth - 96) / Math.Max(1, MaximumSpawnPoints - 1);
            LandPoint? point = FindSafeSpawnAtColumn(x, preferredY: null);
            if (point.HasValue)
                AddSpawnPointWithoutCountLimit(generated, point.Value);
        }

        PrimaryPoints.Clear();
        PrimaryPoints.AddRange(generated);
    }

    public LandPoint? FindNearestUsableSpawnPoint(LandPoint point)
    {
        int x = Math.Clamp(point.X, 0, ExpectedWidth - 1);
        int y = Math.Clamp(point.Y, 0, ExpectedHeight - 1);
        if (IsManualSpawnPositionValid(x, y))
            return new LandPoint(x, y);

        if (IsSpawnPositionValid(x, y))
            return new LandPoint(x, y);

        return FindSafeSpawnNear(x, y);
    }

    public void Save(string path)
    {
        Terrain.ClampPalette(MaximumTerrainColors);

        using var stream = File.Create(path);
        BinaryUtil.WriteAscii(stream, Magic, nullTerminate: false);
        BinaryUtil.WriteInt32(stream, 0);
        BinaryUtil.WriteInt32(stream, Width);
        BinaryUtil.WriteInt32(stream, Height);
        BinaryUtil.WriteInt32(stream, ClosedMapFlag);
        WritePoints(stream, PrimaryPoints);
        WritePoints(stream, SecondaryPoints);
        Terrain.Write(stream);
        CollisionMask.Write(stream);
        AuxiliaryMask.Write(stream);
        PreviewMask.Write(stream);
        WriteSizedAscii(stream, NameA);
        WriteSizedAscii(stream, NameB);
        BinaryUtil.PatchInt32(stream, 4, checked((int)stream.Length));
    }

    public string Describe()
    {
        return L10n.Core("LandDescription", Width, Height, L10n.Core(IsClosedMap ? "Yes" : "No"), PrimaryPoints.Count, SecondaryPoints.Count,
            Terrain.BitsPerPixel, Terrain.Palette.Count, CollisionMask.Width, CollisionMask.Height,
            AuxiliaryMask.Width, AuxiliaryMask.Height, PreviewMask.Width, PreviewMask.Height);
    }

    private static void ReadPoints(ReadOnlySpan<byte> bytes, ref int offset, int count, List<LandPoint> points)
    {
        if (count < 0 || count > 4096 || count > (bytes.Length - offset) / 8)
            throw new InvalidDataException(L10n.Core("InvalidPointCount", count));

        for (int i = 0; i < count; i++)
            points.Add(new LandPoint(BinaryUtil.ReadInt32(bytes, ref offset), BinaryUtil.ReadInt32(bytes, ref offset)));
    }

    private static void WritePoints(Stream stream, List<LandPoint> points)
    {
        BinaryUtil.WriteInt32(stream, points.Count);
        foreach (LandPoint point in points)
        {
            BinaryUtil.WriteInt32(stream, point.X);
            BinaryUtil.WriteInt32(stream, point.Y);
        }
    }

    private static string ReadSizedAscii(ReadOnlySpan<byte> bytes, ref int offset)
    {
        int length = BinaryUtil.ReadByte(bytes, ref offset);
        if (length <= 0 || length >= 128)
            throw new InvalidDataException(L10n.Core("InvalidLandStringLength", length));

        return Encoding.ASCII.GetString(BinaryUtil.ReadBytes(bytes, ref offset, length));
    }

    private static void WriteSizedAscii(Stream stream, string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length is <= 0 or >= 128)
            throw new InvalidDataException(L10n.Core("LandStringLengthRange"));

        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes);
    }

    private LandPoint NormalizeSpawnPoint(LandPoint point)
    {
        int x = Math.Clamp(point.X, 0, ExpectedWidth - 1);
        int y = Math.Clamp(point.Y, 0, ExpectedHeight - 1);
        if (IsSpawnPositionValid(x, y))
            return new LandPoint(x, y);

        LandPoint? nearby = FindSafeSpawnNear(x, y);
        if (nearby.HasValue)
            return nearby.Value;

        int solidY = FindFirstSolidBelow(x, y);
        if (solidY >= 0)
            y = Math.Clamp(solidY - SpawnClearance, 16, ExpectedHeight - 1);

        return IsSpawnPositionValid(x, y) ? new LandPoint(x, y) : new LandPoint(x, Math.Clamp(y, 16, ExpectedHeight - 1));
    }

    private int FindFirstSolidBelow(int x, int startY)
    {
        for (int y = Math.Clamp(startY, 0, ExpectedHeight - 1); y < ExpectedHeight; y++)
        {
            if (IsSolidAt(x, y))
                return y;
        }

        return -1;
    }

    private LandPoint? FindSafeSpawnNear(int x, int y)
    {
        LandPoint? best = null;
        int bestDistance = int.MaxValue;

        for (int radius = 0; radius <= 320; radius += 8)
        {
            foreach (int dx in EnumerateOffsets(radius))
            {
                int candidateX = Math.Clamp(x + dx, 0, ExpectedWidth - 1);
                LandPoint? candidate = FindSafeSpawnAtColumn(candidateX, y);
                if (!candidate.HasValue)
                    continue;

                int distance = Math.Abs(candidate.Value.X - x) + Math.Abs(candidate.Value.Y - y);
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            if (best.HasValue)
                return best;
        }

        return best;
    }

    private LandPoint? FindSafeSpawnAtColumn(int x, int? preferredY)
    {
        LandPoint? best = null;
        int bestDistance = int.MaxValue;

        for (int surfaceY = 80; surfaceY < ExpectedHeight; surfaceY++)
        {
            if (!IsSolidAt(x, surfaceY) ||
                IsSolidAt(x, Math.Max(0, surfaceY - 16)))
            {
                continue;
            }

            int y = Math.Clamp(surfaceY - SpawnClearance, 16, ExpectedHeight - 1);
            if (!IsSpawnPositionValid(x, y))
                continue;

            int distance = preferredY.HasValue ? Math.Abs(y - preferredY.Value) : surfaceY;
            if (distance < bestDistance)
            {
                best = new LandPoint(x, y);
                bestDistance = distance;
            }
        }

        return best;
    }

    private bool IsSpawnPositionValid(int x, int y)
    {
        if ((uint)x >= ExpectedWidth || (uint)y >= ExpectedHeight)
            return false;

        if (IsSolidAt(x, y))
            return false;

        int left = Math.Max(0, x - 16);
        int right = Math.Min(ExpectedWidth - 1, x + 16);
        int top = Math.Max(0, y - 48);
        int bottom = Math.Min(ExpectedHeight - 1, y + 16);
        for (int yy = top; yy <= bottom; yy++)
        {
            for (int xx = left; xx <= right; xx++)
            {
                if (IsSolidAt(xx, yy))
                    return false;
            }
        }

        return HasLandingSupportBelow(x, y);
    }

    private bool IsManualSpawnPositionValid(int x, int y)
    {
        if ((uint)x >= ExpectedWidth || (uint)y >= ExpectedHeight)
            return false;

        if (IsSolidAt(x, y))
            return false;

        int left = Math.Max(0, x - 10);
        int right = Math.Min(ExpectedWidth - 1, x + 10);
        int top = Math.Max(0, y - 24);
        for (int yy = top; yy <= y; yy++)
        {
            for (int xx = left; xx <= right; xx++)
            {
                if (IsSolidAt(xx, yy))
                    return false;
            }
        }

        return HasLandingSupportBelow(x, y);
    }

    private bool IsSolidAt(int x, int y)
    {
        if ((uint)x >= ExpectedWidth || (uint)y >= ExpectedHeight)
            return false;

        return CollisionMask.GetPixelIndex(x, y) != 0;
    }

    private bool HasLandingSupportBelow(int x, int y)
    {
        for (int xx = Math.Max(0, x - SpawnSupportHalfWidth); xx <= Math.Min(ExpectedWidth - 1, x + SpawnSupportHalfWidth); xx += 4)
        {
            int solidY = FindFirstSolidBelow(xx, y);
            int drop = solidY - y;
            if (drop >= SpawnMinimumDrop && drop <= SpawnMaximumDrop)
                return true;
        }

        return false;
    }

    private static IEnumerable<int> EnumerateOffsets(int radius)
    {
        if (radius == 0)
        {
            yield return 0;
            yield break;
        }

        yield return radius;
        yield return -radius;
    }

    private static void AddSpawnPoint(List<LandPoint> points, LandPoint point)
    {
        if (points.Count >= MaximumSpawnPoints)
            return;

        AddSpawnPointWithoutCountLimit(points, point);
    }

    private static void AddSpawnPointWithoutCountLimit(List<LandPoint> points, LandPoint point)
    {
        foreach (LandPoint existing in points)
        {
            int dx = existing.X - point.X;
            int dy = existing.Y - point.Y;
            if (dx * dx + dy * dy < SpawnMinimumDistance * SpawnMinimumDistance)
                return;
        }

        points.Add(point);
    }
}
