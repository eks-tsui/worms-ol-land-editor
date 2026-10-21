using System.Drawing.Imaging;
using WormsLandEditor.Formats;
using WormsLandEditor.Localization;
using WormsLandEditor.Services;

namespace WormsLandEditor;

public sealed partial class MainForm
{
    private async void ImportPng(object? sender, EventArgs e)
    {
        if (_operationInProgress || !EnsureGameDataDirectoryConfigured())
            return;
        using var dialog = new OpenFileDialog
        {
            Title = L10n.Ui("ChooseMapPng"),
            Filter = L10n.Ui("PngFilter"),
            DefaultExt = "png",
            CheckFileExists = true,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string inputDat = Path.Combine(Path.GetTempPath(), $"worms-map-input-{Guid.NewGuid():N}.dat");
        string outputDat = Path.Combine(Path.GetTempPath(), $"worms-map-output-{Guid.NewGuid():N}.dat");
        SetOperationBusy(true);
        try
        {
            ValidateMapPngSize(dialog.FileName);
            LandFile edited;
            if (_land is null)
            {
                await RunConverterAsync(L10n.Ui("CreatingFromPng"), "--frompng", dialog.FileName, outputDat);
                edited = LandFile.Load(outputDat);
                edited.PrimaryPoints.Clear();
                edited.NameA = SanitizeMapName(Path.GetFileNameWithoutExtension(dialog.FileName));
                edited.NameB = SelectedWaterResourcePath();
                edited.SourcePath = CurrentLandPath();
                LoadResourcesForCurrentMap(useTemplateIfMissing: true);
            }
            else
            {
                _land.Save(inputDat);
                await RunConverterAsync(L10n.Ui("ImportingPng"), "--replacepng", inputDat, dialog.FileName, outputDat);
                edited = LandFile.Load(outputDat);
                edited.SourcePath = _land.SourcePath;
            }

            _land = edited;
            _currentMapPngPath = dialog.FileName;
            MarkDirty();
            RemapCurrentLandToGamePalette();
            _canvas.Land = _land;
            _canvas.SetSourcePreview(dialog.FileName);
            RefreshMapOptions();
            RefreshResourcePanel();
            UpdateStatus(L10n.Ui("PngImported", Path.GetFileName(dialog.FileName)));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            TryDelete(inputDat);
            TryDelete(outputDat);
            SetOperationBusy(false);
        }
    }

    private void ImportMap(object? sender, EventArgs e)
    {
        if (_operationInProgress)
            return;

        if (!EnsureGameDataDirectoryConfigured())
            return;
        using var dialog = new OpenFileDialog
        {
            Title = L10n.Ui("ChooseLegacyMap"),
            Filter = L10n.Ui("LegacyMapFilter"),
            CheckFileExists = true,
            RestoreDirectory = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            if (string.Equals(Path.GetExtension(dialog.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                ImportMapPackage(dialog.FileName);
                return;
            }
            if (!ConfirmDiscard())
                return;

            int mapNumber = (int)_mapNumber.Value;
            LegacyMapKind kind = LegacyMapImporter.DetectKind(dialog.FileName);
            LegacyMapImportResult result = LegacyMapImporter.Import(kind, dialog.FileName, CurrentDataDirectory(), mapNumber, SelectedWaterResourcePath());
            _land = result.Land;
            _levelResources = result.Resources;
            _currentMapPngPath = null;
            MarkDirty();
            RefreshMapOptions();
            RefreshWaterResources();
            _canvas.Land = _land;
            _canvas.SetSourcePreview(null);
            RefreshResourcePanel();
            UpdateStatus(L10n.Ui("LegacyMapImported", LegacyMapKindDisplayName(kind), result.SourceDescription));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private static string LegacyMapKindDisplayName(LegacyMapKind kind)
    {
        return kind switch
        {
            LegacyMapKind.WorldParty => L10n.Ui("WorldParty"),
            LegacyMapKind.Armageddon => "WA",
            LegacyMapKind.Underwater => L10n.Ui("Underwater"),
            _ => L10n.Ui("Legacy")
        };
    }

    private void SaveMap(object? sender, EventArgs e)
    {
        TrySaveCurrentMap();
    }

    private bool TrySaveCurrentMap(bool showSuccessMessage = true)
    {
        if (_operationInProgress)
            return false;
        try
        {
            if (!EnsureGameDataDirectoryConfigured())
                return false;
            if (_land is null)
            {
                MessageBox.Show(this, L10n.Ui("MapRequired"), L10n.Ui("NoMapTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (_land.PrimaryPoints.Count < LandFile.MinimumSpawnPoints)
            {
                MessageBox.Show(this, L10n.Ui("SpawnPointsRequired"), L10n.Ui("MissingSpawnsTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            int mapNumber = (int)_mapNumber.Value;
            string dataDirectory = CurrentDataDirectory();
            string landName = $"land{mapNumber:00}.dat";
            string landPath = Path.Combine(dataDirectory, landName);
            string tmapDirectory = Path.Combine(dataDirectory, "Mfx", $"Tmap{mapNumber:00}");
            string levelPath = Path.Combine(tmapDirectory, "Level.Dir");
            if ((File.Exists(landPath) || File.Exists(levelPath)) &&
                MessageBox.Show(this, L10n.Ui("OverwriteFiles", landPath, levelPath), L10n.Ui("ConfirmOverwriteTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return false;

            string? cleanupWarning = null;
            RunWithWait(L10n.Ui("SavingMap"), () =>
            {
                DirFile levelResources = _levelResources ?? DirFile.LoadTemplate(dataDirectory);
                MarkDirty();
                _land.SetClientResourcePaths(mapNumber, SelectedWaterResourcePath());
                _levelResources = levelResources;
                RefreshCurrentLandFromSourcePngIfAvailable();
                RemapCurrentLandToGamePalette();
                cleanupWarning = MapSaveService.Save(_land, levelResources, landPath, levelPath);
                _land.SourcePath = landPath;
                _dirty = false;
                _loadedMapNumber = mapNumber;
                try
                {
                    _levelResources = DirFile.Load(levelPath);
                    _canvas.Land = _land;
                    RefreshResourcePanel();
                }
                catch (Exception reloadError)
                {
                    string warning = L10n.Ui("SavedResourceReloadFailed", reloadError.Message);
                    cleanupWarning = cleanupWarning is null ? warning : cleanupWarning + Environment.NewLine + warning;
                }
            });
            string message = L10n.Ui("SaveSucceeded", landName, mapNumber);
            UpdateStatus(message);
            if (showSuccessMessage || cleanupWarning is not null)
                MessageBox.Show(this, cleanupWarning is null ? message : message + Environment.NewLine + Environment.NewLine + cleanupWarning,
                    L10n.Ui("SavedTitle"), MessageBoxButtons.OK, cleanupWarning is null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            return true;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return false;
        }
    }

    private void DeleteMap(object? sender, EventArgs e)
    {
        if (_operationInProgress)
            return;
        try
        {
            if (!EnsureGameDataDirectoryConfigured())
                return;
            int mapNumber = (int)_mapNumber.Value;
            string dataDirectory = CurrentDataDirectory();
            string landName = $"land{mapNumber:00}.dat";
            string landPath = Path.GetFullPath(Path.Combine(dataDirectory, landName));
            string mfxDirectory = Path.GetFullPath(Path.Combine(dataDirectory, "Mfx"));
            string tmapDirectory = Path.GetFullPath(Path.Combine(mfxDirectory, $"Tmap{mapNumber:00}"));
            string mfxPrefix = mfxDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!tmapDirectory.StartsWith(mfxPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L10n.Ui("UnsafeDeletePath"));

            if (MessageBox.Show(this, L10n.Ui("DeleteFilesWarning", mapNumber, landPath, tmapDirectory), L10n.Ui("ConfirmDeleteTitle"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            RunWithWait(L10n.Ui("DeletingMap"), () =>
            {
                if (File.Exists(landPath))
                    File.Delete(landPath);
                if (Directory.Exists(tmapDirectory))
                    Directory.Delete(tmapDirectory, recursive: true);
            });
            ResetCurrentMap();
            UpdateStatus(L10n.Ui("MapDeleted", landName, mapNumber));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void SetMapDirectory(object? sender, EventArgs e)
    {
        if (_operationInProgress || !ConfigureGameDataDirectory())
            return;
        try
        {
            RefreshDefaultMapNumber();
            RefreshWaterResources();
            LoadMapForCurrentNumber(confirmDiscard: false);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ClearSpawnPoints(object? sender, EventArgs e)
    {
        if (_operationInProgress || _land is null || _land.PrimaryPoints.Count == 0)
            return;

        _land.PrimaryPoints.Clear();
        _canvas.ResetSpawnPointSlots();
        MarkDirty();
        UpdateStatus(L10n.Ui("SpawnsCleared"));
    }

    private void RemapCurrentLandToGamePalette()
    {
        if (_land is null || _levelResources is null || string.IsNullOrWhiteSpace(CurrentGameDirectory()))
            return;

        PaletteUtil.RemapTerrainResources(_land, _levelResources);
    }

    private void RefreshCurrentLandFromSourcePngIfAvailable()
    {
        if (_land is null || string.IsNullOrWhiteSpace(_currentMapPngPath))
            return;

        if (!File.Exists(_currentMapPngPath))
            throw new FileNotFoundException(L10n.Ui("SourcePngMissing"), _currentMapPngPath);

        ValidateMapPngSize(_currentMapPngPath);
        using var bitmap = new Bitmap(_currentMapPngPath);
        _land.ReplacePng(bitmap);
    }

    private void ClearMapPreview(object? sender, EventArgs e)
    {
        if (_operationInProgress || (_land is null && !_dirty) || !ConfirmDiscard())
            return;
        ResetCurrentMap();
        UpdateStatus(L10n.Ui("MapCleared"));
    }

    private void ExportMapPreview(object? sender, EventArgs e)
    {
        if (_land is null)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = L10n.Ui("ExportMapPng"),
            Filter = L10n.Ui("PngFilter"),
            DefaultExt = "png",
            FileName = $"land{(int)_mapNumber.Value:00}.png",
            RestoreDirectory = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            using Bitmap bitmap = _land.Terrain.ToBitmap(transparentZero: true);
            bitmap.Save(dialog.FileName, ImageFormat.Png);
            UpdateStatus(L10n.Ui("PngExported", dialog.FileName));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }
}
