using WormsLandEditor.Formats;
using WormsLandEditor.Localization;

namespace WormsLandEditor.Services;

internal static class MapSaveService
{
    /// <summary>
    /// Stages and validates both files, then replaces them with backup-based recovery.
    /// Not a cross-file transaction: interruption or rollback failure can require manual recovery.
    /// </summary>
    public static string? Save(LandFile land, DirFile resources, string landPath, string levelPath)
    {
        string dataDirectory = Path.GetDirectoryName(landPath)!;
        string stageDirectory = Path.Combine(dataDirectory, $".worms-map-save-{Guid.NewGuid():N}");
        string stagedLand = Path.Combine(stageDirectory, Path.GetFileName(landPath));
        string stagedResourceDirectory = Path.Combine(stageDirectory, "resources");
        string stagedLevel = Path.Combine(stagedResourceDirectory, "Level.Dir");
        var destinations = new[]
        {
            new PendingFile(stagedLand, landPath, Path.Combine(stageDirectory, "original-land.dat")),
            new PendingFile(stagedLevel, levelPath, Path.Combine(stageDirectory, "original-Level.Dir"))
        };
        bool keepRecoveryFiles = false;
        bool saved = false;
        string? cleanupWarning = null;
        try
        {
            Directory.CreateDirectory(stageDirectory);
            land.Save(stagedLand);
            resources.SaveTo(stagedResourceDirectory);
            _ = LandFile.Load(stagedLand);
            _ = DirFile.Load(stagedLevel);

            foreach (PendingFile file in destinations)
            {
                file.Existed = File.Exists(file.Destination);
                if (file.Existed)
                    File.Copy(file.Destination, file.Backup, overwrite: false);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(levelPath)!);

            try
            {
                foreach (PendingFile file in destinations)
                {
                    File.Move(file.Staged, file.Destination, overwrite: true);
                    file.Replaced = true;
                }
                saved = true;
            }
            catch (Exception saveError)
            {
                var failedRestores = new List<string>();
                foreach (PendingFile file in destinations.Reverse())
                {
                    if (!file.Replaced)
                        continue;
                    try
                    {
                        if (file.Existed)
                            File.Copy(file.Backup, file.Destination, overwrite: true);
                        else
                            File.Delete(file.Destination); // Only a new file created by this save.
                    }
                    catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        failedRestores.Add(file.Destination);
                    }
                }
                if (failedRestores.Count > 0)
                {
                    keepRecoveryFiles = true;
                    throw new IOException(L10n.Ui("SaveRecoveryRequired", string.Join(Environment.NewLine, failedRestores),
                        stageDirectory, saveError.Message), saveError);
                }
                throw new IOException(L10n.Ui("SaveRestored", saveError.Message), saveError);
            }
        }
        finally
        {
            if (!keepRecoveryFiles)
            {
                try
                {
                    if (Directory.Exists(stageDirectory))
                        Directory.Delete(stageDirectory, recursive: true); // Only our unique staging folder.
                }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    if (saved)
                        cleanupWarning = L10n.Ui("SaveCleanupWarning", stageDirectory);
                }
            }
        }
        return cleanupWarning;
    }

    private sealed class PendingFile(string staged, string destination, string backup)
    {
        public string Staged { get; } = staged;
        public string Destination { get; } = destination;
        public string Backup { get; } = backup;
        public bool Existed { get; set; }
        public bool Replaced { get; set; }
    }
}
