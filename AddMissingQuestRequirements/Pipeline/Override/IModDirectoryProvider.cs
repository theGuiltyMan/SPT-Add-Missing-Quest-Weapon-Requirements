namespace AddMissingQuestRequirements.Pipeline.Override;

/// <summary>Returns paths to all installed mod root directories.</summary>
public interface IModDirectoryProvider
{
    IEnumerable<string> GetModDirectories();
}
