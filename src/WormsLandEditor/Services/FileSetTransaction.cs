namespace WormsLandEditor.Services;

internal interface IMapPackageFileOperations
{
    void Replace(string staged, string destination);
    void Restore(string backup, string destination);
}

/// <summary>
/// Shared save/import replacement and backup recovery. The caller owns preparation
/// until invoking CommitPrepared; this helper then owns cleanup or recovery files.
/// Not a power-loss transaction or protection against hostile concurrent changes.
/// </summary>
internal static class FileSetTransaction
{
    internal sealed record PreparedFile(string Staged, string Destination);

    internal static string? CommitPrepared(string stage, PreparedFile[] prepared,
        Func<string, object?[], string> message, Action<string>? checkPath = null,
        IMapPackageFileOperations? operations = null)
    {
        operations ??= new PhysicalOperations();
        checkPath ??= _ => { };
        var files = prepared.Select((file, i) => new Pending(file, Path.Combine(stage, $"original-{i}"))).ToArray();
        var createdDirectories = new List<string>();
        bool preserveRecovery = false, committed = false;
        string? cleanupWarning = null;
        try
        {
            foreach (Pending file in files)
            {
                checkPath(file.File.Destination);
                file.Existed = File.Exists(file.File.Destination);
                if (file.Existed) File.Copy(file.File.Destination, file.Backup, overwrite: false);
            }
            try
            {
                foreach (Pending file in files)
                {
                    CreateDirectories(Path.GetDirectoryName(file.File.Destination)!, createdDirectories, checkPath);
                    checkPath(file.File.Destination);
                    // A failed operation may have partly changed a file. Remember the
                    // attempt, but never treat a pre-existing directory as our output.
                    file.Attempted = !Directory.Exists(file.File.Destination);
                    operations.Replace(file.File.Staged, file.File.Destination);
                }
                committed = true;
            }
            catch (Exception error)
            {
                var failures = new List<string>();
                foreach (Pending file in files.Reverse().Where(f => f.Attempted))
                {
                    try
                    {
                        checkPath(file.File.Destination);
                        if (file.Existed) operations.Restore(file.Backup, file.File.Destination);
                        else if (File.Exists(file.File.Destination)) File.Delete(file.File.Destination);
                        else if (Directory.Exists(file.File.Destination)) throw new IOException(message("Restored", [file.File.Destination]));
                    }
                    catch (Exception restoreError) when (IsFileError(restoreError)) { failures.Add(file.File.Destination); }
                }
                foreach (string directory in createdDirectories.AsEnumerable().Reverse())
                {
                    try
                    {
                        checkPath(directory);
                        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                    }
                    catch (Exception cleanupError) when (IsFileError(cleanupError)) { failures.Add(directory); }
                }
                if (failures.Count > 0)
                {
                    preserveRecovery = true;
                    throw new IOException(message("RecoveryRequired", [string.Join(Environment.NewLine, failures), stage, error.Message]), error);
                }
                throw new IOException(message("Restored", [error.Message]), error);
            }
        }
        finally
        {
            if (!preserveRecovery)
            {
                try
                {
                    checkPath(stage);
                    if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
                }
                catch (Exception cleanupError) when (IsFileError(cleanupError))
                {
                    if (committed) cleanupWarning = message("CleanupWarning", [stage]);
                }
            }
        }
        return cleanupWarning;
    }

    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or System.Security.SecurityException;
    private static void CreateDirectories(string path, List<string> created, Action<string> checkPath)
    {
        checkPath(path);
        if (Directory.Exists(path)) return;
        string? parent = Path.GetDirectoryName(path);
        if (parent is not null) CreateDirectories(parent, created, checkPath);
        Directory.CreateDirectory(path);
        created.Add(path);
    }
    private sealed class Pending(PreparedFile file, string backup)
    {
        public PreparedFile File { get; } = file;
        public string Backup { get; } = backup;
        public bool Existed { get; set; }
        public bool Attempted { get; set; }
    }
    private sealed class PhysicalOperations : IMapPackageFileOperations
    {
        public void Replace(string staged, string destination) => File.Move(staged, destination, overwrite: true);
        public void Restore(string backup, string destination) => File.Copy(backup, destination, overwrite: true);
    }
}
