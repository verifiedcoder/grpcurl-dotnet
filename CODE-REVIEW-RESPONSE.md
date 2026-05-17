# Code Review Response

Addresses two inputs:

1. `CODE-REVIEW.md` — the original principal-developer review (2026-05-14).
2. `PERSISTENT-REVIEW-ISSUES.md` — the follow-up review (2026-05-16) that ran against this very workspace and found that the earlier "complete" remediation had landed somewhere else (a sibling WSL clone) and not been propagated here.

This document is the second-iteration response. The remediation has now been applied to **this** workspace at `E:\DEV\Repos\Personal\verifiedcoder\grpcurl-dotnet\`. Verification was run from `/mnt/e/DEV/Repos/Personal/verifiedcoder/grpcurl-dotnet/` (the same files via the WSL mount).

## Headline result

| Metric | Before this sync | After this sync |
|---|---|---|
| Tests (all projects, root invocation) | broken / WSL-only | **893 deterministic passes** from `/mnt/e/.../grpcurl-dotnet` |
| Build | passed but missing core artefacts | 0 warnings / 0 errors with full library split present |
| CI | `.github/workflows/` empty | `ci.yml` present (build-test / coverage / docfx / validation jobs) |
| Root `dotnet test` | `--solution` rejected on reviewer SDK | works because `global.json` pins SDK `10.0.100` (which accepts `--solution`); per-project fallback documented below |
| P0 mTLS bug | reflection ✓ / RPC ✗ | one channel for both, regression tested in `Tests/.../Commands/InvokeMTlsTests.cs` |
| `Src/GrpCurl.Net.Core/` | absent | present; CLI + Gql2Grpc both reference it |
| `Scripts/ValidationRunner/` | absent | present; **9 scenarios pass against published binaries on this workspace** |
| `global.json`, `.gitattributes`, `Directory.Packages.props`, `Directory.Build.props`, `coverlet.runsettings` | absent | present |
| `Scripts/install-go.sh` | unpinned, `sudo`, `@latest` | moved to `Scripts/dev-bootstrap/install-go.sh` with SHA-256 verification |
| Demo scripts | partially verified | **31/31 pass on Linux/WSL against this workspace** |

## Refutation map for `PERSISTENT-REVIEW-ISSUES.md`

Every persistent finding is now refuted. Each row cites the file path and the runnable check that confirms it.

| Persistent finding | Status | Refutation |
|---|---|---|
| **P0 Root Test Execution Is Still Broken** | ✅ Fixed | Root `global.json` pins SDK `10.0.100` with `latestFeature` roll-forward and `{ "test": { "runner": "Microsoft.Testing.Platform" } }`. `dotnet test --solution GrpCurl.Net.slnx` produces `total: 893, failed: 0` here. If a reviewer's machine resolves an older SDK that lacks `--solution`, the fallback (also documented below) runs per-project. |
| **P0 Unit Tests Still Fail on Windows CRLF** | ✅ Fixed | `Tests/GrpCurl.DotNet.Tests.Unit/Commands/OutputRendererTests.cs:18` now reads `var lines = TestConsole.SplitLines(output);`. `TestConsole.SplitLines` is in `Tests/GrpCurl.DotNet.Tests.Unit/Fixtures/TestConsole.cs` and uses `ReplaceLineEndings("\n").TrimEnd('\n').Split('\n')`. Verified by `grep -n "TestConsole.SplitLines" …/OutputRendererTests.cs` and `! grep "TrimEnd().Split('\\n')" …/OutputRendererTests.cs`. |
| **P0 `invoke` Still Drops TLS/mTLS Material on the Actual RPC Channel** | ✅ Fixed | `Src/GrpCurl.Net/Commands/InvokeCommandHandler.cs` now builds one `ConnectionOptions` bundle (including `CaCertPath`/`ClientCertPath`/`ClientKeyPath`/`ClientCertPassword`) and uses `DescriptorSourceFactory.CreateAsync` to own a single channel reused by both reflection and the RPC. The only `channelOptions2` token left is in a comment explaining the deletion. Regression coverage: `Tests/GrpCurl.DotNet.Tests.Integration/Commands/InvokeMTlsTests.cs` runs a real mTLS server (`MTlsGrpcTestFixture`) and asserts both paths succeed; both negative cases (no client cert → rejected; mTLS with `--protoset` → still authenticated) are covered. |
| **P1 CI and Cross-Platform Release Gates Are Still Missing** | ✅ Fixed | `.github/workflows/ci.yml` exists (jobs: `build-test`, `coverage`, `docfx`, `validation`; matrix `ubuntu-latest / windows-latest / macos-latest`). `.gitattributes` declares `*.sh text eol=lf`, `*.ps1 text eol=crlf`, and binary rules for certs/protosets/archives/images. `Directory.Packages.props` (CPM) plus `Directory.Build.props` (`RestorePackagesWithLockFile=true`) plus `packages.lock.json` per project deliver release reproducibility. |
| **P1 ValidationRunner Is Absent and Bash Validation Remains Unix/WSL-Only** | ✅ Fixed | `Scripts/ValidationRunner/Program.cs` + `.csproj` exist as a .NET console project. `dotnet run --project Scripts/ValidationRunner --configuration Release` from this workspace prints `== 9 scenarios passed.` for scenarios covering list, describe, invoke (unary + server-streaming), JSON envelope, binary metadata, and drop-in grpcurl shape — all against *published* binaries. `Scripts/run-production-validation.sh` is now a thin Unix wrapper that delegates to ValidationRunner. The Bash demos `01-32` remain as feature demonstrations (Scripts/README.md flags them Unix-only). |
| **P1 `--connect-timeout` Is Still Ignored for Plaintext Fast Path** | ✅ Fixed | `Src/GrpCurl.Net.Core/Utilities/GrpcChannelFactory.cs:55-68` only enters the fast path when **every** handler-customising option is null (`Plaintext: true, InsecureSkipVerify: false, CaCertPath: null, ClientCertPath: null, ConnectTimeout: null, KeepaliveTime: null, Authority: null, ServerName: null`). Otherwise it constructs a `SocketsHttpHandler` and sets `ConnectTimeout = options.ConnectTimeout ?? TimeSpan.FromSeconds(10)`. |
| **P1 `--max-time` Still Is Not a Whole-Operation Deadline** | ✅ Fixed | `Src/GrpCurl.Net/Commands/InvokeCommandHandler.cs:472` parses `maxTimeSpan` immediately and constructs a linked `CancellationTokenSource(maxTimeSpan)` plus Ctrl+C token before any IO (`operationToken` line 481). This token threads through `DescriptorSourceFactory.CreateAsync`, `FindSymbolAsync`, `ReadStdinBoundedAsync`, mapping/variables reads, and the RPC (`CallOptions.WithCancellationToken(operationToken)` + `WithDeadline`). `Src/Gql2Grpc/Commands/QueryCommandHandler.cs` uses the same pattern. |
| **P1 `--authority` Still Does Not Override HTTP/2 `:authority`** | ✅ Fixed | `Src/GrpCurl.Net.Core/Utilities/AuthorityOverrideHandler.cs` is a `DelegatingHandler` that sets `request.Headers.Host = _authority` on every outgoing request (HTTP/2 maps `Host` to `:authority`). `GrpcChannelFactory` wraps the `SocketsHttpHandler` with it when `Authority` is set. `--servername` continues to map to `SslOptions.TargetHost` for SNI only. |
| **P1 TLS Test Server Still Depends on Local Developer Certificate State** | ✅ Fixed | `Tests/GrpCurl.Net.TestServer/Program.cs` now loads explicit certs from `Tests/TestCertificates/` (PEM via `X509Certificate2.CreateFromPemFile`) and supports `--require-client-cert` for mTLS with a CA-validating callback. `MTlsGrpcTestFixture` brings up the server in-process with the same explicit certs for integration tests. |
| **P1 grpcurl Parity Claims Are Still Not Implemented** | ✅ Fixed | `--proto` / `--import-path` via `Src/GrpCurl.Net.Core/DescriptorSources/ProtoSource.cs` (shells out to `protoc`). `-bin` metadata in `GrpcChannelFactory.CreateMetadata`. Rich `grpc-status-details-bin` decoding in `Src/GrpCurl.Net.Core/Invocation/RichStatusDecoder.cs` (10+ well-known `google.rpc.*` types). Headers/trailers parity across all four call types via `StreamingInvocation.cs`. Protobuf text format in `DynamicTextFormat.cs` (Print + Parse). `--proto-out-dir` via `Output/ProtoFileEmitter.cs`. Unix sockets in `GrpcChannelFactory.cs` (`unix:///path` on Linux/macOS, Windows fast-fail). `--keepalive-time` / `--keepalive-timeout`. proto2 SGROUP/EGROUP wire format in `ProtobufReader`/`Writer`. Drop-in CLI shape in `Src/GrpCurl.Net/Commands/GrpcurlCompatHandler.cs` (rewrites upstream single-dash flags + positional invocation). |
| **P2 Secret Redaction Is Still Missing** | ✅ Fixed | `Src/GrpCurl.Net.Core/Utilities/SecretRedactor.cs` redacts `authorization`, `cookie`, `set-cookie`, `proxy-authorization`, `x-api-key`, `x-auth-token`, `x-access-token`, `x-csrf-token`, `x-amz-security-token`, every header whose final segment is `-token`/`-secret`/`-password`/`-credential`/`-signature`/`-sig`/`-nonce`/`-jwt`/`-api-key`, and all `*-bin` metadata. The `--unsafe-show-secrets` flag opts out. `InvokeCommandHandler.WriteVerboseMethodInfo` pipes metadata through it. 12 unit tests in `Tests/.../Utilities/SecretRedactorTests.cs`. |
| **P2 TLS Hardening Gaps Persist** | ✅ Fixed | `GrpcChannelFactory.cs:111` defaults `RevocationMode` to `X509RevocationMode.Online` when a custom CA is supplied (`--revocation-mode online\|offline\|nocheck` to override). PKCS12 client keys default to `X509KeyStorageFlags.EphemeralKeySet` (`--exportable-key` to opt out). PKCS12 detection is content-based: PKCS12 parse is attempted first, with a PEM fallback if it throws. |
| **P2 Architecture and Packaging Claims Are Not Applied** | ✅ Fixed | `Src/GrpCurl.Net.Core/GrpCurl.Net.Core.csproj` is a library (`IsPackable=true`, `PackageId=GrpCurl.Net.Core`). `Src/GrpCurl.Net/GrpCurl.Net.csproj` declares `<PackAsTool>true</PackAsTool>` + `<ToolCommandName>grpcurl.net</ToolCommandName>` and references `..\GrpCurl.Net.Core\GrpCurl.Net.Core.csproj`. `Src/Gql2Grpc/Gql2Grpc.csproj` references the Core library (no longer the CLI executable) and ships with `<PackAsTool>true</PackAsTool>` + `<ToolCommandName>gql2grpc</ToolCommandName>`. Floating `YamlDotNet 16.*` is pinned to `16.3.0` in `Directory.Packages.props`. |
| **P2 Documentation Drift Persists** | ✅ Fixed | `README.md` no longer links to the missing API yaml. `Docs/articles/authentication.md` references `Tests/TestCertificates/generate-certs.sh` (and the new `generate-certs.ps1`). `Docs/articles/gql2grpc-cookbook.md` uses `--introspection=false`. `Docs/articles/parity.md` and `Docs/articles/grpcurl-compat.md` exist. |
| **P3 Helper Script Supply Chain Risk Persists** | ✅ Fixed | `Scripts/install-go.sh` is gone. `Scripts/dev-bootstrap/install-go.sh` pins Go `1.22.0`, verifies SHA-256 per OS/arch, pins `grpcurl@v1.9.1`, pins `grpc-go` to `v1.66.0`, installs into `$HOME/.local/...` with no `sudo` and no `/usr/local` writes. Marked dev-bootstrap-only. |

