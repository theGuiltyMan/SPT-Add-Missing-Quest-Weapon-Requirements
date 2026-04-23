using System.IO;
using AddMissingQuestRequirements.Reporting;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Inspector;

public class HtmlReportWriterTests
{
    [Fact]
    public void Write_EmbedsRenderInspectorFromAsset()
    {
        var result = new InspectorResult
        {
            Settings = new SettingsSnapshot
            {
                ExcludedQuestCount = 0,
                ExcludedQuests = [],
                ManualTypeOverrides = new Dictionary<string, string>(),
                Rules = [],
                AttachmentRules = []
            },
            Weapons = [],
            Types = new Dictionary<string, List<WeaponResult>>(),
            Attachments = [],
            AttachmentTypes = new Dictionary<string, List<AttachmentResult>>(),
            Quests = []
        };

        var tmp = Path.Combine(Path.GetTempPath(), $"mqw-test-{Guid.NewGuid():N}.html");
        try
        {
            HtmlReportWriter.Write(result, tmp);
            var html = File.ReadAllText(tmp);
            html.Should().Contain("function renderInspector(");
            html.Should().Contain("<script>");
            html.Should().Contain("window.__INSPECTOR_DATA__");
        }
        finally
        {
            if (File.Exists(tmp)) { File.Delete(tmp); }
        }
    }
}
