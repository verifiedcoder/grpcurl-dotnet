using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class ConnectionValidationTests
{
    [Theory]
    [InlineData("localhost:9090")]
    [InlineData("api.example.com:443")]
    [InlineData("127.0.0.1:50051")]
    [InlineData("[::1]:443")]
    [InlineData("[2001:db8::1]:8080")]
    public void Valid_addresses_pass(string address)
        => ConnectionValidation.ValidateAddress(address).ShouldBeNull();

    [Theory]
    [InlineData("", "required")]
    [InlineData("localhost", "port")]
    [InlineData("localhost:0", "1 and 65535")]
    [InlineData("localhost:99999", "1 and 65535")]
    [InlineData("localhost:abc", "number")]
    [InlineData("[::1]", "port")]
    public void Invalid_addresses_report_error(string address, string expectedFragment)
    {
        var error = ConnectionValidation.ValidateAddress(address);

        _ = error.ShouldNotBeNull();
        error.ShouldContain(expectedFragment);
    }

    [Fact]
    public void Unix_socket_address_is_rejected_only_on_windows()
    {
        var result = ConnectionValidation.ValidateAddress("unix:///var/run/app.sock");

        if (OperatingSystem.IsWindows())
        {
            _ = result.ShouldNotBeNull();
            result.ShouldContain("Windows");
        }
        else
        {
            result.ShouldBeNull();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("10s")]
    [InlineData("500ms")]
    [InlineData("1.5m")]
    [InlineData("1h")]
    public void Valid_durations_pass(string? duration)
        => ConnectionValidation.ValidateDuration(duration).ShouldBeNull();

    [Theory]
    [InlineData("abc")]
    [InlineData("10x")]
    [InlineData("-5s")]
    public void Invalid_durations_report_error(string duration)
        => ConnectionValidation.ValidateDuration(duration).ShouldNotBeNull();

    [Fact]
    public void Connection_validity_requires_name_and_address()
    {
        var valid = new SavedConnection { Name = "x", Address = "localhost:9090" };
        ConnectionValidation.IsConnectionValid(valid).ShouldBeTrue();

        ConnectionValidation.IsConnectionValid(new SavedConnection { Name = "", Address = "localhost:9090" }).ShouldBeFalse();
        ConnectionValidation.IsConnectionValid(new SavedConnection { Name = "x", Address = "bad" }).ShouldBeFalse();
    }
}
