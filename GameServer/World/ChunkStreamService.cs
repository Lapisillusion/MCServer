using GameServer.Core.Session;
using GameServer.Network;
using GameServer.World;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.World;

/// <summary>
/// Handles chunk streaming to players — decides which chunks to send,
/// builds ChunkData frames, and enqueues them via the tick pipeline.
///
/// Known client behavior (Minecraft 1.12.2): some chunks may not render
/// immediately after first load. The chunk data is correct and blocks are
/// solid (player does not fall through, crosshair can target blocks).
/// Mining any block in the affected chunk triggers a BlockChange packet,
/// which causes the client to rebuild the chunk mesh and render correctly.
/// This is a vanilla client rendering-pipeline quirk, not a server bug.
/// </summary>
public sealed class ChunkStreamService
{
    private readonly ChunkProvider _chunkProvider;

    public ChunkStreamService(ChunkProvider chunkProvider)
    {
        _chunkProvider = chunkProvider;
    }

    /// <summary>
    /// Generate and send a (2*radius+1)^2 chunk grid centered on (centerX, centerZ).
    /// All chunks use groundUp=true because Minecraft 1.12.2 clients require full
    /// biome data on first load.
    /// </summary>
    public void SendSpawnGrid(SessionContext session, int centerX, int centerZ, int radius)
    {
        var columns = _chunkProvider.GetOrGenerateSpawnGrid(centerX, centerZ, radius);
        var totalBytes = 0;

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var rawData = column.BuildChunkData(primaryBitMask: 0x01, includeBiomes: true);
            var chunkPacket = S2CPacketBuilders.BuildChunkData(
                column.Position.X, column.Position.Z,
                groundUp: true, primaryBitMask: 0x01, rawData);

            session.EnqueueOutput(chunkPacket);
            totalBytes += chunkPacket.Length;

            Info("ChunkStream", session.SessionId,
                $"  [{i + 1}/{columns.Count}] Chunk({column.Position.X,3},{column.Position.Z,3}) frame={chunkPacket.Length}B raw={rawData.Length}B groundUp=true");
            LogChunkHexDump(session.SessionId, column.Position.X, column.Position.Z, i + 1, columns.Count, chunkPacket);
        }

        Info("ChunkStream", session.SessionId,
            $"Chunk batch enqueued, {columns.Count} chunks, {totalBytes} total bytes sent");
    }

    // ── Hex dump diagnostics ──────────────────────────────

    private static void LogChunkHexDump(long sessionId, int cx, int cz, int index, int total, byte[] frame)
    {
        const int dumpLen = 48;
        var len = Math.Min(frame.Length, dumpLen);
        var hexChars = new char[len * 3];
        for (var i = 0; i < len; i++)
        {
            var b = frame[i];
            hexChars[i * 3] = ToHexUpper(b >> 4);
            hexChars[i * 3 + 1] = ToHexUpper(b & 0xF);
            hexChars[i * 3 + 2] = ' ';
        }

        Info("ChunkStream", sessionId,
            $"[{index}/{total}] Chunk({cx},{cz}) hex[{len}B]: {new string(hexChars)}");
    }

    private static char ToHexUpper(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
}
