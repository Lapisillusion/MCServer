using GameServer.Core.Diagnostics;
using GameServer.Network.Internal;

namespace GameServer.Core.Dispatch;

public sealed class InternalMessageDispatcher
{
    private readonly Dictionary<DispatchRouteKey, DispatchHandler> _handlers = new();

    public int RouteCount => _handlers.Count;

    public void Register(GameSessionState state, InternalMessageType messageType, DispatchHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var key = new DispatchRouteKey(state, messageType);
        if (!_handlers.TryAdd(key, handler))
        {
            throw new InvalidOperationException(
                $"Duplicate dispatch route: state={state}, messageType={messageType}");
        }
    }

    public bool ContainsRoute(DispatchRouteKey key) => _handlers.ContainsKey(key);

    public ValueTask DispatchAsync(
        GameSessionState state,
        InternalMessageType messageType,
        in RuntimeLogContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        var key = new DispatchRouteKey(state, messageType);
        if (!_handlers.TryGetValue(key, out var handler))
        {
            throw new KeyNotFoundException(
                $"No dispatch route: state={state}, messageType={messageType}");
        }

        return handler(context, key, payload, cancellationToken);
    }
}
