using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Util;

public class FileModLoggerTests
{
    [Fact]
    public void Writes_each_level_with_prefix_and_timestamp_format()
    {
        var dir = Path.Combine(Path.GetTempPath(), "amqr-filelogger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "log.txt");

        try
        {
            var logger = new FileModLogger(path);
            logger.Success("s-msg");
            logger.Warning("w-msg");
            logger.Info("i-msg");
            logger.Debug("d-msg");

            var lines = File.ReadAllLines(path);
            lines.Should().HaveCount(4);

            lines[0].Should().StartWith("[20").And.Contain("[SUCCESS]").And.EndWith("s-msg");
            lines[1].Should().StartWith("[20").And.Contain("[WARNING]").And.EndWith("w-msg");
            lines[2].Should().StartWith("[20").And.Contain("[INFO]").And.EndWith("i-msg");
            lines[3].Should().StartWith("[20").And.Contain("[DEBUG]").And.EndWith("d-msg");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Truncates_existing_file_on_construction()
    {
        var dir = Path.Combine(Path.GetTempPath(), "amqr-filelogger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "log.txt");

        try
        {
            File.WriteAllText(path, "stale content\n");

            var logger = new FileModLogger(path);
            logger.Info("fresh");

            var lines = File.ReadAllLines(path);
            lines.Should().ContainSingle();
            lines[0].Should().Contain("[INFO]").And.EndWith("fresh");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Does_not_throw_when_directory_does_not_exist()
    {
        var root = Path.Combine(Path.GetTempPath(), "amqr-filelogger-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "level1", "level2");
        var path = Path.Combine(nested, "log.txt");

        try
        {
            Directory.Exists(nested).Should().BeFalse();

            var logger = new FileModLogger(path);
            logger.Info("hello");

            File.Exists(path).Should().BeTrue();
            var lines = File.ReadAllLines(path);
            lines.Should().ContainSingle();
            lines[0].Should().Contain("[INFO]").And.EndWith("hello");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
