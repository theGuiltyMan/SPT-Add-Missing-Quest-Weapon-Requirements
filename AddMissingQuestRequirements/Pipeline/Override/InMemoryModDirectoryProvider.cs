namespace AddMissingQuestRequirements.Pipeline.Override;

/// <summary>Test double — returns a fixed list of directory paths.</summary>
public sealed class InMemoryModDirectoryProvider(IEnumerable<string> directories) : IModDirectoryProvider
{
    public IEnumerable<string> GetModDirectories() => directories;
}
