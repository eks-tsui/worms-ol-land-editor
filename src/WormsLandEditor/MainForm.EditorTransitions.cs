using WormsLandEditor.Controls;
using WormsLandEditor.Formats;
using WormsLandEditor.Services;

namespace WormsLandEditor;

public sealed partial class MainForm
{
    private PreparedEditorView PrepareEditorView(EditorState<LandFile, DirFile, PreparedMapPackage> state)
    {
        var candidate = new PreparedEditorView(new MapCanvas { Size = _canvas.Size });
        try
        {
            // Every allocating parser/renderer runs against independent objects before the
            // visible canvas or the current model is touched. No temporary game files exist.
            candidate.Canvas.Land = state.Map;
            candidate.Canvas.SetSourcePreview(state.SourcePngPath);
            var water = PrepareWaterResourceSelection(state.Map);
            candidate.WaterItems = water.Items;
            candidate.WaterIndex = water.SelectedIndex;
            LoadWaterPreview(candidate.Canvas, water.Items[water.SelectedIndex] as string);
            foreach (string name in new[] { "gradient.img", "back.spr", "debris.spr", "soil.img", "grass.img" })
                candidate.Cards.Add(BuildResourceCard(name, state.Resources, failOnPreviewError: true));
            return candidate;
        }
        catch
        {
            candidate.Dispose();
            throw;
        }
    }

    private void InstallPreparedEditorView(PreparedEditorView candidate,
        EditorState<LandFile, DirFile, PreparedMapPackage> state)
    {
        List<Control> previousCards = _resourcePanel.Controls.Cast<Control>().ToList();
        Control[] previousCardArray = previousCards.ToArray();
        Control[] nextCards = candidate.Cards.ToArray();
        object[] previousWaterItems = _waterResource.Items.Cast<object>().ToArray();
        int previousWaterIndex = _waterResource.SelectedIndex;
        // Enabled includes the disabled busy parent; restore the model-driven local state.
        bool previousClosedEnabled = _session.Current.Map is not null;
        bool previousClosedChecked = _closedMap.Checked;
        bool previousLoadingWater = _loadingWaterResources;
        bool previousLoadingOptions = _loadingMapOptions;
        bool canvasSwapped = false;
        _loadingWaterResources = true;
        _loadingMapOptions = true;
        try
        {
            _waterResource.Items.Clear();
            _waterResource.Items.AddRange(candidate.WaterItems);
            _waterResource.SelectedIndex = candidate.WaterIndex;
            _closedMap.Enabled = state.Map is not null;
            _closedMap.Checked = state.Map?.IsClosedMap == true;
            ReplaceResourceCards(nextCards);
            _canvas.SwapEditorState(candidate.Canvas);
            canvasSwapped = true;
            _canvas.Invalidate();
            // Only after every visible installation succeeds does the candidate take
            // ownership of the old cards and images for disposal after session commit.
            candidate.Cards = previousCards;
        }
        catch
        {
            if (canvasSwapped)
                _canvas.SwapEditorState(candidate.Canvas);
            ReplaceResourceCards(previousCardArray);
            _waterResource.Items.Clear();
            _waterResource.Items.AddRange(previousWaterItems);
            _waterResource.SelectedIndex = previousWaterIndex;
            _closedMap.Enabled = previousClosedEnabled;
            _closedMap.Checked = previousClosedChecked;
            _canvas.Invalidate();
            throw;
        }
        finally
        {
            _loadingWaterResources = previousLoadingWater;
            _loadingMapOptions = previousLoadingOptions;
        }
    }

    private void ReplaceResourceCards(Control[] cards)
    {
        _resourcePanel.SuspendLayout();
        try
        {
            _resourcePanel.Controls.Clear();
            _resourcePanel.Controls.AddRange(cards);
        }
        finally
        {
            // New cards start at (0, 0). Finish flow layout inside the install/rollback
            // transaction, before the candidate takes ownership of the old cards.
            _resourcePanel.ResumeLayout(performLayout: true);
        }
    }

    private sealed class PreparedEditorView(MapCanvas canvas) : IDisposable
    {
        public MapCanvas Canvas { get; } = canvas;
        public List<Control> Cards { get; set; } = [];
        public object[] WaterItems { get; set; } = [];
        public int WaterIndex { get; set; }

        public void Dispose()
        {
            Canvas.Dispose();
            DisposeResourcePreviewImages(Cards);
            foreach (Control card in Cards)
                card.Dispose();
            Cards.Clear();
        }
    }
}
