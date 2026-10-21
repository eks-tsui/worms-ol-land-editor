using WormsLandEditor.Localization;

namespace WormsLandEditor.Services;

internal static class MapPackagePaths
{
    internal static void Check(string path)
    {
        string full = Path.GetFullPath(path);
        var ancestors = new Stack<string>();
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
        while (ancestors.TryPop(out string? current))
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(L10n.Package("UnsafeFileSystemPath", current));
                if (current != full && (attributes & FileAttributes.Directory) == 0)
                    throw new InvalidDataException(L10n.Package("UnsafeFileSystemPath", current));
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    internal static void CheckFile(string path)
    {
        Check(path);
        if (Directory.Exists(path)) throw new InvalidDataException(L10n.Package("UnsafeFileSystemPath", path));
    }
}
