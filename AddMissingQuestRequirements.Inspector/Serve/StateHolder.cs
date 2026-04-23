using AddMissingQuestRequirements.Reporting;

namespace AddMissingQuestRequirements.Inspector.Serve;

public sealed record StateSnapshot(
    LoadResult? Loaded,
    InspectorResult? Result,
    long Version,
    string? LastError);

/// <summary>
/// Thread-safe holder for the current inspector state.
/// Mutations (<see cref="ApplyReload"/>, <see cref="ApplyReloadError"/>) are serialized;
/// reads (<see cref="GetSnapshot"/>) are wait-free via a volatile reference swap.
/// </summary>
public sealed class StateHolder
{
    private readonly object _writeLock = new();
    private StateSnapshot _snapshot = new(null, null, 0, null);

    public StateSnapshot GetSnapshot()
    {
        return Volatile.Read(ref _snapshot);
    }

    public void ApplyReload(LoadResult loaded, InspectorResult result)
    {
        lock (_writeLock)
        {
            var prev = _snapshot;
            Volatile.Write(ref _snapshot, new StateSnapshot(
                Loaded: loaded,
                Result: result,
                Version: prev.Version + 1,
                LastError: null));
        }
    }

    public void ApplyReloadError(string message)
    {
        lock (_writeLock)
        {
            var prev = _snapshot;
            Volatile.Write(ref _snapshot, prev with { LastError = message });
        }
    }
}
