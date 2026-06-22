using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Models.History;

namespace GrpCurl.Net.Studio.Tests.Perf;

/// <summary>
///     Synthetic data at the scale of the SPEC-060 budgets, built in memory so the perf suite needs no
///     external server. Today this covers the descriptor tree (NFR-P5: a 500-service catalog); the
///     history (NFR-P10) and workspace (NFR-P11) fixtures land alongside their wall-clock tests in A3.3.
/// </summary>
internal static class PerfFixtures
{
    /// <summary>The SPEC-060 NFR-P5 reference scale: a server with 500 services.</summary>
    public const int LargeServiceCount = 500;

    private static readonly StreamingShape[] Shapes =
        [StreamingShape.Unary, StreamingShape.ServerStreaming, StreamingShape.ClientStreaming, StreamingShape.BidiStreaming];

    /// <summary>
    ///     A catalog of <paramref name="serviceCount" /> services, each with <paramref name="methodsPerService" />
    ///     methods cycling through the four streaming shapes. Method/type names are unique so the explorer's
    ///     de-dup and filtering see realistic cardinality.
    /// </summary>
    public static ServiceCatalog SyntheticCatalog(int serviceCount, int methodsPerService)
    {
        var services = new List<ServiceEntry>(serviceCount);

        for (var s = 0; s < serviceCount; s++)
        {
            var serviceName = $"perf.v1.Service{s:D4}";
            var methods = new List<ServiceMethod>(methodsPerService);

            for (var m = 0; m < methodsPerService; m++)
            {
                var method = $"Method{m:D2}";
                methods.Add(new ServiceMethod(
                    method, $"{serviceName}/{method}", Shapes[m % Shapes.Length],
                    $"{serviceName}.{method}Request", $"{serviceName}.{method}Response"));
            }

            services.Add(new ServiceEntry(serviceName, methods));
        }

        return new ServiceCatalog(services, []);
    }

    /// <summary>
    ///     A history of <paramref name="count" /> completed unary calls, newest timestamps last. Used to
    ///     exercise the history list at scale (NFR-P6/P10); <paramref name="now" /> anchors the timestamps
    ///     so the caller controls the clock.
    /// </summary>
    public static IReadOnlyList<HistoryEntry> SyntheticHistory(int count, DateTimeOffset now)
    {
        var entries = new List<HistoryEntry>(count);

        for (var i = 0; i < count; i++)
        {
            entries.Add(new HistoryEntry(
                HistoryEntry.CurrentVersion, $"e{i:D5}", now.AddSeconds(i), HistoryKind.Grpc,
                new HistoryConnection("staging", "h:1", "tls", null), null, $"perf.v1.Service0001/Method{i % 100:D2}",
                new HistoryRequest("json", "{}", false, [], "10s", false, false, null, null, null),
                new HistoryOutcome("OK", "success", 0, 12, 1, 1, null, false, null)));
        }

        return entries;
    }
}
