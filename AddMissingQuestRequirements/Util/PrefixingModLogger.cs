namespace AddMissingQuestRequirements.Util;

/// <summary>
/// <see cref="IModLogger"/> decorator that prepends a fixed string to every
/// message before forwarding it to <paramref name="inner"/>. Used by
/// <c>QuestPatcher</c> to tag every log line emitted while a given quest is
/// being processed with a <c>[questName (questId)]</c> prefix.
/// </summary>
public sealed class PrefixingModLogger : IModLogger
{
    private readonly IModLogger _inner;
    private readonly string _prefix;

    public PrefixingModLogger(IModLogger inner, string prefix)
    {
        _inner = inner;
        _prefix = prefix;
    }

    public void Success(string message)
    {
        _inner.Success(_prefix + message);
    }

    public void Warning(string message)
    {
        _inner.Warning(_prefix + message);
    }

    public void Info(string message)
    {
        _inner.Info(_prefix + message);
    }

    public void Debug(string message)
    {
        _inner.Debug(_prefix + message);
    }
}
