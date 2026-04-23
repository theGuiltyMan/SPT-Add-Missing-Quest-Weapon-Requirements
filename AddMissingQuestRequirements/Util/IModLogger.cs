namespace AddMissingQuestRequirements.Util;

/// <summary>
/// Logging abstraction used by all pipeline phases.
/// Decoupled from SPT's <c>ISptLogger&lt;T&gt;</c> so phases are testable without the server.
/// </summary>
public interface IModLogger
{
    void Info(string message);
    void Success(string message);
    void Warning(string message);

    /// <summary>
    /// Verbose output. Implementations may suppress this when debug mode is off.
    /// </summary>
    void Debug(string message);
}
