using GrpCurl.Net.Commands;
using GrpCurl.Net.Tests.Integration.Fixtures;

namespace GrpCurl.Net.Tests.Integration.Commands;

/// <summary>
///     Regression tests for the review finding that <c>--revocation-mode</c> existed only on
///     <c>invoke</c>: discovery against a CA without CRL/OCSP endpoints (like the test
///     fixture) failed on <c>list</c>/<c>describe</c> because the default Online revocation
///     check could not complete. Both commands now accept the flag.
/// </summary>
[Collection("MTlsGrpcServer")]
public sealed class ListDescribeMTlsTests(MTlsGrpcTestFixture fixture)
{
    [Fact]
    public async Task ListWithCaAndClientCert_RevocationNocheck_Succeeds()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        // Act
        try

        // Assert
        {
            await ListCommandHandler.ExecuteAsync(
                address: fixture.Address,
                service: null,
                protosets: [],
                plaintext: false,
                insecure: false,
                cacert: fixture.CaCertPath,
                cert: fixture.ClientCertPath,
                key: fixture.ClientKeyPath,
                certPassword: null,
                connectTimeout: "10s",
                authority: null,
                serverName: "localhost",
                verbose: false,
                veryVerbose: false,
                userAgent: null,
                headers: [],
                reflectHeaders: [],
                protosetOut: null,
                maxTime: "10s",
                revocationMode: "nocheck");

            var captured = output.ToString();

            captured.ShouldContain("testing.TestService");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task DescribeWithCaAndClientCert_RevocationNocheck_Succeeds()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        // Act
        try

        // Assert
        {
            await DescribeCommandHandler.ExecuteAsync(
                address: fixture.Address,
                symbol: "testing.TestService",
                protosets: [],
                plaintext: false,
                insecure: false,
                cacert: fixture.CaCertPath,
                cert: fixture.ClientCertPath,
                key: fixture.ClientKeyPath,
                certPassword: null,
                connectTimeout: "10s",
                authority: null,
                serverName: "localhost",
                verbose: false,
                veryVerbose: false,
                userAgent: null,
                headers: [],
                reflectHeaders: [],
                msgTemplate: false,
                protosetOut: null,
                maxTime: "10s",
                revocationMode: "nocheck");

            var captured = output.ToString();

            captured.ShouldContain("testing.TestService");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
