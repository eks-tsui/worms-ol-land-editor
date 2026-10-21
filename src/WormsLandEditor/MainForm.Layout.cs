using WormsLandEditor.Localization;

namespace WormsLandEditor;

public sealed partial class MainForm
{
    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24, 18, 24, 14),
            BackColor = Color.FromArgb(245, 247, 250)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, PreviewHeight + 24));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));

        root.Controls.Add(BuildToolbar(), 0, 0);
        root.Controls.Add(BuildEditorSurface(), 0, 1);
        root.Controls.Add(BuildResourceLibrary(), 0, 2);
        return root;
    }

    private Control BuildToolbar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            ColumnCount = 10,
            RowCount = 1,
            BackColor = Color.White,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 0, 0, 14)
        };
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));

        bar.Controls.Add(Label(L10n.Ui("MapNumber")), 0, 0);
        _mapNumber.Dock = DockStyle.Fill;
        _mapNumber.Margin = new Padding(4, 6, 16, 6);
        bar.Controls.Add(_mapNumber, 1, 0);
        bar.Controls.Add(Label(L10n.Ui("Water")), 2, 0);
        _waterResource.Dock = DockStyle.Fill;
        _waterResource.Margin = new Padding(4, 6, 16, 6);
        bar.Controls.Add(_waterResource, 3, 0);
        bar.Controls.Add(Button(L10n.Ui("ClearSpawns"), ClearSpawnPoints), 4, 0);
        bar.Controls.Add(Button(L10n.Ui("ImportMap"), ImportMap), 5, 0);
        bar.Controls.Add(Button(L10n.Ui("Save"), SaveMap, primary: true), 6, 0);
        bar.Controls.Add(Button(L10n.Ui("Delete"), DeleteMap, danger: true), 7, 0);
        _closedMap.Anchor = AnchorStyles.None;
        _closedMap.Margin = new Padding(12, 0, 0, 0);
        bar.Controls.Add(_closedMap, 8, 0);
        var directoryButton = Button(L10n.Ui("GameDirectory"), SetMapDirectory);
        directoryButton.Dock = DockStyle.Right;
        directoryButton.Width = 146;
        bar.Controls.Add(directoryButton, 9, 0);
        return bar;
    }

    private Control BuildEditorSurface()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Width = PreviewWidth + 24,
            Height = PreviewHeight + 24,
            BackColor = Color.White,
            Padding = new Padding(12),
            Margin = new Padding(0)
        };
        _canvas.Location = new Point(12, 12);
        _canvas.Size = new Size(PreviewWidth, PreviewHeight);
        panel.Controls.Add(_canvas);

        var package = OverlayButton(L10n.Ui("ExportMapPackage"), ExportMapPackage);
        package.BackColor = Color.FromArgb(37, 99, 235);
        package.ForeColor = Color.White;
        package.FlatAppearance.BorderColor = package.BackColor;

        // Size the localized labels independently of the already full main toolbar.
        int right = _canvas.Width - 8;
        foreach (Button action in new[]
        {
            package,
            OverlayButton(L10n.Ui("ImportTerrainPng"), ImportPng),
            OverlayButton(L10n.Ui("ExportTerrainPng"), ExportMapPreview),
            OverlayButton(L10n.Ui("Clear"), ClearMapPreview)
        })
        {
            action.Width = Math.Max(74, TextRenderer.MeasureText(action.Text, action.Font).Width + 28);
            right -= action.Width;
            action.Location = new Point(right, _canvas.Height - 38);
            action.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _canvas.Controls.Add(action);
            right -= 8;
        }
        return panel;
    }

    private Control BuildResourceLibrary()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 102,
            BackColor = Color.White,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 14, 0, 0)
        };

        _resourcePanel.Dock = DockStyle.Fill;
        _resourcePanel.WrapContents = false;
        _resourcePanel.AutoScroll = true;
        _resourcePanel.BackColor = Color.White;
        panel.Controls.Add(_resourcePanel);
        return panel;
    }

    private static Button Button(string text, EventHandler click, bool primary = false, bool danger = false)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 0, 4, 0),
            Padding = new Padding(0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(37, 99, 235) : danger ? Color.FromArgb(220, 38, 38) : Color.FromArgb(241, 245, 249),
            ForeColor = primary || danger ? Color.White : Color.FromArgb(30, 41, 59),
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 9f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            UseCompatibleTextRendering = true,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += click;
        return button;
    }

    private static Button OverlayButton(string text, EventHandler click)
    {
        var button = Button(text, click);
        button.Size = new Size(74, 30);
        button.Dock = DockStyle.None;
        button.Margin = new Padding(4, 0, 0, 0);
        button.BackColor = Color.FromArgb(248, 250, 252);
        button.ForeColor = Color.FromArgb(15, 23, 42);
        button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private static Label Label(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(71, 85, 105),
            Padding = new Padding(0),
            Margin = new Padding(4, 0, 0, 0)
        };
    }
}
