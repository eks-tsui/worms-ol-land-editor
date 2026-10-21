using WormsLandEditor.Localization;
using WormsLandEditor.Formats;
using WormsLandEditor.Services;

namespace WormsLandEditor;

public sealed partial class MainForm
{
    private void ExportMapPackage(object? sender, EventArgs e)
    {
        if (_operationInProgress || !EnsureGameDataDirectoryConfigured())
            return;
        if (_land is null)
        {
            MessageBox.Show(this, L10n.Ui("MapRequired"), L10n.Ui("NoMapTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        UpdateStatus(L10n.Ui("MapPackageContents"));
        if (MessageBox.Show(this, L10n.Ui("ConfirmExportMapPackage"), L10n.Ui("ConfirmExportMapPackageTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            UpdateStatus(L10n.Ui("MapPackageExportCancelled"));
            return;
        }

        int mapNumber = (int)_mapNumber.Value;
        using var dialog = new SaveFileDialog
        {
            Title = L10n.Ui("ExportMapPackage"),
            Filter = L10n.Ui("MapPackageFilter"),
            DefaultExt = "zip",
            AddExtension = true,
            FileName = $"land{mapNumber:00}.zip",
            RestoreDirectory = true,
            OverwritePrompt = true,
            CheckPathExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            UpdateStatus(L10n.Ui("MapPackageExportCancelled"));
            return;
        }

        try
        {
            if (!string.Equals(Path.GetExtension(dialog.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L10n.Ui("MapPackageZipExtensionRequired"));

            string dataDirectory = CurrentDataDirectory();
            string levelPath = Path.Combine(dataDirectory, "Mfx", $"Tmap{mapNumber:00}", "Level.Dir");
            if (_dirty || !File.Exists(CurrentLandPath()) || !File.Exists(levelPath))
            {
                if (MessageBox.Show(this, L10n.Ui("SaveBeforeMapPackageExport", mapNumber), L10n.Ui("UnsavedChangesTitle"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    UpdateStatus(L10n.Ui("MapPackageExportCancelled"));
                    return;
                }
                // A cancelled or failed save must never fall back to exporting older files.
                if (!TrySaveCurrentMap(showSuccessMessage: false))
                    return;
            }

            RunWithWait(L10n.Ui("ExportingMapPackage"), () =>
                MapPackageService.Export(dataDirectory, mapNumber, dialog.FileName));
            UpdateStatus(L10n.Ui("MapPackageExported", dialog.FileName));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ImportMapPackage(string zipPath)
    {
        int mapNumber = (int)_mapNumber.Value;
        SetOperationBusy(true);
        UpdateStatus(L10n.Ui("ValidatingMapPackage"));
        Refresh();
        PreparedEditorView? view = null;
        try
        {
            bool imported = _session.TryReplace(() =>
            {
                // Validation and user confirmation never write to the game directory.
                PreparedMapPackage? package = MapPackageService.PrepareImport(zipPath, mapNumber, ConfirmMapPackageImport);
                if (package is null)
                    return null;
                string levelPath = Path.Combine(CurrentDataDirectory(), "Mfx", $"Tmap{mapNumber:00}", "Level.Dir");
                var candidate = new EditorState<LandFile, DirFile, PreparedMapPackage>(
                    LandFile.Load(package.LandBytes, CurrentLandPath()),
                    DirFile.Load(package.DirectoryBytes, levelPath), null, true, mapNumber, package);
                view = PrepareEditorView(candidate);
                return candidate;
            }, candidate => InstallPreparedEditorView(view!, candidate));

            UpdateStatus(L10n.Ui(imported ? "MapPackageImported" : "MapPackageImportCancelled", mapNumber));
        }
        finally
        {
            // After commit this owns the old preview; after failure it owns only the candidate.
            view?.Dispose();
            SetOperationBusy(false);
        }
    }

    private bool ConfirmMapPackageImport(MapPackageInfo package)
    {
        string editorChanges = L10n.Ui(_dirty ? "MapPackageDiscardUnsavedChanges" : "MapPackageReplaceEditor");
        string message = L10n.Ui("ConfirmImportMapPackage", package.SourceMapNumber, package.TargetMapNumber,
            editorChanges);
        UseWaitCursor = false;
        try
        {
            if (MessageBox.Show(this, message, L10n.Ui("MapPackageImportTitle"), MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return false;

            UpdateStatus(L10n.Ui("ImportingMapPackage"));
            Refresh();
            return true;
        }
        finally
        {
            UseWaitCursor = true;
        }
    }
}
