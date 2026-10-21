using WormsLandEditor.Services;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
    Console.WriteLine("PASS " + description);
}
void Throws(Action action, string description)
{
    bool failed = false;
    try { action(); }
    catch (InvalidDataException) { failed = true; }
    Check(failed, description);
}

var session = new EditorSession<Map, Resources, Package>(105);
Check(session.Current.Map is null && session.Current.Resources is null, "New session has no map or resources");
Check(session.Current.MapNumber == 105 && !session.Current.IsDirty, "New session retains selected number and clean state");
var original = new EditorState<Map, Resources, Package>(new("existing"), new("original resources"), "terrain.png", true, 105, new("pending package"));
session.Update(_ => original);
Check(ReferenceEquals(session.Current, original), "Update installs the requested state");

bool viewCalled = false;
Check(!session.TryReplace(() => null, _ => viewCalled = true), "Cancellation returns false");
Check(!viewCalled && ReferenceEquals(session.Current, original), "Cancellation does not install a view or change state");
Throws(() => session.TryReplace(() => throw new InvalidDataException("Invalid candidate"), _ => viewCalled = true), "Candidate failure propagates");
Check(!viewCalled && ReferenceEquals(session.Current, original), "Candidate failure retains old state and view");

var importedMap = new Map("imported map");
var importedResources = new Resources("complete DIR including hidden TXT");
var rawPackage = new Package("mapped DAT and original DIR");
var imported = new EditorState<Map, Resources, Package>(importedMap, importedResources, null, true, 105, rawPackage);
Throws(() => session.TryReplace(() => imported, _ => throw new InvalidDataException("Preview creation failed")), "View failure propagates");
Check(ReferenceEquals(session.Current, original), "View failure retains the exact old state");
Check(ReferenceEquals(session.Current.Map, original.Map) && ReferenceEquals(session.Current.Resources, original.Resources), "View failure retains old map and all resources");
Check(session.Current.SourcePngPath == "terrain.png" && session.Current.IsDirty && session.Current.MapNumber == 105, "View failure retains preview source, dirty flag and map number");
Check(ReferenceEquals(session.Current.OriginalPackage, original.OriginalPackage), "View failure retains pending raw package");

Check(!session.TryReplace(() => { session.MarkEdited(); return null; }, _ => { })
      && ReferenceEquals(session.Current, original), "Cancellation restores state changed by a synchronous preparation event");
Throws(() => session.TryReplace(() => { session.MarkEdited(); throw new InvalidDataException("Preparation event failure"); }, _ => { }), "Preparation failure after a synchronous event propagates");
Check(ReferenceEquals(session.Current, original), "Preparation failure restores the original state reference after a synchronous event");
Throws(() => session.TryReplace(() => imported, _ => { session.MarkEdited(); throw new InvalidDataException("View event failure"); }), "View failure after a synchronous event propagates");
Check(ReferenceEquals(session.Current, original), "View failure restores the original state reference after a synchronous event");

Check(session.TryReplace(() => imported, candidate =>
{
    Check(ReferenceEquals(session.Current, original), "Old state remains active while the candidate view installs");
    Check(ReferenceEquals(candidate.Map, importedMap) && ReferenceEquals(candidate.Resources, importedResources), "Installer receives candidate map and full resources");
}), "Valid import commits only after view installation");
Check(ReferenceEquals(session.Current, imported), "Successful import installs the candidate state");
Check(session.Current.IsDirty && session.Current.MapNumber == 105 && session.Current.SourcePngPath is null, "Import is unsaved, keeps target number and clears stale PNG source");
Check(ReferenceEquals(session.Current.OriginalPackage, rawPackage), "Unedited import retains exact-save payload");

session.MarkSaved();
Check(!session.Current.IsDirty, "Successful save clears dirty state");
Check(ReferenceEquals(session.Current.OriginalPackage, rawPackage), "Successful save keeps unedited payload for subsequent saves");
Check(ReferenceEquals(session.Current.Map, importedMap) && ReferenceEquals(session.Current.Resources, importedResources), "Marking saved does not replace map or resources");
session.MarkEdited();
Check(session.Current.IsDirty && session.Current.OriginalPackage is null, "Any real edit invalidates raw payload and marks dirty");
Check(ReferenceEquals(session.Current.Resources, importedResources), "Editing does not discard hidden resources");

session.Update(_ => imported);
var beforeDecliningReplacement = session.Current;
Check(!session.TryReplace(() => null, _ => throw new InvalidOperationException("Unexpected install"))
      && ReferenceEquals(session.Current, beforeDecliningReplacement), "Declining a new replacement preserves the imported unsaved state and raw snapshot");

var second = new EditorState<Map, Resources, Package>(new("second"), new("second full DIR"), null, true, 105, new("second raw package"));
Check(session.TryReplace(() => second, _ => { }), "Repeated import can replace an accepted unsaved candidate");
Check(ReferenceEquals(session.Current, second) && !ReferenceEquals(session.Current.OriginalPackage, rawPackage), "Repeated import uses only the newest raw payload");

Console.WriteLine($"PASS {checks} editor-session checks. These exercise the shared state coordinator, not WinForms controls or image rendering.");

sealed record Map(string Description);
sealed record Resources(string Description);
sealed record Package(string Description);
