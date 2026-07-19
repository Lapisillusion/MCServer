using System.Net.Sockets;
using GameServer.Application;
using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Network;
using Xunit;

namespace GameServer.Tests;

public class SessionLivenessTests
{
    [Fact]
    public void Update_WaitsForInitialDelayThenSendsOneKeepAlive()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero));
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var session = CreatePlaySession(socket, clock);
        var service = new SessionLivenessService(CreateOptions());

        service.Update(session);
        Assert.Empty(session.DrainAllOutput());

        clock.Advance(TimeSpan.FromSeconds(2));
        service.Update(session);

        Assert.Single(session.DrainAllOutput());
        Assert.NotNull(session.Liveness.PendingKeepAliveId);
    }

    [Fact]
    public void Update_DoesNotReplacePendingKeepAlive()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero));
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var session = CreatePlaySession(socket, clock);
        var service = new SessionLivenessService(CreateOptions());

        service.Update(session);
        clock.Advance(TimeSpan.FromSeconds(2));
        service.Update(session);
        var pending = session.Liveness.PendingKeepAliveId;
        session.DrainAllOutput();

        clock.Advance(TimeSpan.FromSeconds(10));
        service.Update(session);

        Assert.Equal(pending, session.Liveness.PendingKeepAliveId);
        Assert.Empty(session.DrainAllOutput());
    }

    [Fact]
    public void Acknowledge_MatchingIdRecordsRttAndAllowsNextSend()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero));
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var session = CreatePlaySession(socket, clock);
        var service = new SessionLivenessService(CreateOptions());

        service.Update(session);
        clock.Advance(TimeSpan.FromSeconds(2));
        service.Update(session);
        var pending = session.Liveness.PendingKeepAliveId!.Value;
        session.DrainAllOutput();

        clock.Advance(TimeSpan.FromMilliseconds(125));
        Assert.True(session.Liveness.TryAcknowledge(pending, clock.GetUtcNow(), out var rtt));
        Assert.Equal(TimeSpan.FromMilliseconds(125), rtt);
        Assert.Equal(rtt, session.Liveness.LastRtt);
        Assert.Null(session.Liveness.PendingKeepAliveId);

        clock.Advance(TimeSpan.FromSeconds(9.875));
        service.Update(session);
        Assert.Single(session.DrainAllOutput());
    }

    [Fact]
    public void Acknowledge_WrongIdLeavesPendingStateUntouched()
    {
        var state = new SessionLivenessState();
        var now = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);
        state.EnsureScheduled(now, TimeSpan.Zero);
        state.MarkSent(42, now, TimeSpan.FromSeconds(10));

        Assert.False(state.TryAcknowledge(43, now.AddMilliseconds(10), out _));
        Assert.Equal(42, state.PendingKeepAliveId);
    }

    [Fact]
    public void Update_RequestsCloseAfterTimeoutWithoutClaimingFinalCleanup()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero));
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var session = CreatePlaySession(socket, clock);
        var service = new SessionLivenessService(CreateOptions());

        service.Update(session);
        clock.Advance(TimeSpan.FromSeconds(2));
        service.Update(session);
        session.DrainAllOutput();

        clock.Advance(TimeSpan.FromSeconds(30));
        service.Update(session);

        Assert.True(session.CloseRequested);
        Assert.False(session.Closed);
        Assert.Equal(GameSessionState.Closing, session.State);
        Assert.Contains("KeepAlive timeout", session.CloseReason);
        Assert.True(session.TryMarkClosed());
    }

    [Fact]
    public void Update_IgnoresNonPlayAndAlreadyClosingSessions()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero));
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var session = new SessionContext(1, socket, clock);
        var service = new SessionLivenessService(CreateOptions());

        clock.Advance(TimeSpan.FromMinutes(1));
        service.Update(session);
        Assert.Empty(session.DrainAllOutput());

        session.State = GameSessionState.Play;
        session.RequestClose("test");
        service.Update(session);
        Assert.Empty(session.DrainAllOutput());
    }

    private static SessionContext CreatePlaySession(Socket socket, TimeProvider clock)
        => new(1, socket, clock) { State = GameSessionState.Play };

    private static GameServerOptions CreateOptions()
        => GameServerOptions.CreateDefault() with
        {
            KeepAliveInitialDelay = TimeSpan.FromSeconds(2),
            KeepAliveInterval = TimeSpan.FromSeconds(10),
            KeepAliveTimeout = TimeSpan.FromSeconds(30)
        };

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount) => _utcNow += amount;
    }
}
