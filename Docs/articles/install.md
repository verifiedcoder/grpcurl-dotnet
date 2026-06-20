# Install &amp; releases

Every tagged release publishes ready-to-run binaries on the
[GitHub Releases page](https://github.com/verifiedcoder/grpcurl-dotnet/releases) — no NuGet,
no `dotnet tool`, no .NET runtime to install first. Each release contains three products,
one archive per platform:

| Product | What it is |
|---------|------------|
| **GrpCurl.Net Studio** | The desktop app (Avalonia). |
| **`grpcn`** | The grpcurl-compatible CLI. |
| **`gql2grpc`** | The GraphQL-to-gRPC proxy CLI. |

All binaries are **self-contained** (the .NET runtime is bundled) and **statically versioned**
to the release tag.

## Pick your platform

Archives are named `<product>-<rid>-<version>.<ext>`, where `<rid>` is one of:

| Runtime ID | Use on |
|------------|--------|
| `win-x64` | Windows 10 1809+ (Intel/AMD) |
| `win-arm64` | Windows on ARM |
| `osx-x64` | macOS 13+ (Intel) |
| `osx-arm64` | macOS 13+ (Apple Silicon) |
| `linux-x64` | Linux, glibc 2.35+ (Ubuntu 22.04+) |
| `linux-arm64` | Linux on ARM, glibc 2.35+ |

`win-*` archives are `.zip`; everything else is `.tar.gz` (the tarball preserves the executable bit).

## Verify your download (optional but recommended)

Each release includes a `SHA256SUMS` file covering every asset. After downloading:

```bash
# from the folder containing the downloaded files + SHA256SUMS
sha256sum --ignore-missing -c SHA256SUMS
```

On Windows (PowerShell):

```powershell
(Get-FileHash .\grpcn-win-x64-1.0.0.zip -Algorithm SHA256).Hash
# compare against the matching line in SHA256SUMS
```

## A note on code signing

These are **unsigned** binaries — this is a zero-budget, open-source project with no paid
code-signing certificate or Apple notarization. The first launch therefore shows an OS security
prompt; the per-platform steps below explain how to proceed. Verifying the `SHA256SUMS` is the
recommended integrity check.

## Windows

**CLIs:** unzip, then run the executable. To use it anywhere, put the folder on your `PATH`.

```powershell
grpcn.exe --version
```

**Studio:** unzip `GrpCurlNetStudio-win-x64-<version>.zip` and run `GrpCurl.Net.Studio.exe`.
SmartScreen will warn that the app is from an unknown publisher — choose **More info → Run anyway**.

## macOS

The Studio archive contains a `GrpCurl.Net Studio.app` bundle. It is **ad-hoc signed** (so it runs
on Apple Silicon) but **not notarized**, so Gatekeeper quarantines a freshly downloaded copy.

After extracting, either right-click the app and choose **Open** once (then confirm), or clear the
quarantine attribute:

```bash
xattr -dr com.apple.quarantine "GrpCurl.Net Studio.app"
open "GrpCurl.Net Studio.app"
```

**CLIs:** extract, mark executable if needed, and run:

```bash
tar -xzf grpcn-osx-arm64-1.0.0.tar.gz
chmod +x grpcn
./grpcn --version
```

Pick `osx-arm64` on Apple Silicon (M-series) and `osx-x64` on Intel Macs.

## Linux

```bash
tar -xzf grpcn-linux-x64-1.0.0.tar.gz
chmod +x grpcn
./grpcn --version
```

Studio extracts to a folder; run the `GrpCurl.Net.Studio` executable inside it. A desktop
environment and glibc 2.35+ are required.

## Updates

Studio checks for a newer release on launch (offline-safe; can be turned off in
**Settings → Updates**). When one is available it shows an **Update available** link in the status
bar that opens the Releases page — download and replace the app manually. There is no in-app
auto-installer.

The CLIs do not self-update; re-download to upgrade.

## Building from source

Prefer to build yourself? See the [CLI reference](cli-reference.md) and the repository README for
`dotnet build` / `dotnet run` instructions. The same `Scripts/package/publish.sh` the release
pipeline uses can produce a self-contained build for any single RID locally.
