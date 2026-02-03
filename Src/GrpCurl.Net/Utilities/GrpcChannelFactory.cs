using Grpc.Core;
using Grpc.Net.Client;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace GrpCurl.Net.Utilities;

/// <summary>
///     Factory for creating configured GrpcChannel instances with TLS and other options.
/// </summary>
internal static partial class GrpcChannelFactory
{
    public static GrpcChannel Create(string address, ChannelOptions? options = null)
    {
        options ??= new ChannelOptions();

        // Ensure address has a scheme
        if (!address.StartsWith("http://") && !address.StartsWith("https://"))
        {
            address = options.Plaintext ? $"http://{address}" : $"https://{address}";
        }

        var channelOptions = new GrpcChannelOptions
        {
            MaxReceiveMessageSize = options.MaxReceiveMessageSize,
            MaxSendMessageSize = options.MaxSendMessageSize
        };

        // Configure HTTP handler for TLS and other options
        if (options is { Plaintext: true, InsecureSkipVerify: false, CaCertPath: null, ClientCertPath: null })
        {
            return GrpcChannel.ForAddress(address, channelOptions);
        }

        var httpHandler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = options.KeepaliveTime ?? TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = options.KeepaliveTime ?? TimeSpan.FromSeconds(30),
            EnableMultipleHttp2Connections = true,

            // Apply connection timeout (default: 10 seconds for parity with grpcurl)
            ConnectTimeout = options.ConnectTimeout ?? TimeSpan.FromSeconds(10)
        };

        // Configure TLS server name for certificate validation
        // ServerName takes precedence over Authority for TLS SNI
        if (options.ServerName is not null)
        {
            httpHandler.SslOptions.TargetHost = options.ServerName;
        }
        else if (options.Authority is not null)
        {
            // Fallback to authority if servername not specified
            httpHandler.SslOptions.TargetHost = options.Authority;
        }

        // Configure TLS
        if (options.InsecureSkipVerify)
        {
            httpHandler.SslOptions.RemoteCertificateValidationCallback ??= (_, _, _, _) => true;
        }
        else if (options.CaCertPath is not null)
        {
            var caCert = X509CertificateLoader.LoadCertificateFromFile(options.CaCertPath);

            httpHandler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, sslPolicyErrors) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                // Check for hostname mismatch - SslPolicyErrors is a flags enum
                // Reject if there's a name mismatch (certificate CN/SAN doesn't match TargetHost)
                if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                {
                    return false;
                }

                // Check for other errors besides chain errors (which we'll validate with custom CA)
                // Remove RemoteCertificateChainErrors flag to check if there are other errors
                var nonChainErrors = sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors;

                if (nonChainErrors != SslPolicyErrors.None)
                {
                    // Some other error besides chain errors - fail validation
                    return false;
                }

                // Validate certificate chain with custom CA
                var chainPolicy = new X509Chain();

                chainPolicy.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chainPolicy.ChainPolicy.CustomTrustStore.Add(caCert);
                chainPolicy.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                var x509Cert = new X509Certificate2(certificate);

