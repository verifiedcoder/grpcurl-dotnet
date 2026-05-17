namespace GrpCurl.Net.Tests.Unit.Fixtures;

/// <summary>
///     xUnit collection that serializes execution of tests which mutate
///     <see cref="Console.Out"/> or <see cref="Console.Error"/>. Without this, parallel
///     classes race on the global <c>Console</c> writers and leave the streams pointed at
///     a disposed <see cref="StringWriter"/> for whichever class loses the race.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ConsoleStreamCollection
{
    public const string Name = "ConsoleStream";
}
