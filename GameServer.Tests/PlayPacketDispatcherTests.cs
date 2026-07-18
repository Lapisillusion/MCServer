using GameServer.Core.Diagnostics;
using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using Xunit;

namespace GameServer.Tests;

public class PlayPacketDispatcherTests
{
    [Fact]
    public void Register_Duplicate_Throws()
    {
        var dispatcher = new PlayPacketDispatcher();
        dispatcher.Register(GameSessionState.Play, 0x01, NoopHandler);

        Assert.Throws<InvalidOperationException>(() =>
            dispatcher.Register(GameSessionState.Play, 0x01, NoopHandler));
    }

    [Fact]
    public async Task Dispatch_RegisteredRoute_CallsHandler()
    {
        var dispatcher = new PlayPacketDispatcher();
        var called = false;
        ValueTask Handler(SessionContext s, in RuntimeLogContext c, in PlayPacketRouteKey r, ReadOnlyMemory<byte> f, CancellationToken ct)
        {
            called = true;
            return ValueTask.CompletedTask;
        }
        dispatcher.Register(GameSessionState.Play, 0x01, Handler);

        var session = new SessionContext(1, null!);
        session.State = GameSessionState.Play;
        var context = RuntimeLogContext.Empty;

        await dispatcher.DispatchOrIgnoreAsync(session, 0x01, context, ReadOnlyMemory<byte>.Empty);

        Assert.True(called);
    }

    [Fact]
    public async Task Dispatch_UnregisteredRoute_DoesNotThrow()
    {
        var dispatcher = new PlayPacketDispatcher();
        var session = new SessionContext(1, null!);
        var context = RuntimeLogContext.Empty;

        await dispatcher.DispatchOrIgnoreAsync(session, 0x99, context, ReadOnlyMemory<byte>.Empty);
    }

    [Fact]
    public async Task Dispatch_WrongState_DoesNotCallHandler()
    {
        var dispatcher = new PlayPacketDispatcher();
        var called = false;
        ValueTask Handler(SessionContext s, in RuntimeLogContext c, in PlayPacketRouteKey r, ReadOnlyMemory<byte> f, CancellationToken ct)
        {
            called = true;
            return ValueTask.CompletedTask;
        }
        dispatcher.Register(GameSessionState.Play, 0x01, Handler);

        var session = new SessionContext(1, null!); // default state is New
        var context = RuntimeLogContext.Empty;

        await dispatcher.DispatchOrIgnoreAsync(session, 0x01, context, ReadOnlyMemory<byte>.Empty);

        Assert.False(called);
    }

    [Fact]
    public void RouteCount_ReflectsRegisteredHandlers()
    {
        var dispatcher = new PlayPacketDispatcher();
        Assert.Equal(0, dispatcher.RouteCount);

        dispatcher.Register(GameSessionState.Play, 0x01, NoopHandler);
        Assert.Equal(1, dispatcher.RouteCount);

        dispatcher.Register(GameSessionState.Play, 0x02, NoopHandler);
        Assert.Equal(2, dispatcher.RouteCount);
    }

    private static ValueTask NoopHandler(SessionContext s, in RuntimeLogContext c, in PlayPacketRouteKey r, ReadOnlyMemory<byte> f, CancellationToken ct)
        => ValueTask.CompletedTask;
}