                return chainPolicy.Build(x509Cert);
            };
        }

        // Configure client certificates (mutual TLS)
        if (options.ClientCertPath is not null)
        {
            X509Certificate2 clientCert;

            // Check if it's a PKCS12 file (.p12 or .pfx)
            if (options.ClientCertPath.EndsWith(".p12", StringComparison.OrdinalIgnoreCase) ||
                options.ClientCertPath.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase))
            {
                // Load PKCS12 with optional password
                clientCert = X509CertificateLoader.LoadPkcs12FromFile(
                    options.ClientCertPath,
                    options.ClientCertPassword,
                    X509KeyStorageFlags.Exportable);
            }
            else if (options.ClientKeyPath is not null)
            {
                // PEM format with separate key file
                clientCert = X509Certificate2.CreateFromPemFile(options.ClientCertPath, options.ClientKeyPath);
            }
            else
            {
                throw new ArgumentException(
                    "Client certificate requires either a PKCS12 file (.p12/.pfx) or both --cert and --key for PEM files");
            }

            httpHandler.SslOptions.ClientCertificates = [clientCert];
        }

        channelOptions.HttpHandler = httpHandler;

        return GrpcChannel.ForAddress(address, channelOptions);
    }

    /// <summary>
    ///     Creates metadata from header strings in "name: value" format.
    /// </summary>
    /// <param name="headers">Header strings in "name: value" format</param>
    /// <param name="userAgent">Optional user-agent header value. If not specified, defaults to "grpcurl-dotnet/1.0.0"</param>
    public static Metadata CreateMetadata(IEnumerable<string>? headers, string? userAgent = null)
    {
        var metadata = new Metadata();

        // Add user-agent header first
        var effectiveUserAgent = userAgent ?? "grpcurl-dotnet/1.0.0";

        metadata.Add("user-agent", effectiveUserAgent);

        if (headers is null)
        {
            return metadata;
        }

        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var parts = header.Split(':', 2);

            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid header format: {header}. Expected 'name: value'");
            }

            var name = parts[0].Trim();
            var value = parts[1].Trim();

            // Expand environment variables if in ${VAR} format
            value = ExpandEnvironmentVariables(value, header);

            metadata.Add(name, value);
        }

        return metadata;
    }

    /// <summary>
    ///     Expands environment variables in the format ${VAR_NAME}.
    /// </summary>
    /// <param name="value">The value containing environment variable references.</param>
    /// <param name="headerContext">The full header string for error context.</param>
    private static string ExpandEnvironmentVariables(string value, string headerContext)
    {
        var result = value;
        var startIndex = 0;

        while (true)
        {
            var start = result.IndexOf("${", startIndex, StringComparison.Ordinal);

            if (start == -1)
            {
                break;
            }

            var end = result.IndexOf('}', start + 2);

            if (end == -1)
            {
                break;
            }

            var varName = result.Substring(start + 2, end - start - 2);
            var varValue = Environment.GetEnvironmentVariable(varName);

            if (varValue is null)
            {
                throw new ArgumentException(
                    $"Environment variable '${{{varName}}}' not found. Header: '{headerContext}'");
            }

            result = result[..start] + varValue + result[(end + 1)..];

            startIndex = start + varValue.Length;
        }

        return result;
    }

    /// <summary>
    ///     Parses a duration string in formats like "10s", "1m", "500ms", "1.5m".
    ///     Plain numbers are treated as seconds for compatibility.
    /// </summary>
    /// <param name="duration">Duration string to parse</param>
    /// <returns>TimeSpan representing the duration</returns>
    /// <exception cref="ArgumentException">Thrown when duration format is invalid</exception>
    public static TimeSpan ParseDuration(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            throw new ArgumentException("Duration cannot be empty", nameof(duration));
        }

        // Match pattern: optional number (with decimal), optional unit
        var match = DurationRegex().Match(duration);

        if (!match.Success)
        {
            throw new ArgumentException($"Invalid duration format: '{duration}'. Expected formats: '10s', '1m', '500ms', '1.5h', or plain number for seconds");
        }

        if (!double.TryParse(match.Groups[1].Value, out var value))
        {
            throw new ArgumentException($"Invalid numeric value in duration: '{duration}'");
        }

        if (value < 0)
        {
            throw new ArgumentException($"Duration must be positive: '{duration}'");
        }

        var unit = match.Groups[2].Value;

        return unit switch
        {
            "ms" => TimeSpan.FromMilliseconds(value),
            "s" => TimeSpan.FromSeconds(value),
            "m" => TimeSpan.FromMinutes(value),
            "h" => TimeSpan.FromHours(value),
            "" => TimeSpan.FromSeconds(value), // Default to seconds for compatibility
            _ => throw new ArgumentException($"Unknown duration unit: '{unit}'")
        };
    }

    /// <summary>
    ///     Parses a size string in formats like "4MB", "10MB", "1GB".
    ///     Plain numbers are treated as bytes.
    /// </summary>
    /// <param name="size">Size string to parse</param>
    /// <returns>Integer representing size in bytes</returns>
    /// <exception cref="ArgumentException">Thrown when size format is invalid</exception>
    public static int ParseSize(string size)
    {
        if (string.IsNullOrWhiteSpace(size))
        {
            throw new ArgumentException("Size cannot be empty", nameof(size));
        }

        // Match pattern: number (with optional decimal), optional unit (case-insensitive)
        var match = SizeRegex().Match(size);

        if (!match.Success)
        {
            throw new ArgumentException($"Invalid size format: '{size}'. Expected formats: '4MB', '10MB', '1GB', or plain number for bytes");
        }

        if (!double.TryParse(match.Groups[1].Value, out var value))
        {
            throw new ArgumentException($"Invalid numeric value in size: '{size}'");
        }

        if (value < 0)
        {
            throw new ArgumentException($"Size must be positive: '{size}'");
        }

        var unit = match.Groups[2].Value.ToUpperInvariant();

        var bytes = unit switch
        {
            "B" or "" => value, // Plain number or explicit bytes
            "KB" => value * 1024,
            "MB" => value * 1024 * 1024,
            "GB" => value * 1024 * 1024 * 1024,
            _ => throw new ArgumentException($"Unknown size unit: '{unit}'")
        };

        // Check for overflow
        if (bytes > int.MaxValue)
        {
            throw new ArgumentException($"Size too large: '{size}'. Maximum is {int.MaxValue} bytes (2GB)");
        }

        return (int)bytes;
    }

    public class ChannelOptions
    {
        public bool Plaintext { get; init; }

        public bool InsecureSkipVerify { get; init; }

        public string? CaCertPath { get; init; }

        public string? ClientCertPath { get; init; }

        public string? ClientKeyPath { get; init; }

        public string? ClientCertPassword { get; init; }

        public TimeSpan? ConnectTimeout { get; init; }

        public TimeSpan? KeepaliveTime { get; init; }

        public int? MaxReceiveMessageSize { get; init; }

        public int? MaxSendMessageSize { get; init; }

        public string? Authority { get; init; }

        public string? ServerName { get; init; }
    }

    [GeneratedRegex(@"^(\d+\.?\d*)(ms|s|m|h)?$")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"^(\d+\.?\d*)\s*(B|KB|MB|GB)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();
}