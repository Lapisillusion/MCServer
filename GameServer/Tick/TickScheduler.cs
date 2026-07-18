using System.Diagnostics;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Tick;

/// <summary>
/// 20 TPS loop. Drives pipeline stages in order, logs timing.
/// </summary>
public sealed class TickScheduler
{
    private const double MsPerTick = 50.0;
    private const double WarnThresholdMs = 55.0;
    private const int StatsIntervalTicks = 200; // ~10 seconds

    private readonly ITickStage[] _stages;

    public TickScheduler(ITickStage[] stages)
    {
        _stages = stages.OrderBy(s => (int)s.Stage).ToArray();

        // Validate coverage
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
        var sw = Stopwatch.StartNew();
        long tickNumber = 0;

        while (!ct.IsCancellationRequested)
        {
            var tickStart = sw.ElapsedTicks;
            long totalTicks = 0;

            foreach (var stage in _stages)
            {
                var stageStart = sw.ElapsedTicks;
                await stage.ExecuteAsync(tickNumber, ct);
                totalTicks += sw.ElapsedTicks - stageStart;
            }

            tickNumber++;
            var elapsedMs = totalTicks * 1000.0 / Stopwatch.Frequency;
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
                Info("Tick", $"Tick #{tickNumber}, last={elapsedMs:F1}ms, stages={_stages.Length}");
            }
        }
    }
}
