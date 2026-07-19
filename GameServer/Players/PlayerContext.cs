using GameServer.Entities;
using GameServer.Inventory;
using GameServer.World;

namespace GameServer.Players;

public sealed class PlayerContext : EntityState
{
    public PlayerContext()
    {
        X = 0.5;
        Y = 4.0;
        Z = 0.5;
        Pitch = 90f;
    }
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public override double Width => 0.6;
    public override double Height => 1.8;
    public byte Gamemode { get; set; }
    public bool ChunksSent { get; set; }
    public int TeleportId { get; set; } = 1;
    public bool AwaitingTeleportConfirm { get; set; }
    public long LastClientPositionTick { get; set; } = -1;
    public HotbarInventory Hotbar { get; } = HotbarInventory.CreateDefault();
    public PlayerChunkView ChunkView { get; } = new();
}
