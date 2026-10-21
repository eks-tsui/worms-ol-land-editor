using WormsLandEditor.Controls;
using WormsLandEditor.Formats;
using WormsLandEditor.Localization;

namespace WormsLandEditor;

public sealed partial class MainForm : Form
{
    private const int WindowClientWidth = 1220;
    private const int WindowClientHeight = 728;
    private const int OuterHorizontalPadding = 48;
    private const int SurfaceHorizontalPadding = 24;
    private const int PreviewWidth = WindowClientWidth - OuterHorizontalPadding - SurfaceHorizontalPadding;
    private const int MapPreviewHeight = PreviewWidth * LandFile.ExpectedHeight / LandFile.ExpectedWidth;
    private const int WaterPreviewHeight = 52;
    private const int PreviewHeight = MapPreviewHeight + WaterPreviewHeight;

    private readonly MapCanvas _canvas = new();
    private readonly FlowLayoutPanel _resourcePanel = new();
    private readonly System.Windows.Forms.Timer _resourceAnimationTimer = new() { Interval = 120 };
    private readonly NumericUpDown _mapNumber = new()
    {
        Minimum = LandFile.MinimumMapNumber,
        Maximum = LandFile.MaximumMapNumber,
        Value = 40,
        Width = 70,
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly ComboBox _waterResource = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
    private readonly CheckBox _closedMap = new() { Text = L10n.Ui("ClosedMap"), AutoSize = true, Enabled = false };
    private readonly AppSettings _settings = AppSettings.Load();
    private LandFile? _land;
    private DirFile? _levelResources;
    private string? _currentMapPngPath;
    private bool _dirty;
    private bool _loadingWaterResources;
    private bool _loadingMapOptions;
    private bool _changingMapNumber;
    private int _loadedMapNumber = 40;
    private bool _operationInProgress;
    private Control? _editorLayout;
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    public MainForm()
    {
        Text = L10n.Ui("AppTitle");
        ClientSize = new Size(WindowClientWidth, WindowClientHeight + 24);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 9f);

        _canvas.SpawnPointsChanged += (_, _) => MarkDirty();
        _resourceAnimationTimer.Tick += (_, _) => AdvanceResourceAnimations();
        _resourceAnimationTimer.Start();
        _waterResource.SelectedIndexChanged += (_, _) => ApplyWaterSelectionToLand();
        _closedMap.CheckedChanged += (_, _) => ApplyClosedMapSelection();
        _mapNumber.ValueChanged += (_, _) => ChangeCurrentMapNumber();
        _editorLayout = BuildLayout();
        Controls.Add(_editorLayout);
        var statusStrip = new StatusStrip { SizingGrip = false };
        statusStrip.Items.Add(_statusLabel);
        Controls.Add(statusStrip);
        UpdateStatus(L10n.Ui("Ready"));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_settings.LoadError is not null)
            MessageBox.Show(this, L10n.Ui("SettingsLoadFailed", _settings.LoadError.Message), L10n.Ui("SettingsTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (!EnsureGameDataDirectoryConfigured())
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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_operationInProgress)
        {
            e.Cancel = true;
            MessageBox.Show(this, L10n.Ui("OperationInProgress"), L10n.Ui("OperationInProgressTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else if (!ConfirmDiscard())
            e.Cancel = true;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _resourceAnimationTimer.Stop();
            _resourceAnimationTimer.Dispose();
            DisposeResourcePreviewImages();
        }
        base.Dispose(disposing);
    }

    private void ResetCurrentMap()
    {
        _land = null;
        _levelResources = null;
        _currentMapPngPath = null;
        _dirty = false;
        _canvas.Land = null;
        RefreshMapOptions();
        RefreshResourcePanel();
        RefreshWindowTitle();
    }

    private void RefreshWindowTitle()
    {
        Text = L10n.Ui("AppTitle") + (_dirty ? " *" : string.Empty);
    }

    private void SetOperationBusy(bool busy)
    {
        _operationInProgress = busy;
        UseWaitCursor = busy;
        if (_editorLayout is not null)
            _editorLayout.Enabled = !busy;
        if (busy)
            _resourceAnimationTimer.Stop();
        else if (!IsDisposed)
            _resourceAnimationTimer.Start();
    }

}
