using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Tests.Unit.Utilities;

[Collection("EnvironmentVariables")]
public sealed class ProxyEnvironmentTests : IDisposable
{
    private static readonly string[] AllVariables =
    [
        "HTTP_PROXY", "http_proxy",
        "HTTPS_PROXY", "https_proxy",
        "ALL_PROXY", "all_proxy",
        "NO_PROXY", "no_proxy"
    ];

    private readonly Dictionary<string, string?> _saved = [];

    public ProxyEnvironmentTests()
    {
        foreach (var name in AllVariables)
        {
            _saved[name] = Environment.GetEnvironmentVariable(name);

            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    [Fact]
    public void GetActiveProxyVariables_NoneSet_ReturnsEmpty()
    {
        ProxyEnvironment.GetActiveProxyVariables("localhost:9090").ShouldBeEmpty();
    }

    [Fact]
    public void GetActiveProxyVariables_HttpProxySet_ReturnsVariableName()
    {
        Environment.SetEnvironmentVariable("HTTP_PROXY", "http://proxy.corp:3128");

        var active = ProxyEnvironment.GetActiveProxyVariables("localhost:9090");

        active.ShouldBe(["HTTP_PROXY"]);
    }

    [Fact]
    public void GetActiveProxyVariables_MultipleSet_ReturnsAll()
    {
        Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://proxy.corp:3128");
        Environment.SetEnvironmentVariable("all_proxy", "socks5://proxy.corp:1080");

        var active = ProxyEnvironment.GetActiveProxyVariables("api.example.com:443");

        // Canonical (upper-case) names, regardless of which case form was set; ALL_PROXY
        // is reported here even though all_proxy was the form set (lower-case input).
        active.ShouldBe(["HTTPS_PROXY", "ALL_PROXY"]);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("other.host,localhost")]
    [InlineData("*")]
    public void GetActiveProxyVariables_HostExcludedByNoProxy_ReturnsEmpty(string noProxy)
    {
        Environment.SetEnvironmentVariable("HTTP_PROXY", "http://proxy.corp:3128");
        Environment.SetEnvironmentVariable("NO_PROXY", noProxy);

        ProxyEnvironment.GetActiveProxyVariables("localhost:9090").ShouldBeEmpty();
    }

    [Fact]
    public void GetActiveProxyVariables_NoProxyDomainSuffix_MatchesSubdomains()
    {
        Environment.SetEnvironmentVariable("HTTP_PROXY", "http://proxy.corp:3128");
        Environment.SetEnvironmentVariable("NO_PROXY", ".example.com");

        ProxyEnvironment.GetActiveProxyVariables("svc.example.com:443").ShouldBeEmpty();
        ProxyEnvironment.GetActiveProxyVariables("example.org:443").ShouldNotBeEmpty();
    }

    [Fact]
    public void GetActiveProxyVariables_UnixSocketAddress_ReturnsEmpty()
    {
        Environment.SetEnvironmentVariable("HTTP_PROXY", "http://proxy.corp:3128");

        ProxyEnvironment.GetActiveProxyVariables("unix:///var/run/app.sock").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("localhost:9090", "localhost")]
    [InlineData("https://api.example.com:8443", "api.example.com")]
    [InlineData("127.0.0.1:50051", "127.0.0.1")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void ExtractHost_ParsesAddressForms(string? address, string? expected)
    {
        ProxyEnvironment.ExtractHost(address).ShouldBe(expected);
    }
}
