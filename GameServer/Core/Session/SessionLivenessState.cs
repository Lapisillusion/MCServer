namespace GameServer.Core.Session;

/// <summary>
/// Tick-owned KeepAlive state for a single Gateway-backed client session.
/// The input and simulation stages run serially, so no additional locking is required.
/// </summary>
public sealed class SessionLivenessState
{
    public long? PendingKeepAliveId { get; private set; }
    public DateTimeOffset? SentAtUtc { get; private set; }
    public DateTimeOffset? NextSendUtc { get; private set; }
    public long? LastAcknowledgedId { get; private set; }
    public DateTimeOffset? LastAcknowledgedUtc { get; private set; }
    public TimeSpan? LastRtt { get; private set; }

    public void EnsureScheduled(DateTimeOffset now, TimeSpan initialDelay)
    {
        if (NextSendUtc == null)
            NextSendUtc = now + initialDelay;
    }

    public bool ShouldSend(DateTimeOffset now)
        => PendingKeepAliveId == null && NextSendUtc is { } next && now >= next;

    public void MarkSent(long keepAliveId, DateTimeOffset now, TimeSpan interval)
    {
        if (PendingKeepAliveId != null)
            throw new InvalidOperationException("A KeepAlive response is already pending.");

        PendingKeepAliveId = keepAliveId;
        SentAtUtc = now;
        NextSendUtc = now + interval;
    }

    public bool TryAcknowledge(long keepAliveId, DateTimeOffset now, out TimeSpan rtt)
    {
        rtt = TimeSpan.Zero;
        if (PendingKeepAliveId != keepAliveId || SentAtUtc == null)
            return false;

        rtt = now >= SentAtUtc.Value ? now - SentAtUtc.Value : TimeSpan.Zero;
        PendingKeepAliveId = null;
        SentAtUtc = null;
        LastAcknowledgedId = keepAliveId;
        LastAcknowledgedUtc = now;
        LastRtt = rtt;
        return true;
    }

    public bool IsTimedOut(DateTimeOffset now, TimeSpan timeout)
        => PendingKeepAliveId != null && SentAtUtc is { } sentAt && now - sentAt >= timeout;
}
