// RateLimiter.cs

using System.Collections.Concurrent;
using System.Diagnostics;

namespace GateWay;

/// <summary>
/// 限流器
/// </summary>
public sealed class RateLimiter
{
    private readonly long _cleanupIntervalTicks;

    private readonly TokenBucket _globalConn;
    private readonly ConcurrentDictionary<uint, TokenBucket> _perIpConn = new();
    private readonly ConcurrentDictionary<uint, TokenBucket> _perIpLogin = new();
    private readonly double _ticksPerSec = Stopwatch.Frequency;

    // 清理
    private long _lastCleanupTicks;

    public RateLimiter()
    {
        var now = Stopwatch.GetTimestamp();

        // 起步阈值
        _globalConn = new TokenBucket(200, 400, now);
        _cleanupIntervalTicks = (long)(_ticksPerSec * 60);
        _lastCleanupTicks = now;
    }

    public bool TryAcceptConnection(uint ipV4)
    {
        var now = Stopwatch.GetTimestamp();

        if (!_globalConn.TryConsume(1, now, _ticksPerSec))
            return false;

        var b = _perIpConn.GetOrAdd(ipV4, _ => new TokenBucket(3, 10, now));
        if (!b.TryConsume(1, now, _ticksPerSec))
            return false;

        CleanupIfNeeded(now);
        return true;
    }

    public bool TryAcceptLogin(uint ipV4)
    {
        var now = Stopwatch.GetTimestamp();

        var b = _perIpLogin.GetOrAdd(ipV4, _ => new TokenBucket(2, 5, now));
        if (!b.TryConsume(1, now, _ticksPerSec))
            return false;

        CleanupIfNeeded(now);
        return true;
    }

    private void CleanupIfNeeded(long now)
    {
        if (now - _lastCleanupTicks < _cleanupIntervalTicks) return;
        _lastCleanupTicks = now;

        // 简单清理策略：删除很久没补充过的桶（LastTicks 太旧）
        var stale = now - (long)(_ticksPerSec * 10 * 60); // 10 分钟未使用
        foreach (var kv in _perIpConn)
            if (kv.Value.LastTicks < stale)
                _perIpConn.TryRemove(kv.Key, out _);

        foreach (var kv in _perIpLogin)
            if (kv.Value.LastTicks < stale)
                _perIpLogin.TryRemove(kv.Key, out _);
    }

    private sealed class TokenBucket
    {
        public readonly double Burst;
        public readonly double RatePerSec;
        public long LastTicks;
        public double Tokens;

        public TokenBucket(double ratePerSec, double burst, long nowTicks)
        {
            RatePerSec = ratePerSec;
            Burst = burst;
            Tokens = burst;
            LastTicks = nowTicks;
        }

        public bool TryConsume(double cost, long nowTicks, double ticksPerSec)
        {
            var dt = (nowTicks - LastTicks) / ticksPerSec;
            if (dt > 0)
            {
                Tokens = Math.Min(Burst, Tokens + dt * RatePerSec);
                LastTicks = nowTicks;
            }

            if (Tokens >= cost)
            {
                Tokens -= cost;
                return true;
            }

            return false;
        }
    }
}