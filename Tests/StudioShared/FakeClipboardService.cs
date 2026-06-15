using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>In-memory <see cref="IClipboardService" /> that records the last text set.</summary>
public sealed class FakeClipboardService : IClipboardService
{
    public string? Text { get; private set; }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        Text = text;
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => Task.FromResult(Text);
}
