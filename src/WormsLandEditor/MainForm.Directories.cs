using WormsLandEditor.Formats;
using WormsLandEditor.Localization;

namespace WormsLandEditor;

public sealed partial class MainForm
{
    private static bool IsGameDirectory(string path)
    {
        return NormalizeGameDirectory(path) is not null;
    }

    private bool EnsureGameDataDirectoryConfigured()
    {
        if (IsConfiguredGameDataDirectory())
            return true;
        MessageBox.Show(this, L10n.Ui("ChooseGameDirectoryPrompt"), L10n.Ui("GameDirectory"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        if (ConfigureGameDataDirectory())
            return true;
        UpdateStatus(L10n.Ui("NoGameDirectory"));
        return false;
    }

    private bool IsConfiguredGameDataDirectory()
    {
        return !string.IsNullOrWhiteSpace(CurrentGameDirectory()) &&
               IsGameDirectory(CurrentGameDirectory());
    }

    private bool ConfigureGameDataDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = L10n.Ui("ChooseGameDirectory"),
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(CurrentGameDirectory()) ? CurrentGameDirectory() : string.Empty,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return false;
        string? gameDirectory = NormalizeGameDirectory(dialog.SelectedPath);
        if (gameDirectory is null)
        {
            MessageBox.Show(this, L10n.Ui("InvalidGameDirectory"), L10n.Ui("InvalidDirectoryTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (!ConfirmDiscard())
            return false;
        string? previousDirectory = _settings.GameDataDirectory;
        try
        {
            _settings.GameDataDirectory = gameDirectory;
            _settings.Save();
        }
        catch (Exception ex)
        {
            _settings.GameDataDirectory = previousDirectory;
            ShowError(ex);
            return false;
        }
        ResetCurrentMap();
        UpdateStatus(L10n.Ui("GameDirectoryStatus", gameDirectory));
        return true;
    }

    private void RefreshDefaultMapNumber()
    {
        string dataDirectory = CurrentDataDirectory();
        if (string.IsNullOrWhiteSpace(CurrentGameDirectory()) || !Directory.Exists(dataDirectory))
            return;

        int next = Directory.EnumerateFiles(dataDirectory, "land*.dat")
            .Select(TryGetMapNumber)
            .Where(n => n is not null)
            .Select(n => n!.Value)
            .DefaultIfEmpty(0)
            .Max() + 1;

        _changingMapNumber = true;
        _mapNumber.Value = Math.Clamp(next, (int)_mapNumber.Minimum, (int)_mapNumber.Maximum);
        _loadedMapNumber = (int)_mapNumber.Value;
        _changingMapNumber = false;
    }

    private void ChangeCurrentMapNumber()
    {
        if (_changingMapNumber)
            return;

        if (!LoadMapForCurrentNumber(confirmDiscard: true))
        {
            _changingMapNumber = true;
            _mapNumber.Value = Math.Clamp(_loadedMapNumber, (int)_mapNumber.Minimum, (int)_mapNumber.Maximum);
            _changingMapNumber = false;
        }
    }

    private bool LoadMapForCurrentNumber(bool confirmDiscard, string? failureContext = null)
    {
        if (confirmDiscard && !ConfirmDiscard())
            return false;

        if (!EnsureGameDataDirectoryConfigured())
            return false;
        int mapNumber = (int)_mapNumber.Value;
        string landPath = CurrentLandPath();
        LandFile? previousLand = _land;
        DirFile? previousResources = _levelResources;
        string? previousPng = _currentMapPngPath;
        bool previousDirty = _dirty;
        int previousNumber = _loadedMapNumber;
        object[] previousWaterItems = _waterResource.Items.Cast<object>().ToArray();
        int previousWaterIndex = _waterResource.SelectedIndex;

        try
        {
            LandFile? loadedLand = File.Exists(landPath) ? LandFile.Load(landPath) : null;
            _land = loadedLand;
            RefreshMapOptions();
            _currentMapPngPath = null;
            LoadResourcesForCurrentMap(useTemplateIfMissing: true, refreshPanel: false);
            RefreshWaterResources(updatePalettePreview: false);
            _canvas.Land = _land;
            _canvas.SetSourcePreview(null);
            _canvas.SetPalettePreview(null);
            _dirty = false;
            _loadedMapNumber = mapNumber;
            Text = L10n.Ui("AppTitle");
            RefreshResourcePanel();
            UpdateStatus(_land is null ? L10n.Ui("MapNotCreated", mapNumber) : L10n.Ui("MapLoaded", mapNumber));
            return true;
        }
        catch (Exception ex)
        {
            // Keep the loaded number and model together if preview/resource initialization fails.
            _land = previousLand;
            _levelResources = previousResources;
            _currentMapPngPath = previousPng;
            _dirty = previousDirty;
            _loadedMapNumber = previousNumber;
            try
            {
                _loadingWaterResources = true;
                try
                {
                    _waterResource.Items.Clear();
                    _waterResource.Items.AddRange(previousWaterItems);
                    _waterResource.SelectedIndex = previousWaterIndex;
                }
                finally { _loadingWaterResources = false; }
                _canvas.Land = previousLand;
                if (!string.IsNullOrWhiteSpace(previousPng) && File.Exists(previousPng))
                    _canvas.SetSourcePreview(previousPng);
                RefreshMapOptions();
                RefreshWindowTitle();
                RefreshWaterPreview();
                RefreshResourcePanel();
            }
            catch (Exception previewError)
            {
                // The model is retained even when rendering the old preview also fails.
                ex = new AggregateException(ex.Message + Environment.NewLine + previewError.Message, ex, previewError);
            }
            ShowError(failureContext is null ? ex : new InvalidOperationException(failureContext + Environment.NewLine + Environment.NewLine + ex.Message, ex));
            return false;
        }
    }

    private string CurrentLandPath()
    {
        string dataDirectory = CurrentDataDirectory();
        return Path.Combine(dataDirectory, $"land{(int)_mapNumber.Value:00}.dat");
    }

    private string CurrentGameDirectory()
    {
        return NormalizeGameDirectory(_settings.GameDataDirectory ?? string.Empty) ?? string.Empty;
    }

    private string CurrentDataDirectory()
    {
        string gameDirectory = CurrentGameDirectory();
        return string.IsNullOrWhiteSpace(gameDirectory) ? string.Empty : Path.Combine(gameDirectory, "Data");
    }

    private static string? NormalizeGameDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        string trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Directory.Exists(Path.Combine(trimmed, "Data", "Mfx")))
            return trimmed;

        if (string.Equals(Path.GetFileName(trimmed), "Data", StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(Path.Combine(trimmed, "Mfx")))
        {
            return Path.GetDirectoryName(trimmed);
        }

        return null;
    }
}
