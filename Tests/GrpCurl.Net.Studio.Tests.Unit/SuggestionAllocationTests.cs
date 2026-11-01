using GrpCurl.Net.Studio.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class SuggestionAllocationTests
{
    [Fact]
    public void Oversized_candidate_does_not_allocate_a_distance_matrix()
    {
        string[] candidates = [new string('x', 2 * 1024 * 1024)];
        _ = GraphQlService.Closest(["Service"], "Servic");
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = GraphQlService.Closest(candidates, "Service");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        result.ShouldBeNull();
        allocated.ShouldBeLessThan(32 * 1024, $"suggestion lookup allocated {allocated:N0} bytes");
    }
}
