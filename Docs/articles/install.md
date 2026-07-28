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

## Verify your download

Every release asset carries cryptographic provenance: a [Sigstore](https://www.sigstore.dev/)-signed
[SLSA](https://slsa.dev/) attestation that binds the file to the commit, tag and workflow that built
it. That is what lets you prove where a binary came from — a checksum published beside the file it
describes cannot do that on its own.

**Expected publisher identity.** Every attestation and signature on a genuine release asset carries
these claims. If a verification command reports anything else, do not run the binary.

| Claim | Value |
|-------|-------|
| Repository | `verifiedcoder/grpcurl-dotnet` |
| Workflow | `.github/workflows/release.yml` |
| Ref | `refs/tags/v<version>` |
| OIDC issuer | `https://token.actions.githubusercontent.com` |

### 1. Verify build provenance (strongest check)

Requires the [GitHub CLI](https://cli.github.com/) 2.49 or newer. The command hashes your local file
and checks it against the attestations GitHub holds, so it needs network access to `api.github.com`:

```bash
gh attestation verify grpcn-linux-x64-1.0.0.tar.gz \
  --repo verifiedcoder/grpcurl-dotnet \
  --signer-workflow verifiedcoder/grpcurl-dotnet/.github/workflows/release.yml \
  --source-ref refs/tags/v1.0.0
```

`--repo` on its own is a weak policy: it accepts an attestation produced by *any* workflow on *any*
ref in this repository. `--signer-workflow` and `--source-ref` are what make the command actually
enforce the publisher identity in the table above, so keep them.

The command prints the workflow, commit and ref the artifact was built from, and fails if the file
was modified, was not produced by this repository's release workflow, or has no attestation at all.

**Air-gapped verification.** Fetch the attestation bundle once on a connected machine
(`gh attestation download <asset> --repo verifiedcoder/grpcurl-dotnet`), carry the resulting
`.jsonl` across, and verify with `--bundle <file>` and the same policy flags. Without `--bundle`,
`gh attestation verify` requires network access.

### 2. Verify the signed checksum manifest

`SHA256SUMS` is itself signed keylessly with [cosign](https://docs.sigstore.dev/) (v3.x); the
signature ships beside it as `SHA256SUMS.sigstore.json`:

```bash
cosign verify-blob SHA256SUMS --bundle SHA256SUMS.sigstore.json \
  --certificate-identity-regexp '^https://github.com/verifiedcoder/grpcurl-dotnet/\.github/workflows/release\.yml@refs/tags/v' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
```

The `refs/tags/v` suffix matters: it accepts only a signature produced by a tagged release run, so a
signature from a branch or a dry run is rejected.

### 3. Check the hashes

Once the manifest is trusted, every asset hash chains off it:

```bash
# from the folder containing the downloaded files + SHA256SUMS
sha256sum --ignore-missing -c SHA256SUMS
```

On Windows (PowerShell):

```powershell
(Get-FileHash .\grpcn-win-x64-1.0.0.zip -Algorithm SHA256).Hash
# compare against the matching line in SHA256SUMS
```

## Software bill of materials

Each archive is published with a matching CycloneDX SBOM — `<product>-<rid>-<version>.cdx.json` —
listing every NuGet package that went into it plus the exact .NET runtime pack the self-contained
build embedded (with the SHA-512 NuGet records for it), each with versions and licences. The SBOM is
attested to its artifact, so you can confirm the inventory belongs to the binary you downloaded:

```bash
gh attestation verify grpcn-linux-x64-1.0.0.tar.gz \
  --repo verifiedcoder/grpcurl-dotnet \
  --signer-workflow verifiedcoder/grpcurl-dotnet/.github/workflows/release.yml \
  --source-ref refs/tags/v1.0.0 \
  --predicate-type https://cyclonedx.org/bom
```

Add `--format json --jq '.[0].verificationResult.statement.predicate'` to print the attested SBOM
itself, which lets you confirm the published `.cdx.json` asset is the document that was signed —
the release pipeline runs exactly that comparison before drafting a release.

Note that the SBOM enumerates the package graph and the runtime pack; the runtime pack's own internal
components are covered by the runtime's third-party notice, which ships in every archive (below).

## Licence and third-party notices

Every archive ships four legal files:

| File | Covers |
|------|--------|
| `LICENSE` | GrpCurl.Net itself (MIT) |
| `THIRD-PARTY-NOTICES.md` | every bundled NuGet package, with licence text where the package supplies it |
| `LICENSE.dotnet-runtime.txt` | the embedded .NET runtime (MIT) |
| `THIRD-PARTY-NOTICES.dotnet-runtime.txt` | the runtime's own third-party attributions, including non-MIT terms |

The last two are shipped verbatim from the runtime pack the build embedded, because a self-contained
archive is mostly .NET runtime and those attributions are not part of the NuGet package graph. In the
macOS Studio bundle all four live in `GrpCurl.Net Studio.app/Contents/Resources/`.

## A note on code signing

These binaries carry **no Authenticode or Apple Developer ID signature**. GrpCurl.Net is free of
charge, and publisher certificates and Apple notarization are not — so the first launch shows an OS
security prompt on Windows and macOS, and the per-platform steps below explain how to proceed. This
is not expected to change.

What replaces it is the verification above: provenance attestations, a signed checksum manifest and
per-artifact SBOMs give a cryptographic answer to "did this come from the project?", which is what
an allow-listing or procurement process actually needs. Note that neither SmartScreen nor Gatekeeper
understands Sigstore attestations, so the prompts remain regardless.

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
