using System.Buffers;

namespace GameServer.Tick;

/// <summary>Low-allocation rolling metrics collected by the tick thread.</summary>
public sealed class TickMetrics
{
    private readonly double[] _tickWindowMs;
    private readonly double[] _stageElapsedMs = new double[4];
    private readonly double[] _stageMaxMs = new double[4];
    private readonly long[] _stageAllocatedBytes = new long[4];
    private int _windowCount;
    private int _windowIndex;
    private long _intervalTicks;
    private long _inputFrames;
    private long _outputFrames;
    private long _outputBytes;
    private long _chunksLoaded;
    private long _chunksUnloaded;
    private long _collisionQueries;

    public TickMetrics(int windowSize = 1200)
    {
        if (windowSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowSize));
        _tickWindowMs = new double[windowSize];
    }

    public int WindowCount => _windowCount;

    public void RecordStage(TickPipelineStage stage, long elapsedTimestampTicks, long allocatedBytes)
    {
        var index = (int)stage;
        var elapsedMs = elapsedTimestampTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _stageElapsedMs[index] += elapsedMs;
        _stageMaxMs[index] = Math.Max(_stageMaxMs[index], elapsedMs);
        if (allocatedBytes > 0)
            _stageAllocatedBytes[index] += allocatedBytes;
    }

    public void EndTick(double elapsedMs)
    {
        _tickWindowMs[_windowIndex] = elapsedMs;
        _windowIndex = (_windowIndex + 1) % _tickWindowMs.Length;
        if (_windowCount < _tickWindowMs.Length)
            _windowCount++;
        _intervalTicks++;
    }

    public void AddInputFrames(int count) => _inputFrames += count;

    public void AddOutput(int frames, int bytes)
    {
        _outputFrames += frames;
        _outputBytes += bytes;
    }

    public void AddChunks(int loaded, int unloaded)
    {
        _chunksLoaded += loaded;
        _chunksUnloaded += unloaded;
    }

    public void AddCollisionQueries(int count = 1) => _collisionQueries += count;

    public TickMetricsSnapshot TakeSnapshotAndResetInterval()
    {
        var sampleCount = _windowCount;
        var rented = ArrayPool<double>.Shared.Rent(Math.Max(1, sampleCount));
        try
        {
            _tickWindowMs.AsSpan(0, sampleCount).CopyTo(rented);
            var samples = rented.AsSpan(0, sampleCount);
            samples.Sort();

            var divisor = Math.Max(1, _intervalTicks);
            var snapshot = new TickMetricsSnapshot(
                sampleCount,
                Percentile(samples, 0.50),
                Percentile(samples, 0.95),
                Percentile(samples, 0.99),
                BuildStageSnapshot(TickPipelineStage.InputCollect, divisor),
                BuildStageSnapshot(TickPipelineStage.Simulate, divisor),
                BuildStageSnapshot(TickPipelineStage.Replication, divisor),
                BuildStageSnapshot(TickPipelineStage.NetworkFlush, divisor),
                _inputFrames,
                _outputFrames,
                _outputBytes,
                _chunksLoaded,
                _chunksUnloaded,
                _collisionQueries);

            ResetInterval();
            return snapshot;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(rented);
        }
    }

    private StageMetricsSnapshot BuildStageSnapshot(TickPipelineStage stage, long divisor)
    {
        var index = (int)stage;
        return new StageMetricsSnapshot(
            _stageElapsedMs[index] / divisor,
            _stageMaxMs[index],
            _stageAllocatedBytes[index] / divisor);
    }

    private static double Percentile(ReadOnlySpan<double> sorted, double percentile)
    {
        if (sorted.Length == 0)
            return 0;
        var rank = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[rank];
    }

    private void ResetInterval()
    {
        Array.Clear(_stageElapsedMs);
        Array.Clear(_stageMaxMs);
        Array.Clear(_stageAllocatedBytes);
        _intervalTicks = 0;
        _inputFrames = 0;
        _outputFrames = 0;
        _outputBytes = 0;
        _chunksLoaded = 0;
        _chunksUnloaded = 0;
        _collisionQueries = 0;
    }
}

public readonly record struct StageMetricsSnapshot(
    double AverageMs,
    double MaxMs,
    long AverageAllocatedBytes);

public readonly record struct TickMetricsSnapshot(
    int SampleCount,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    StageMetricsSnapshot InputCollect,
    StageMetricsSnapshot Simulate,
    StageMetricsSnapshot Replication,
    StageMetricsSnapshot NetworkFlush,
    long InputFrames,
    long OutputFrames,
    long OutputBytes,
    long ChunksLoaded,
    long ChunksUnloaded,
    long CollisionQueries);
