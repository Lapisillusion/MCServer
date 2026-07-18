namespace Common.MC;

/// <summary>
/// Runtime lookup from Minecraft protocol 340 packet ID to human-readable name.
/// Mirrors the pattern of InternalMessageCatalog for internal messages.
/// Zero Serilog dependency — pure data, usable from any project.
/// </summary>
public static class PacketNameResolver
{
    // ── Play C2S (33 packets) ────────────────────────────

    private static readonly Dictionary<int, string> C2SPlay = new()
    {
        [Protocol340Ids.PlayC2S.TeleportConfirm] = nameof(Protocol340Ids.PlayC2S.TeleportConfirm),
        [Protocol340Ids.PlayC2S.TabComplete] = nameof(Protocol340Ids.PlayC2S.TabComplete),
        [Protocol340Ids.PlayC2S.ChatMessage] = nameof(Protocol340Ids.PlayC2S.ChatMessage),
        [Protocol340Ids.PlayC2S.ClientStatus] = nameof(Protocol340Ids.PlayC2S.ClientStatus),
        [Protocol340Ids.PlayC2S.ClientSettings] = nameof(Protocol340Ids.PlayC2S.ClientSettings),
        [Protocol340Ids.PlayC2S.ConfirmTransaction] = nameof(Protocol340Ids.PlayC2S.ConfirmTransaction),
        [Protocol340Ids.PlayC2S.EnchantItem] = nameof(Protocol340Ids.PlayC2S.EnchantItem),
        [Protocol340Ids.PlayC2S.ClickWindow] = nameof(Protocol340Ids.PlayC2S.ClickWindow),
        [Protocol340Ids.PlayC2S.CloseWindow] = nameof(Protocol340Ids.PlayC2S.CloseWindow),
        [Protocol340Ids.PlayC2S.PluginMessage] = nameof(Protocol340Ids.PlayC2S.PluginMessage),
        [Protocol340Ids.PlayC2S.UseEntity] = nameof(Protocol340Ids.PlayC2S.UseEntity),
        [Protocol340Ids.PlayC2S.KeepAlive] = nameof(Protocol340Ids.PlayC2S.KeepAlive),
        [Protocol340Ids.PlayC2S.Player] = nameof(Protocol340Ids.PlayC2S.Player),
        [Protocol340Ids.PlayC2S.PlayerPosition] = nameof(Protocol340Ids.PlayC2S.PlayerPosition),
        [Protocol340Ids.PlayC2S.PlayerPositionAndLook] = nameof(Protocol340Ids.PlayC2S.PlayerPositionAndLook),
        [Protocol340Ids.PlayC2S.PlayerLook] = nameof(Protocol340Ids.PlayC2S.PlayerLook),
        [Protocol340Ids.PlayC2S.VehicleMove] = nameof(Protocol340Ids.PlayC2S.VehicleMove),
        [Protocol340Ids.PlayC2S.SteerBoat] = nameof(Protocol340Ids.PlayC2S.SteerBoat),
        [Protocol340Ids.PlayC2S.CraftRecipeRequest] = nameof(Protocol340Ids.PlayC2S.CraftRecipeRequest),
        [Protocol340Ids.PlayC2S.PlayerAbilities] = nameof(Protocol340Ids.PlayC2S.PlayerAbilities),
        [Protocol340Ids.PlayC2S.PlayerDigging] = nameof(Protocol340Ids.PlayC2S.PlayerDigging),
        [Protocol340Ids.PlayC2S.EntityAction] = nameof(Protocol340Ids.PlayC2S.EntityAction),
        [Protocol340Ids.PlayC2S.SteerVehicle] = nameof(Protocol340Ids.PlayC2S.SteerVehicle),
        [Protocol340Ids.PlayC2S.CraftingBookData] = nameof(Protocol340Ids.PlayC2S.CraftingBookData),
        [Protocol340Ids.PlayC2S.ResourcePackStatus] = nameof(Protocol340Ids.PlayC2S.ResourcePackStatus),
        [Protocol340Ids.PlayC2S.AdvancementTab] = nameof(Protocol340Ids.PlayC2S.AdvancementTab),
        [Protocol340Ids.PlayC2S.HeldItemChange] = nameof(Protocol340Ids.PlayC2S.HeldItemChange),
        [Protocol340Ids.PlayC2S.CreativeInventoryAction] = nameof(Protocol340Ids.PlayC2S.CreativeInventoryAction),
        [Protocol340Ids.PlayC2S.UpdateSign] = nameof(Protocol340Ids.PlayC2S.UpdateSign),
        [Protocol340Ids.PlayC2S.Animation] = nameof(Protocol340Ids.PlayC2S.Animation),
        [Protocol340Ids.PlayC2S.Spectate] = nameof(Protocol340Ids.PlayC2S.Spectate),
        [Protocol340Ids.PlayC2S.PlayerBlockPlacement] = nameof(Protocol340Ids.PlayC2S.PlayerBlockPlacement),
        [Protocol340Ids.PlayC2S.UseItem] = nameof(Protocol340Ids.PlayC2S.UseItem),
    };

