using System.Diagnostics;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Tick;

/// <summary>20 TPS loop. Drives pipeline stages in order and records rolling performance metrics.</summary>
public sealed class TickScheduler
{
    private const double MsPerTick = 50.0;
    private const double WarnThresholdMs = 55.0;
    private const int StatsIntervalTicks = 200; // ~10 seconds

    private readonly ITickStage[] _stages;
    private readonly TickMetrics _metrics;

    public TickScheduler(ITickStage[] stages, TickMetrics? metrics = null)
    {
        _stages = stages.OrderBy(s => (int)s.Stage).ToArray();
        _metrics = metrics ?? new TickMetrics();

        var expected = TickPipelineOrder.Default;
        var present = _stages.Select(s => s.Stage).ToHashSet();
        foreach (var stage in expected)
        {
            if (!present.Contains(stage))
                Warn("TickScheduler", $"Missing stage: {stage}");
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        long tickNumber = 0;

        while (!ct.IsCancellationRequested)
        {
            var tickStart = Stopwatch.GetTimestamp();

            foreach (var stage in _stages)
            {
                var allocationStart = GC.GetTotalAllocatedBytes(precise: false);
                var stageStart = Stopwatch.GetTimestamp();
                await stage.ExecuteAsync(tickNumber, ct);
                var stageElapsed = Stopwatch.GetTimestamp() - stageStart;
                var allocated = GC.GetTotalAllocatedBytes(precise: false) - allocationStart;
                _metrics.RecordStage(stage.Stage, stageElapsed, allocated);
            }

            tickNumber++;
            var elapsedTicks = Stopwatch.GetTimestamp() - tickStart;
            var elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;
            _metrics.EndTick(elapsedMs);
            var delayMs = (int)(MsPerTick - elapsedMs);

            if (delayMs > 1)
            {
                await Task.Delay(delayMs, ct);
            }
            else if (elapsedMs > WarnThresholdMs)
            {
                Warn("Tick", $"Tick #{tickNumber} took {elapsedMs:F1}ms (behind by {elapsedMs - MsPerTick:F1}ms)");
            }

            if (tickNumber % StatsIntervalTicks == 0)
            {
                var stats = _metrics.TakeSnapshotAndResetInterval();
                Info("Tick",
                    $"Tick #{tickNumber} P50/P95/P99={stats.P50Ms:F2}/{stats.P95Ms:F2}/{stats.P99Ms:F2}ms; " +
                    $"stageAvg/max(ms) input={stats.InputCollect.AverageMs:F2}/{stats.InputCollect.MaxMs:F2}, " +
                    $"simulate={stats.Simulate.AverageMs:F2}/{stats.Simulate.MaxMs:F2}, " +
                    $"replication={stats.Replication.AverageMs:F2}/{stats.Replication.MaxMs:F2}, " +
                    $"network={stats.NetworkFlush.AverageMs:F2}/{stats.NetworkFlush.MaxMs:F2}; " +
                    $"allocAvg(B) input={stats.InputCollect.AverageAllocatedBytes}, simulate={stats.Simulate.AverageAllocatedBytes}, " +
                    $"replication={stats.Replication.AverageAllocatedBytes}, network={stats.NetworkFlush.AverageAllocatedBytes}; " +
                    $"frames in/out={stats.InputFrames}/{stats.OutputFrames}, outBytes={stats.OutputBytes}, " +
                    $"chunks load/unload={stats.ChunksLoaded}/{stats.ChunksUnloaded}, collisionQueries={stats.CollisionQueries}");
            }
        }
    }
}
