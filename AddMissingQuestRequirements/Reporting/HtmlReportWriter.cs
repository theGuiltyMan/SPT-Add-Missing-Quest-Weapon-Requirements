using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AddMissingQuestRequirements.Reporting;

public static class HtmlReportWriter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Lazy<string> _reportJs = new(() =>
    {
        var asm = typeof(HtmlReportWriter).Assembly;
        using var stream = asm.GetManifestResourceStream("AddMissingQuestRequirements.Reporting.Assets.report.js")
            ?? throw new InvalidOperationException("report.js embedded resource not found");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static string ReportCss { get; } =
        """
  /* ── Base ──────────────────────────────────────────────────────────── */
  *, *::before, *::after { box-sizing: border-box; }
  body { font-family: system-ui, -apple-system, sans-serif; margin: 0;
         background: #1a1a1a; color: #d4d4d4; font-size: 13px; }
  .mono { font-family: monospace; }

  /* ── Tabs ──────────────────────────────────────────────────────────── */
  .tabs { display: flex; background: #252526; border-bottom: 1px solid #444; }
  .tab-btn { padding: 10px 22px; cursor: pointer; border: none; background: none;
             color: #aaa; font-size: 13px; font-family: inherit; }
  .tab-btn.active { color: #fff; border-bottom: 2px solid #007acc; }
  .tab-content { display: none; padding: 16px 20px; }
  .tab-content.active { display: block; }

  /* ── Search / filters ──────────────────────────────────────────────── */
  .filter-bar { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; }
  input[type=search] { padding: 6px 10px; background: #3c3c3c; color: #d4d4d4;
                       border: 1px solid #555; font-size: 12px; font-family: inherit;
                       border-radius: 3px; width: 340px; }
  label { font-size: 12px; color: #aaa; cursor: pointer; user-select: none; }

  /* ── Tables ────────────────────────────────────────────────────────── */
  table { border-collapse: collapse; width: 100%; font-size: 12px; }
  th { background: #2d2d2d; padding: 7px 12px; text-align: left; color: #9cdcfe;
       font-weight: 600; }
  td { padding: 7px 12px; border-bottom: 1px solid #2d2d2d; vertical-align: top; }
  tr:hover td { background: #232626; }

  /* ── Tags / badges ─────────────────────────────────────────────────── */
  .badge { display: inline-block; padding: 2px 7px; border-radius: 3px;
           font-size: 11px; font-weight: 700; }
  .badge-noop     { background: #3a3a3a; color: #888; }
  .badge-expanded { background: #0e4c20; color: #4ec94e; }
  .type-tag { display: inline-block; background: #1e3a5f; color: #9cdcfe;
              padding: 2px 6px; margin: 1px 2px; border-radius: 2px; font-size: 11px;
              cursor: pointer; }
  .type-tag:hover { background: #264f78; }
  .tag-amber { background: #2e2000; color: #ffd54f; padding: 2px 6px; border-radius: 2px;
               font-size: 11px; display: inline-block; }
  .count-badge { background: #2d2d2d; color: #888; padding: 1px 6px; border-radius: 10px;
                 font-size: 11px; margin-left: 6px; }

  /* ── Hoverable IDs ─────────────────────────────────────────────────── */
  .id-hover { color: #555; font-size: 10px; font-family: monospace; cursor: default; }
  .id-hover:hover { color: #888; }

  /* ── Quest rows ────────────────────────────────────────────────────── */
  .quest-row { border-top: 1px solid #2d2d2d; }
  .quest-header { display: flex; align-items: center; gap: 10px; padding: 8px 12px;
                  cursor: pointer; user-select: none; }
  .quest-header:hover { background: #222; }
  .quest-name { flex: 1; color: #e0e0e0; }
  .quest-chevron { color: #555; font-size: 11px; min-width: 12px; }
  .quest-meta { display: none; background: #1e1e1e; border-top: 1px solid #2d2d2d;
                padding: 8px 12px; gap: 28px; flex-wrap: wrap; }
  .quest-meta.open { display: flex; }
  .meta-field { font-size: 11px; }
  .meta-label { color: #555; text-transform: uppercase; font-size: 9px;
                letter-spacing: .05em; margin-bottom: 2px; }
  .meta-value { color: #9cdcfe; }
  .quest-description { width: 100%; color: #b0b0b0; font-size: 12px; line-height: 1.55;
                       white-space: pre-wrap; margin-top: 4px; padding-top: 6px;
                       border-top: 1px dashed #2d2d2d; }
  .cond-description { color: #b0b0b0; font-size: 12px; line-height: 1.5;
                      white-space: pre-wrap; margin-bottom: 8px; padding-bottom: 6px;
                      border-bottom: 1px solid #252525; }

  /* ── Condition rows ─────────────────────────────────────────────────── */
  .quest-conditions { display: none; }
  .quest-conditions.open { display: block; }
  .cond-row { border-top: 1px solid #252525; }
  .cond-header { display: flex; align-items: center; gap: 8px; padding: 6px 12px 6px 24px;
                 cursor: pointer; background: #212121; }
  .cond-header:hover { background: #252525; }
  .cond-id { color: #444; font-family: monospace; font-size: 10px; }
  .cond-chevron { color: #555; font-size: 10px; margin-left: auto; }
  .cond-detail { display: none; padding: 6px 12px 8px 24px; background: #1a1a1a; }
  .cond-detail.open { display: block; }
  .counter-bar { display: flex; gap: 18px; flex-wrap: wrap; font-size: 11px;
                 margin-bottom: 8px; padding-bottom: 6px; border-bottom: 1px solid #252525; }
  .counter-field { color: #888; }
  .counter-field span { color: #d4d4d4; margin-left: 3px; }

  /* ── Git diff ──────────────────────────────────────────────────────── */
  .git-diff { font-family: monospace; font-size: 11px; line-height: 1.8;
              background: #161616; padding: 8px 12px; border-radius: 3px; }
  .diff-same    { color: #666; }
  .diff-added   { color: #4ec94e; }
  .diff-removed { color: #f47174; }
  .mod-group-label { color: #888; font-size: 11px; font-family: monospace;
                     margin: 6px 0 2px; }

  /* ── Settings ───────────────────────────────────────────────────────── */
  h2 { color: #9cdcfe; font-size: 13px; font-weight: 600; margin: 18px 0 8px; }
  h2:first-child { margin-top: 0; }
  .rule-row { display: flex; align-items: center; gap: 8px; padding: 6px 10px;
              background: #222; border-radius: 3px; margin: 2px 0; cursor: pointer;
              flex-wrap: wrap; }
  .rule-row:hover { background: #2a2a2a; }
  .rule-type { color: #4ec94e; min-width: 140px; font-weight: 600; font-size: 12px; }
  .rule-index { color: #444; font-size: 10px; min-width: 28px; font-family: monospace; }
  .rule-alsoAs { color: #555; font-size: 11px; margin-left: auto; }
  .rule-detail { display: none; background: #1a1a1a; padding: 8px 12px 10px;
                 border-radius: 0 0 3px 3px; margin-top: -2px; margin-bottom: 2px; }
  .rule-detail.open { display: block; }
  .cond-tree { font-size: 12px; }
  .cond-leaf { display: flex; gap: 8px; align-items: center; margin: 3px 0; }
  .cond-key { color: #888; min-width: 120px; }
  .cond-val { background: #1e3a5f; color: #9cdcfe; padding: 1px 6px; border-radius: 2px;
              font-family: monospace; font-size: 11px; }

  /* AND / OR / NOT blocks */
  .cond-and, .cond-or, .cond-not { border-radius: 3px; padding: 6px 10px; margin: 4px 0; }
  .cond-and { border: 1px solid #264f78; background: #111d2e; }
  .cond-or  { border: 1px solid #5c4a00; background: #1e1800; }
  .cond-not { border: 1px solid #5c1a1a; background: #1e0a0a; }
  .cond-op-label { font-size: 9px; font-weight: 700; text-transform: uppercase;
                   letter-spacing: .08em; margin-bottom: 5px; }
  .cond-and .cond-op-label { color: #4fc3f7; }
  .cond-or  .cond-op-label { color: #ffd54f; }
  .cond-not .cond-op-label { color: #ef9a9a; }

  /* ── Types tab ──────────────────────────────────────────────────────── */
  .type-section { margin-bottom: 16px; }
  .type-heading { color: #9cdcfe; font-size: 13px; font-weight: 600;
                  margin: 0 0 6px; display: flex; align-items: center; }
  .weapons-list { font-size: 12px; line-height: 2; }
  .weapon-link { color: #b5cea8; cursor: pointer; }
  .weapon-link:hover { color: #d4d4d4; text-decoration: underline; }

  /* ── Misc ───────────────────────────────────────────────────────────── */
  .hidden { display: none !important; }
""";

    public static void Write(InspectorResult result, string outputPath)
    {
        var json = JsonSerializer.Serialize(result, _jsonOptions);
        var html = BuildHtml(json);
        File.WriteAllText(outputPath, html);
        Console.WriteLine($"Report written to {outputPath}");
    }

    /// <summary>
    /// The tab bar + panel containers shared by the one-shot HTML report and the
    /// live-serve inspector shell. Keeping it in one place stops the two
    /// consumers from drifting — missing panels here cause the JS render chain
    /// (<c>renderAttachments</c>, <c>renderQuests</c>, …) to null-ref and abort.
    /// Does not include a <c>&lt;script&gt;</c> block — callers add their own
    /// (inline data + report.js vs external /report.js + /shell.js).
    /// </summary>
    public static string ReportShellBody { get; } =
        """
<div class="tabs">
  <button class="tab-btn active" onclick="showTab('settings')">Settings</button>
  <button class="tab-btn" onclick="showTab('weapons')">Weapons</button>
  <button class="tab-btn" onclick="showTab('types')">Types</button>
  <button class="tab-btn" onclick="showTab('attachments')">Attachments</button>
  <button class="tab-btn" onclick="showTab('attachment-types')">Attachment Types</button>
  <button class="tab-btn" onclick="showTab('quests')">Quests</button>
</div>

<div id="tab-settings" class="tab-content active">
  <div id="settings-panel"></div>
</div>
<div id="tab-weapons" class="tab-content">
  <div class="filter-bar">
    <input type="search" id="weapon-search" placeholder="Filter by name or id..."
           oninput="filterWeapons()">
  </div>
  <table id="weapon-table">
    <thead><tr><th>Name</th><th>Types</th><th>Caliber</th></tr></thead>
    <tbody id="weapon-tbody"></tbody>
  </table>
</div>
<div id="tab-types" class="tab-content">
  <div class="filter-bar">
    <input type="search" id="type-search" placeholder="Filter by type name..."
           oninput="filterTypes()">
  </div>
  <div id="types-panel"></div>
</div>
<div id="tab-attachments" class="tab-content">
  <div class="filter-bar">
    <input type="search" id="attachment-search" placeholder="Filter by name or id..."
           oninput="filterAttachments()">
  </div>
  <table id="attachment-table">
    <thead><tr><th>Name</th><th>Types</th></tr></thead>
    <tbody id="attachment-tbody"></tbody>
  </table>
</div>
<div id="tab-attachment-types" class="tab-content">
  <div class="filter-bar">
    <input type="search" id="attachment-type-search" placeholder="Filter by type name..."
           oninput="filterAttachmentTypes()">
  </div>
  <div id="attachment-types-panel"></div>
</div>
<div id="tab-quests" class="tab-content">
  <div class="filter-bar">
    <input type="search" id="quest-search" placeholder="Filter by quest name or id..."
           oninput="filterQuests()">
    <label><input type="checkbox" id="hide-noop" onchange="filterQuests()" checked>
      Hide NOOP quests</label>
  </div>
  <div id="quests-panel"></div>
</div>
""";

    private static string BuildHtml(string dataJson)
    {
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>MQR Inspector</title>
<style>
{{ReportCss}}
</style>
</head>
<body>
{{ReportShellBody}}
<script>
window.__INSPECTOR_DATA__ = {{dataJson}};
{{_reportJs.Value}}
renderInspector(window.__INSPECTOR_DATA__, document.body);
</script>
</body>
</html>
""";
    }
}
