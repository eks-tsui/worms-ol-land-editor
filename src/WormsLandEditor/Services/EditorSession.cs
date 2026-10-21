namespace WormsLandEditor.Services;

internal sealed record EditorState<TMap, TResources, TPackage>(
    TMap? Map, TResources? Resources, string? SourcePngPath, bool IsDirty,
    int MapNumber, TPackage? OriginalPackage)
    where TMap : class where TResources : class where TPackage : class;

/// <summary>Keeps the editor's model and save provenance together across prepared view transitions.</summary>
internal sealed class EditorSession<TMap, TResources, TPackage>
    where TMap : class where TResources : class where TPackage : class
{
    public EditorSession(int mapNumber)
    {
        Current = new(null, null, null, false, mapNumber, null);
    }

    public EditorState<TMap, TResources, TPackage> Current { get; private set; }

    public void Update(Func<EditorState<TMap, TResources, TPackage>, EditorState<TMap, TResources, TPackage>> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        Current = update(Current) ?? throw new InvalidOperationException();
    }

    public void MarkEdited() => Current = Current with { IsDirty = true, OriginalPackage = null };
    public void MarkSaved() => Current = Current with { IsDirty = false };

    /// <summary>
    /// Preparation must use independent candidate models. A null result means cancellation.
    /// The view installer must restore its own controls if it fails; this method retains the
    /// exact previous session even if a synchronous view event updated it before the failure.
    /// </summary>
    public bool TryReplace(Func<EditorState<TMap, TResources, TPackage>?> prepare,
        Action<EditorState<TMap, TResources, TPackage>> installView)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(installView);
        var previous = Current;
        try
        {
            var candidate = prepare();
            if (candidate is null)
            {
                Current = previous;
                return false;
            }
            installView(candidate);
            Current = candidate;
            return true;
        }
        catch
        {
            Current = previous;
            throw;
        }
    }
}
