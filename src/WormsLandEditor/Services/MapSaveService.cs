using WormsLandEditor.Formats;
using WormsLandEditor.Localization;

namespace WormsLandEditor.Services;

internal static class MapSaveService
{
    /// <summary>
    /// Stages and validates both files before replacing them with backup recovery.
    /// Interrupted commits or failed rollback may need manual recovery.
    /// </summary>
    public static string? Save(LandFile land, DirFile resources, string landPath, string levelPath)
    {
        MapPackagePaths.Check(landPath);
        MapPackagePaths.Check(levelPath);
        return SaveCore(landPath, levelPath, (stagedLand, stagedLevel) =>
        {
            land.Save(stagedLand);
            resources.SaveTo(Path.GetDirectoryName(stagedLevel)!);
            _ = LandFile.Load(stagedLand);
            _ = DirFile.Load(stagedLevel);
        }, MapPackagePaths.Check);
    }

    /// <summary>
    /// Saves an unchanged imported snapshot without normalizing palettes, rebuilding
    /// directory entries, or reading the ZIP again. Only explicit Save writes files.
    /// </summary>
    public static string? Save(PreparedMapPackage package, string landPath, string levelPath)
        => Save(package, landPath, levelPath, null);

    internal static string? Save(PreparedMapPackage package, string landPath, string levelPath,
        IMapPackageFileOperations? operations)
    {
        ArgumentNullException.ThrowIfNull(package);
        MapPackagePaths.CheckFile(landPath);
        MapPackagePaths.CheckFile(levelPath);
        return SaveCore(landPath, levelPath, (stagedLand, stagedLevel) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stagedLevel)!);
            WriteStaged(stagedLand, package.LandBytes);
            WriteStaged(stagedLevel, package.DirectoryBytes);
        }, MapPackagePaths.Check, operations);
    }

    private static string? SaveCore(string landPath, string levelPath,
        Action<string, string> prepare, Action<string>? checkPath = null,
        IMapPackageFileOperations? operations = null)
    {
        string stage = Path.Combine(Path.GetDirectoryName(landPath)!, $".worms-map-save-{Guid.NewGuid():N}");
        string stagedLand = Path.Combine(stage, Path.GetFileName(landPath));
        string stagedResources = Path.Combine(stage, "resources");
        string stagedLevel = Path.Combine(stagedResources, "Level.Dir");
        bool prepared = false;
        try
        {
            checkPath?.Invoke(stage);
            Directory.CreateDirectory(stage);
            prepare(stagedLand, stagedLevel);
            prepared = true;
            return FileSetTransaction.CommitPrepared(stage,
                [new(stagedLand, landPath), new(stagedLevel, levelPath)],
                (key, args) => key switch
                {
                    "Restored" => L10n.Ui("SaveRestored", args),
                    "RecoveryRequired" => L10n.Ui("SaveRecoveryRequired", args),
                    "CleanupWarning" => L10n.Ui("SaveCleanupWarning", args),
                    _ => throw new ArgumentOutOfRangeException(nameof(key))
                }, checkPath, operations);
        }
        finally
        {
            if (!prepared)
            {
                try { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        }
    }

    private static void WriteStaged(string path, ReadOnlySpan<byte> bytes)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes);
        file.Flush(flushToDisk: true);
    }
}
