using WormsLandEditor.Formats;
using WormsLandEditor.Localization;
using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace WormsLandEditor.Controls;

public sealed class MapCanvas : Control
{
    private const int SpawnHitRadius = 10;
    private const int WaterPreviewHeight = 52;

    private LandFile? _land;
    private Bitmap? _terrainPreview;
    private Bitmap? _palettePreview;
    private Bitmap? _sourcePreview;
    private Bitmap? _waterFillPreview;
    private List<Bitmap> _waterSurfaceFrames = [];
    private int _waterSurfaceFrameIndex;
    private readonly SortedSet<int> _freeSpawnPointIndexes = [];

    public event EventHandler? SpawnPointsChanged;

    public MapCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 28, 34);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public LandFile? Land
    {
        get => _land;
        set
        {
            _land = value;
            _freeSpawnPointIndexes.Clear();
            RebuildPreviewCache();
            Invalidate();
        }
    }

    public void RebuildPreviewCache()
    {
        _terrainPreview?.Dispose();
        _palettePreview?.Dispose();
        _sourcePreview?.Dispose();
        _terrainPreview = null;
        _palettePreview = null;
        _sourcePreview = null;
        if (_land is null)
            return;

        _terrainPreview = _land.Terrain.ToBitmap();
    }

    public void SetPalettePreview(Bitmap? bitmap)
    {
        _palettePreview?.Dispose();
        _palettePreview = bitmap;
        Invalidate();
    }

    public void SetSourcePreview(string? pngPath)
    {
        _sourcePreview?.Dispose();
        _sourcePreview = null;
        if (string.IsNullOrWhiteSpace(pngPath))
        {
            Invalidate();
            return;
        }

        using var source = new Bitmap(pngPath);
        Rectangle mapRect = MapRect;
        _sourcePreview = new Bitmap(source, Math.Max(1, mapRect.Width), Math.Max(1, mapRect.Height));
        Invalidate();
    }

    public void SetWaterPreview(Bitmap? fillPreview, List<Bitmap> surfaceFrames)
    {
        DisposeWaterPreview();
        _waterFillPreview = fillPreview;
        _waterSurfaceFrames = surfaceFrames;
        _waterSurfaceFrameIndex = 0;
        Invalidate();
    }

    public void AdvanceWaterAnimation()
    {
        if (_waterSurfaceFrames.Count <= 1)
            return;

        _waterSurfaceFrameIndex = (_waterSurfaceFrameIndex + 1) % _waterSurfaceFrames.Count;
        Invalidate();
    }

    public void ResetSpawnPointSlots()
    {
        _freeSpawnPointIndexes.Clear();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DisposePreviewCache();

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

        if (_land is null)
        {
            DrawEmptyState(e.Graphics);
            return;
        }

        Rectangle mapRect = MapRect;

        if (_palettePreview is not null)
            e.Graphics.DrawImage(_palettePreview, mapRect);
        else if (_sourcePreview is not null)
            e.Graphics.DrawImage(_sourcePreview, mapRect);
        else if (_terrainPreview is not null)
            e.Graphics.DrawImage(_terrainPreview, mapRect);

        DrawWaterPreview(e.Graphics);
        DrawSpawnPoints(e.Graphics);

        using var border = new Pen(Color.FromArgb(120, 255, 255, 255), 1);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        if (e.Button != MouseButtons.Left || _land is null || Width <= 0 || Height <= 0)
            return;

        if (!MapRect.Contains(e.Location))
            return;

        int existing = FindSpawnPointAt(e.Location);
        if (existing >= 0)
        {
            _land.PrimaryPoints.RemoveAt(existing);
            _freeSpawnPointIndexes.Add(existing);
        }
        else
        {
            if (_land.PrimaryPoints.Count >= LandFile.MaximumSpawnPoints)
                return;

            LandPoint clickedPoint = ToLandPoint(e.Location);
            LandPoint? point = _land.FindNearestUsableSpawnPoint(clickedPoint);
            if (!point.HasValue)
                return;

            int insertIndex = NextSpawnPointInsertIndex();
            if (insertIndex >= 0 && insertIndex <= _land.PrimaryPoints.Count)
                _land.PrimaryPoints.Insert(insertIndex, point.Value);
            else
                _land.PrimaryPoints.Add(point.Value);
        }

        SpawnPointsChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private int NextSpawnPointInsertIndex()
    {
        while (_freeSpawnPointIndexes.Count > 0)
        {
            int index = _freeSpawnPointIndexes.Min;
            _freeSpawnPointIndexes.Remove(index);
            if (index <= (_land?.PrimaryPoints.Count ?? 0))
                return index;
        }

        return -1;
    }

    private void DrawSpawnPoints(Graphics graphics)
    {
        if (_land is null || _land.PrimaryPoints.Count == 0)
            return;

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
        using var border = new Pen(Color.FromArgb(37, 99, 235), 2);
        using var outerBorder = new Pen(Color.White, 2);
        using var shadow = new SolidBrush(Color.FromArgb(130, 15, 23, 42));
        using var font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 7.5f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(30, 64, 175));

        for (int i = 0; i < _land.PrimaryPoints.Count; i++)
        {
            Point point = ToCanvasPoint(_land.PrimaryPoints[i]);
            var shadowRect = new Rectangle(point.X - 11, point.Y - 9, 22, 18);
            shadowRect.Offset(1, 2);
            graphics.FillEllipse(shadow, shadowRect);

            var rect = new Rectangle(point.X - 11, point.Y - 9, 22, 18);
            graphics.DrawEllipse(outerBorder, rect);
            graphics.FillEllipse(fill, rect);
            graphics.DrawEllipse(border, rect);

            string label = (i + 1).ToString("00");
            SizeF size = graphics.MeasureString(label, font);
            graphics.DrawString(label, font, textBrush, point.X - size.Width / 2, point.Y - size.Height / 2);
        }
    }

    private void DrawWaterPreview(Graphics graphics)
    {
        if (_waterFillPreview is null && _waterSurfaceFrames.Count == 0)
            return;

        var waterRect = WaterRect;

        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;

        if (_waterFillPreview is not null)
        {
            using var brush = new TextureBrush(_waterFillPreview, WrapMode.Tile);
            brush.TranslateTransform(waterRect.Left, waterRect.Top);
            graphics.FillRectangle(brush, waterRect);
        }
        else
        {
            using var brush = new SolidBrush(Color.FromArgb(100, 48, 109, 181));
            graphics.FillRectangle(brush, waterRect);
        }

        if (_waterSurfaceFrames.Count == 0)
            return;

        Bitmap surface = _waterSurfaceFrames[Math.Clamp(_waterSurfaceFrameIndex, 0, _waterSurfaceFrames.Count - 1)];
        int surfaceHeight = Math.Min(waterRect.Height, Math.Max(1, surface.Height));
        int surfaceWidth = Math.Max(1, surface.Width);
        int y = waterRect.Top;
        for (int x = 0; x < Width; x += surfaceWidth)
        {
            var target = new Rectangle(x, y, surfaceWidth, surfaceHeight);
            graphics.DrawImage(surface, target, 0, 0, surface.Width, surface.Height, GraphicsUnit.Pixel);
        }
    }

    private int FindSpawnPointAt(Point location)
    {
        if (_land is null)
            return -1;

        int bestIndex = -1;
        int bestDistance = SpawnHitRadius * SpawnHitRadius;
        for (int i = 0; i < _land.PrimaryPoints.Count; i++)
        {
            Point point = ToCanvasPoint(_land.PrimaryPoints[i]);
            int dx = point.X - location.X;
            int dy = point.Y - location.Y;
            int distance = dx * dx + dy * dy;
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private LandPoint ToLandPoint(Point point)
    {
        Rectangle mapRect = MapRect;
        int x = Math.Clamp((point.X - mapRect.Left) * LandFile.ExpectedWidth / Math.Max(1, mapRect.Width), 0, LandFile.ExpectedWidth - 1);
        int y = Math.Clamp((point.Y - mapRect.Top) * LandFile.ExpectedHeight / Math.Max(1, mapRect.Height), 0, LandFile.ExpectedHeight - 1);
        return new LandPoint(x, y);
    }

    private Point ToCanvasPoint(LandPoint point)
    {
        Rectangle mapRect = MapRect;
        int x = Math.Clamp(mapRect.Left + point.X * mapRect.Width / LandFile.ExpectedWidth, mapRect.Left, Math.Max(mapRect.Left, mapRect.Right - 1));
        int y = Math.Clamp(mapRect.Top + point.Y * mapRect.Height / LandFile.ExpectedHeight, mapRect.Top, Math.Max(mapRect.Top, mapRect.Bottom - 1));
        return new Point(x, y);
    }

    private Rectangle MapRect
    {
        get
        {
            int waterHeight = WaterRect.Height;
            return new Rectangle(0, 0, Width, Math.Max(1, Height - waterHeight));
        }
    }

    private Rectangle WaterRect
    {
        get
        {
            int waterHeight = Math.Min(WaterPreviewHeight, Math.Max(24, Height / 5));
            return new Rectangle(0, Math.Max(0, Height - waterHeight), Width, waterHeight);
        }
    }

    private void DrawEmptyState(Graphics graphics)
    {
        using var font = new Font(Font.FontFamily, 12, FontStyle.Regular);
        using var brush = new SolidBrush(Color.FromArgb(190, 230, 236, 242));
        string text = L10n.Ui("EmptyMap");
        SizeF size = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, brush, (Width - size.Width) / 2, (Height - size.Height) / 2);
    }

    private void DisposePreviewCache()
    {
        _terrainPreview?.Dispose();
        _palettePreview?.Dispose();
        _sourcePreview?.Dispose();
        DisposeWaterPreview();
        _terrainPreview = null;
        _palettePreview = null;
        _sourcePreview = null;
    }

    private void DisposeWaterPreview()
    {
        _waterFillPreview?.Dispose();
        foreach (Bitmap frame in _waterSurfaceFrames)
            frame.Dispose();

        _waterFillPreview = null;
        _waterSurfaceFrames = [];
    }
}