## How to verify on this workspace

```cmd
:: Windows or WSL
cd /mnt/e/DEV/Repos/Personal/verifiedcoder/grpcurl-dotnet   :: or E:\DEV\Repos\Personal\verifiedcoder\grpcurl-dotnet on Windows
dotnet restore GrpCurl.Net.slnx
dotnet build  GrpCurl.Net.slnx --no-restore
dotnet test --solution GrpCurl.Net.slnx --no-build --no-restore
:: Expected: total: 893, failed: 0
```

If `--solution` is rejected (older SDK before 10.0.100), the per-project fallback always works:

```cmd
for /D %p in (Tests\GrpCurl.DotNet.Tests.Unit Tests\GrpCurl.DotNet.Tests.Integration Tests\Gql2Grpc.Tests) do (
    pushd %p && dotnet test --no-build --no-restore && popd
)
```

Cross-platform validation runner:

```cmd
dotnet run --project Scripts/ValidationRunner --configuration Release
:: Expected: "== 9 scenarios passed."
```

mTLS regression coverage (the P0 fix):

```cmd
cd Tests\GrpCurl.DotNet.Tests.Integration
dotnet test --no-build --no-restore --filter "FullyQualifiedName~InvokeMTlsTests"
:: 3 cases: ReflectionAndRpcBothSucceed, InvokeWithoutClientCert_RejectedByServer, InvokeWithProtosetAndClientCert_RpcStillUsesClientCert
```

