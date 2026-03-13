using GameServer.Core.Diagnostics;
using GameServer.Core.Session;

namespace GameServer.Core.Dispatch;

public sealed class PlayPacketDispatcher
{
    private readonly Dictionary<PlayPacketRouteKey, PlayPacketHandler> _handlers = new();

    public int RouteCount => _handlers.Count;

    public void Register(GameSessionState state, int packetId, PlayPacketHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var route = new PlayPacketRouteKey(state, packetId);
        if (!_handlers.TryAdd(route, handler))
            throw new InvalidOperationException($"Duplicate play route: state={state}, packetId=0x{packetId:X2}");
    }

    public bool ContainsRoute(PlayPacketRouteKey route) => _handlers.ContainsKey(route);

    public bool TryGetHandler(GameSessionState state, int packetId, out PlayPacketRouteKey route, out PlayPacketHandler? handler)
    {
        route = new PlayPacketRouteKey(state, packetId);
        if (_handlers.TryGetValue(route, out var value))
        {
            handler = value;
            return true;
        }

        handler = null;
        return false;
    }

    public ValueTask DispatchOrIgnoreAsync(
        SessionContext session,
        int packetId,
        in RuntimeLogContext context,
        ReadOnlyMemory<byte> framePayload,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetHandler(session.State, packetId, out var route, out var handler) || handler == null)
            return ValueTask.CompletedTask;

        return handler(session, context, route, framePayload, cancellationToken);
    }
}
