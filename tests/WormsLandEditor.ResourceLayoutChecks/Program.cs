using WormsLandEditor.Localization;

namespace WormsLandEditor;

internal static class ResourceLayoutChecksProgram
{
    [STAThread]
    private static int Main()
    {
        // A modal dialog or a hung message loop must fail CI rather than wait for input.
        using var timeout = new System.Threading.Timer(_ =>
        {
            Console.Error.WriteLine("FAIL: Windows resource-layout checks timed out.");
            Environment.Exit(124);
        }, null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
        try
        {
            LocalizationManager.Initialize();
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            MainForm.RunResourceLayoutChecks();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
