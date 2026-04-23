using System.Diagnostics;
using System.Text.Json;
using AddMissingQuestRequirements.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AddMissingQuestRequirements.Inspector.Serve;

public static class ServeCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        string? configPath = null;
        int port = 5173;
        bool openBrowser = true;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out port))
                    {
                        Console.Error.WriteLine("--port requires an integer argument");
                        return 2;
                    }
                    break;
                case "--no-open":
                    openBrowser = false;
                    break;
                default:
                    if (configPath is null)
                    {
                        configPath = args[i];
                    }
                    else
                    {
                        Console.Error.WriteLine($"Unknown argument: {args[i]}");
                        return 2;
                    }
                    break;
            }
        }

        var baseDir = FindGitRoot(Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory();
        configPath ??= FindConfigPath(baseDir);

        InspectorConfig rawConfig;
        if (configPath is null)
        {
            rawConfig = new InspectorConfig();
            Console.WriteLine("No inspector-config.json found; using defaults.");
        }
        else if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Config not found: {configPath}");
            return 1;
        }
        else
        {
            rawConfig = JsonSerializer.Deserialize<InspectorConfig>(
                await File.ReadAllTextAsync(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }

        var config = rawConfig.Resolve(baseDir);

        var logger = ConsoleModLogger.Instance;
        var holder = new StateHolder();
        if (!ReloadPipeline.Run(config, holder, logger))
        {
            Console.Error.WriteLine("Initial load failed. See logs above.");
            return 1;
        }

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { ApplicationName = typeof(ServeCommand).Assembly.GetName().Name });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(holder);
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton<SseBroadcaster>();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Listen(System.Net.IPAddress.Loopback, port);
        });

        var app = builder.Build();
        Endpoints.Map(app);

        var broadcaster = app.Services.GetRequiredService<SseBroadcaster>();

        void OnConfigChanged()
        {
            var ok = ReloadPipeline.Run(config, holder, logger);
            var snap = holder.GetSnapshot();
            if (ok)
            {
                broadcaster.Publish("state-changed",
                    JsonSerializer.Serialize(new { version = snap.Version }));
            }
            else
            {
                broadcaster.Publish("reload-error",
                    JsonSerializer.Serialize(new { message = snap.LastError ?? "unknown" }));
            }
        }

        var watchers = new List<ConfigWatcher>();
        var mainWatcher = new ConfigWatcher(config.MainConfigPath, debounceMs: 300, onReload: OnConfigChanged);
        mainWatcher.Start();
        watchers.Add(mainWatcher);

        foreach (var extra in config.OtherModConfigPaths)
        {
            if (!Directory.Exists(extra))
            {
                continue;
            }
            var w = new ConfigWatcher(extra, debounceMs: 300, onReload: OnConfigChanged);
            w.Start();
            watchers.Add(w);
        }

        var url = $"http://127.0.0.1:{port}";
        Console.WriteLine($"Inspector serve mode listening on {url}");
        Console.WriteLine("Edit files under MainConfigPath; saves trigger auto-reload. Ctrl+C to stop.");

        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        if (openBrowser)
        {
            lifetime.ApplicationStarted.Register(() => TryOpenBrowser(url));
        }

        try
        {
            await app.RunAsync();
        }
        catch (Exception ex) when (IsAddressInUse(ex))
        {
            Console.Error.WriteLine($"Port {port} is already in use. Use --port to specify a different port.");
            return 3;
        }
        finally
        {
            foreach (var w in watchers)
            {
                w.Dispose();
            }
        }

        return 0;
    }

    private static string? FindConfigPath(string baseDir)
    {
        const string FileName = "inspector-config.json";

        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), FileName);
        if (File.Exists(cwdPath))
        {
            return cwdPath;
        }

        var rootPath = Path.Combine(baseDir, FileName);
        if (File.Exists(rootPath))
        {
            return rootPath;
        }

        return null;
    }

    private static string? FindGitRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not open browser ({ex.Message}). Visit {url} manually.");
        }
    }

    private static bool IsAddressInUse(Exception ex)
    {
        var e = ex;
        while (e != null)
        {
            if (e is System.Net.Sockets.SocketException se &&
                se.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
            {
                return true;
            }
            e = e.InnerException;
        }
        return false;
    }
}
