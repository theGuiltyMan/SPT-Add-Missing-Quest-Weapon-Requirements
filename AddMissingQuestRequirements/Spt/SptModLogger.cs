using AddMissingQuestRequirements.Util;
using SPTarkov.Server.Core.Models.Utils;

namespace AddMissingQuestRequirements.Spt;

/// <summary>
/// <see cref="IModLogger"/> adapter that routes to SPT's <see cref="ISptLogger{T}"/>.
/// Only used inside the SPT server process (Phase 10) — tests rely on
/// <c>CapturingModLogger</c> or <c>NullModLogger</c>.
///
/// Every level maps to the same-named method on <see cref="ISptLogger{T}"/>. Earlier
/// versions routed <see cref="Info"/> through <c>LogWithColor</c>, but SPT's console
/// filter sometimes dropped those lines — use the dedicated <c>Info</c> channel so
/// the pipeline's progress logs actually surface.
/// </summary>
public sealed class SptModLogger<T>(ISptLogger<T> inner) : IModLogger
{
    private readonly ISptLogger<T> _inner = inner;

    public void Success(string message)
    {
        _inner.Success(message);
    }

    public void Warning(string message)
    {
        _inner.Warning(message);
    }

    public void Info(string message)
    {
        _inner.Info(message);
    }

    public void Debug(string message)
    {
        _inner.Debug(message);
    }
}
