# Worms OL Land Editor

[English](README.md) | [简体中文](README.zh-CN.md)

A Windows desktop tool for creating and editing 2D terrain maps, built with C#, .NET 10, and WinForms.

Supports map PNG import, spawn point editing, water resource previews, map resource replacement, and reading and saving DAT / DIR files.

## Features

- Load and edit maps numbered `1–999`.
- Export a numbered map as a ZIP package and import it into another map number with overwrite confirmation.
- Import fixed-size terrain PNGs and automatically generate collision and auxiliary masks.
- Edit spawn points and choose open or closed maps.
- Select water resources and preview water surface animations.
- Preview, replace, clear, and export these map resources:
  - `gradient.img`
  - `back.spr`
  - `debris.spr`
  - `soil.img`
  - `grass.img`
- Save maps as `landNN.dat` and the corresponding `Mfx/TmapNN/Level.Dir`.
- Export terrain PNGs with transparent backgrounds; export single-frame resources as PNG and multi-frame resources as GIF.
- Provides compatibility import options for map formats associated with WA, WWP, and the Underwater edition. Compatibility depends on the specific file version and accompanying resources; not every IMG / DAT / WAM file is guaranteed to import.

## Download

Download the Windows x64 portable package from [GitHub Releases](https://github.com/eks-tsui/worms-ol-land-editor/releases). Extract it to a writable folder and run `WormsLandEditor.exe`. No map assets are included. Versions start at **1.0.0**.

## Requirements

- Windows x64.
- Building from source requires the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
- Running a framework-dependent build requires the .NET 10 Desktop Runtime. The self-contained build described below includes the required runtime.
- The GUI uses WinForms and Windows image-processing capabilities. Native macOS and Linux execution is not supported.

The application project has no additional NuGet package dependencies and does not reference other application projects. The initial restore may still need to download .NET targeting and runtime components.

## Interface language

The application automatically reads the system UI language at startup:

- Chinese system languages (`zh`, `zh-CN`, `zh-TW`, `zh-HK`, etc.): Simplified Chinese is displayed. There is currently no separate Traditional Chinese translation.
- English and all other system languages: English is displayed.

Buttons, window titles, file dialog descriptions, prompts, errors, and status text are managed through resource files. File paths, resource names, extensions, command arguments, and format identifiers are not translated.

To add a translation, create resource files for the corresponding culture alongside `Resources/UiStrings.resx`, `CoreStrings.resx`, `CliStrings.resx`, and `PackageStrings.resx`, and register the language selection rules in `Localization/LocalizationManager.cs`. Keys and format placeholders in each language should match the English base resources.

## Preparing map data

The editor source code does not include map templates, water resources, terrain images, or font files. Use assets you are authorized to use, and back up your working data first.

The GUI requires a folder containing `Data/Mfx`. You can also select its `Data` subfolder directly. The button is labeled “Game folder” in the English interface.

A typical data folder looks like this:

```text
MapWorkspace/
└── Data/
    ├── land01.dat                       # Existing map, optional
    ├── land42.dat                       # Existing map, optional
    ├── Mfx/
    │   ├── Tmap01/
    │   │   └── Level.Dir                # Default template required for new maps or compatibility imports
    │   └── Tmap42/
    │       └── Level.Dir                # Resource archive for map 42
    └── Water/
        └── Red/
            └── Water.dir               # Preview data for the corresponding water resource, optional
```

When loading a map, the editor first uses the `Level.Dir` for that map number. If that resource archive does not exist, it attempts to use `Data/Mfx/Tmap01/Level.Dir` as a template.

The template must be an existing, parseable resource archive containing the required resource entries. Resource archive writing currently reuses the existing directory index; creating a new DIR index from an empty folder is not supported. Copying a PNG alone does not replace the template.

Water resources come from `Data/Water/<name>/Water.dir`. If a water resource is missing, its water surface preview may be unavailable. Fallback names in the dropdown do not mean those resources are present.

The folder selection is saved in `%APPDATA%/WormsLandEditor/settings.txt` for the current Windows user.

## Building and running from source

Open PowerShell in the project root.

Restore and build:

```powershell
dotnet restore .\src\WormsLandEditor\WormsLandEditor.csproj
dotnet build .\src\WormsLandEditor\WormsLandEditor.csproj -c Release
```

Run the GUI:

```powershell
dotnet run --project .\src\WormsLandEditor\WormsLandEditor.csproj -c Release
```

Keep the default Windows apphost. The GUI converts PNGs by launching the same executable again. Do not build with `UseAppHost=false`, and do not launch the GUI by running the DLL directly.

Publish a self-contained Windows x64 build:

```powershell
dotnet publish .\src\WormsLandEditor\WormsLandEditor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\artifacts\win-x64
```

Run:

```powershell
.\artifacts\win-x64\WormsLandEditor.exe
```

Keep any accompanying files actually generated in the publish folder. Do not copy only one file without verifying that it is sufficient. See the [.NET deployment documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) for the meaning of the self-contained and single-file publishing options.

## Basic usage

### 1. Choose a folder and map number

After setting the map data folder, choose a map number. An existing number loads `Data/landNN.dat`. For a number that does not yet exist, use the “Import” button at the lower-right corner of the canvas to create a map from a PNG.

Map numbers use at least two digits: 1 corresponds to `land01.dat` / `Tmap01`, while 100 corresponds to `land100.dat` / `Tmap100`.

### 2. Import the main map PNG

The main map must be **1920 × 696**. Images with other dimensions are rejected.

- Pixels with alpha `0` are empty.
- Pixels with alpha `1–255` are treated as solid terrain.
- Solid terrain detection for the main map does not depend on pixel brightness. Semitransparent edges also become solid, so a fully transparent background and opaque terrain are recommended.
- Importing rebuilds the palette and masks. Colors are not guaranteed to match the original image exactly.

Internally, terrain uses an 8-bit indexed image. Collision and auxiliary masks are `1920 × 696` 1-bit images, and the low-resolution preview mask is `240 × 87`. The current PNG import workflow leaves the low-resolution preview mask empty.

After importing, keep the source PNG until the map has been saved. The current GUI rereads that file when saving to generate terrain and collision data. If the file is moved or deleted, you will need to import it again.

### 3. Edit spawn points and map options

- Left-click the map to place a spawn point.
- Click an existing spawn point to remove it. Subsequent additions preferentially reuse the vacant position in the spawn order.
- If a point cannot be placed directly, the application tries to find a nearby valid position. No point is added if no suitable footing is found.
- You can place up to **32** spawn points. Saving through the GUI requires at least **12**.
- “Clear spawns” removes all placed points.
- The “Closed map” setting is saved in the DAT file.

These count limits are enforced mainly by the GUI. Saving through lower-level code or the command line should not be treated as a complete validation that the map is usable.

### 4. Replace map resources

Click the preview image on a resource card at the bottom to import a replacement asset.

- `gradient.img`, `soil.img`, `grass.img`: Import a PNG with the same dimensions as the current resource.
- `back.spr`: Import a PNG or GIF with a width of **640**. The height may vary.
- `debris.spr`: Import a PNG or GIF with both width and height no greater than **80**.
- Compatibility handling for other SPR entries is based on their existing canvas dimensions.

Resources are converted using the existing palette and resource format, so colors, transparency, and canvas appearance may differ from the input file. Check the result after saving and reloading.

“Clear” clears the image pixels without deleting the resource entry. “Export” saves single-frame resources as PNG and multi-frame resources as GIF. GIF export currently uses **120 milliseconds** per frame throughout; it does not preserve the original animation timing losslessly.

### 5. Save and export

“Save” writes two components:

```text
Data/landNN.dat
Data/Mfx/TmapNN/Level.Dir
```

If the destination DAT or `Level.Dir` already exists, the GUI asks whether to overwrite it. Saving stages and validates both files, then backs up and replaces the destinations. Ordinary write failures trigger an attempt to restore the originals, but this is not a cross-file transaction. Always keep independent backups and check that both the map file and resource archive were written successfully.

The canvas “Export” button outputs only the terrain PNG. It does not include spawn point markers, the water surface preview, map options, or the resource archive. A PNG is not a complete map project file.

“Delete” deletes the DAT for the current map number and the entire `Mfx/TmapNN` folder, including any extra files placed in that folder. Do not store your only copy of a file or unrelated files in the map resource folder.

## Map ZIP packages

Use **Export map package**, not the terrain PNG export button, to transfer a map together with its resource archive. After confirmation, the save dialog suggests `land01.zip`, `land100.zip`, or the filename for the current map number. You can change the filename and location.

If the map is new or has unsaved changes, export requires saving the current map first. Cancelling or failing that save stops the export; existing edits are never silently replaced with an older saved copy.

A package contains exactly a versioned `manifest.json`, `Data/landNN.dat`, and `Data/Mfx/TmapNN/Level.Dir`. The complete `Level.Dir` is included, not just the resource cards visible in the editor. Unrelated files in the map folder, other maps, and shared water resources are not included. The water resource remains an external dependency identified in the manifest and confirmation; the destination needs the corresponding `Data/Water/<name>/Water.dir` for its preview.

Choose **Import map** and select a ZIP produced by this editor. The editor validates the package before asking to replace the currently selected map number. For example, importing `land01.zip` while map 100 is selected writes `land100.dat` and `Mfx/Tmap100/Level.Dir`, and rewrites the DAT's internal terrain resource path to `data\mfx\Tmap100`. The external water reference is preserved. Cancelling leaves the destination files unchanged.

The importer stages both files and uses backups for failure recovery. It does not extract arbitrary archive paths or overwrite other maps. As with ordinary saving, recovery cannot guarantee atomicity across crashes, power loss, or a failure while restoring backups. Keep independent backups. See [the package format and limits](docs/map-packages.md).

## Compatibility map import

Use “Import map” on the toolbar to select a supported IMG, DAT, WAM, or `Level.dir` file.

Importing infers the type and looks for accompanying resources using the file header, filename, and surrounding folders. Having only a single file may therefore be insufficient. Preserve the source assets’ neighboring folder structure whenever possible.

Compatibility import produces terrain and resources in the current editing format and may adjust image dimensions, palettes, and spawn points. It is not a lossless migration of a complete map project or all its settings. After importing, check transparent terrain areas, spawn points, resource previews, and map options before saving to a test folder.

## Common command-line operations

Use the following examples after publishing as described above. Command-line writes do not display the GUI overwrite confirmation. Use new output filenames and create output folders beforehand.

Inspect map structure:

```powershell
.\artifacts\win-x64\WormsLandEditor.exe --inspect ".\work\land42.dat"
```

Export a terrain PNG:

```powershell
.\artifacts\win-x64\WormsLandEditor.exe --exportpng ".\work\land42.dat" ".\out\land42.png"
```

Replace the terrain PNG in an existing DAT:

```powershell
.\artifacts\win-x64\WormsLandEditor.exe --replacepng ".\work\land42.dat" ".\art\terrain.png" ".\out\land42-edited.dat"
```

Generate a basic DAT from a PNG:

```powershell
.\artifacts\win-x64\WormsLandEditor.exe --frompng ".\art\terrain.png" ".\out\draft.dat"
```

`--frompng` can generate a basic DAT without `Level.Dir`, but it does not set spawn points, create a resource archive, or establish a complete numbered resource path. Its output is not a complete map ready for use.

Export the first frame of a resource as PNG:

```powershell
.\artifacts\win-x64\WormsLandEditor.exe --export-resource-frame ".\work\Level.Dir" "back.spr" ".\out\back.png"
```

The application is currently built as a Windows GUI program. Terminals may differ in how they handle standard output and wait for the process to exit. To read inspection reports, launch the application from a caller that can capture standard output. See `docs/cli.md` for the arguments, risks, and limitations of additional diagnostic and write commands.

## Limitations and usage notes

- The current main map editing workflow is fixed at `1920 × 696`. This is not an image editor for arbitrary dimensions.
- Writing currently quantizes and remaps palettes. A DAT read/write round trip is not guaranteed to preserve identical bytes.
- `Level.Dir` requires an existing valid template or resource archive. Creating a directory index from scratch is not supported, and there is no GUI for arbitrarily adding or deleting resource entries.
- Supported image inputs are PNG, plus GIF for SPR resources. There is no general-purpose BMP file import option.
- Brush drawing and undo/redo are not provided.
- Saving DAT and DIR is currently not a single transaction. If saving fails partway through, check both files separately. One successfully written file does not mean the save is complete.
- Compatibility import and resource decoding cover only the implemented variants. Untested assets, damaged files, or mismatched resource combinations may fail.
- Only process assets from trusted sources, and check saving and reloading results in a separate test folder.

## Project structure

```text
src/WormsLandEditor/       WinForms application
  Commands/              Command-line operations and inspection reports
  Controls/              Map canvas
  Formats/               Map, image, animation, and resource archive reading and writing
  Localization/          System language selection and resource loading
  Resources/             English and Simplified Chinese UI, error, and command text
  Services/              Staged map saving and failure recovery
  Properties/            Publish profiles
tests/                   Language and save-coordination regression checks
docs/                    Data layout, format, and command documentation
```

See [validation](docs/validation.md) for reproducible checks, verified scope, and the Windows acceptance checklist.

## License and assets

The project source code and documentation are licensed under the [MIT License](LICENSE), Copyright (c) 2026 eks-tsui.

Maps, templates, images, animations, and fonts you provide remain subject to their respective rights and licenses. Only commit assets you have the right to distribute publicly, and retain applicable copyright and license notices.
