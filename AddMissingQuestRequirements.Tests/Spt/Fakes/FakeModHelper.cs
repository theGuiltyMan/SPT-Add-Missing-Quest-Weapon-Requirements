using System.Reflection;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Utils;

namespace AddMissingQuestRequirements.Tests.Spt.Fakes;

/// <summary>
/// Minimal stand-in for <see cref="ModHelper"/> returning a fixed path from
/// <see cref="GetAbsolutePathToModFolder"/>. The base constructor requires
/// <see cref="FileUtil"/> and <see cref="JsonUtil"/>; we pass no-op instances
/// so tests don't touch disk or JSON converters.
/// </summary>
public sealed class FakeModHelper : ModHelper
{
    private readonly string _fixedPath;

    public FakeModHelper(string fixedPath)
        : base(new FileUtil(), new JsonUtil([]))
    {
        _fixedPath = fixedPath;
    }

    public override string GetAbsolutePathToModFolder(Assembly modAssembly)
    {
        return _fixedPath;
    }
}