    // ── Play S2C (80 packets) ────────────────────────────

    private static readonly Dictionary<int, string> S2CPlay = new()
    {
        [Protocol340Ids.PlayS2C.SpawnObject] = nameof(Protocol340Ids.PlayS2C.SpawnObject),
        [Protocol340Ids.PlayS2C.SpawnExperienceOrb] = nameof(Protocol340Ids.PlayS2C.SpawnExperienceOrb),
        [Protocol340Ids.PlayS2C.SpawnGlobalEntity] = nameof(Protocol340Ids.PlayS2C.SpawnGlobalEntity),
        [Protocol340Ids.PlayS2C.SpawnMob] = nameof(Protocol340Ids.PlayS2C.SpawnMob),
        [Protocol340Ids.PlayS2C.SpawnPainting] = nameof(Protocol340Ids.PlayS2C.SpawnPainting),
        [Protocol340Ids.PlayS2C.SpawnPlayer] = nameof(Protocol340Ids.PlayS2C.SpawnPlayer),
        [Protocol340Ids.PlayS2C.Animation] = nameof(Protocol340Ids.PlayS2C.Animation),
        [Protocol340Ids.PlayS2C.Statistics] = nameof(Protocol340Ids.PlayS2C.Statistics),
        [Protocol340Ids.PlayS2C.BlockBreakAnimation] = nameof(Protocol340Ids.PlayS2C.BlockBreakAnimation),
        [Protocol340Ids.PlayS2C.UpdateBlockEntity] = nameof(Protocol340Ids.PlayS2C.UpdateBlockEntity),
        [Protocol340Ids.PlayS2C.BlockAction] = nameof(Protocol340Ids.PlayS2C.BlockAction),
        [Protocol340Ids.PlayS2C.BlockChange] = nameof(Protocol340Ids.PlayS2C.BlockChange),
        [Protocol340Ids.PlayS2C.BossBar] = nameof(Protocol340Ids.PlayS2C.BossBar),
        [Protocol340Ids.PlayS2C.ServerDifficulty] = nameof(Protocol340Ids.PlayS2C.ServerDifficulty),
        [Protocol340Ids.PlayS2C.TabComplete] = nameof(Protocol340Ids.PlayS2C.TabComplete),
        [Protocol340Ids.PlayS2C.ChatMessage] = nameof(Protocol340Ids.PlayS2C.ChatMessage),
        [Protocol340Ids.PlayS2C.MultiBlockChange] = nameof(Protocol340Ids.PlayS2C.MultiBlockChange),
        [Protocol340Ids.PlayS2C.ConfirmTransaction] = nameof(Protocol340Ids.PlayS2C.ConfirmTransaction),
        [Protocol340Ids.PlayS2C.CloseWindow] = nameof(Protocol340Ids.PlayS2C.CloseWindow),
        [Protocol340Ids.PlayS2C.OpenWindow] = nameof(Protocol340Ids.PlayS2C.OpenWindow),
        [Protocol340Ids.PlayS2C.WindowItems] = nameof(Protocol340Ids.PlayS2C.WindowItems),
        [Protocol340Ids.PlayS2C.WindowProperty] = nameof(Protocol340Ids.PlayS2C.WindowProperty),
        [Protocol340Ids.PlayS2C.SetSlot] = nameof(Protocol340Ids.PlayS2C.SetSlot),
        [Protocol340Ids.PlayS2C.SetCooldown] = nameof(Protocol340Ids.PlayS2C.SetCooldown),
        [Protocol340Ids.PlayS2C.PluginMessage] = nameof(Protocol340Ids.PlayS2C.PluginMessage),
        [Protocol340Ids.PlayS2C.NamedSoundEffect] = nameof(Protocol340Ids.PlayS2C.NamedSoundEffect),
        [Protocol340Ids.PlayS2C.Disconnect] = nameof(Protocol340Ids.PlayS2C.Disconnect),
        [Protocol340Ids.PlayS2C.EntityStatus] = nameof(Protocol340Ids.PlayS2C.EntityStatus),
        [Protocol340Ids.PlayS2C.Explosion] = nameof(Protocol340Ids.PlayS2C.Explosion),
        [Protocol340Ids.PlayS2C.UnloadChunk] = nameof(Protocol340Ids.PlayS2C.UnloadChunk),
        [Protocol340Ids.PlayS2C.ChangeGameState] = nameof(Protocol340Ids.PlayS2C.ChangeGameState),
        [Protocol340Ids.PlayS2C.KeepAlive] = nameof(Protocol340Ids.PlayS2C.KeepAlive),
        [Protocol340Ids.PlayS2C.ChunkData] = nameof(Protocol340Ids.PlayS2C.ChunkData),
        [Protocol340Ids.PlayS2C.Effect] = nameof(Protocol340Ids.PlayS2C.Effect),
        [Protocol340Ids.PlayS2C.Particle] = nameof(Protocol340Ids.PlayS2C.Particle),
        [Protocol340Ids.PlayS2C.JoinGame] = nameof(Protocol340Ids.PlayS2C.JoinGame),
        [Protocol340Ids.PlayS2C.Map] = nameof(Protocol340Ids.PlayS2C.Map),
        [Protocol340Ids.PlayS2C.Entity] = nameof(Protocol340Ids.PlayS2C.Entity),
        [Protocol340Ids.PlayS2C.EntityRelativeMove] = nameof(Protocol340Ids.PlayS2C.EntityRelativeMove),
        [Protocol340Ids.PlayS2C.EntityLookAndRelativeMove] = nameof(Protocol340Ids.PlayS2C.EntityLookAndRelativeMove),
        [Protocol340Ids.PlayS2C.EntityLook] = nameof(Protocol340Ids.PlayS2C.EntityLook),
        [Protocol340Ids.PlayS2C.VehicleMove] = nameof(Protocol340Ids.PlayS2C.VehicleMove),
        [Protocol340Ids.PlayS2C.OpenSignEditor] = nameof(Protocol340Ids.PlayS2C.OpenSignEditor),
        [Protocol340Ids.PlayS2C.CraftRecipeResponse] = nameof(Protocol340Ids.PlayS2C.CraftRecipeResponse),
        [Protocol340Ids.PlayS2C.PlayerAbilities] = nameof(Protocol340Ids.PlayS2C.PlayerAbilities),
        [Protocol340Ids.PlayS2C.CombatEvent] = nameof(Protocol340Ids.PlayS2C.CombatEvent),
        [Protocol340Ids.PlayS2C.PlayerListItem] = nameof(Protocol340Ids.PlayS2C.PlayerListItem),
        [Protocol340Ids.PlayS2C.PlayerPositionAndLook] = nameof(Protocol340Ids.PlayS2C.PlayerPositionAndLook),
        [Protocol340Ids.PlayS2C.UseBed] = nameof(Protocol340Ids.PlayS2C.UseBed),
        [Protocol340Ids.PlayS2C.UnlockRecipes] = nameof(Protocol340Ids.PlayS2C.UnlockRecipes),
        [Protocol340Ids.PlayS2C.DestroyEntities] = nameof(Protocol340Ids.PlayS2C.DestroyEntities),
        [Protocol340Ids.PlayS2C.RemoveEntityEffect] = nameof(Protocol340Ids.PlayS2C.RemoveEntityEffect),
        [Protocol340Ids.PlayS2C.ResourcePackSend] = nameof(Protocol340Ids.PlayS2C.ResourcePackSend),
        [Protocol340Ids.PlayS2C.Respawn] = nameof(Protocol340Ids.PlayS2C.Respawn),
        [Protocol340Ids.PlayS2C.EntityHeadLook] = nameof(Protocol340Ids.PlayS2C.EntityHeadLook),
        [Protocol340Ids.PlayS2C.SelectAdvancementTab] = nameof(Protocol340Ids.PlayS2C.SelectAdvancementTab),
        [Protocol340Ids.PlayS2C.WorldBorder] = nameof(Protocol340Ids.PlayS2C.WorldBorder),
        [Protocol340Ids.PlayS2C.Camera] = nameof(Protocol340Ids.PlayS2C.Camera),
        [Protocol340Ids.PlayS2C.HeldItemChange] = nameof(Protocol340Ids.PlayS2C.HeldItemChange),
        [Protocol340Ids.PlayS2C.DisplayScoreboard] = nameof(Protocol340Ids.PlayS2C.DisplayScoreboard),
        [Protocol340Ids.PlayS2C.EntityMetadata] = nameof(Protocol340Ids.PlayS2C.EntityMetadata),
        [Protocol340Ids.PlayS2C.AttachEntity] = nameof(Protocol340Ids.PlayS2C.AttachEntity),
        [Protocol340Ids.PlayS2C.EntityVelocity] = nameof(Protocol340Ids.PlayS2C.EntityVelocity),
        [Protocol340Ids.PlayS2C.EntityEquipment] = nameof(Protocol340Ids.PlayS2C.EntityEquipment),
        [Protocol340Ids.PlayS2C.SetExperience] = nameof(Protocol340Ids.PlayS2C.SetExperience),
        [Protocol340Ids.PlayS2C.UpdateHealth] = nameof(Protocol340Ids.PlayS2C.UpdateHealth),
        [Protocol340Ids.PlayS2C.ScoreboardObjective] = nameof(Protocol340Ids.PlayS2C.ScoreboardObjective),
        [Protocol340Ids.PlayS2C.SetPassengers] = nameof(Protocol340Ids.PlayS2C.SetPassengers),
        [Protocol340Ids.PlayS2C.Teams] = nameof(Protocol340Ids.PlayS2C.Teams),
        [Protocol340Ids.PlayS2C.UpdateScore] = nameof(Protocol340Ids.PlayS2C.UpdateScore),
        [Protocol340Ids.PlayS2C.SpawnPosition] = nameof(Protocol340Ids.PlayS2C.SpawnPosition),
        [Protocol340Ids.PlayS2C.TimeUpdate] = nameof(Protocol340Ids.PlayS2C.TimeUpdate),
        [Protocol340Ids.PlayS2C.Title] = nameof(Protocol340Ids.PlayS2C.Title),
        [Protocol340Ids.PlayS2C.SoundEffect] = nameof(Protocol340Ids.PlayS2C.SoundEffect),
        [Protocol340Ids.PlayS2C.PlayerListHeaderAndFooter] = nameof(Protocol340Ids.PlayS2C.PlayerListHeaderAndFooter),
        [Protocol340Ids.PlayS2C.CollectItem] = nameof(Protocol340Ids.PlayS2C.CollectItem),
        [Protocol340Ids.PlayS2C.EntityTeleport] = nameof(Protocol340Ids.PlayS2C.EntityTeleport),
        [Protocol340Ids.PlayS2C.Advancements] = nameof(Protocol340Ids.PlayS2C.Advancements),
        [Protocol340Ids.PlayS2C.EntityProperties] = nameof(Protocol340Ids.PlayS2C.EntityProperties),
        [Protocol340Ids.PlayS2C.EntityEffect] = nameof(Protocol340Ids.PlayS2C.EntityEffect),
    };

