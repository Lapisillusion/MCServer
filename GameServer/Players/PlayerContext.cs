using GameServer.Inventory;

namespace GameServer.Players;

public sealed class PlayerContext
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public byte Gamemode { get; set; }
    public int Dimension { get; set; }
    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 4.0;
    public double Z { get; set; } = 0.5;
    public float Yaw { get; set; }
    public float Pitch { get; set; } = 90f;
    public bool OnGround { get; set; }
    public long LastKeepAliveId { get; set; }
    public bool ChunksSent { get; set; }
    public int TeleportId { get; set; } = 1;
    public HotbarInventory Hotbar { get; } = HotbarInventory.CreateDefault();
}
