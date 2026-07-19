using GameServer.Application;
using GameServer.Core.Session;
using GameServer.Network;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Tick.Stages;

/// <summary>
/// Converts queued packets into bounded ArrayPool-backed batches. Socket I/O is performed by the
/// per-session send pump, so a slow client can no longer stall the tick thread.
/// </summary>
public sealed class NetworkFlushStage : ITickStage
{
    private readonly SessionRegistry _sessions;
    private readonly GameServerOptions _options;
    private readonly TickMetrics? _metrics;
    private readonly Dictionary<long, PendingCompressedFrame> _compressedPending = new();

    public TickPipelineStage Stage => TickPipelineStage.NetworkFlush;

    public NetworkFlushStage(
        SessionRegistry sessions,
        GameServerOptions options,
        TickMetrics? metrics = null)
    {
        _sessions = sessions;
        _options = options;
        _metrics = metrics;
    }

    public ValueTask ExecuteAsync(long tickNumber, CancellationToken ct)
    {
        var maxBatchBytes = Math.Max(1024, _options.MaxNetworkBatchBytes);
        var maxPendingBytes = Math.Max(maxBatchBytes, _options.MaxPendingSendBytes);

        foreach (var (sid, session) in _sessions.All)
        {
            if (session.Closed || session.CloseRequested || session.Stream == null)
            {
                _compressedPending.Remove(sid);
                continue;
            }

            if (session.OutgoingBytes == 0 && !_compressedPending.ContainsKey(sid))
                continue;

            var compressedPendingBytes = _compressedPending.TryGetValue(sid, out var pendingFrame)
                ? pendingFrame.Remaining
                : 0;
            if (session.TotalPendingOutputBytes + compressedPendingBytes > maxPendingBytes)
            {
                Warn("NetworkFlush", sid, session.PlayerName,
                    $"Output backlog exceeded {maxPendingBytes}B (queued={session.OutgoingBytes}B, send={session.PendingSendBytes}B)");
                session.RequestClose("Output backlog limit exceeded");
                continue;
            }

            OutboundBatch? batch = null;
            try
            {
                batch = OutboundBatch.Rent(maxBatchBytes);
                var completedFrames = 0;
                var bytes = _options.EnableCompression
                    ? CopyCompressedOutput(session, sid, batch.WritableSpan[..maxBatchBytes], ref completedFrames)
                    : session.CopyOutputTo(batch.WritableSpan[..maxBatchBytes], out completedFrames);

                if (bytes == 0)
                {
                    batch.Dispose();
                    continue;
                }

                batch.SetLength(bytes);
                if (!session.TryQueueSendBatch(batch, maxPendingBytes))
                {
                    batch.Dispose();
                    session.RequestClose("Async send queue backlog limit exceeded");
                    Warn("NetworkFlush", sid, session.PlayerName,
                        $"Could not queue {bytes}B batch; pending={session.PendingSendBytes}B");
                    continue;
                }

                batch = null; // ownership transferred to SessionContext/send pump
                _metrics?.AddOutput(completedFrames, bytes);
                if (IsDebugEnabled && bytes >= 32 * 1024)
                    Debug("NetworkFlush", sid, session.PlayerName,
                        $"Tick #{tickNumber}: queued async batch frames={completedFrames}, bytes={bytes}, pending={session.PendingSendBytes}B");
            }
            finally
            {
                batch?.Dispose();
            }
        }

        return ValueTask.CompletedTask;
    }

    private int CopyCompressedOutput(
        SessionContext session,
        long sessionId,
        Span<byte> destination,
        ref int completedFrames)
    {
        var written = 0;
        if (_compressedPending.TryGetValue(sessionId, out var pending))
        {
            written += pending.CopyTo(destination);
            if (pending.IsComplete)
            {
                _compressedPending.Remove(sessionId);
                completedFrames++;
            }
            else
            {
                return written;
            }
        }

        while (written < destination.Length && session.TryDequeueOutput(out var frame))
        {
            var wrapped = McProtocolWriter.WrapCompressed(frame, 256);
            var next = new PendingCompressedFrame(wrapped);
            written += next.CopyTo(destination[written..]);
            if (next.IsComplete)
            {
                completedFrames++;
                continue;
            }

            _compressedPending[sessionId] = next;
            break;
        }

        return written;
    }

    private sealed class PendingCompressedFrame
    {
        private readonly byte[] _data;
        private int _offset;

        public PendingCompressedFrame(byte[] data) => _data = data;
        public int Remaining => _data.Length - _offset;
        public bool IsComplete => _offset == _data.Length;

        public int CopyTo(Span<byte> destination)
        {
            var count = Math.Min(destination.Length, _data.Length - _offset);
            _data.AsSpan(_offset, count).CopyTo(destination);
            _offset += count;
            return count;
        }
    }
}
