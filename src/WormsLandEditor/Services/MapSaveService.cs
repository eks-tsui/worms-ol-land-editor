using WormsLandEditor.Formats;
using WormsLandEditor.Localization;

namespace WormsLandEditor.Services;

internal static class MapSaveService
{
    /// <summary>
    /// Stages and validates both files, then uses the same backup-based recovery as
    /// package import. Interrupted commits or failed rollback may need manual recovery.
    /// </summary>
    public static string? Save(LandFile land, DirFile resources, string landPath, string levelPath)
    {
        string stage = Path.Combine(Path.GetDirectoryName(landPath)!, $".worms-map-save-{Guid.NewGuid():N}");
        string stagedLand = Path.Combine(stage, Path.GetFileName(landPath));
        string stagedResources = Path.Combine(stage, "resources");
        string stagedLevel = Path.Combine(stagedResources, "Level.Dir");
        bool prepared = false;
        try
        {
            Directory.CreateDirectory(stage);
            land.Save(stagedLand);
            resources.SaveTo(stagedResources);
            _ = LandFile.Load(stagedLand);
            _ = DirFile.Load(stagedLevel);
            prepared = true;
            return FileSetTransaction.CommitPrepared(stage,
                [new(stagedLand, landPath), new(stagedLevel, levelPath)],
                (key, args) => key switch
                {
                    "Restored" => L10n.Ui("SaveRestored", args),
                    "RecoveryRequired" => L10n.Ui("SaveRecoveryRequired", args),
                    "CleanupWarning" => L10n.Ui("SaveCleanupWarning", args),
                    _ => throw new ArgumentOutOfRangeException(nameof(key))
                });
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
}
