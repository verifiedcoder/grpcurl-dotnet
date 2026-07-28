# Release packaging scripts

Zero-budget, GitHub-Releases packaging for **GrpCurl.Net Studio** and the two CLIs
(`grpcn`, `gql2grpc`). No Velopack, no paid code signing/notarization — self-contained
`dotnet publish` archives uploaded to GitHub Releases. See `studio-specs/SPEC-080` and the
P5 plan for the rationale and the (paid) upgrade path.

The product is free of charge, so publisher certificates (Authenticode, Apple Developer ID +
notarization) are out of reach and SmartScreen/Gatekeeper prompts are permanent. Trust is instead
established with free, keyless supply-chain metadata (PRD-002), all of it produced by
`.github/workflows/release.yml`: bundled legal material, a per-artifact CycloneDX SBOM, Sigstore
build-provenance attestations, a cosign-signed `SHA256SUMS`, and a pre-draft gate that refuses to
publish a release when any of it is missing.

These are plain Bash scripts and run on the Linux, macOS, and Windows GitHub runners
(Windows via Git Bash). They publish settings on the command line only — no `.csproj`
changes — so normal `dotnet build`/test and `PackAsTool` are untouched.

## Scripts

| Script | Purpose |
|--------|---------|
| `publish.sh <rid> <version> [staging]` | Publish Studio + both CLIs self-contained for one RID and archive them into `<staging>/dist`. CLIs are single-file; Studio is a folder archive (a `.app` bundle on `osx-*`). `win-*` → `.zip`, else `.tar.gz`. Every archive also gets `LICENSE` + `THIRD-PARTY-NOTICES.md`. |
| `make-macos-app.sh <publish-dir> <version> <app-path>` | Wrap a Studio publish dir into `GrpCurl.Net Studio.app`, put the legal material in `Contents/Resources`, and **ad-hoc** codesign it (free; lets arm64 run after quarantine is cleared — not notarization). `codesign` runs only on macOS. |
| `generate-sbom.sh <rid> <version> [staging]` | Emit a CycloneDX SBOM per product beside the archives (`<product>-<rid>-<version>.cdx.json`). Run **after** `publish.sh` for the same RID: restore is disabled, so the SBOM describes the graph that publish actually used and no lock file is touched. Uses the CycloneDX tool pinned in `.config/dotnet-tools.json`. |
| `generate-third-party-notices.sh [--check]` | Regenerate the committed `THIRD-PARTY-NOTICES.md` from the shipped projects' `packages.lock.json` plus the `.nuspec` metadata in the local NuGet cache (offline; needs a completed restore and `python3`). `--check` fails on drift and runs in CI. |
| `verify-trust-artifacts.sh <dir> [--allow-partial] [--skip-signature]` | Pre-draft gate: every archive has a well-formed SBOM and ships `LICENSE` + `THIRD-PARTY-NOTICES.md`, `SHA256SUMS` is complete and verifies, the cosign bundle is present, and the full 6×3 matrix is staged. |
| `verify-version.sh <version> <exe>` | Assert a published CLI reports the expected version (core before `+`), guarding tag↔binary agreement for the Studio update check. |

## RIDs

`win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`.
arm64 Windows/Linux are cross-published and not smoke-tested on a native runner.

## Notes

- **`PublishReadyToRun` is OFF** — R2R can't cross-gen to a foreign architecture and 3/6 RIDs cross-publish.
- Publish uses `-p:RestoreLockedMode=false` so RID-specific runtime packs don't fail/churn the
  committed `packages.lock.json`; the locked-mode gate lives in the `build-test` job. Do **not** commit
  any lock-file changes a local `publish.sh` run produces.
- Output lands in `artifacts/` (git-ignored).

## Local example

```bash
GIT_SHA=$(git rev-parse --short HEAD) Scripts/package/publish.sh linux-x64 1.2.3
Scripts/package/generate-sbom.sh linux-x64 1.2.3
tar -xzf artifacts/release/dist/grpcn-linux-x64-1.2.3.tar.gz -C /tmp/x
Scripts/package/verify-version.sh 1.2.3 /tmp/x/grpcn

# gate a single-RID local staging dir (no cosign signature, no full matrix)
( cd artifacts/release/dist && rm -f SHA256SUMS && sha256sum -- * > SHA256SUMS )
Scripts/package/verify-trust-artifacts.sh artifacts/release/dist --allow-partial --skip-signature
```
