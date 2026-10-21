using System.Drawing.Imaging;
using WormsLandEditor.Formats;
using WormsLandEditor.Localization;

namespace WormsLandEditor;

public sealed partial class MainForm
{
    private void RefreshWaterResources(bool updatePalettePreview = true)
    {
        _loadingWaterResources = true;
        try
        {
            string? current = TryGetWaterResourceName(_land?.NameB);
            if (string.IsNullOrWhiteSpace(current) && _waterResource.SelectedItem is string selected)
                current = selected;

            var names = new List<string>();
            if (!string.IsNullOrWhiteSpace(CurrentGameDirectory()))
            {
                string waterDirectory = Path.Combine(CurrentDataDirectory(), "Water");
                if (Directory.Exists(waterDirectory))
                {
                    foreach (string directory in Directory.EnumerateDirectories(waterDirectory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                        names.Add(Path.GetFileName(directory));
                }
            }

            if (names.Count == 0)
                names.AddRange(["Red", "Blue", "Green", "Purple", "yellow"]);

            // Missing preview assets must not silently change the map's water path.
            if (!string.IsNullOrWhiteSpace(current) && !names.Contains(current, StringComparer.OrdinalIgnoreCase))
                names.Add(current);

            _waterResource.Items.Clear();
            _waterResource.Items.AddRange(names.Cast<object>().ToArray());
            SelectWaterResource(current);
        }
        finally
        {
            _loadingWaterResources = false;
        }

        RefreshWaterPreview(updatePalettePreview);
    }

    private void SelectWaterResource(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            for (int i = 0; i < _waterResource.Items.Count; i++)
            {
                if (_waterResource.Items[i] is string item &&
                    string.Equals(item, name, StringComparison.OrdinalIgnoreCase))
                {
                    _waterResource.SelectedIndex = i;
                    return;
                }
            }
        }

        for (int i = 0; i < _waterResource.Items.Count; i++)
        {
            if (_waterResource.Items[i] is string item &&
                string.Equals(item, "Red", StringComparison.OrdinalIgnoreCase))
            {
                _waterResource.SelectedIndex = i;
                return;
            }
        }

        if (_waterResource.Items.Count > 0)
            _waterResource.SelectedIndex = 0;
    }

    private void ApplyWaterSelectionToLand()
    {
        if (!_loadingWaterResources)
            RefreshWaterPreview(updatePalettePreview: true);

        if (_loadingWaterResources || _land is null)
            return;

        _land.NameB = SelectedWaterResourcePath();
        MarkDirty();
    }

    private string SelectedWaterResourcePath()
    {
        string name = _waterResource.SelectedItem as string ?? "Red";
        return $@"data\water\{name}";
    }

    private static string? TryGetWaterResourceName(string? path)
    {
        const string prefix = @"data\water\";
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return path[prefix.Length..];
    }

    private void RefreshWaterPreview(bool updatePalettePreview = true)
    {
        if (string.IsNullOrWhiteSpace(CurrentGameDirectory()) || _waterResource.SelectedItem is not string waterName)
        {
            _canvas.SetWaterPreview(null, []);
            if (updatePalettePreview)
                RefreshPalettePreview();
            return;
        }

        string waterDirPath = Path.Combine(CurrentDataDirectory(), "Water", waterName, "Water.dir");
        if (!File.Exists(waterDirPath))
        {
            _canvas.SetWaterPreview(null, []);
            if (updatePalettePreview)
                RefreshPalettePreview();
            return;
        }

        Bitmap? fillPreview = null;
        List<Bitmap> surfaceFrames = [];
        try
        {
            DirFile waterDir = DirFile.Load(waterDirPath);
            fillPreview = waterDir.CreatePreview("fill.img");
            surfaceFrames = waterDir.CreatePreviewFrames(@"layer\layer.spr");
        }
        catch
        {
            fillPreview?.Dispose();
            foreach (Bitmap frame in surfaceFrames)
                frame.Dispose();

            fillPreview = null;
            surfaceFrames = [];
        }

        _canvas.SetWaterPreview(fillPreview, surfaceFrames);
        if (updatePalettePreview)
            RefreshPalettePreview();
    }

    private void RefreshPalettePreview()
    {
        if (_land is null || _levelResources is null)
        {
            _canvas.SetPalettePreview(null);
            return;
        }

        try
        {
            List<Color> terrainPalette = PaletteUtil.BuildTerrainOnlyPalette(_land.Terrain, maxColors: 96);
            if (terrainPalette.Count == 0)
            {
                _canvas.SetPalettePreview(null);
                return;
            }

            WolImage preview = _land.Terrain.Clone();
            preview.RemapToPalette(terrainPalette, terrainPalette.Count);
            _canvas.SetPalettePreview(preview.ToBitmap());
        }
        catch
        {
            _canvas.SetPalettePreview(null);
        }
    }

    private void LoadResourcesForCurrentMap(bool useTemplateIfMissing = true, bool refreshPanel = true)
    {
        if (string.IsNullOrWhiteSpace(CurrentGameDirectory()))
        {
            _levelResources = null;
            if (refreshPanel)
                RefreshResourcePanel();
            return;
        }

        int mapNumber = (int)_mapNumber.Value;
        string levelPath = Path.Combine(CurrentDataDirectory(), "Mfx", $"Tmap{mapNumber:00}", "Level.Dir");
        bool hasMapResources = File.Exists(levelPath);
        try
        {
            _levelResources = hasMapResources
                ? DirFile.Load(levelPath)
                : useTemplateIfMissing ? DirFile.LoadTemplate(CurrentDataDirectory()) : null;
            if (refreshPanel)
                RefreshResourcePanel();
        }
        catch (Exception ex) when (!hasMapResources)
        {
            _levelResources = null;
            MessageBox.Show(this, ex.Message, L10n.Ui("ResourceLoadFailedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (refreshPanel)
                RefreshResourcePanel();
        }
    }

    private void RefreshResourcePanel()
    {
        DisposeResourcePreviewImages();
        while (_resourcePanel.Controls.Count > 0)
            _resourcePanel.Controls[0].Dispose();
        string[] resourceNames = ["gradient.img", "back.spr", "debris.spr", "soil.img", "grass.img"];
        foreach (string name in resourceNames)
            _resourcePanel.Controls.Add(BuildResourceCard(name));
    }

    private Control BuildResourceCard(string name)
    {
        DirResourceEntry? entry = _levelResources?.Find(name);
        var card = new Panel
        {
            Width = 220,
            Height = 82,
            BackColor = Color.FromArgb(248, 250, 252),
            Margin = new Padding(0, 0, 8, 0)
        };

        var preview = new PictureBox
        {
            Location = new Point(8, 8),
            Size = new Size(72, 64),
            BackColor = Color.FromArgb(15, 23, 42),
            SizeMode = PictureBoxSizeMode.Zoom,
            Cursor = entry is null ? Cursors.Default : Cursors.Hand
        };
        if (entry is not null)
            preview.MouseClick += (sender, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    ReplaceResource(name);
            };

        List<Image> frames = TryCreateResourcePreviewFrames(name);
        if (frames.Count > 0)
        {
            preview.Image = frames[0];
            preview.Tag = new ResourceAnimation(frames);
        }
        card.Controls.Add(preview);

        var title = new Label
        {
            Text = name,
            Location = new Point(90, 7),
            Size = new Size(128, 20),
            ForeColor = Color.FromArgb(15, 23, 42),
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        card.Controls.Add(title);

        string detail = entry is null ? L10n.Ui("ResourceNotFound") : ResourceDetailText(name, entry);
        var info = new Label
        {
            Text = detail,
            Location = new Point(90, 28),
            Size = new Size(128, 18),
            ForeColor = Color.FromArgb(100, 116, 139),
            TextAlign = ContentAlignment.MiddleLeft
        };
        card.Controls.Add(info);

        var export = Button(L10n.Ui("Export"), (_, _) => ExportResource(name));
        export.Enabled = entry is not null && frames.Count > 0;
        export.Location = new Point(90, 51);
        export.Size = new Size(54, 24);
        export.Dock = DockStyle.None;
        export.Margin = new Padding(0);
        export.Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8f, FontStyle.Bold);
        card.Controls.Add(export);

        var clear = Button(L10n.Ui("Clear"), (_, _) => ClearResource(name));
        clear.Enabled = entry is not null;
        clear.Location = new Point(150, 51);
        clear.Size = new Size(60, 24);
        clear.Dock = DockStyle.None;
        clear.Margin = new Padding(0);
        clear.Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8f, FontStyle.Bold);
        card.Controls.Add(clear);

        return card;
    }

    private string ResourceDetailText(string name, DirResourceEntry entry)
    {
        try
        {
            Size? size = _levelResources?.GetResourceDimensions(name);
            string fileSize = L10n.Ui("ResourceSize", entry.Data.Length / 1024.0);
            return size.HasValue ? L10n.Ui("ResourceDimensions", size.Value.Width, size.Value.Height, fileSize) : fileSize;
        }
        catch
        {
            return L10n.Ui("ResourceSize", entry.Data.Length / 1024.0);
        }
    }

    private List<Image> TryCreateResourcePreviewFrames(string name)
    {
        try
        {
            return _levelResources?.CreatePreviewFrames(name, preserveSpriteCanvas: true).Cast<Image>().ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void ExportResource(string name)
    {
        if (_levelResources is null)
            return;

        List<Bitmap> frames;
        try
        {
            frames = _levelResources.CreatePreviewFrames(name, preserveSpriteCanvas: true);
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }

        if (frames.Count == 0)
            return;

        bool animated = frames.Count > 1;
        string fileName = name.Replace('\\', '_').Replace('/', '_');
        using var dialog = new SaveFileDialog
        {
            Title = L10n.Ui("ExportResource", name),
            Filter = animated ? L10n.Ui("GifFilter") : L10n.Ui("PngFilter"),
            DefaultExt = animated ? "gif" : "png",
            FileName = $"{Path.GetFileNameWithoutExtension(fileName)}.{(animated ? "gif" : "png")}",
            RestoreDirectory = true,
            OverwritePrompt = true
        };

        try
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            if (animated)
                AnimatedGifWriter.Save(dialog.FileName, frames, delayMilliseconds: 120);
            else
                frames[0].Save(dialog.FileName, ImageFormat.Png);
            UpdateStatus(L10n.Ui("ResourceExported", dialog.FileName));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            foreach (Bitmap frame in frames)
                frame.Dispose();
        }
    }

    private void ReplaceResource(string name)
    {
        if (_levelResources is null)
            return;

        bool isImg = name.EndsWith(".img", StringComparison.OrdinalIgnoreCase);
        using var dialog = new OpenFileDialog
        {
            Title = L10n.Ui("ImportResource", name),
            Filter = isImg ? L10n.Ui("PngFilter") : L10n.Ui("PngGifFilter"),
            CheckFileExists = true,
            RestoreDirectory = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            _levelResources.ReplaceResourceFromFile(name, dialog.FileName);
            MarkDirty();
            RefreshResourcePanel();
            UpdateStatus(L10n.Ui("ResourceImported", name));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ClearResource(string name)
    {
        if (_levelResources is null)
            return;

        try
        {
            _levelResources.ClearResourceImage(name);
            MarkDirty();
            RefreshResourcePanel();
            UpdateStatus(L10n.Ui("ResourceCleared", name));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void AdvanceResourceAnimations()
    {
        _canvas.AdvanceWaterAnimation();

        foreach (Control control in _resourcePanel.Controls)
        {
            foreach (PictureBox picture in control.Controls.OfType<PictureBox>())
            {
                if (picture.Tag is not ResourceAnimation animation || animation.Frames.Count <= 1)
                    continue;

                animation.Index = (animation.Index + 1) % animation.Frames.Count;
                picture.Image = animation.Frames[animation.Index];
            }
        }
    }

    private void DisposeResourcePreviewImages()
    {
        foreach (Control control in _resourcePanel.Controls)
        {
            foreach (PictureBox picture in control.Controls.OfType<PictureBox>())
            {
                if (picture.Tag is ResourceAnimation animation)
                {
                    foreach (Image frame in animation.Frames)
                        frame.Dispose();
                }
            }
        }
    }

    private sealed class ResourceAnimation
    {
        public ResourceAnimation(List<Image> frames)
        {
            Frames = frames;
        }

        public List<Image> Frames { get; }
        public int Index { get; set; }
    }
}