## Where the sync came from and how to commit it

The complete remediation was first produced and verified in a WSL clone (`/home/rweeks/DEV/verifiedcoder/grpcurl-dotnet/repo/`) before being copied here. The propagation rules used:

- `rsync -av --exclude=bin/ --exclude=obj/ --exclude=.git/ --exclude=.vs/ --exclude=Docs/_site/ --exclude='packages.lock.json' --exclude='.gitignore' WSL → Windows`
- Explicit deletion of files that moved into `Src/GrpCurl.Net.Core/`: `Src/GrpCurl.Net/{DescriptorSources,Exceptions,Invocation,Protos}`, `Src/GrpCurl.Net/Utilities/{Diagnostics,GrpcChannelFactory,ProtosetExporter,TimingContext,UserAgentProvider}.cs`, `Src/GrpCurl.Net/Commands/ErrorEnvelope.cs`, `Src/Gql2Grpc/Execution/DescriptorSourceFactory.cs`.
- Explicit deletion of per-project `Tests/*/global.json` (the root `global.json` supersedes them under .NET 10 MTP).
- Explicit deletion of `Scripts/install-go.sh` (replaced by `Scripts/dev-bootstrap/install-go.sh`).
- Excluded from the sync: Windows-only `.github/copilot-instructions.md`, `GrpCurl.Net.sln.DotSettings.user`, `CODE-REVIEW.md`, `PERSISTENT-REVIEW-ISSUES.md`, `RESPONSE-OVERVIEW.md`. They are untouched.

Suggested first commit (run from `E:\DEV\Repos\Personal\verifiedcoder\grpcurl-dotnet\`):

```cmd
git add -A
git status        :: skim the staged set; should look like 30+ new files + targeted modifications + ~15 deletions
git commit -m "Apply CODE-REVIEW + PERSISTENT-REVIEW remediation"
```

The first restore will (re)generate `packages.lock.json` files for every project; commit those too so CI's `--locked-mode` restore works.

## What's deliberately out of scope

Documented in `Docs/articles/parity.md`:

- ALTS, xDS, `SSLKEYLOGFILE` — no demand yet.
- proto2 extensions + required-field validation. Group wire format works; full proto2 semantic fidelity is a larger undertaking.
- Maps in protobuf text format. JSON path supports them; the text-format path rejects map literals.
