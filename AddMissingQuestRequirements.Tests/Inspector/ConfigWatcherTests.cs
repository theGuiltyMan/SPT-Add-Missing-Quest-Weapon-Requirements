using AddMissingQuestRequirements.Inspector.Serve;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Inspector;

public class ConfigWatcherTests
{
    [Fact]
    public async Task SingleSave_FiresReloadOnce()
    {
        using var fx = new InspectorFixture();
        var calls = 0;
        using var watcher = new ConfigWatcher(fx.ConfigPath, debounceMs: 100,
            onReload: () => Interlocked.Increment(ref calls));
        watcher.Start();

        await Task.Delay(50);   // let watcher settle
        File.AppendAllText(Path.Combine(fx.ConfigPath, "config.jsonc"), " ");

        await WaitUntil(() => calls >= 1, timeout: TimeSpan.FromSeconds(3));
        calls.Should().Be(1);
    }

    [Fact]
    public async Task BurstOfSaves_DebouncesToOne()
    {
        using var fx = new InspectorFixture();
        var calls = 0;
        using var watcher = new ConfigWatcher(fx.ConfigPath, debounceMs: 200,
            onReload: () => Interlocked.Increment(ref calls));
        watcher.Start();

        await Task.Delay(50);
        for (var i = 0; i < 5; i++)
        {
            File.AppendAllText(Path.Combine(fx.ConfigPath, "config.jsonc"), $" /*{i}*/");
            await Task.Delay(20);
        }

        await WaitUntil(() => calls >= 1, timeout: TimeSpan.FromSeconds(3));
        await Task.Delay(400);   // ensure no late stragglers
        calls.Should().Be(1);
    }

    private static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) { return; }
            await Task.Delay(25);
        }
    }
}
