using System.Diagnostics;
using System.Runtime.Versioning;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     macOS secret backend over the login Keychain via the always-present <c>security</c> tool
///     (generic passwords). Chosen over raw <c>Security.framework</c> CFDictionary interop for
///     reliability; the integration is the real Keychain. A locked keychain / tool failure throws so
///     the facade falls back to the encrypted-file store.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacKeychainSecretStore : ISecretStore
{
    private const string Service = "GrpCurl.Net Studio";

    public async Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
    {
        // -U updates an existing item instead of failing on duplicate.
        var (exit, _) = await RunAsync(["add-generic-password", "-a", keyRef, "-s", Service, "-w", value, "-U"], cancellationToken).ConfigureAwait(false);

        if (exit != 0)
        {
            throw new InvalidOperationException("security add-generic-password failed.");
        }
    }

    public async Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        var (exit, stdout) = await RunAsync(["find-generic-password", "-a", keyRef, "-s", Service, "-w"], cancellationToken).ConfigureAwait(false);
        return exit == 0 ? stdout.TrimEnd('\n', '\r') : null;
    }

    public async Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
        => await RunAsync(["delete-generic-password", "-a", keyRef, "-s", Service], cancellationToken).ConfigureAwait(false);

    private static async Task<(int ExitCode, string Output)> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("security")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the security tool.");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return (process.ExitCode, output);
    }
}
