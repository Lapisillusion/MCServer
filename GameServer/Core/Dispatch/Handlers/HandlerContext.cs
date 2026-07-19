using GameServer.Application;
using GameServer.Core.Session;
using GameServer.World;

namespace GameServer.Core.Dispatch.Handlers;

internal static class HandlerContext
{
    internal static ChunkProvider ChunkProvider { get; private set; } = null!;
    internal static SessionRegistry Sessions { get; private set; } = null!;
    internal static ChunkStreamService ChunkStream { get; private set; } = null!;
    internal static GameServerOptions Options { get; private set; } = null!;

    internal static void Initialize(
        ChunkProvider chunkProvider,
        SessionRegistry sessions,
        ChunkStreamService chunkStream,
        GameServerOptions options)
    {
        ChunkProvider = chunkProvider;
        Sessions = sessions;
        ChunkStream = chunkStream;
        Options = options;
    }
}
