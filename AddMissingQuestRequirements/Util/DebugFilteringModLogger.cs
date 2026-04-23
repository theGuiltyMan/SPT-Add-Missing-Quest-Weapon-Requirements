namespace AddMissingQuestRequirements.Util;

/// <summary>
/// Decorator that suppresses <see cref="IModLogger.Debug"/> calls when debug mode is off.
/// All other levels are forwarded unconditionally.
/// </summary>
public sealed class DebugFilteringModLogger : IModLogger
{
    private readonly IModLogger _inner;
    private readonly bool _debugEnabled;

    public DebugFilteringModLogger(IModLogger inner, bool debugEnabled)
    {
        _inner = inner;
        _debugEnabled = debugEnabled;
    }

    public void Info(string message)
    {
        _inner.Info(message);
    }

    public void Success(string message)
    {
        _inner.Success(message);
    }

    public void Warning(string message)
    {
        _inner.Warning(message);
    }

    public void Debug(string message)
    {
        if (_debugEnabled)
        {
            _inner.Debug(message);
        }
    }
}
