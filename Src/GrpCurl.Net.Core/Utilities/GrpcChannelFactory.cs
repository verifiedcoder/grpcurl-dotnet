using Grpc.Core;
using Grpc.Net.Client;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
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

        /*
         * Unix-domain socket addresses are accepted as `unix:///absolute/path`. They are
         * valid on Linux and macOS; Windows fails fast with a clear error rather than
         * pretending to dial a TCP endpoint. Grpc.Net.Client doesn't natively resolve
         * unix: schemes, so we plug a ConnectCallback on the SocketsHttpHandler and ask
         * it to open a UnixDomainSocketEndPoint instead.
         */
        var unixSocketPath = TryExtractUnixSocketPath(address);

        if (unixSocketPath is not null)
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsFreeBSD())
            {
                throw new PlatformNotSupportedException(
                    $"Unix domain sockets are only supported on Unix-like platforms. " +
                    $"Cannot dial '{address}' on {Environment.OSVersion.Platform}.");
            }

            /*
             * Grpc.Net.Client requires an http(s):// scheme even when the actual transport
             * is a Unix socket, so we feed it a placeholder address and let the connect
             * callback redirect the socket.
             */
            return CreateUnixSocketChannel(unixSocketPath, options);
        }

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

        // Fast path: only when there are absolutely no handler-customising options.
        // Anything that needs SocketsHttpHandler — connect timeout, keepalive, SNI/authority,
        // TLS material, custom CA, mTLS, or insecure-skip-verify — falls through to the
        // explicit handler construction below so the configured values actually take effect.
        // Bug history: previously the fast path triggered for any plaintext call, silently
        // discarding --connect-timeout (see CODE-REVIEW.md P1 "plaintext --connect-timeout").
        if (options is
        {
            Plaintext: true,
            InsecureSkipVerify: false,
            CaCertPath: null,
            ClientCertPath: null,
            ConnectTimeout: null,
            KeepaliveTime: null,
            Authority: null,
            ServerName: null
        })
        {
            return GrpcChannel.ForAddress(address, channelOptions);
        }

        var keepaliveTime = options.KeepaliveTime ?? TimeSpan.FromSeconds(60);
        var keepaliveTimeout = options.KeepaliveTimeout ?? TimeSpan.FromSeconds(30);

        var httpHandler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = keepaliveTime,
            KeepAlivePingTimeout = keepaliveTimeout,
            EnableMultipleHttp2Connections = true,

            // Apply connection timeout (default: 10 seconds for parity with grpcurl).
            // Applies to both plaintext and TLS paths now that we never short-circuit when
            // a timeout is configured.
            ConnectTimeout = options.ConnectTimeout ?? TimeSpan.FromSeconds(10)
        };

        // --servername drives TLS SNI / TargetHost only. --authority no longer touches TLS
        // SNI — it rewrites the HTTP/2 :authority pseudo-header instead, applied via the
        // AuthorityOverrideHandler below. This separation matches upstream grpcurl, where
        // -servername controls cert validation and -authority controls virtual hosting.
        if (options.ServerName is not null)
        {
            httpHandler.SslOptions.TargetHost = options.ServerName;
        }

        // Configure TLS
        if (options.InsecureSkipVerify)
        {
            httpHandler.SslOptions.RemoteCertificateValidationCallback ??= (_, _, _, _) => true;
        }
        else if (options.CaCertPath is not null)
        {
            var caCert = X509CertificateLoader.LoadCertificateFromFile(options.CaCertPath);
            // Revocation defaults to Online when a custom CA is supplied so revoked
            // certificates are rejected — previously this was hard-coded to NoCheck which
            // accepted revoked certs (see CODE-REVIEW.md P2 "TLS Hardening Gaps"). The
            // operator can override with --revocation-mode for air-gapped environments.
            var revocationMode = options.RevocationMode ?? X509RevocationMode.Online;

            httpHandler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, sslPolicyErrors) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                {
                    return false;
                }

                var nonChainErrors = sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors;

                if (nonChainErrors != SslPolicyErrors.None)
                {
                    return false;
                }

                using var chainPolicy = new X509Chain();

                chainPolicy.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chainPolicy.ChainPolicy.CustomTrustStore.Add(caCert);
                chainPolicy.ChainPolicy.RevocationMode = revocationMode;

                using var x509Cert = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());

                return chainPolicy.Build(x509Cert);
            };
        }

        // Configure client certificates (mutual TLS).
        // PKCS12 detection is content-based: the loader is asked to parse the file as
        // PKCS12 first, and falls back to PEM if that throws. This matches the documented
        // behaviour in Docs/articles/authentication.md and replaces the previous
        // extension-based check that misled callers who used extensionless filenames.
        // Default storage is ephemeral on platforms whose TLS stack supports it. Windows
        // SslStream client authentication cannot use ephemeral private keys, and macOS
        // requires keychain-backed PFX imports, so those platforms use non-exportable
        // platform storage; opt in to Exportable only when the operator explicitly asks
        // via --exportable-key.
        if (options.ClientCertPath is not null)
        {
            X509Certificate2 clientCert;

            var storageFlags = GetClientCertificateStorageFlags(options.ExportableClientKey);

            // Content-based detection: try PKCS12 first because its magic bytes are stable.
            // If parsing fails for any reason, fall through to PEM (cert + separate key).
            try
            {
                clientCert = X509CertificateLoader.LoadPkcs12FromFile(
                    options.ClientCertPath,
                    options.ClientCertPassword,
                    storageFlags);
            }
            catch (CryptographicException) when (options.ClientKeyPath is not null)
            {
                using var pemCert = X509Certificate2.CreateFromPemFile(options.ClientCertPath, options.ClientKeyPath);
                clientCert = X509CertificateLoader.LoadPkcs12(
                    pemCert.Export(X509ContentType.Pkcs12),
                    null,
                    storageFlags);
            }
            catch (CryptographicException ex)
            {
                throw new ArgumentException(
                    $"Could not load client certificate at '{options.ClientCertPath}'. " +
                    "Supply --key for PEM cert+key pairs, or provide a valid PKCS12 bundle " +
                    "(with --cert-password if it is encrypted).",
                    ex);
            }

            httpHandler.SslOptions.ClientCertificates = [clientCert];
        }

        // When --authority is set, wrap the socket handler so every outgoing request has
        // its Host header overwritten — HTTP/2 maps Host to :authority. Affects both
        // reflection and the business RPC because the same channel backs both.
        HttpMessageHandler outerHandler = httpHandler;

        if (!string.IsNullOrEmpty(options.Authority))
        {
            outerHandler = new AuthorityOverrideHandler(options.Authority, httpHandler);
        }

        channelOptions.HttpHandler = outerHandler;

        return GrpcChannel.ForAddress(address, channelOptions);
    }

    /// <summary>
    ///     Returns the Unix socket path encoded in <paramref name="address" /> if it begins
    ///     with the <c>unix://</c> or <c>unix:</c> scheme, otherwise <see langword="null" />.
    /// </summary>
    /// <remarks>
    ///     Accepted forms (matching upstream grpcurl):
    ///     <list type="bullet">
    ///         <item>
    ///             <description><c>unix:///var/run/foo.sock</c> (absolute path with triple slash)</description>
    ///         </item>
    ///         <item>
    ///             <description><c>unix:/var/run/foo.sock</c> (single slash variant)</description>
    ///         </item>
    ///     </list>
    /// </remarks>
    internal static string? TryExtractUnixSocketPath(string address)
    {
        const string prefix = "unix:";

        if (!address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = address[prefix.Length..];

        // Strip the optional double-slash from unix:// for parity with upstream grpcurl.
        if (path.StartsWith("//", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        return string.IsNullOrEmpty(path) ? null : path;
    }

    private static GrpcChannel CreateUnixSocketChannel(string socketPath, ChannelOptions options)
    {
        var channelOptions = new GrpcChannelOptions
        {
            MaxReceiveMessageSize = options.MaxReceiveMessageSize,
            MaxSendMessageSize = options.MaxSendMessageSize
        };

        var keepaliveTime = options.KeepaliveTime ?? TimeSpan.FromSeconds(60);
        var keepaliveTimeout = options.KeepaliveTimeout ?? TimeSpan.FromSeconds(30);

        var httpHandler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = keepaliveTime,
            KeepAlivePingTimeout = keepaliveTimeout,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = options.ConnectTimeout ?? TimeSpan.FromSeconds(10),
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(
                    AddressFamily.Unix,
                    SocketType.Stream,
                    ProtocolType.Unspecified);

                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken).ConfigureAwait(false);

                    return new NetworkStream(socket, true);
                }
                catch
                {
                    socket.Dispose();

                    throw;
                }
            }
        };

        HttpMessageHandler outer = httpHandler;

        if (!string.IsNullOrEmpty(options.Authority))
        {
            outer = new AuthorityOverrideHandler(options.Authority, httpHandler);
        }

        channelOptions.HttpHandler = outer;

        // The address is fictional — the connect callback is what actually opens the
        // Unix socket. Grpc.Net.Client still requires a syntactically valid URL.
        return GrpcChannel.ForAddress($"http://{Path.GetFileName(socketPath)}.local", channelOptions);
    }

    /// <summary>
    ///     Creates metadata from header strings in "name: value" format.
    /// </summary>
    /// <param name="headers">Header strings in "name: value" format</param>
    /// <param name="userAgent">Optional user-agent header value. Defaults to <see cref="UserAgentProvider.Default" />.</param>
    public static Metadata CreateMetadata(IEnumerable<string>? headers, string? userAgent = null)
    {
        var metadata = new Metadata();

        // Add user-agent header first
        var effectiveUserAgent = userAgent ?? UserAgentProvider.Default;

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

            // Binary metadata (RFC 7540 / gRPC spec): header names ending in -bin carry
            // base64-encoded byte payloads on the wire. The CLI accepts the base64 form
            // verbatim and decodes it here so callers can pass arbitrary binary payloads
            // (e.g. -H "trace-bin: AQIDBA==") and have them transmitted as bytes.
            if (name.EndsWith("-bin", StringComparison.OrdinalIgnoreCase))
            {
                byte[] decoded;

                try
                {
                    decoded = Convert.FromBase64String(value);
                }
                catch (FormatException ex)
                {
                    throw new ArgumentException(
                        $"Header '{name}' ends in -bin but its value is not valid base64. " +
                        "Binary metadata values must be base64-encoded on the wire.",
                        ex);
                }

                metadata.Add(name, decoded);

                continue;
            }

            metadata.Add(name, value);
        }

        return metadata;
    }

    /// <summary>
    ///     Expands environment variables in the format ${VAR_NAME}.
    /// </summary>
    /// <param name="value">The value containing environment variable references.</param>
    /// <param name="headerContext">The full header string for error context.</param>
    internal static string ExpandEnvironmentVariables(string value, string headerContext)
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
            var varValue = Environment.GetEnvironmentVariable(varName)
                           ?? throw new ArgumentException($"Environment variable '${{{varName}}}' not found. Header: '{headerContext}'");

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
            "s"  => TimeSpan.FromSeconds(value),
            "m"  => TimeSpan.FromMinutes(value),
            "h"  => TimeSpan.FromHours(value),
            ""   => TimeSpan.FromSeconds(value), // Default to seconds for compatibility
            _    => throw new ArgumentException($"Unknown duration unit: '{unit}'")
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
            "KB"      => value * 1024,
            "MB"      => value * 1024 * 1024,
            "GB"      => value * 1024 * 1024 * 1024,
            _         => throw new ArgumentException($"Unknown size unit: '{unit}'")
        };

        // Check for overflow
        if (bytes > int.MaxValue)
        {
            throw new ArgumentException($"Size too large: '{size}'. Maximum is {int.MaxValue} bytes (2GB)");
        }

        return (int)bytes;
    }

    /// <summary>
    ///     Parses a <c>--revocation-mode</c> CLI value into an <see cref="X509RevocationMode" />.
    ///     Accepts "online", "offline", and "nocheck" (also "no-check"/"none").
    ///     Returns <see langword="null" /> for empty input so the channel default applies.
    /// </summary>
    /// <param name="mode">Revocation mode string to parse</param>
    /// <exception cref="ArgumentException">Thrown when the mode is not recognised</exception>
    public static X509RevocationMode? ParseRevocationMode(string? mode)
    {
        if (string.IsNullOrEmpty(mode))
        {
            return null;
        }

        return mode.ToLowerInvariant() switch
        {
            "online"                          => X509RevocationMode.Online,
            "offline"                         => X509RevocationMode.Offline,
            "nocheck" or "no-check" or "none" => X509RevocationMode.NoCheck,
            _ => throw new ArgumentException(
                $"Unknown --revocation-mode '{mode}'. Expected: online, offline, nocheck.",
                nameof(mode))
        };
    }

    internal static X509KeyStorageFlags GetClientCertificateStorageFlags(bool exportableClientKey)
    {
        if (exportableClientKey)
        {
            return X509KeyStorageFlags.Exportable;
        }

        if (OperatingSystem.IsWindows())
        {
            return X509KeyStorageFlags.UserKeySet;
        }

        return OperatingSystem.IsMacOS()
            ? X509KeyStorageFlags.DefaultKeySet
            : X509KeyStorageFlags.EphemeralKeySet;
    }

    [GeneratedRegex(@"^(\d+\.?\d*)(ms|s|m|h)?$")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"^(\d+\.?\d*)\s*(B|KB|MB|GB)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();

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

        public TimeSpan? KeepaliveTimeout { get; init; }

        public int? MaxReceiveMessageSize { get; init; }

        public int? MaxSendMessageSize { get; init; }

        public string? Authority { get; init; }

        public string? ServerName { get; init; }

        /// <summary>
        ///     Revocation policy for the custom-CA chain validator. Default is
        ///     <see cref="X509RevocationMode.Online" /> when a custom CA is supplied; set
        ///     to <see cref="X509RevocationMode.NoCheck" /> for air-gapped environments via
        ///     <c>--revocation-mode nocheck</c>.
        /// </summary>
        public X509RevocationMode? RevocationMode { get; init; }

        /// <summary>
        ///     If <see langword="true" />, PKCS12 client keys are loaded with
        ///     <see cref="X509KeyStorageFlags.Exportable" />. Linux defaults to ephemeral
        ///     storage, macOS uses platform default keychain handling, and Windows uses a
        ///     non-exportable user key set because SslStream client authentication cannot
        ///     use ephemeral private keys there. Opt in only when an upstream operation
        ///     needs to re-export the private key.
        /// </summary>
        public bool ExportableClientKey { get; init; }
    }
}
