using System.Diagnostics;

namespace GrpCurl.Net.Studio.Tests.Perf;

/// <summary>
///     Minimal wall-clock harness for the nightly Performance lane. Runs a scenario after a warm-up and
///     returns the 95th-percentile elapsed time (SPEC-060 §0: targets are p95 over ≥ 20 runs). Thresholds
///     at the call site carry the spec's 25% CI-noise headroom via <see cref="Headroom" />.
/// </summary>
internal static class PerfMeasure
{
    /// <summary>SPEC-060 §2: the V-BENCH thresholds include 25% headroom for shared-runner noise.</summary>
    public const double Headroom = 1.25;

    public static double P95Millis(int runs, int warmup, Action action)
    {
        for (var i = 0; i < warmup; i++)
        {
            action();
        }

        var samples = new double[runs];

        for (var i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        var index = Math.Clamp((int)Math.Ceiling(0.95 * runs) - 1, 0, runs - 1);
        return samples[index];
    }
}
