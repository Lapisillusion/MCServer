namespace GameServer.Tick;

/// <summary>Enqueued C2S frame — struct avoids heap allocation per packet.</summary>
public readonly record struct InputFrame(int PacketId, byte[] Frame, long SessionId);
