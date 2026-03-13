using GameServer.Core.Diagnostics;
using GameServer.Core.Session;

namespace GameServer.Core.Dispatch;

public delegate ValueTask PlayPacketHandler(
    SessionContext session,
    in RuntimeLogContext context,
    in PlayPacketRouteKey route,
    ReadOnlyMemory<byte> framePayload,
    CancellationToken cancellationToken);
