using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     FR-155 / SPEC-030 §9: the rolling diagnostics file sink — append/read, 7-day + 10 MB retention, a
///     tolerant read, and the Microsoft.Extensions.Logging bridge.
/// </summary>
public sealed class FileDiagnosticsLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "grpcn-diag-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public FileDiagnosticsLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string Path_ => Path.Combine(_dir, "diagnostics.ndjson");

    [Fact]
    public async Task Appends_and_reads_entries_in_order()
    {
        var log = new FileDiagnosticsLog(Path_);

        log.Log(DiagnosticsLevel.Information, "A", "first");
        log.Log(DiagnosticsLevel.Warning, "B", "second");

        var entries = await log.ReadRecentAsync(Ct);
        entries.Select(e => e.Message).ShouldBe(["first", "second"]);
        entries[1].Level.ShouldBe(DiagnosticsLevel.Warning);
        entries[1].Category.ShouldBe("B");
    }

    [Fact]
    public void Exposes_the_log_file_and_folder()
    {
        var log = new FileDiagnosticsLog(Path_);

        log.LogFilePath.ShouldBe(Path_);
        log.LogFolderPath.ShouldBe(_dir);
    }

    [Fact]
    public async Task Evicts_entries_older_than_the_max_age()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var log = new FileDiagnosticsLog(Path_, maxAge: TimeSpan.FromDays(7), now: () => now);

        log.Log(DiagnosticsLevel.Information, "old", "stale");
        now = now.AddDays(8); // age the clock past the window
        log.Log(DiagnosticsLevel.Information, "new", "fresh");

        var entries = await log.ReadRecentAsync(Ct);
        entries.Select(e => e.Message).ShouldBe(["fresh"]); // the 8-day-old entry is evicted
    }

    [Fact]
    public async Task Evicts_oldest_entries_past_the_byte_cap()
    {
        var log = new FileDiagnosticsLog(Path_, maxBytes: 300); // tiny cap to force eviction

        for (var i = 0; i < 50; i++)
        {
            log.Log(DiagnosticsLevel.Information, "cat", $"message number {i}");
        }

        var entries = await log.ReadRecentAsync(Ct);
        entries.ShouldNotBeEmpty();
        entries.Count.ShouldBeLessThan(50);                 // older lines evicted
        entries[^1].Message.ShouldBe("message number 49");  // the newest is always kept
    }

    [Fact]
    public async Task A_torn_line_is_skipped_rather_than_failing_the_read()
    {
        var log = new FileDiagnosticsLog(Path_);
        log.Log(DiagnosticsLevel.Information, "cat", "good");
        await File.AppendAllTextAsync(Path_, "{ not valid json\n", Ct);
        log.Log(DiagnosticsLevel.Information, "cat", "also good");

        var entries = await log.ReadRecentAsync(Ct);
        entries.Select(e => e.Message).ShouldBe(["good", "also good"]);
    }

    [Fact]
    public async Task The_logging_provider_routes_ilogger_output_to_the_sink()
    {
        var log = new FileDiagnosticsLog(Path_);
        using var provider = new DiagnosticsLoggerProvider(log);
        var logger = provider.CreateLogger("MyCategory");

        logger.Log(LogLevel.Error, default, "boom 42", null, (state, _) => state);

        var entry = (await log.ReadRecentAsync(Ct)).ShouldHaveSingleItem();
        entry.Level.ShouldBe(DiagnosticsLevel.Error);
        entry.Category.ShouldBe("MyCategory");
        entry.Message.ShouldBe("boom 42");
    }
}
