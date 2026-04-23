using System.Text.RegularExpressions;

namespace AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;

/// <summary>
/// Shared regex hardening for user-authored rule patterns. A malicious or
/// careless mod author could ship a catastrophically-backtracking pattern in
/// <c>customTypeRules</c> (nested quantifiers, alternations with overlap,
/// etc.) that hangs server startup when matched against a real item name.
/// <para>
/// <see cref="Timeout"/> is passed to every <see cref="Regex"/> compiled from
/// user input. <see cref="IsMatchSafe"/> wraps <see cref="Regex.IsMatch(string)"/>
/// and treats a timeout as a non-match rather than a fatal exception — the
/// pipeline must survive bad regex even if it silently ignores it.
/// </para>
/// </summary>
internal static class RegexGuard
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    public static bool IsMatchSafe(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
