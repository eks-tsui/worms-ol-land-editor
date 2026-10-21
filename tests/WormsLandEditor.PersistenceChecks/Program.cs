using WormsLandEditor.Services;
using WormsLandEditor.Formats;

string root = Path.Combine(Path.GetTempPath(), "worms-save-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
    checks++;
}
(string folder, string dat, string dir) Setup(string name, bool oldDat = true)
{
    string folder = Path.Combine(root, name);
    string tmap = Path.Combine(folder, "Mfx", "Tmap01");
    Directory.CreateDirectory(tmap);
    string dat = Path.Combine(folder, "land01.dat");
    if (oldDat) File.WriteAllText(dat, "old-DAT");
    return (folder, dat, Path.Combine(tmap, "Level.Dir"));
}
void NoStage(string folder) => Check(!Directory.EnumerateDirectories(folder, ".worms-map-save-*").Any(), "Owned stage folder cleaned: " + Path.GetFileName(folder));
try
{
    var success = Setup("success");
    File.WriteAllText(success.dir, "old-DIR");
    string extra = Path.Combine(Path.GetDirectoryName(success.dir)!, "keep-me.txt");
    File.WriteAllText(extra, "untouched");
    Check(MapSaveService.Save(new(), new(), success.dat, success.dir) is null, "Success without cleanup warning");
    Check(File.ReadAllText(success.dat) == "new-DAT" && File.ReadAllText(success.dir) == "new-DIR", "Both destination files committed");
    Check(File.ReadAllText(extra) == "untouched", "Unrelated Tmap file preserved");
    NoStage(success.folder);

    var serialize = Setup("serialize-failure");
    File.WriteAllText(serialize.dir, "old-DIR");
    try { MapSaveService.Save(new(), new() { ThrowOnSave = true }, serialize.dat, serialize.dir); throw new Exception("Expected failure"); }
    catch (InvalidDataException) { }
    Check(File.ReadAllText(serialize.dat) == "old-DAT" && File.ReadAllText(serialize.dir) == "old-DIR", "DIR serialization failure leaves both originals unchanged");
    NoStage(serialize.folder);

    var rollback = Setup("rollback-existing");
    Directory.CreateDirectory(rollback.dir); // A directory where Level.Dir should be makes the second commit fail.
    File.WriteAllText(Path.Combine(rollback.dir, "untouched.txt"), "untouched");
    try { MapSaveService.Save(new(), new(), rollback.dat, rollback.dir); throw new Exception("Expected failure"); }
    catch (IOException e) { Check(e.Message.StartsWith("SaveRestored"), "Existing DAT rollback is reported"); }
    Check(File.ReadAllText(rollback.dat) == "old-DAT", "Second-file commit failure restores original DAT");
    Check(File.ReadAllText(Path.Combine(rollback.dir, "untouched.txt")) == "untouched", "Conflicting destination directory is not deleted");
    NoStage(rollback.folder);

    var newRollback = Setup("rollback-new", false);
    Directory.CreateDirectory(newRollback.dir);
    try { MapSaveService.Save(new(), new(), newRollback.dat, newRollback.dir); throw new Exception("Expected failure"); }
    catch (IOException e) { Check(e.Message.StartsWith("SaveRestored"), "New DAT rollback is reported"); }
    Check(!File.Exists(newRollback.dat), "Failed two-file save removes only the newly created DAT");
    NoStage(newRollback.folder);
    Console.WriteLine($"{checks} checks passed. Save service checks use test serializers; file-format and GUI behavior are not covered.");
}
finally { Directory.Delete(root, recursive:true); }

namespace WormsLandEditor.Formats
{
    public sealed class LandFile
    {
        public void Save(string path) => File.WriteAllText(path, "new-DAT");
        public static LandFile Load(string path) { _ = File.ReadAllText(path); return new(); }
    }
    public sealed class DirFile
    {
        public bool ThrowOnSave { get; init; }
        public void SaveTo(string path)
        {
            if (ThrowOnSave) throw new InvalidDataException("Injected serialization failure");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "Level.Dir"), "new-DIR");
        }
        public static DirFile Load(string path) { _ = File.ReadAllText(path); return new(); }
    }
}
namespace WormsLandEditor.Localization
{
    public static class L10n
    {
        public static string Ui(string key, params object?[] args) => key + ": " + string.Join("; ", args);
    }
}
