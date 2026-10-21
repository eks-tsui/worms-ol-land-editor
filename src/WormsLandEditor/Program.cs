using WormsLandEditor.Commands;
using WormsLandEditor.Localization;

namespace WormsLandEditor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        LocalizationManager.Initialize();
        ApplicationConfiguration.Initialize();

        if (args.Length > 0)
            return CommandLine.Run(args);

        Application.Run(new MainForm());
        return 0;
    }
}
