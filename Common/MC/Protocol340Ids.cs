namespace Common.MC;

/// <summary>
/// Minecraft Java 1.12.2 (protocol 340) packet ID constants,
/// organized by protocol state and direction.
/// </summary>
public static class Protocol340Ids
{
    public const int ProtocolVersion = 340;

    // ── Handshake ──────────────────────────────────────
    public static class Handshake
    {
        public const int C2S_Handshake = 0x00;
        public const int C2S_LegacyPing = 0xFE;
    }

    // ── Status ─────────────────────────────────────────
    public static class Status
    {
        public const int C2S_Request = 0x00;
        public const int C2S_Ping = 0x01;
        public const int S2C_Response = 0x00;
        public const int S2C_Pong = 0x01;
    }

    // ── Login ──────────────────────────────────────────
    public static class Login
    {
        public const int C2S_LoginStart = 0x00;
        public const int C2S_EncryptionResponse = 0x01;

        public const int S2C_Disconnect = 0x00;
        public const int S2C_EncryptionRequest = 0x01;
        public const int S2C_LoginSuccess = 0x02;
        public const int S2C_SetCompression = 0x03;
    }

    // ── Play — Clientbound (S2C) ──────────────────────
    public static class PlayS2C
    {
        public const int SpawnObject = 0x00;
        public const int SpawnExperienceOrb = 0x01;
        public const int SpawnGlobalEntity = 0x02;
        public const int SpawnMob = 0x03;
        public const int SpawnPainting = 0x04;
        public const int SpawnPlayer = 0x05;
        public const int Animation = 0x06;
        public const int Statistics = 0x07;
        public const int BlockBreakAnimation = 0x08;
        public const int UpdateBlockEntity = 0x09;
        public const int BlockAction = 0x0A;
        public const int BlockChange = 0x0B;
        public const int BossBar = 0x0C;
        public const int ServerDifficulty = 0x0D;
        public const int TabComplete = 0x0E;
        public const int ChatMessage = 0x0F;
        public const int MultiBlockChange = 0x10;
        public const int ConfirmTransaction = 0x11;
        public const int CloseWindow = 0x12;
        public const int OpenWindow = 0x13;
        public const int WindowItems = 0x14;
        public const int WindowProperty = 0x15;
        public const int SetSlot = 0x16;
        public const int SetCooldown = 0x17;
        public const int PluginMessage = 0x18;
        public const int NamedSoundEffect = 0x19;
        public const int Disconnect = 0x1A;
        public const int EntityStatus = 0x1B;
        public const int Explosion = 0x1C;
        public const int UnloadChunk = 0x1D;
        public const int ChangeGameState = 0x1E;
        public const int KeepAlive = 0x1F;
        public const int ChunkData = 0x20;
        public const int Effect = 0x21;
        public const int Particle = 0x22;
        public const int JoinGame = 0x23;
        public const int Map = 0x24;
        public const int Entity = 0x25;
        public const int EntityRelativeMove = 0x26;
        public const int EntityLookAndRelativeMove = 0x27;
        public const int EntityLook = 0x28;
        public const int VehicleMove = 0x29;
        public const int OpenSignEditor = 0x2A;
        public const int CraftRecipeResponse = 0x2B;
        public const int PlayerAbilities = 0x2C;
        public const int CombatEvent = 0x2D;
        public const int PlayerListItem = 0x2E;
        public const int PlayerPositionAndLook = 0x2F;
        public const int UseBed = 0x30;
        public const int UnlockRecipes = 0x31;
        public const int DestroyEntities = 0x32;
        public const int RemoveEntityEffect = 0x33;
        public const int ResourcePackSend = 0x34;
        public const int Respawn = 0x35;
        public const int EntityHeadLook = 0x36;
        public const int SelectAdvancementTab = 0x37;
        public const int WorldBorder = 0x38;
        public const int Camera = 0x39;
        public const int HeldItemChange = 0x3A;
        public const int DisplayScoreboard = 0x3B;
        public const int EntityMetadata = 0x3C;
        public const int AttachEntity = 0x3D;
        public const int EntityVelocity = 0x3E;
        public const int EntityEquipment = 0x3F;
        public const int SetExperience = 0x40;
        public const int UpdateHealth = 0x41;
        public const int ScoreboardObjective = 0x42;
        public const int SetPassengers = 0x43;
        public const int Teams = 0x44;
        public const int UpdateScore = 0x45;
        public const int SpawnPosition = 0x46;
        public const int TimeUpdate = 0x47;
        public const int Title = 0x48;
        public const int SoundEffect = 0x49;
        public const int PlayerListHeaderAndFooter = 0x4A;
        public const int CollectItem = 0x4B;
        public const int EntityTeleport = 0x4C;
        public const int Advancements = 0x4D;
        public const int EntityProperties = 0x4E;
        public const int EntityEffect = 0x4F;
    }

    // ── Play — Serverbound (C2S) ──────────────────────
    public static class PlayC2S
    {
        public const int TeleportConfirm = 0x00;
        public const int TabComplete = 0x01;
        public const int ChatMessage = 0x02;
        public const int ClientStatus = 0x03;
        public const int ClientSettings = 0x04;
        public const int ConfirmTransaction = 0x05;
        public const int EnchantItem = 0x06;
        public const int ClickWindow = 0x07;
        public const int CloseWindow = 0x08;
        public const int PluginMessage = 0x09;
        public const int UseEntity = 0x0A;
        public const int KeepAlive = 0x0B;
        public const int Player = 0x0C;
        public const int PlayerPosition = 0x0D;
        public const int PlayerPositionAndLook = 0x0E;
        public const int PlayerLook = 0x0F;
        public const int VehicleMove = 0x10;
        public const int SteerBoat = 0x11;
        public const int CraftRecipeRequest = 0x12;
        public const int PlayerAbilities = 0x13;
        public const int PlayerDigging = 0x14;
        public const int EntityAction = 0x15;
        public const int SteerVehicle = 0x16;
        public const int CraftingBookData = 0x17;
        public const int ResourcePackStatus = 0x18;
        public const int AdvancementTab = 0x19;
        public const int HeldItemChange = 0x1A;
        public const int CreativeInventoryAction = 0x1B;
        public const int UpdateSign = 0x1C;
        public const int Animation = 0x1D;
        public const int Spectate = 0x1E;
        public const int PlayerBlockPlacement = 0x1F;
        public const int UseItem = 0x20;
    }
}
