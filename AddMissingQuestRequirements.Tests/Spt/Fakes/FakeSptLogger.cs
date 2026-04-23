using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Spt.Logging;
using SPTarkov.Server.Core.Models.Utils;

namespace AddMissingQuestRequirements.Tests.Spt.Fakes;

/// <summary>
/// Hand-rolled <see cref="ISptLogger{T}"/> stub for <c>SptModLogger</c> tests.
/// Only the five methods we exercise record their calls; every other member
/// throws <see cref="NotImplementedException"/> so surprise invocations fail loud.
/// </summary>
public sealed class FakeSptLogger<T> : ISptLogger<T>
{
    public List<string> Successes { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Infos { get; } = [];
    public List<string> Debugs { get; } = [];
    public List<(string Message, LogTextColor? TextColor, LogBackgroundColor? BackgroundColor)> WithColor { get; } = [];

    public void Success(string data, Exception? ex = null)
    {
        Successes.Add(data);
    }

    public void Warning(string data, Exception? ex = null)
    {
        Warnings.Add(data);
    }

    public void Info(string data, Exception? ex = null)
    {
        Infos.Add(data);
    }

    public void Debug(string data, Exception? ex = null)
    {
        Debugs.Add(data);
    }

    public void LogWithColor(
        string data,
        LogTextColor? textColor = null,
        LogBackgroundColor? backgroundColor = null,
        Exception? ex = null)
    {
        WithColor.Add((data, textColor, backgroundColor));
    }

    public void Error(string data, Exception? ex = null)
    {
        throw new NotImplementedException("FakeSptLogger.Error not modelled");
    }

    public void Critical(string data, Exception? ex = null)
    {
        throw new NotImplementedException("FakeSptLogger.Critical not modelled");
    }

    public void Log(
        LogLevel level,
        string data,
        LogTextColor? textColor = null,
        LogBackgroundColor? backgroundColor = null,
        Exception? ex = null)
    {
        throw new NotImplementedException("FakeSptLogger.Log not modelled");
    }

    public bool IsLogEnabled(LogLevel level)
    {
        throw new NotImplementedException("FakeSptLogger.IsLogEnabled not modelled");
    }

    public void DumpAndStop()
    {
        throw new NotImplementedException("FakeSptLogger.DumpAndStop not modelled");
    }
}
