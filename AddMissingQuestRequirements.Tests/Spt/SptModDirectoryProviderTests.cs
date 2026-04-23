using System.Reflection;
using AddMissingQuestRequirements.Spt;
using AddMissingQuestRequirements.Tests.Spt.Fakes;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Spt;

public class SptModDirectoryProviderTests
{
    [Fact]
    public void GetModDirectories_returns_all_siblings_including_self()
    {
        var root = Path.Combine(Path.GetTempPath(), $"spt-tests-{Guid.NewGuid():N}");
        var ownDir = Path.Combine(root, "AddMissingQuestRequirements");
        var siblingA = Path.Combine(root, "OtherModA");
        var siblingB = Path.Combine(root, "OtherModB");
        Directory.CreateDirectory(ownDir);
        Directory.CreateDirectory(siblingA);
        Directory.CreateDirectory(siblingB);

        try
        {
            var fakeHelper = new FakeModHelper(ownDir);
            var provider = new SptModDirectoryProvider(fakeHelper, Assembly.GetExecutingAssembly());

            var result = provider.GetModDirectories().ToList();

            result.Should().BeEquivalentTo(new[] { ownDir, siblingA, siblingB });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GetModDirectories_single_mod_returns_only_self()
    {
        var root = Path.Combine(Path.GetTempPath(), $"spt-tests-{Guid.NewGuid():N}");
        var ownDir = Path.Combine(root, "OnlyMe");
        Directory.CreateDirectory(ownDir);

        try
        {
            var fakeHelper = new FakeModHelper(ownDir);
            var provider = new SptModDirectoryProvider(fakeHelper, Assembly.GetExecutingAssembly());

            var result = provider.GetModDirectories().ToList();

            result.Should().ContainSingle().Which.Should().Be(ownDir);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
