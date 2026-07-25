# Install Studio

Studio ships as a **self-contained** download — no .NET runtime to install first. Builds are published
on the [GitHub Releases page](https://github.com/verifiedcoder/grpcurl-dotnet/releases) for six platforms:
Windows, macOS, and Linux on x64 and arm64. See the general [Install & releases](../install.md) page for
the full asset list and `SHA256SUMS` verification steps.

The binaries are **unsigned** (the project ships at zero cost), so each OS shows a one-time "unverified
publisher" prompt. The steps below clear it.

## Windows

1. Download `GrpCurlNetStudio-win-x64-<version>.zip` (or `win-arm64`) and unzip it.
2. Run `GrpCurl.Net.Studio.exe`.
3. SmartScreen may show **"Windows protected your PC."** Click **More info → Run anyway**. This is
   expected for unsigned apps and only appears the first time.

## macOS

1. Download `GrpCurlNetStudio-osx-arm64-<version>.tar.gz` (Apple Silicon) or `osx-x64` (Intel) and
   extract it — you get `GrpCurl.Net Studio.app`.
2. The app is **ad-hoc signed** (free; not notarized), so the first launch is quarantined. Either:
   - **Right-click** the app → **Open** → **Open** in the dialog, or
   - clear the quarantine flag from Terminal: `xattr -dr com.apple.quarantine "GrpCurl.Net Studio.app"`.
3. Requires macOS 13 or newer.

## Linux

1. Download `GrpCurlNetStudio-linux-x64-<version>.tar.gz` (or `linux-arm64`) and extract it.
2. Make the launcher executable if needed and run it: `chmod +x GrpCurl.Net.Studio && ./GrpCurl.Net.Studio`.
3. Built against glibc 2.35 (Ubuntu 22.04 and newer).

## Updates

Studio checks the Releases page on launch (when enabled in **Settings → Updates**) and shows a status-bar
link when a newer version exists. There is no in-app auto-apply — the link opens the Releases page so you
download and replace the app yourself. See [Troubleshooting → Updates](troubleshooting.md#updates).

> _📷 Screenshot: the Settings → Updates section._
