using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>Scripted <see cref="IDescriptorService" />: returns a canned result or a custom handler.</summary>
public sealed class FakeDescriptorService : IDescriptorService
{
    /// <summary>Result returned when <see cref="OnLoad" /> is not set.</summary>
    public DescriptorLoadResult Result { get; set; } = DescriptorLoadResult.Success(ServiceCatalog.Empty);

    /// <summary>Optional custom behaviour (e.g. to honour cancellation or throw).</summary>
    public Func<SavedConnection, CancellationToken, Task<DescriptorLoadResult>>? OnLoad { get; set; }

    /// <summary>Result returned when <see cref="OnDescribe" /> is not set.</summary>
    public DescribeResult DescribeResult { get; set; } = DescribeResult.Failure(new DescriptorLoadError("not configured", null, false));

    /// <summary>Optional custom describe behaviour (e.g. to vary by symbol, honour cancellation, or throw).</summary>
    public Func<SavedConnection, string, CancellationToken, Task<DescribeResult>>? OnDescribe { get; set; }

    public SavedConnection? LastLoaded { get; private set; }

    public int LoadCount { get; private set; }

    public string? LastDescribed { get; private set; }

    public int DescribeCount { get; private set; }

    /// <summary>Result returned by <see cref="ExportProtosetAsync" /> unless <see cref="OnExportProtoset" /> is set.</summary>
    public SchemaExportResult ExportProtosetResult { get; set; } = SchemaExportResult.Success([], TimeSpan.Zero);

    public SchemaExportResult ExportProtosResult { get; set; } = SchemaExportResult.Success([], TimeSpan.Zero);

    /// <summary>Custom per-call behaviour keyed on (path, overwrite) — e.g. conflict-then-success.</summary>
    public Func<string, bool, SchemaExportResult>? OnExportProtoset { get; set; }

    public Func<string, bool, SchemaExportResult>? OnExportProtos { get; set; }

    public string? LastExportProtosetPath { get; private set; }

    public string? LastExportProtosDirectory { get; private set; }

    public Task<DescriptorLoadResult> LoadAsync(SavedConnection connection, CancellationToken cancellationToken = default)
    {
        LastLoaded = connection;
        LoadCount++;

        if (OnLoad is not null)
        {
            return OnLoad(connection, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result);
    }

    public Task<DescribeResult> DescribeAsync(SavedConnection connection, string symbol, CancellationToken cancellationToken = default)
    {
        LastDescribed = symbol;
        DescribeCount++;

        if (OnDescribe is not null)
        {
            return OnDescribe(connection, symbol, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DescribeResult);
    }

    public Task<SchemaExportResult> ExportProtosetAsync(SavedConnection connection, string path, bool overwrite, CancellationToken cancellationToken = default)
    {
        LastExportProtosetPath = path;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OnExportProtoset?.Invoke(path, overwrite) ?? ExportProtosetResult);
    }

    public Task<SchemaExportResult> ExportProtosAsync(SavedConnection connection, string directory, bool overwrite, CancellationToken cancellationToken = default)
    {
        LastExportProtosDirectory = directory;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OnExportProtos?.Invoke(directory, overwrite) ?? ExportProtosResult);
    }
}
