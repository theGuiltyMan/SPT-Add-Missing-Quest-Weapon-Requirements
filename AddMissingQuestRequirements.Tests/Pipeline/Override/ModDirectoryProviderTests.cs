using AddMissingQuestRequirements.Pipeline.Override;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Override;

public sealed class ModDirectoryProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"moddir-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Dir(params string[] parts)
    {
        var path = Path.Combine([_root, ..parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    // ── SPT shape: own folder is a sibling inside the container ──────────────

    [Fact]
    public void Own_folder_inside_container_yields_self_first_then_siblings_sorted_case_insensitively()
    {
        // Ordinal (case-sensitive) order would put "Bravo" before "alpha-mod".
        var charlie = Dir("mods", "charlie");
        var own = Dir("mods", "Zz-OwnMod");
        var bravo = Dir("mods", "Bravo");
        var alpha = Dir("mods", "alpha-mod");

        // Trailing separator mimics ModHelper's mod-folder path shape.
        var provider = new ModDirectoryProvider(own + Path.DirectorySeparatorChar, [Path.Combine(_root, "mods")]);

        provider.GetModDirectories().Should().Equal(own, alpha, bravo, charlie);
    }

    [Fact]
    public void Own_folder_alone_in_container_returns_only_self()
    {
        var own = Dir("mods", "OnlyMe");

        var provider = new ModDirectoryProvider(own, [Path.Combine(_root, "mods")]);

        provider.GetModDirectories().Should().ContainSingle().Which.Should().Be(own);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("//")]
    [InlineData("/.")]
    public void ForInstalledMod_scans_the_parent_mods_folder_whatever_the_path_suffix(string suffix)
    {
        var own = Dir("mods", "OwnMod");
        var sibling = Dir("mods", "Sibling");
        Dir("mods", "OwnMod", "MissingQuestWeapons");

        var provider = ModDirectoryProvider.ForInstalledMod(own + suffix.Replace('/', Path.DirectorySeparatorChar));

        provider.GetModDirectories().Should().Equal(own, sibling);
    }

    // ── Inspector shape: own folder outside, several containers ──────────────

    [Fact]
    public void Own_folder_outside_containers_comes_first_then_all_containers_merged_and_sorted()
    {
        var own = Dir("config");
        var delta = Dir("extrasA", "delta");
        var alpha = Dir("extrasA", "Alpha");
        var charlie = Dir("extrasB", "charlie");
        var bravo = Dir("extrasB", "bravo");

        var provider = new ModDirectoryProvider(
            own, [Path.Combine(_root, "extrasA"), Path.Combine(_root, "extrasB")]);

        provider.GetModDirectories().Should().Equal(own, alpha, bravo, charlie, delta);
    }

    [Fact]
    public void Same_container_listed_twice_yields_each_folder_once()
    {
        var own = Dir("config");
        var alpha = Dir("extras", "alpha");
        var extras = Path.Combine(_root, "extras");

        var provider = new ModDirectoryProvider(own, [extras, extras + Path.DirectorySeparatorChar]);

        provider.GetModDirectories().Should().Equal(own, alpha);
    }

    [Fact]
    public void No_containers_returns_only_self()
    {
        var own = Dir("config");

        var provider = new ModDirectoryProvider(own, []);

        provider.GetModDirectories().Should().Equal(own);
    }
}
