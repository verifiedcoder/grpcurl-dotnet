using GrpCurl.Net.Studio.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class GraphQlSuggestionTests
{
    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    public void Length_limit_applies_to_both_operands(int length)
    {
        var name = new string('a', length);
        GraphQlService.Closest([name], name).ShouldBe(length <= 256 ? name : null);
        GraphQlService.Closest([name], new string('a', 256)).ShouldBe(length <= 256 ? name : null);
    }

    [Fact]
    public void Oversized_target_does_not_enumerate_and_exact_match_stops_enumeration()
    {
        GraphQlService.Closest(ThrowOnEnumeration(), new string('a', 257)).ShouldBeNull();
        GraphQlService.Closest(ExactThenThrow(), "exact").ShouldBe("exact");
    }

    private static IEnumerable<string> ThrowOnEnumeration()
        => Enumerable.Repeat("unused", 1).Select<string, string>(_ => throw new InvalidOperationException("must not enumerate"));

    private static IEnumerable<string> ExactThenThrow()
    {
        yield return "exact";
        throw new InvalidOperationException("must stop at first exact match");
    }

    [Fact]
    public void Ranking_ties_empty_strings_and_case_remain_ordinal()
    {
        GraphQlService.Closest(["cat", "bat"], "hat").ShouldBe("cat");
        GraphQlService.Closest(["cat", "bat", "hit"], "hint").ShouldBe("hit");
        GraphQlService.Closest(["ABCD"], "abcd").ShouldBeNull();
        GraphQlService.Closest([""], "abc").ShouldBe("");
        GraphQlService.Closest(["abcd"], "").ShouldBeNull();
        GraphQlService.Closest([], "").ShouldBeNull();
        GraphQlService.Closest([new string('a', 257), "test"], "tst").ShouldBe("test");
    }

    [Fact]
    public void Cancellation_is_observed_before_and_during_enumeration()
    {
        using var cts = new CancellationTokenSource();
        IEnumerable<string> CancelDuringEnumeration()
        {
            yield return "first";
            cts.Cancel();
            yield return "second";
        }
        _ = Should.Throw<OperationCanceledException>(() => GraphQlService.Closest(CancelDuringEnumeration(), "target", cts.Token));
        _ = Should.Throw<OperationCanceledException>(() => GraphQlService.Closest(ThrowOnEnumeration(), new string('a', 257), cts.Token));
    }

    [Fact]
    public void Scratch_allocation_does_not_grow_with_candidate_count()
    {
        var candidates = Enumerable.Repeat(new string('a', 256), 10_000).ToArray();
        var target = new string('a', 253) + "bbb";
        _ = GraphQlService.Closest(candidates, target);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = GraphQlService.Closest(candidates, target);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        result.ShouldBe(candidates[0]);
        allocated.ShouldBeLessThan(32 * 1024);
    }

    [Fact]
    public void Banded_results_match_a_reference_matrix()
    {
        var random = new Random(31);
        var names = new List<string> { "", "a", "abcd", "ab", "abcdxyz", "abc", "xyzabcd", "abcdx", "abxcd", "😀a", "😀b" };
        for (var i = 0; i < 150; i++)
        {
            names.Add(new string(Enumerable.Range(0, random.Next(0, 12)).Select(_ => "abc"[random.Next(3)]).ToArray()));
        }
        foreach (var target in names)
        {
            foreach (var candidate in names)
            {
                var distance = ReferenceDistance(candidate, target);
                GraphQlService.Closest([candidate], target).ShouldBe(distance <= 3 ? candidate : null);
            }
            var reversed = names.AsEnumerable().Reverse().ToArray();
            var expected = reversed.Select(n => (Name: n, Distance: ReferenceDistance(n, target)))
                .Where(x => x.Distance <= 3).OrderBy(x => x.Distance).First().Name;
            GraphQlService.Closest(reversed, target).ShouldBe(expected);
        }
    }

    private static int ReferenceDistance(string a, string b)
    {
        var matrix = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) { matrix[i, 0] = i; }
        for (var j = 0; j <= b.Length; j++) { matrix[0, j] = j; }
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                matrix[i, j] = Math.Min(Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1), matrix[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }
        }
        return matrix[a.Length, b.Length];
    }
}
