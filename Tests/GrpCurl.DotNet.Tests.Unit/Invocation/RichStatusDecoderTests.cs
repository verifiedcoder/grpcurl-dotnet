using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using GrpCurl.Net.Invocation;

namespace GrpCurl.Net.Tests.Unit.Invocation;

public sealed class RichStatusDecoderTests
{
    [Fact]
    public void TryDecodeBytes_RoundTrips_CodeAndMessage()
    {
        // Arrange
        var status = new Status
        {
            Code = 5,
            Message = "not found"
        };

        // Act
        var decoded = RichStatusDecoder.TryDecodeBytes(status.ToByteArray());

        // Assert
        decoded.ShouldNotBeNull();
        decoded.Code.ShouldBe(5);
        decoded.Message.ShouldBe("not found");
        decoded.Details.ShouldBeEmpty();
    }

    [Fact]
    public void TryDecodeBytes_KnownDetail_ParsesIntoTypedMessage()
    {
        // Arrange
        var errorInfo = new ErrorInfo
        {
            Reason = "QUOTA_EXCEEDED",
            Domain = "api.example.com"
        };

        errorInfo.Metadata.Add("region", "us-east-1");

        var status = new Status
        {
            Code = 8,
            Message = "Quota exceeded",
            Details = { Any.Pack(errorInfo) }
        };

        // Act
        var decoded = RichStatusDecoder.TryDecodeBytes(status.ToByteArray());

        // Assert
        decoded.ShouldNotBeNull();
        decoded.Details.Count.ShouldBe(1);

        var detail = decoded.Details[0];

        detail.TypeUrl.ShouldContain("ErrorInfo");
        detail.ParsedMessage.ShouldNotBeNull();
        detail.ParsedMessage.ShouldBeOfType<ErrorInfo>();
        ((ErrorInfo)detail.ParsedMessage!).Reason.ShouldBe("QUOTA_EXCEEDED");
    }

    [Fact]
    public void TryDecodeBytes_UnknownDetail_KeepsRawBytes()
    {
        // Arrange
        var unknownTypePayload = new Any
        {
            TypeUrl = "type.googleapis.com/com.example.Custom",
            Value = ByteString.CopyFrom([0x01, 0x02, 0x03])
        };

        var status = new Status
        {
            Code = 13,
            Message = "internal",
            Details = { unknownTypePayload }
        };

        // Act
        var decoded = RichStatusDecoder.TryDecodeBytes(status.ToByteArray());

        // Assert
        decoded.ShouldNotBeNull();
        decoded.Details.Count.ShouldBe(1);
        decoded.Details[0].ParsedMessage.ShouldBeNull();
        decoded.Details[0].RawValue.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void TryDecodeBytes_GarbagePayload_ReturnsNull()
    {
        // Arrange

        // Act
        var decoded = RichStatusDecoder.TryDecodeBytes([0xff, 0xff, 0xff, 0xff]);

        // Assert
        decoded.ShouldBeNull();
    }
}
