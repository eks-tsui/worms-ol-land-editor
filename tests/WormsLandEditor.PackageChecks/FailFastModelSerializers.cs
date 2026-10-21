// Package checks exercise real validated bytes and file transactions without the
// Windows image stack. A raw snapshot save must never call a model serializer.
namespace WormsLandEditor.Formats;

public sealed class LandFile
{
    public void Save(string path) => throw new InvalidOperationException("Unexpected DAT serialization during raw save.");
    public static LandFile Load(string path) => throw new InvalidOperationException("Unexpected DAT model load during raw save.");
}

public sealed class DirFile
{
    public void SaveTo(string path) => throw new InvalidOperationException("Unexpected DIR serialization during raw save.");
    public static DirFile Load(string path) => throw new InvalidOperationException("Unexpected DIR model load during raw save.");
}
