using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Reporting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AddMissingQuestRequirements.Inspector.Serve;

public static class Endpoints
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Lazy<string> _reportJs = new(() => LoadAsset("report.js"));
    private static readonly Lazy<string> _shellJs = new(() => LoadAsset("shell.js"));
    private static readonly Lazy<string> _shellHtml = new(BuildShellHtml);

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/events", async (HttpContext ctx, SseBroadcaster broadcaster, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Append("X-Accel-Buffering", "no");
            ctx.Response.ContentType = "text/event-stream";
            await ctx.Response.Body.FlushAsync(ct);

            var (id, reader) = broadcaster.Subscribe();
            try
            {
                var keepAlive = Encoding.UTF8.GetBytes(": ok\n\n");
                await ctx.Response.Body.WriteAsync(keepAlive, ct);
                await ctx.Response.Body.FlushAsync(ct);

                await foreach (var frame in reader.ReadAllAsync(ct))
                {
                    var bytes = Encoding.UTF8.GetBytes(frame);
                    await ctx.Response.Body.WriteAsync(bytes, ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                broadcaster.Unsubscribe(id);
            }
        });

        app.MapGet("/", () => Results.Content(_shellHtml.Value, "text/html; charset=utf-8"));
        app.MapGet("/report.js", () => Results.Content(_reportJs.Value, "application/javascript; charset=utf-8"));
        app.MapGet("/shell.js", () => Results.Content(_shellJs.Value, "application/javascript; charset=utf-8"));

        app.MapGet("/api/state", (StateHolder holder) =>
        {
            var snap = holder.GetSnapshot();
            if (snap.Result is null)
            {
                return Results.Problem("Inspector state not initialized", statusCode: 503);
            }
            return Results.Json(snap.Result, _jsonOptions);
        });
    }

    private static string LoadAsset(string name)
    {
        // Assets now live in the main AddMissingQuestRequirements assembly alongside
        // HtmlReportWriter — reference a type from there to locate the right manifest.
        var asm = typeof(HtmlReportWriter).Assembly;
        using var stream = asm.GetManifestResourceStream($"AddMissingQuestRequirements.Reporting.Assets.{name}")
            ?? throw new InvalidOperationException($"Asset not found: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string BuildShellHtml()
    {
        var css = HtmlReportWriter.ReportCss;
        var body = HtmlReportWriter.ReportShellBody;
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>MQR Inspector (live)</title>
<style>
{{css}}
  .toast { position: fixed; bottom: 16px; right: 16px; padding: 10px 14px;
           background: #2d2d2d; color: #d4d4d4; border-radius: 4px;
           font-size: 12px; max-width: 480px; box-shadow: 0 2px 8px rgba(0,0,0,.5);
           z-index: 1000; }
  .toast.error { background: #5c1a1a; color: #ffd0d0; }
  .toast.hidden { display: none; }
</style>
</head>
<body>
{{body}}
<div id="mqw-toast" class="toast hidden"></div>
<script src="/report.js"></script>
<script src="/shell.js"></script>
</body>
</html>
""";
    }
}
