// SPT's ModHelper.GetAbsolutePathToModFolder is virtual (confirmed via reflection on
// SPTarkov.Server.Core 4.0.13), so tests can subclass ModHelper and override the method
// directly. No delegate-based fallback constructor is required.

using System.Reflection;
using AddMissingQuestRequirements.Pipeline.Override;
using SPTarkov.Server.Core.Helpers;

namespace AddMissingQuestRequirements.Spt;

/// <summary>
/// Walks <c>user/mods/</c> to enumerate every installed mod directory (self included).
/// <see cref="AddMissingQuestRequirements.Pipeline.Override.OverrideReader"/> silently
/// skips directories without a <c>MissingQuestWeapons/</c> child, so this provider
/// applies no filtering of its own.
/// </summary>
public sealed class SptModDirectoryProvider : IModDirectoryProvider
{
    private readonly ModHelper _modHelper;
    private readonly Assembly _ownAssembly;

    public SptModDirectoryProvider(ModHelper modHelper, Assembly ownAssembly)
    {
        _modHelper = modHelper;
        _ownAssembly = ownAssembly;
    }

    public IEnumerable<string> GetModDirectories()
    {
        var ownPath = _modHelper.GetAbsolutePathToModFolder(_ownAssembly);
        var parent = Path.GetDirectoryName(ownPath);
        if (parent is null)
        {
            throw new InvalidOperationException(
                $"Cannot resolve parent of mod folder '{ownPath}'.");
        }
        return Directory.GetDirectories(parent);
    }
}
