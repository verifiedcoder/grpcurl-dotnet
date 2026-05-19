using Grpc.Core;
using System.Runtime.CompilerServices;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Extension methods for async streams.
/// </summary>
public static class AsyncStreamExtensions
{
    /// <summary>
    ///     Reads all items from an async stream reader as an async enumerable.
    /// </summary>
    /// <typeparam name="T">The type of items in the stream.</typeparam>
    /// <param name="stream">The async stream reader to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of all items from the stream.</returns>
    public static async IAsyncEnumerable<T> ReadAllAsync<T>(
        this IAsyncStreamReader<T> stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await stream.MoveNext(cancellationToken)) yield return stream.Current;
    }
}