using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using AddMissingQuestRequirements.Inspector.Serve;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AddMissingQuestRequirements.Tests.Inspector;

public class ServeEndpointsTests
{
    [Fact]
    public async Task Shell_ReferencesReportAndShellJs()
    {
        using var host = await StartAsync();
        var client = host.GetTestClient();

        var resp = await client.GetAsync("/");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("<script src=\"/report.js\"></script>");
        body.Should().Contain("<script src=\"/shell.js\"></script>");
    }

    [Fact]
    public async Task ReportJs_ExposesRenderInspector()
    {
        using var host = await StartAsync();
        var client = host.GetTestClient();

        var resp = await client.GetAsync("/report.js");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("function renderInspector(");
    }

    [Fact]
    public async Task ApiState_ReturnsInspectorResult()
    {
        using var host = await StartAsync();
        var client = host.GetTestClient();

        var resp = await client.GetAsync("/api/state");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("\"settings\"");
        body.Should().Contain("\"weapons\"");
        body.Should().Contain("\"types\"");
        body.Should().Contain("\"quests\"");
    }

    [Fact]
    public async Task EditingConfig_EmitsStateChanged()
    {
        using var fx = new InspectorFixture();
        var holder = new StateHolder();
        var broadcaster = new SseBroadcaster();
        ReloadPipeline.Run(fx.Config, holder, fx.Logger).Should().BeTrue();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(holder);
        builder.Services.AddSingleton(fx.Config);
        builder.Services.AddSingleton(broadcaster);
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.Lifetime.ApplicationStopped.Register(() => fx.Dispose());
        Endpoints.Map(app);
        await app.StartAsync();

        using var watcher = new ConfigWatcher(fx.ConfigPath, debounceMs: 150, onReload: () =>
        {
            if (ReloadPipeline.Run(fx.Config, holder, fx.Logger))
            {
                broadcaster.Publish("state-changed",
                    JsonSerializer.Serialize(new { version = holder.GetSnapshot().Version }));
            }
            else
            {
                broadcaster.Publish("reload-error",
                    JsonSerializer.Serialize(new { message = holder.GetSnapshot().LastError ?? "x" }));
            }
        });
        watcher.Start();

        var client = app.GetTestClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await resp.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        await Task.Delay(200);
        File.AppendAllText(Path.Combine(fx.ConfigPath, "config.jsonc"), " ");

        var buf = new char[4096];
        var text = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            while (!cts.IsCancellationRequested && !text.ToString().Contains("state-changed"))
            {
                var read = await reader.ReadAsync(buf.AsMemory(), cts.Token);
                if (read > 0)
                {
                    text.Append(buf, 0, read);
                }
                else
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }

        text.ToString().Should().Contain("event: state-changed");
        await app.StopAsync();
    }

    private static async Task<IHost> StartAsync()
    {
        var fx = new InspectorFixture();
        var holder = new StateHolder();
        ReloadPipeline.Run(fx.Config, holder, fx.Logger).Should().BeTrue();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(holder);
        builder.Services.AddSingleton(fx.Config);
        builder.Services.AddSingleton<SseBroadcaster>();
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.Lifetime.ApplicationStopped.Register(() => fx.Dispose());
        Endpoints.Map(app);
        await app.StartAsync();
        return app;
    }
}
