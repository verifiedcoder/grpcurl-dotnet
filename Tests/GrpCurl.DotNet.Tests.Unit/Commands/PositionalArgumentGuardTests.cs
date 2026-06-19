using GrpCurl.Net.Commands;
using GrpCurl.Net.Exceptions;

namespace GrpCurl.Net.Tests.Unit.Commands;

public sealed class PositionalArgumentGuardTests
{
    [Fact]
    public void RejectOptionLikeValues_DoubleDashValue_ThrowsUsageErrorExit2()
    {
        var originalError = Console.Error;

        try
        {
            using var stderr = new StringWriter();

            Console.SetError(stderr);

            var ex = Should.Throw<GrpcCommandException>(() =>
                PositionalArgumentGuard.RejectOptionLikeValues("list", OutputFormat.Text, ("address", "--bogus-flag")));

            ex.ExitCode.ShouldBe(2);
            _ = ex.Envelope.ShouldNotBeNull();
            ex.Envelope.Category.ShouldBe(ErrorCategory.Usage);
            ex.Envelope.Message.ShouldContain("--bogus-flag");
            ex.Envelope.Message.ShouldContain("address");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Theory]
    [InlineData("localhost:9090")]
    [InlineData("-plaintext")] // single-dash stays accepted: grpcurl drop-in compat path
    [InlineData(null)]
    [InlineData("")]
    public void RejectOptionLikeValues_NonDoubleDashValues_DoNotThrow(string? value)
    {
        Should.NotThrow(() =>
            PositionalArgumentGuard.RejectOptionLikeValues("list", OutputFormat.Text, ("address", value)));
    }

    [Fact]
    public void RejectOptionLikeValues_ChecksAllPositionals()
    {
        var originalError = Console.Error;

        try
        {
            Console.SetError(new StringWriter());

            var ex = Should.Throw<GrpcCommandException>(() =>
                PositionalArgumentGuard.RejectOptionLikeValues(
                    "describe",
                    OutputFormat.Text,
                    ("address", "localhost:9090"),
                    ("symbol", "--oops")));

            _ = ex.Envelope.ShouldNotBeNull();
            ex.Envelope.Message.ShouldContain("symbol");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
