using Serilog;
using Serilog.Events;

namespace GameServer.Core.Diagnostics;

/// <summary>
/// Structured logger for GameServer — backed by Serilog (same lib as GateWay).
/// Signature-compatible with existing call sites; structured fields emitted as
/// Serilog properties following the layered hierarchy:
///   L0: Service (via Enrich.WithProperty in Program.cs)
///   L1: Module / Component
///   L2: SessionId
///   L3: PlayerId, EntityId, Dimension, Gamemode
///   L4: PacketId, PacketName, Direction
///   L5: TickNumber, Stage
/// </summary>
public static class GameLogger
{
    // ── Full-context overloads (legacy, kept for backward compat) ──

    public static void Info(string component, long sessionId, string playerId, string packetId, string message)
        => WriteLegacy(LogEventLevel.Information, component, sessionId, playerId, packetId, message);

    public static void Warn(string component, long sessionId, string playerId, string packetId, string message)
        => WriteLegacy(LogEventLevel.Warning, component, sessionId, playerId, packetId, message);

    public static void Error(string component, long sessionId, string playerId, string packetId, string message)
        => WriteLegacy(LogEventLevel.Error, component, sessionId, playerId, packetId, message);

    // ── Session-only overloads (legacy) ─────────────────────

    public static void Info(string component, long sessionId, string message)
        => WriteLegacy(LogEventLevel.Information, component, sessionId, null, null, message);

    public static void Warn(string component, long sessionId, string message)
        => WriteLegacy(LogEventLevel.Warning, component, sessionId, null, null, message);

    public static void Error(string component, long sessionId, string message)
        => WriteLegacy(LogEventLevel.Error, component, sessionId, null, null, message);

    // ── Component-only overloads (legacy) ───────────────────

    public static void Info(string component, string message)
        => WriteLegacy(LogEventLevel.Information, component, null, null, null, message);

    public static void Warn(string component, string message)
        => WriteLegacy(LogEventLevel.Warning, component, null, null, null, message);

    public static void Error(string component, string message)
        => WriteLegacy(LogEventLevel.Error, component, null, null, null, message);

    // ── RuntimeLogContext-based overloads (new) ─────────────
    // These emit the full layered hierarchy as Serilog properties.

    public static void Info(string module, in RuntimeLogContext ctx, string message, params object[] args)
        => Write(LogEventLevel.Information, module, ctx, message, args);

    public static void Warn(string module, in RuntimeLogContext ctx, string message, params object[] args)
        => Write(LogEventLevel.Warning, module, ctx, message, args);

    public static void Error(string module, in RuntimeLogContext ctx, string message, params object[] args)
        => Write(LogEventLevel.Error, module, ctx, message, args);

    // ── Internal write (legacy path) ────────────────────────
    // Now emits Component as a Serilog property (not just in template).

    private static void WriteLegacy(LogEventLevel level, string component, long? sessionId, string? playerId, string? packetId, string message)
    {
        var logger = Log.Logger;
        logger = logger.ForContext("Component", component);
        logger = logger.ForContext("Module", component);
        if (sessionId.HasValue) logger = logger.ForContext("SessionId", sessionId.Value);
        if (!string.IsNullOrEmpty(playerId)) logger = logger.ForContext("PlayerId", playerId);
        if (!string.IsNullOrEmpty(packetId)) logger = logger.ForContext("PacketId", packetId);

        logger.Write(level, "[{Component}] {Message}", component, message);
    }

    // ── Internal write (RuntimeLogContext path) ─────────────
    // Emits the full layered hierarchy conditionally.

    private static void Write(LogEventLevel level, string module, in RuntimeLogContext ctx, string message, params object[] args)
    {
        var logger = Log.Logger;

        // L1: Module / Component
        logger = logger.ForContext("Module", module);
        logger = logger.ForContext("Component", module);

        // L2: Session
        if (!string.IsNullOrEmpty(ctx.SessionId))
            logger = logger.ForContext("SessionId", ctx.SessionId);

        // L3: Entity
        if (!string.IsNullOrEmpty(ctx.PlayerId))
            logger = logger.ForContext("PlayerId", ctx.PlayerId);
        if (ctx.EntityId >= 0)
            logger = logger.ForContext("EntityId", ctx.EntityId);
        if (ctx.Dimension != int.MinValue)
            logger = logger.ForContext("Dimension", ctx.Dimension);
        if (ctx.Gamemode != byte.MaxValue)
            logger = logger.ForContext("Gamemode", ctx.Gamemode);

        // L4: Packet / Message
        if (!string.IsNullOrEmpty(ctx.PacketId))
            logger = logger.ForContext("PacketId", ctx.PacketId);
        if (!string.IsNullOrEmpty(ctx.PacketName))
            logger = logger.ForContext("PacketName", ctx.PacketName);
        if (!string.IsNullOrEmpty(ctx.Direction))
            logger = logger.ForContext("Direction", ctx.Direction);

        // L5: Temporal
        if (ctx.TickId >= 0)
            logger = logger.ForContext("TickNumber", ctx.TickId);
        if (!string.IsNullOrEmpty(ctx.Stage))
            logger = logger.ForContext("Stage", ctx.Stage);

        // Message template — [Module] is string-interpolated to avoid
        // Serilog consuming a positional arg for {Module} (which is already
        // set via ForContext). Without this, args[0] would overwrite Module
        // and shift all subsequent property mappings by one position.
        if (args is { Length: > 0 })
            logger.Write(level, $"[{module}] " + message, args);
        else
            logger.Write(level, $"[{module}] {message}");
    }
}
