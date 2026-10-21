namespace WormsLandEditor;

public sealed class AppSettings
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WormsLandEditor");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.txt");

    public string? GameDataDirectory { get; set; }
    public Exception? LoadError { get; private init; }

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? new AppSettings { GameDataDirectory = File.ReadAllText(SettingsPath).Trim() }
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new AppSettings { LoadError = ex };
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        string temporaryPath = Path.Combine(SettingsDirectory, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, GameDataDirectory ?? string.Empty);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
