using WormsLandEditor.Formats;
using WormsLandEditor.Services;

namespace WormsLandEditor;

// Test-only partial access keeps the production installation path private and unchanged.
public sealed partial class MainForm
{
    internal static void RunResourceLayoutChecks()
    {
        int checks = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + description);
            checks++;
            Console.WriteLine("PASS: " + description);
        }

        using var editor = new MainForm();
        editor._resourceAnimationTimer.Stop();
        editor._settings.GameDataDirectory = null;
        // Show the complete production layout without invoking MainForm.OnShown, which
        // prompts for a user's game directory. All models and images below are synthetic.
        using var host = new Form
        {
            ClientSize = editor.ClientSize,
            Font = editor.Font,
            AutoScaleMode = AutoScaleMode.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(10, 10)
        };
        host.Controls.Add(editor._editorLayout!);
        host.Show();
        Application.DoEvents();
        Check(editor._resourcePanel.Visible && editor._resourcePanel.IsHandleCreated,
            "Production resource panel is visible and has a native Windows handle");

        string[] names = ["gradient.img", "back.spr", "debris.spr", "soil.img", "grass.img"];
        Control[] Cards() => editor._resourcePanel.Controls.Cast<Control>().ToArray();
        static PictureBox Picture(Control card) => card.Controls.OfType<PictureBox>().Single();
        static Image[] Images(IEnumerable<Control> cards) => cards.SelectMany(card =>
            (Picture(card).Tag as ResourceAnimation)?.Frames ?? []).ToArray();
        static bool ImageIsDisposed(Image image)
        {
            try { _ = image.Width; return false; }
            catch (ArgumentException) { return true; }
        }
        void CheckCards(string description)
        {
            Control[] cards = Cards();
            Check(cards.Length == names.Length, description + ": all five resource cards exist");
            Check(cards.Select(card => card.Controls.OfType<Label>().First().Text).SequenceEqual(names),
                description + ": resource order is preserved");
            Check(cards.All(card => !card.IsDisposed && card.Visible && ReferenceEquals(card.Parent, editor._resourcePanel)),
                description + ": cards are live children of the visible resource panel");
            // Deliberately inspect immediately after the production installer. Do not
            // PerformLayout, resize, refresh or pump messages first: those hide the bug.
            Check(cards.Zip(cards.Skip(1), (left, right) => left.Right <= right.Left && left.Top == right.Top).All(x => x),
                "Resource cards must not overlap (" + description + ")");
            Check(cards.All(card => card.Width > 0 && card.Height > 0
                && editor._resourcePanel.DisplayRectangle.Contains(card.Bounds)
                && (editor._resourcePanel.ClientRectangle.Contains(card.Bounds)
                    || (editor._resourcePanel.AutoScroll && editor._resourcePanel.HorizontalScroll.Visible))),
                description + ": every card is visible or accessible by horizontal scrolling");
            Check(Images(cards).All(image => !ImageIsDisposed(image)), description + ": all preview frames remain usable");
        }

        void Commit(EditorState<LandFile, DirFile, PreparedMapPackage> state, string description, bool busy = false)
        {
            Control[] oldCards = Cards();
            Image[] oldImages = Images(oldCards);
            byte[][] resourceBytes = state.Resources!.Resources.Select(entry => entry.Data.ToArray()).ToArray();
            if (busy) editor.SetOperationBusy(true);
            try
            {
                using (PreparedEditorView candidate = editor.PrepareEditorView(state))
                {
                    Check(candidate.Cards.Count == 5 && candidate.Cards.All(card => card.Parent is null && card.Location == Point.Empty),
                        description + ": detached candidate starts with five unpositioned cards");
                    Check(editor._session.TryReplace(() => state, next => editor.InstallPreparedEditorView(candidate, next)),
                        description + ": production installer commits the candidate");
                    CheckCards(description);
                    Check(candidate.Cards.SequenceEqual(oldCards), description + ": candidate now owns only the retired cards");
                    Check(oldCards.All(card => !card.IsDisposed), description + ": old cards survive until commit cleanup");
                }
                Check(oldCards.All(card => card.IsDisposed) && oldImages.All(ImageIsDisposed),
                    description + ": commit cleanup disposes the old cards and all their frames");
                CheckCards(description + " after candidate disposal");
                Check(ReferenceEquals(editor._session.Current, state) && ReferenceEquals(editor._canvas.Land, state.Map),
                    description + ": model and visible canvas agree");
                Check(state.Resources.Resources.Select((entry, i) => entry.Data.SequenceEqual(resourceBytes[i])).All(x => x),
                    description + ": preview installation leaves every resource byte unchanged");
            }
            finally
            {
                if (busy) editor.SetOperationBusy(false);
                editor._resourceAnimationTimer.Stop();
            }
        }

        Commit(CreateLayoutState(105), "Initial map load");
        Commit(CreateLayoutState(106), "Map-number switch");
        var imported = CreateLayoutState(105) with
        {
            IsDirty = true,
            // Provenance is opaque to view installation. Package parsing and exact-byte
            // saving are covered by PackageChecks and PersistenceChecks, not this test.
            OriginalPackage = new PreparedMapPackage(new(40, 105, @"data\water\Red", 1920, 696), [1, 2, 3], [4, 5, 6])
        };
        Commit(imported, "Prepared import while editor is busy", busy: true);
        Check(ReferenceEquals(editor._originalPackage, imported.OriginalPackage) && editor._dirty,
            "Imported state retains unsaved status and exact-save provenance");

        Control[] retained = Cards();
        Check(!editor._session.TryReplace(() => null, _ => throw new InvalidOperationException("Unexpected installation"))
            && ReferenceEquals(editor._session.Current, imported) && Cards().SequenceEqual(retained),
            "Cancelled replacement preserves the model and existing cards");

        void CheckFailedInstallation(InstallationFault fault)
        {
            string description = fault switch
            {
                InstallationFault.CanvasInvalidation => "Canvas invalidation failure",
                InstallationFault.PartialCardAddition => "Partial card insertion failure",
                _ => "Flow layout failure"
            };
            var before = editor._session.Current;
            Control[] oldCards = Cards();
            Rectangle[] oldBounds = oldCards.Select(card => card.Bounds).ToArray();
            Image[] oldImages = Images(oldCards);
            object[] waterItems = editor._waterResource.Items.Cast<object>().ToArray();
            int waterIndex = editor._waterResource.SelectedIndex;
            bool closed = editor._closedMap.Checked;
            var next = CreateLayoutState(107);
            using (PreparedEditorView candidate = editor.PrepareEditorView(next))
            {
                Control[] newCards = candidate.Cards.ToArray();
                Image[] newImages = Images(newCards);
                bool injected = false;
                void FailOnce()
                {
                    if (injected) return;
                    injected = true;
                    throw new InvalidOperationException("Injected installation failure");
                }
                LayoutEventHandler layoutFailure = (_, _) =>
                {
                    if (editor._resourcePanel.Controls.Count == 5 && ReferenceEquals(editor._resourcePanel.Controls[0], newCards[0]))
                        FailOnce();
                };
                InvalidateEventHandler canvasFailure = (_, _) =>
                {
                    if (ReferenceEquals(editor._canvas.Land, next.Map)) FailOnce();
                };
                ControlEventHandler cardAdditionFailure = (_, e) =>
                {
                    if (ReferenceEquals(e.Control, newCards[1])) FailOnce();
                };
                if (fault == InstallationFault.CanvasInvalidation) editor._canvas.Invalidated += canvasFailure;
                else if (fault == InstallationFault.PartialCardAddition) editor._resourcePanel.ControlAdded += cardAdditionFailure;
                else editor._resourcePanel.Layout += layoutFailure;
                bool failed = false;
                try { editor._session.TryReplace(() => next, state => editor.InstallPreparedEditorView(candidate, state)); }
                catch (InvalidOperationException ex) when (ex.Message == "Injected installation failure") { failed = true; }
                finally
                {
                    editor._canvas.Invalidated -= canvasFailure;
                    editor._resourcePanel.Layout -= layoutFailure;
                    editor._resourcePanel.ControlAdded -= cardAdditionFailure;
                }
                Check(failed && injected, description + ": injected production-path exception propagates");
                Check(ReferenceEquals(editor._session.Current, before) && ReferenceEquals(editor._canvas.Land, before.Map),
                    description + ": old session and canvas are restored");
                Check(Cards().SequenceEqual(oldCards) && Cards().Select(card => card.Bounds).SequenceEqual(oldBounds),
                    description + ": exact old card order and bounds are restored");
                Check(candidate.Cards.SequenceEqual(newCards) && newCards.All(card => card.Parent is null),
                    description + ": rejected candidate still owns only its detached cards");
                Check(editor._waterResource.Items.Cast<object>().SequenceEqual(waterItems)
                    && editor._waterResource.SelectedIndex == waterIndex && editor._closedMap.Checked == closed
                    && editor._closedMap.Enabled && !editor._loadingWaterResources && !editor._loadingMapOptions,
                    description + ": water, map options and event guards are restored");
                candidate.Dispose();
                Check(newCards.All(card => card.IsDisposed) && newImages.All(ImageIsDisposed),
                    description + ": rejected cards and all their frames are disposed");
                Check(oldCards.All(card => !card.IsDisposed) && oldImages.All(image => !ImageIsDisposed(image)),
                    description + ": rollback does not dispose the active cards or previews");
                CheckCards(description + " after rollback");
            }
            Commit(CreateLayoutState(108), description + " recovery");
        }

        CheckFailedInstallation(InstallationFault.Layout);
        CheckFailedInstallation(InstallationFault.CanvasInvalidation);
        CheckFailedInstallation(InstallationFault.PartialCardAddition);

        var beforeBadPreview = editor._session.Current;
        Control[] beforeBadPreviewCards = Cards();
        var malformed = CreateLayoutState(109);
        malformed.Resources!.Find("grass.img")!.Data = [0, 1, 2];
        bool badPreviewRejected = false;
        PreparedEditorView? malformedView = null;
        try
        {
            editor._session.TryReplace(() =>
            {
                malformedView = editor.PrepareEditorView(malformed);
                return malformed;
            }, state => editor.InstallPreparedEditorView(malformedView!, state));
        }
        catch (EndOfStreamException) { badPreviewRejected = true; }
        finally { malformedView?.Dispose(); }
        Check(badPreviewRejected && ReferenceEquals(editor._session.Current, beforeBadPreview)
            && Cards().SequenceEqual(beforeBadPreviewCards),
            "Malformed last resource preview leaves the existing model and cards untouched");
        CheckCards("Preview preparation failure");
        editor.RefreshResourcePanel();
        CheckCards("Resource refresh");
        editor.AdvanceResourceAnimations();
        CheckCards("Animation after refresh");
        Commit(CreateLayoutState(110) with { Resources = new DirFile() }, "Missing-resource placeholders");
        Console.WriteLine($"PASS {checks} Windows WinForms resource-layout checks. Real visible production controls; no manual visual or user-map acceptance claimed.");
    }

    private enum InstallationFault { Layout, CanvasInvalidation, PartialCardAddition }

    private static EditorState<LandFile, DirFile, PreparedMapPackage> CreateLayoutState(int number)
    {
        var land = new LandFile
        {
            Width = LandFile.ExpectedWidth,
            Height = LandFile.ExpectedHeight,
            Terrain = WolImage.Create8Bit(LandFile.ExpectedWidth, LandFile.ExpectedHeight),
            IsClosedMap = number % 2 != 0
        };
        land.SetClientResourcePaths(number, number % 2 == 0 ? @"data\water\Blue" : @"data\water\Red");
        var resources = new DirFile();
        foreach (string name in new[] { "gradient.img", "back.spr", "debris.spr", "soil.img", "grass.img", "hidden.txt" })
        {
            byte[] bytes;
            if (name.EndsWith(".spr", StringComparison.Ordinal))
            {
                var sprite = new WolSprite { Name = name, Flags = 0x8008 };
                sprite.Palette.Add(Color.Green);
                sprite.Frames.Add(new(0, 0, 2, 2, [1, 1, 1, 1]));
                sprite.Frames.Add(new(0, 0, 2, 2, [1, 0, 0, 1]));
                bytes = sprite.ToBytes();
            }
            else if (name.EndsWith(".img", StringComparison.Ordinal))
            {
                WolImage image = WolImage.Create8Bit(2, 2, [Color.Red]);
                Array.Fill(image.Pixels, (byte)1);
                using var output = new MemoryStream();
                image.Write(output);
                bytes = output.ToArray();
            }
            else bytes = [11, 22, 33];
            resources.Resources.Add(new(name, 0, bytes.Length, 0, bytes));
        }
        return new(land, resources, null, false, number, null);
    }
}
