namespace GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

/// <summary>
///     Why a descriptor load failed, shaped to drive the explorer's error state (FR-026).
///     <see cref="ReflectionUnavailable" /> is set when the server does not implement reflection,
///     so the UI can offer the "configure a protoset/.proto instead" hint and jump.
/// </summary>
public sealed record DescriptorLoadError(string Message, string? Hint, bool ReflectionUnavailable);

/// <summary>Outcome of loading a connection's descriptors: a populated catalog, or an error.</summary>
public sealed record DescriptorLoadResult
{
    private DescriptorLoadResult(bool ok, ServiceCatalog? catalog, DescriptorLoadError? error)
    {
        Ok = ok;
        Catalog = catalog;
        Error = error;
    }

    public bool Ok { get; }
    public ServiceCatalog? Catalog { get; }
    public DescriptorLoadError? Error { get; }

    public static DescriptorLoadResult Success(ServiceCatalog catalog) => new(true, catalog, null);
    public static DescriptorLoadResult Failure(DescriptorLoadError error) => new(false, null, error);
}
