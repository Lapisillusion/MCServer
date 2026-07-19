using GameServer.Core.Session;
using GameServer.Network;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.World;

/// <summary>
/// Reconciles each player's desired chunk view with the chunks already sent to that client.
/// Chunk data is generated/cached by ChunkProvider and all frames are queued for NetworkFlush.
/// </summary>
public sealed class ChunkStreamService
{
    private readonly ChunkProvider _chunkProvider;

    public ChunkStreamService(ChunkProvider chunkProvider)
    {
        _chunkProvider = chunkProvider;
    }

    public ChunkViewUpdate InitializeView(SessionContext session)
    {
        if (session.Player == null)
            return ChunkViewUpdate.Empty;

        session.Player.ChunkView.Clear();
        return UpdateView(session, force: true);
    }

    /// <summary>Updates the player's view only when its center chunk or requested radius changed.</summary>
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

        var desiredOrdered = BuildDesiredPositions(center, radius);
        var desired = desiredOrdered.ToHashSet();
        var loaded = 0;
        var unloaded = 0;
        var totalBytes = 0;

        // Load first so crossing a border does not briefly expose void at the leading edge.
        foreach (var pos in desiredOrdered)
        {
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

        foreach (var pos in view.LoadedChunks.Where(pos => !desired.Contains(pos)).ToArray())
        {
            session.EnqueueOutput(S2CPacketBuilders.BuildUnloadChunk(pos.X, pos.Z));
            view.Remove(pos);
            unloaded++;
        }

        view.Center = center;
        view.AppliedRadius = radius;

        if (loaded > 0 || unloaded > 0)
        {
            Info("ChunkStream", session.SessionId, session.PlayerName,
                $"View reconciled center={center}, radius={radius}, loaded={loaded}, unloaded={unloaded}, resident={view.LoadedChunks.Count}, chunkBytes={totalBytes}");
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

    private static List<ChunkPos> BuildDesiredPositions(ChunkPos center, int radius)
    {
        var result = new List<ChunkPos>((radius * 2 + 1) * (radius * 2 + 1));
        for (var dz = -radius; dz <= radius; dz++)
        {
            for (var dx = -radius; dx <= radius; dx++)
                result.Add(new ChunkPos(center.X + dx, center.Z + dz));
        }

        // Center and inner rings are sent first for faster first-paint on the client.
        result.Sort((a, b) =>
        {
            var ar = Math.Max(Math.Abs(a.X - center.X), Math.Abs(a.Z - center.Z));
            var br = Math.Max(Math.Abs(b.X - center.X), Math.Abs(b.Z - center.Z));
            var ring = ar.CompareTo(br);
            if (ring != 0) return ring;
            var x = a.X.CompareTo(b.X);
            return x != 0 ? x : a.Z.CompareTo(b.Z);
        });
        return result;
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
