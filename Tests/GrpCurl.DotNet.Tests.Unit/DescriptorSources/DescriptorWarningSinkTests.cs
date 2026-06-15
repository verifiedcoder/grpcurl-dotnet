using GrpCurl.Net.DescriptorSources;

namespace GrpCurl.Net.Tests.Unit.DescriptorSources;

/// <summary>
///     Verifies the SPEC-031 warning sink: descriptor-load warnings that previously went
///     straight to <c>Console.Error</c> are routed to an injectable sink, with the exact
///     same message text, so a GUI host can collect them instead of corrupting stdio.
/// </summary>
public sealed class DescriptorWarningSinkTests
{
    private sealed class CapturingSink : IDescriptorWarningSink
    {
        public List<string> Messages { get; } = [];

        public void OnWarning(string message) => Messages.Add(message);
    }

    private static string TestProtosetPath
        => Path.Combine(AppContext.BaseDirectory, "TestProtosets", "test.protoset");

    [Fact]
    public async Task ProtosetSource_duplicate_load_routes_warnings_to_sink()
    {
        // Arrange — loading the same protoset twice forces duplicate/overwrite warnings.
        var sink = new CapturingSink();
        var path = TestProtosetPath;

        // Act
        _ = await ProtosetSource.LoadFromFilesAsync(
            [path, path],
            DescriptorSourceOptions.Default,
            CancellationToken.None,
            sink);

        // Assert — the exact strings previously written to Console.Error.
        sink.Messages.ShouldContain(m => m.Contains("already loaded, skipping duplicate"));
        sink.Messages.ShouldContain(m => m.Contains("already cached, overwriting"));
        sink.Messages.ShouldAllBe(m => m.StartsWith("Warning: "));
    }

    [Fact]
    public async Task ProtosetSource_single_load_emits_no_warnings()
    {
        var sink = new CapturingSink();

        _ = await ProtosetSource.LoadFromFilesAsync(
            [TestProtosetPath],
            DescriptorSourceOptions.Default,
            CancellationToken.None,
            sink);

        sink.Messages.ShouldBeEmpty();
    }
}
