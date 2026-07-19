using GameServer.Application;
using GameServer.Core.Session;
using GameServer.Network;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Tick.Stages;

/// <summary>
/// THE ONLY place that writes to sockets. Drains each session's output queue,
/// batches frames into a single byte array, writes and flushes per session.
///
/// Network optimization: all queued frames per session are merged into one
/// WriteAsync + FlushAsync call, eliminating per-packet syscalls.
/// When compression is enabled, each frame is wrapped with the compressed frame format.
/// </summary>
public sealed class NetworkFlushStage : ITickStage
{
    private readonly SessionRegistry _sessions;
    private readonly GameServerOptions _options;

    public TickPipelineStage Stage => TickPipelineStage.NetworkFlush;

    public NetworkFlushStage(SessionRegistry sessions, GameServerOptions options)
    {
        _sessions = sessions;
        _options = options;
    }

    public async ValueTask ExecuteAsync(long tickNumber, CancellationToken ct)
    {
        foreach (var (sid, session) in _sessions.All)
        {
            if (session.Closed || session.CloseRequested || session.Stream == null) continue;

            var frames = session.DrainAllOutput();
            if (frames.Count == 0) continue;

            // Batch all frames into a single write
            var totalBytes = 0;
            var wrappedFrames = frames;
            if (_options.EnableCompression)
            {
                wrappedFrames = new List<byte[]>(frames.Count);
                foreach (var f in frames)
                {
                    var wrapped = McProtocolWriter.WrapCompressed(f, 256);
                    wrappedFrames.Add(wrapped);
                }
            }

            foreach (var f in wrappedFrames)
                totalBytes += f.Length;

            var batch = new byte[totalBytes];
            var offset = 0;
            foreach (var f in wrappedFrames)
            {
                Buffer.BlockCopy(f, 0, batch, offset, f.Length);
                offset += f.Length;
            }

            await session.Stream.WriteAsync(batch, ct);
            await session.Stream.FlushAsync(ct);

            if (totalBytes > 8192)
                Info("NetworkFlush", sid, session.PlayerName,
                    $"Tick #{tickNumber}: {wrappedFrames.Count} frames, {totalBytes}B batched");
        }
    }
}
