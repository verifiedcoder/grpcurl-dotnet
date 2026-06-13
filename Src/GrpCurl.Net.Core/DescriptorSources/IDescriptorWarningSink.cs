namespace GrpCurl.Net.DescriptorSources;

/// <summary>
///     Receives non-fatal diagnostics raised while loading descriptors (e.g. a service that
///     failed to resolve via reflection, or a duplicate/overwritten protoset entry).
/// </summary>
/// <remarks>
///     The CLI writes these to <c>Console.Error</c> (the default <see cref="ConsoleWarningSink" />),
///     but a GUI host needs them as data rather than stdio writes — those would vanish or corrupt
///     a binary stream. Pass a custom sink to collect them.
/// </remarks>
public interface IDescriptorWarningSink
{
    /// <summary>Reports a single non-fatal descriptor-loading warning.</summary>
    void OnWarning(string message);
}
