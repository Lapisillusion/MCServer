using GameServer.Core.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GameLoggerTestCollection
{
    public const string Name = "GameLogger";
}

[Collection(GameLoggerTestCollection.Name)]
public sealed class GameLoggerTests
{
    [Fact]
    public void LegacyPlayerLog_EmitsPlayerNameAndVisiblePlayerPrefix()
    {
        var sink = new CollectingSink();
        WithTestLogger(sink, () =>
            Info("Session", 42, "Steve", "Session entered play state"));

        var logEvent = Assert.Single(sink.Events);
        Assert.Equal("Steve", GetScalarString(logEvent, "PlayerName"));
        Assert.False(logEvent.Properties.ContainsKey("PlayerId"));
        Assert.Contains("[Player:Steve]", logEvent.RenderMessage());
    }

    [Fact]
    public void RuntimeContextLog_UsesPlayerNameWithoutPlayerId()
    {
        var sink = new CollectingSink();
        var context = RuntimeLogContext.Empty
            .WithSessionId("42")
            .WithPlayerName("Alex")
            .WithPacketId("0x0B");

        WithTestLogger(sink, () =>
            Info("KeepAlive", context, "Acknowledged {KeepAliveId}", 123L));

        var logEvent = Assert.Single(sink.Events);
        Assert.Equal("Alex", GetScalarString(logEvent, "PlayerName"));
        Assert.False(logEvent.Properties.ContainsKey("PlayerId"));
        Assert.Contains("[Player:Alex]", logEvent.RenderMessage());
        Assert.Contains("Acknowledged 123", logEvent.RenderMessage());
    }

    private static void WithTestLogger(CollectingSink sink, Action action)
    {
        var original = Log.Logger;
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            Log.Logger = logger;
            action();
        }
        finally
        {
            Log.Logger = original;
            logger.Dispose();
        }
    }

    private static string? GetScalarString(LogEvent logEvent, string propertyName)
    {
        var value = Assert.IsType<ScalarValue>(logEvent.Properties[propertyName]);
        return value.Value as string;
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
