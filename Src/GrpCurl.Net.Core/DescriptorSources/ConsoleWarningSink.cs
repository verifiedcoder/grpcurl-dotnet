namespace GrpCurl.Net.DescriptorSources;

/// <summary>
///     Default <see cref="IDescriptorWarningSink" /> that writes warnings to
///     <c>Console.Error</c>, preserving the CLI's pre-existing behaviour. Used whenever a
///     caller does not supply its own sink.
/// </summary>
internal sealed class ConsoleWarningSink : IDescriptorWarningSink
{
    public static readonly ConsoleWarningSink Instance = new();

    private ConsoleWarningSink()
    {
    }

    public void OnWarning(string message) => Console.Error.WriteLine(message);
}
