using System.Diagnostics;
using WormsLandEditor.Formats;
using WormsLandEditor.Localization;

namespace WormsLandEditor;

public sealed partial class MainForm
{
    private void MarkDirty()
    {
        _dirty = true;
        RefreshWindowTitle();
    }

    private void RefreshMapOptions()
    {
        _loadingMapOptions = true;
        try
        {
            _closedMap.Enabled = _land is not null;
            _closedMap.Checked = _land?.IsClosedMap == true;
        }
        finally
        {
            _loadingMapOptions = false;
        }
    }

    private void ApplyClosedMapSelection()
    {
        if (_loadingMapOptions || _land is null)
            return;

        _land.IsClosedMap = _closedMap.Checked;
        MarkDirty();
    }

    private bool ConfirmDiscard()
    {
        return !_dirty || MessageBox.Show(this, L10n.Ui("DiscardChanges"), L10n.Ui("UnsavedChangesTitle"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void UpdateStatus(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.ToolTipText = message;
        RefreshWindowTitle();
    }

    private void ShowError(Exception ex)
    {
        MessageBox.Show(this, ex.Message, L10n.Ui("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        UpdateStatus(ex.Message);
    }

    private void RunWithWait(string message, Action action)
    {
        SetOperationBusy(true);
        UpdateStatus(message);
        // Refresh paints status without dispatching another edit/save/delete event.
        Refresh();
        try
        {
            action();
        }
        finally
        {
            SetOperationBusy(false);
        }
    }

    private async Task RunConverterAsync(string message, params string[] args)
    {
        UpdateStatus(message);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (string arg in args)
            process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The process may already have exited or may not permit termination.
            }
            throw new TimeoutException(L10n.Ui("ConversionTimeout"));
        }
        string stderr = await stderrTask;
        string stdout = await stdoutTask;
        if (process.ExitCode != 0)
        {
            string details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(L10n.Ui("ConverterFailed", process.ExitCode,
                string.IsNullOrWhiteSpace(details) ? L10n.Ui("ConverterNoDetails") : details.Trim()));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static string SanitizeMapName(string value)
    {
        string cleaned = new(value.Where(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "CustomMap" : cleaned[..Math.Min(cleaned.Length, 32)];
    }

    private static int? TryGetMapNumber(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.StartsWith("land", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name[4..], out int value) &&
            value is >= LandFile.MinimumMapNumber and <= LandFile.MaximumMapNumber)
        {
            return value;
        }

        return null;
    }

    private static void ValidateMapPngSize(string path)
    {
        using var bitmap = new Bitmap(path);
        if (bitmap.Width == LandFile.ExpectedWidth && bitmap.Height == LandFile.ExpectedHeight)
            return;

        throw new InvalidDataException(L10n.Ui("MapPngSize", LandFile.ExpectedWidth, LandFile.ExpectedHeight, bitmap.Width, bitmap.Height));
    }
}