    // ── Login ────────────────────────────────────────────

    private static readonly Dictionary<int, string> Login = new()
    {
        [Protocol340Ids.Login.C2S_LoginStart] = "LoginStart",
        [Protocol340Ids.Login.C2S_EncryptionResponse] = "EncryptionResponse",
        [Protocol340Ids.Login.S2C_Disconnect] = "LoginDisconnect",
        [Protocol340Ids.Login.S2C_EncryptionRequest] = "EncryptionRequest",
        [Protocol340Ids.Login.S2C_LoginSuccess] = "LoginSuccess",
        [Protocol340Ids.Login.S2C_SetCompression] = "SetCompression",
    };

    // ── Handshake ────────────────────────────────────────

    private static readonly Dictionary<int, string> Handshake = new()
    {
        [Protocol340Ids.Handshake.C2S_Handshake] = "Handshake",
        [Protocol340Ids.Handshake.C2S_LegacyPing] = "LegacyPing",
    };

    // ── Status ───────────────────────────────────────────

    private static readonly Dictionary<int, string> Status = new()
    {
        [Protocol340Ids.Status.C2S_Request] = "StatusRequest",
        [Protocol340Ids.Status.C2S_Ping] = "StatusPing",
        [Protocol340Ids.Status.S2C_Response] = "StatusResponse",
        [Protocol340Ids.Status.S2C_Pong] = "StatusPong",
    };

