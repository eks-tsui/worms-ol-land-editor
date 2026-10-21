using System.Globalization;
using WormsLandEditor.Localization;

namespace WormsLandEditor.Commands;

internal static class CommandLine
{
    private sealed record Command(int MinimumArguments, int MaximumArguments, string HelpKey, Action<string[]> Run);

    private static readonly IReadOnlyDictionary<string, Command> Commands =
        new Dictionary<string, Command>(StringComparer.Ordinal)
        {
            ["--inspect"] = new(1, 2, "HelpInspect", InspectionCommands.Inspect),
            ["--palette-report"] = new(1, int.MaxValue, "HelpPaletteReport", a => ForEachPath(a, InspectionCommands.PrintPaletteReport)),
            ["--points-report"] = new(1, 1, "HelpPointsReport", a => InspectionCommands.PrintPointsReport(a[1])),
            ["--mask-terrain-report"] = new(1, int.MaxValue, "HelpMaskTerrainReport", a => ForEachPath(a, InspectionCommands.PrintMaskTerrainReport)),
            ["--repair-spawns"] = new(2, 2, "HelpRepairSpawns", a => MapCommands.RepairSpawns(a[1], a[2])),
            ["--normalize-masks"] = new(2, 2, "HelpNormalizeMasks", a => MapCommands.NormalizeMasks(a[1], a[2])),
            ["--normalize-level-dir"] = new(1, 1, "HelpNormalizeLevelDir", a => ResourceCommands.Normalize(a[1])),
            ["--copy-resource"] = new(3, 3, "HelpCopyResource", a => ResourceCommands.Copy(a[1], a[2], a[3])),
            ["--set-closed-map"] = new(3, 3, "HelpSetClosedMap", a => MapCommands.SetClosedMap(a[1], a[2], a[3])),
            ["--replace-resource"] = new(3, 3, "HelpReplaceResource", a => ResourceCommands.Replace(a[1], a[2], a[3])),
            ["--palette-union"] = new(1, int.MaxValue, "HelpPaletteUnion", InspectionCommands.PaletteUnion),
            ["--slot-conflicts"] = new(2, 2, "HelpSlotConflicts", a => InspectionCommands.PrintSlotConflicts(a[1], a[2])),
            ["--spr-info"] = new(2, 2, "HelpSpriteInfo", a => InspectionCommands.PrintSpriteInfo(a[1], a[2])),
            ["--set-spr-canvas"] = new(4, 4, "HelpSetSpriteCanvas", a => ResourceCommands.SetSpriteCanvas(a[1], a[2], a[3], a[4])),
            ["--export-resource"] = new(3, 3, "HelpExportResource", a => ResourceCommands.ExportResource(a[1], a[2], a[3])),
            ["--export-resource-frame"] = new(3, 3, "HelpExportResourceFrame", a => ResourceCommands.ExportResourceFrame(a[1], a[2], a[3])),
            ["--legacy-spr-info"] = new(2, 2, "HelpLegacySpriteInfo", a => InspectionCommands.PrintLegacySpriteInfo(a[1], a[2])),
            ["--legacy-spr-pixels"] = new(2, 2, "HelpLegacySpritePixels", a => InspectionCommands.PrintLegacySpritePixels(a[1], a[2])),
            ["--roundtrip"] = new(2, 2, "HelpRoundtrip", a => MapCommands.Roundtrip(a[1], a[2])),
            ["--frompng"] = new(2, 2, "HelpFromPng", a => MapCommands.FromPng(a[1], a[2])),
            ["--replacepng"] = new(3, 3, "HelpReplacePng", a => MapCommands.ReplacePng(a[1], a[2], a[3])),
            ["--remap-land-palette"] = new(3, 4, "HelpRemapLandPalette", MapCommands.RemapPalette),
            ["--exportpng"] = new(2, 2, "HelpExportPng", a => MapCommands.ExportPng(a[1], a[2])),
            ["--export-land-debug"] = new(2, 2, "HelpExportLandDebug", a => MapCommands.ExportLandDebug(a[1], a[2])),
            ["--import-legacy"] = new(5, 7, "HelpImportLegacy", MapCommands.ImportLegacy),
        };

    internal static int Run(string[] args)
    {
        if (args is ["--help"])
        {
            PrintHelp(Console.Out);
            return 0;
        }

        if (args.Length == 0 || !Commands.TryGetValue(args[0], out Command? command))
        {
            Console.Error.WriteLine(L10n.Cli("UnknownCommand", args.Length == 0 ? string.Empty : args[0]));
            PrintHelp(Console.Error);
            return 2;
        }

        int count = args.Length - 1;
        if (count < command.MinimumArguments || count > command.MaximumArguments)
        {
            Console.Error.WriteLine(L10n.Cli("InvalidArguments", args[0]));
            Console.Error.WriteLine(L10n.Cli(command.HelpKey));
            Console.Error.WriteLine(L10n.Cli("HelpHint"));
            return 2;
        }

        try
        {
            command.Run(args);
            return 0;
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(L10n.Cli("ArgumentError", ex.Message));
            Console.Error.WriteLine(L10n.Cli(command.HelpKey));
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(L10n.Cli("CommandFailed", args[0], ex.GetType().Name, ex.Message));
            Console.Error.WriteLine(L10n.Cli("FailureWriteWarning"));
            return 1;
        }
    }

    private static void PrintHelp(TextWriter output)
    {
        output.WriteLine(L10n.Cli("HelpIntroduction"));
        foreach (Command command in Commands.Values)
            output.WriteLine(L10n.Cli(command.HelpKey));
        output.WriteLine(L10n.Cli("HelpFooter"));
    }

    internal static int ParseInteger(string value, int minimum, int maximum, string labelKey)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ||
            parsed < minimum || parsed > maximum)
            throw new UsageException(L10n.Cli("InvalidInteger", L10n.Cli(labelKey), minimum, maximum, value));
        return parsed;
    }

    internal static void ReportWrites(params string[] paths)
    {
        Console.Error.WriteLine(L10n.Cli("WriteWarning"));
        foreach (string path in paths)
            Console.Error.WriteLine(L10n.Cli("WriteTarget", Path.GetFullPath(path)));
        if (paths.Length > 1)
            Console.Error.WriteLine(L10n.Cli("NonTransactionalWarning"));
    }

    private static void ForEachPath(string[] args, Action<string> action)
    {
        foreach (string path in args.Skip(1))
            action(path);
    }

    internal sealed class UsageException(string message) : Exception(message)
    {
    }
}
