using GameServer.Core.Diagnostics;
using GameServer.Network.Internal;

namespace GameServer.Core.Dispatch;

public delegate ValueTask DispatchHandler(
    in RuntimeLogContext context,
    in DispatchRouteKey route,
    ReadOnlyMemory<byte> payload,
    CancellationToken cancellationToken);