    // ── Public API ───────────────────────────────────────

    /// <summary>Get the human-readable name for a Play C2S packet ID.</summary>
    public static bool TryGetC2SPlayName(int packetId, out string? name)
        => C2SPlay.TryGetValue(packetId, out name);

    /// <summary>Get the human-readable name for a Play S2C packet ID.</summary>
    public static bool TryGetS2CPlayName(int packetId, out string? name)
        => S2CPlay.TryGetValue(packetId, out name);

    /// <summary>
    /// Get the Play-state packet name with direction hint.
    /// Returns the name if found, otherwise the hex fallback like "0xNN".
    /// </summary>
    public static string GetPlayPacketName(int packetId, bool isC2S)
    {
        if (isC2S && C2SPlay.TryGetValue(packetId, out var c2sName))
            return c2sName;
        if (!isC2S && S2CPlay.TryGetValue(packetId, out var s2cName))
            return s2cName;
        return $"0x{packetId:X2}";
    }

    /// <summary>
    /// Generic lookup across all states. Returns the human-readable name
    /// if found in any dictionary, otherwise the hex fallback.
    /// </summary>
    public static string GetNameOrDefault(int packetId)
    {
        if (C2SPlay.TryGetValue(packetId, out var name)) return name;
        if (S2CPlay.TryGetValue(packetId, out name)) return name;
        if (Login.TryGetValue(packetId, out name)) return name;
        if (Handshake.TryGetValue(packetId, out name)) return name;
        if (Status.TryGetValue(packetId, out name)) return name;
        return $"0x{packetId:X2}";
    }
}
