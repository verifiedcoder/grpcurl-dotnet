using Connectrpc.Conformance.V1;
using Google.Protobuf;
using System.Buffers.Binary;

namespace GrpCurl.Net.Conformance;

/// <summary>
///     Entry point for the connectrpc/conformance client-under-test adapter.
///     Speaks the runner's size-delimited binary protocol: each frame is a four-byte
///     big-endian length prefix followed by a serialized protobuf message
///     (<see cref="ClientCompatRequest" /> in, <see cref="ClientCompatResponse" /> out).
///     stdout carries only binary frames — every diagnostic goes to stderr.
/// </summary>
internal static class Program
{
    /// <summary>Serializes response frames so concurrent test results never interleave on stdout.</summary>
    private static readonly SemaphoreSlim StdoutLock = new(1, 1);

    /// <summary>Bounds concurrent RPCs; results may be written out of order (test_name correlates them).</summary>
    private static readonly SemaphoreSlim Throttle = new(8, 8);

    private static async Task<int> Main()
    {
        await using var stdin = Console.OpenStandardInput();
        await using var stdout = Console.OpenStandardOutput();

        var inFlight = new List<Task>();
        var lengthBuffer = new byte[4];

        while (true)
        {
            var read = await stdin.ReadAtLeastAsync(lengthBuffer, 4, throwOnEndOfStream: false);

            if (read == 0)
            {
                // Clean EOF at a frame boundary: the runner is done sending test cases.
                break;
            }

            if (read < 4)
            {
                await Console.Error.WriteLineAsync("conformance adapter: truncated length prefix on stdin");

                return 1;
            }

            var body = new byte[BinaryPrimitives.ReadUInt32BigEndian(lengthBuffer)];

            await stdin.ReadExactlyAsync(body);

            var request = ClientCompatRequest.Parser.ParseFrom(body);

            inFlight.Add(HandleAsync(request, stdout));
        }

        // Drain in-flight RPCs before exiting, as the protocol requires.
        await Task.WhenAll(inFlight);

        return 0;
    }

    private static async Task HandleAsync(ClientCompatRequest request, Stream stdout)
    {
        await Throttle.WaitAsync();

        ClientCompatResponse response;

        try
        {
            var result = await TestCaseRunner.RunAsync(request);

            response = new ClientCompatResponse
            {
                TestName = request.TestName,
                Response = result
            };
        }
        catch (Exception ex)
        {
            // Only "could not even issue the RPC" lands here; genuine RPC errors are
            // encoded inside ClientResponseResult.Error by the runner above.
            response = new ClientCompatResponse
            {
                TestName = request.TestName,
                Error = new ClientErrorResult { Message = ex.ToString() }
            };
        }
        finally
        {
            Throttle.Release();
        }

        var payload = response.ToByteArray();
        var prefix = new byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)payload.Length);

        await StdoutLock.WaitAsync();

        try
        {
            await stdout.WriteAsync(prefix);
            await stdout.WriteAsync(payload);
            await stdout.FlushAsync();
        }
        finally
        {
            StdoutLock.Release();
        }
    }
}
