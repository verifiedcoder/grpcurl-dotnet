using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class LinuxLibsecretSecretStoreTests
{
    private const string ChildMarker = "GRPCURL_TEST_HEADLESS_SECRET_CHILD";

    [Fact]
    public async Task Headless_native_failures_still_require_explicit_fallback_authorization()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Linux Secret Service only.");
            return;
        }
        if (!NativeLibrary.TryLoad("libsecret-1.so.0", out var library))
        {
            Assert.Skip("Requires libsecret-1.so.0 (installed in the Linux CI lanes).");
            return;
        }
        NativeLibrary.Free(library);

        var directory = Path.Combine(Path.GetTempPath(), "grpcn-headless-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (Environment.GetEnvironmentVariable(ChildMarker) == "1")
            {
                var native = new LinuxLibsecretSecretStore();
                _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => native.GetAsync("probe"));
                foreach (var mode in new[] { SecretFallbackMode.RequireConsent, SecretFallbackMode.Allow, SecretFallbackMode.Block })
                {
                    var path = Path.Combine(directory, mode.ToString());
                    using var store = new SecretStore(path, native, new EncryptedFileSecretStore(path), new(mode));
                    Directory.Exists(path).ShouldBeFalse();
                    if (mode != SecretFallbackMode.Allow)
                    {
                        _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => store.SetAsync("key", "value"));
                        Directory.Exists(path).ShouldBeFalse();
                        store.State.CanEnableFallback.ShouldBe(mode == SecretFallbackMode.RequireConsent);
                        if (mode == SecretFallbackMode.Block)
                        {
                            _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => store.EnableFallbackAsync());
                            continue;
                        }
                        await store.EnableFallbackAsync();
                    }
                    await store.SetAsync("key", "value");
                    (await store.GetAsync("key")).ShouldBe("value");
                }
                return;
            }

            // GIO caches its session bus process-wide. Use fresh processes so tests neither reuse a
            // desktop connection nor change the environment seen by other concurrently running tests.
            _ = Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var refusedPath = Path.Combine(directory, "refused");
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(refusedPath)); // stay bound but never listen
            foreach (var address in new[] { null, string.Empty, "unix:path=" + Path.Combine(directory, "missing"), "unix:path=" + refusedPath })
            {
                var start = new ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                start.ArgumentList.Add(typeof(LinuxLibsecretSecretStoreTests).Assembly.Location);
                start.ArgumentList.Add("-method");
                start.ArgumentList.Add($"{typeof(LinuxLibsecretSecretStoreTests).FullName}.{nameof(Headless_native_failures_still_require_explicit_fallback_authorization)}");
                start.ArgumentList.Add("-failSkips");
                start.ArgumentList.Add("-noAutoReporters");
                start.ArgumentList.Add("-noColor");
                start.Environment[ChildMarker] = "1";
                _ = start.Environment.Remove("DISPLAY");
                _ = start.Environment.Remove("DBUS_STARTER_ADDRESS");
                _ = start.Environment.Remove("DBUS_STARTER_BUS_TYPE");
                start.Environment["XDG_RUNTIME_DIR"] = directory;
                if (address is null)
                {
                    _ = start.Environment.Remove("DBUS_SESSION_BUS_ADDRESS");
                }
                else
                {
                    start.Environment["DBUS_SESSION_BUS_ADDRESS"] = address;
                }
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                try
                {
                    await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30));
                    process.ExitCode.ShouldBe(0, $"D-Bus address '{address ?? "<unset>"}':\n{await output}\n{await error}");
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                    _ = await Task.WhenAll(output, error);
                }
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
