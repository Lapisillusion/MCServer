using GameServer.Application;
using GameServer.Core.Session;
using GameServer.Network;
using GameServer.Tick;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.World;

/// <summary>Budgeted per-player chunk-view reconciliation using tick-owned reusable scratch collections.</summary>
public sealed class ChunkStreamService
{
    private readonly ChunkProvider _chunkProvider;
    private readonly int _maxLoadsPerTick;
    private readonly TickMetrics? _metrics;
    private readonly List<ChunkPos> _desiredOrdered = new(81);
    private readonly HashSet<ChunkPos> _desired = new();
    private readonly List<ChunkPos> _unloadScratch = new(32);

    public ChunkStreamService(
        ChunkProvider chunkProvider,
        GameServerOptions? options = null,
        TickMetrics? metrics = null)
    {
        _chunkProvider = chunkProvider;
        _maxLoadsPerTick = Math.Max(1, (options ?? GameServerOptions.CreateDefault()).MaxChunkLoadsPerTick);
        _metrics = metrics;
    }

    public ChunkViewUpdate InitializeView(SessionContext session)
    {
        if (session.Player == null)
            return ChunkViewUpdate.Empty;

        session.Player.ChunkView.Clear();
        return UpdateView(session, force: true);
    }

    public ChunkViewUpdate UpdateView(SessionContext session, bool force = false)
    {
        var player = session.Player;
        if (player == null || session.Closed || session.CloseRequested)
            return ChunkViewUpdate.Empty;

        var view = player.ChunkView;
        var center = ChunkPos.FromWorldPosition(player.X, player.Z);
        var radius = view.RequestedRadius;
        if (!force && view.Center == center && view.AppliedRadius == radius)
            return ChunkViewUpdate.Empty;

        BuildDesiredPositions(center, radius, _desiredOrdered, _desired);
        var loaded = 0;
        var unloaded = 0;
        var totalBytes = 0;

        foreach (var pos in _desiredOrdered)
        {
            if (loaded >= _maxLoadsPerTick)
                break;
            if (view.Contains(pos))
                continue;

            var column = _chunkProvider.GetOrGenerate(pos);
            var rawData = column.BuildChunkData(primaryBitMask: 0x01, includeBiomes: true);
            var packet = S2CPacketBuilders.BuildChunkData(
                pos.X, pos.Z, groundUp: true, primaryBitMask: 0x01, rawData);
            session.EnqueueOutput(packet);
            view.Add(pos);
            loaded++;
            totalBytes += packet.Length;
        }

        _unloadScratch.Clear();
        foreach (var pos in view.LoadedChunks)
        {
            if (!_desired.Contains(pos))
                _unloadScratch.Add(pos);
        }
        foreach (var pos in _unloadScratch)
        {
            session.EnqueueOutput(S2CPacketBuilders.BuildUnloadChunk(pos.X, pos.Z));
            view.Remove(pos);
            unloaded++;
        }

        view.Center = center;
        view.AppliedRadius = view.LoadedChunks.Count == _desired.Count ? radius : -1;
        _metrics?.AddChunks(loaded, unloaded);

        if (IsDebugEnabled && (loaded > 0 || unloaded > 0))
        {
            Debug("ChunkStream", session.SessionId, session.PlayerName,
                $"View step center={center}, radius={radius}, loaded={loaded}/{_maxLoadsPerTick}, " +
                $"unloaded={unloaded}, resident={view.LoadedChunks.Count}/{_desired.Count}, chunkBytes={totalBytes}");
        }

        return new ChunkViewUpdate(center, radius, loaded, unloaded, totalBytes);
    }

    public void ClearView(SessionContext session, bool enqueueUnload)
    {
        var view = session.Player?.ChunkView;
        if (view == null)
            return;

        if (enqueueUnload)
        {
            foreach (var pos in view.LoadedChunks)
                session.EnqueueOutput(S2CPacketBuilders.BuildUnloadChunk(pos.X, pos.Z));
        }
        view.Clear();
    }

    private static void BuildDesiredPositions(
        ChunkPos center,
        int radius,
        List<ChunkPos> ordered,
        HashSet<ChunkPos> desired)
    {
        ordered.Clear();
        desired.Clear();
        var expected = (radius * 2 + 1) * (radius * 2 + 1);
        ordered.EnsureCapacity(expected);
        desired.EnsureCapacity(expected);

        // Emit center and successive square rings. Within each ring preserve X/Z ordering.
        for (var ring = 0; ring <= radius; ring++)
        {
            for (var x = center.X - ring; x <= center.X + ring; x++)
            for (var z = center.Z - ring; z <= center.Z + ring; z++)
            {
                if (Math.Max(Math.Abs(x - center.X), Math.Abs(z - center.Z)) != ring)
                    continue;
                var pos = new ChunkPos(x, z);
                ordered.Add(pos);
                desired.Add(pos);
            }
        }
    }

    public readonly record struct ChunkViewUpdate(
        ChunkPos Center,
        int Radius,
        int Loaded,
        int Unloaded,
        int ChunkBytes)
    {
        public static ChunkViewUpdate Empty => new(default, 0, 0, 0, 0);
        public bool IsEmpty => Loaded == 0 && Unloaded == 0;
    }
}
