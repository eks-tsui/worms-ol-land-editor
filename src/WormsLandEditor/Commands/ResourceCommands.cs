using WormsLandEditor.Formats;
using WormsLandEditor.Localization;

namespace WormsLandEditor.Commands;

internal static class ResourceCommands
{
    internal static void ExportResource(string dirPath, string name, string outputPath)
    {
        DirFile dir = DirFile.Load(dirPath);
        List<Bitmap> frames = dir.CreatePreviewFrames(name, preserveSpriteCanvas: true);
        if (frames.Count == 0)
            throw new InvalidOperationException(L10n.Cli("NoExportableResource", dirPath, name));

        try
        {
            CommandLine.ReportWrites(outputPath);
            if (frames.Count > 1)
                AnimatedGifWriter.Save(outputPath, frames, delayMilliseconds: 120);
            else
                frames[0].Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
        }
        finally
        {
            foreach (Bitmap frame in frames)
                frame.Dispose();
        }

        using Image verify = Image.FromFile(outputPath);
        Console.WriteLine(L10n.Cli("AnimationExported", outputPath, verify.Width, verify.Height, verify.GetFrameCount(new System.Drawing.Imaging.FrameDimension(verify.FrameDimensionsList[0]))));
    }

    internal static void ExportResourceFrame(string dirPath, string name, string outputPath)
    {
        DirFile dir = DirFile.Load(dirPath);
        List<Bitmap> frames = dir.CreatePreviewFrames(name, preserveSpriteCanvas: true);
        if (frames.Count == 0)
            throw new InvalidOperationException(L10n.Cli("NoExportableResource", dirPath, name));

        try
        {
            CommandLine.ReportWrites(outputPath);
            frames[0].Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine(L10n.Cli("ImageExported", outputPath, frames[0].Width, frames[0].Height));
        }
        finally
        {
            foreach (Bitmap frame in frames)
                frame.Dispose();
        }
    }

    internal static string OutputDirectory(string inputPath) =>
        Path.GetDirectoryName(Path.GetFullPath(inputPath))!;

    internal static string OutputPath(string inputPath) =>
        Path.Combine(OutputDirectory(inputPath), "Level.Dir");

    internal static void Normalize(string inputPath)
    {
        DirFile dir = DirFile.Load(inputPath);
        dir.NormalizeResourceFormats();
        Save(dir, inputPath);
        InspectionCommands.PrintPaletteReport(OutputPath(inputPath));
    }

    internal static void Copy(string sourcePath, string name, string targetPath)
    {
        DirFile source = DirFile.Load(sourcePath);
        DirFile target = DirFile.Load(targetPath);
        DirResourceEntry entry = source.Find(name)
            ?? throw new InvalidOperationException(L10n.Cli("ResourceNotFound", sourcePath, name));
        target.ReplaceResourceBytes(name, entry.Data.ToArray());
        target.NormalizeResourceFormats();
        Save(target, targetPath);
        InspectionCommands.PrintPaletteReport(OutputPath(targetPath));
    }

    internal static void Replace(string targetPath, string name, string replacementPath)
    {
        DirFile target = DirFile.Load(targetPath);
        target.ReplaceResourceFromFile(name, replacementPath);
        Save(target, targetPath);
        // IMG resources must not be parsed as SPR after a successful write.
        PrintResourceInfo(OutputPath(targetPath), name);
    }

    internal static void SetSpriteCanvas(string targetPath, string name, string widthText, string heightText)
    {
        int width = CommandLine.ParseInteger(widthText, 0, ushort.MaxValue, "CanvasWidth");
        int height = CommandLine.ParseInteger(heightText, 0, ushort.MaxValue, "CanvasHeight");
        DirFile dir = DirFile.Load(targetPath);
        DirResourceEntry entry = dir.Find(name)
            ?? throw new InvalidOperationException(L10n.Cli("ResourceNotFound", targetPath, name));
        WolSprite sprite = WolSprite.Read(entry.Data);
        sprite.FieldB = (ushort)width;
        sprite.FieldC = (ushort)height;
        entry.Data = sprite.ToBytes();
        Save(dir, targetPath);
        InspectionCommands.PrintSpriteInfo(OutputPath(targetPath), name);
    }

    private static void Save(DirFile dir, string targetPath)
    {
        CommandLine.ReportWrites(OutputPath(targetPath));
        dir.SaveTo(OutputDirectory(targetPath));
        Console.WriteLine(L10n.Cli("FileWritten", OutputPath(targetPath)));
    }

    private static void PrintResourceInfo(string path, string name)
    {
        DirFile dir = DirFile.Load(path);
        DirResourceEntry entry = dir.Find(name)
            ?? throw new InvalidOperationException(L10n.Cli("ResourceNotFound", path, name));
        if (entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
        {
            InspectionCommands.PrintSpriteInfo(path, name);
        }
        else if (entry.Name.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
        {
            int offset = 0;
            InspectionCommands.PrintImage(entry.Name, WolImage.Read(entry.Data, ref offset));
        }
        else
        {
            Console.WriteLine(L10n.Cli("ResourceBytesReport", entry.Name, entry.Data.Length));
        }
    }
}
