using Grpc.Core;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Tests.Unit.Utilities;

/// <summary>
///     Regression tests for the metadata builder. Covers the binary metadata support
///     introduced in CODE-REVIEW.md P2 "Metadata and Error Parity Is Incomplete".
/// </summary>
public sealed class GrpcChannelFactoryMetadataTests
{
    [Fact]
    public void CreateMetadata_TextHeader_AddsAsString()
    {
        var metadata = GrpcChannelFactory.CreateMetadata(["x-custom: hello"]);

        var entry = metadata.Get("x-custom");

        entry.ShouldNotBeNull();
        entry!.Value.ShouldBe("hello");
        entry.IsBinary.ShouldBeFalse();
    }

    [Fact]
    public void CreateMetadata_BinHeader_Base64Decoded_AddsAsBytes()
    {
        // Bytes 0x01 0x02 0x03 0x04 base64-encode to AQIDBA==
        var metadata = GrpcChannelFactory.CreateMetadata(["trace-bin: AQIDBA=="]);

        var entry = metadata.Get("trace-bin");

        entry.ShouldNotBeNull();
        entry!.IsBinary.ShouldBeTrue();
        entry.ValueBytes.ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void CreateMetadata_BinHeader_InvalidBase64_Throws()
    {
        Should.Throw<ArgumentException>(() => GrpcChannelFactory.CreateMetadata(["trace-bin: not-base64!!!"]))
            .Message.ShouldContain("base64");
    }

    [Fact]
    public void CreateMetadata_StatusDetailsBin_RoundTripsBytes()
    {
        // Real-world example: callers can pass a base64-encoded google.rpc.Status to
        // exercise the grpc-status-details-bin path on a server.
        var payload = new byte[] { 0x0a, 0x05, 0x68, 0x65, 0x6c, 0x6c, 0x6f };
        var base64 = Convert.ToBase64String(payload);

        var metadata = GrpcChannelFactory.CreateMetadata([$"grpc-status-details-bin: {base64}"]);

        var entry = metadata.Get("grpc-status-details-bin");

        entry.ShouldNotBeNull();
        entry!.IsBinary.ShouldBeTrue();
        entry.ValueBytes.ShouldBe(payload);
    }

    [Fact]
    public void CreateMetadata_BinHeaderUppercase_StillDetected()
    {
        // The -bin suffix check must be case-insensitive (matches grpc-spec wire rules).
        var metadata = GrpcChannelFactory.CreateMetadata(["custom-BIN: AQIDBA=="]);

        var entry = metadata.Get("custom-bin");

        entry.ShouldNotBeNull();
        entry!.IsBinary.ShouldBeTrue();
    }

    [Theory]
    [InlineData("unix:///var/run/grpc.sock", "/var/run/grpc.sock")]
    [InlineData("unix:/tmp/grpc.sock", "/tmp/grpc.sock")]
    [InlineData("unix:relative.sock", "relative.sock")]
    public void TryExtractUnixSocketPath_RecognizedForms_ReturnsPath(string address, string expectedPath)
    {
        GrpcChannelFactory.TryExtractUnixSocketPath(address).ShouldBe(expectedPath);
    }

    [Theory]
    [InlineData("localhost:5001")]
    [InlineData("http://example.com:443")]
    [InlineData("https://api.example.com")]
    [InlineData("")]
    public void TryExtractUnixSocketPath_NonUnixAddress_ReturnsNull(string address)
    {
        GrpcChannelFactory.TryExtractUnixSocketPath(address).ShouldBeNull();
    }
}
