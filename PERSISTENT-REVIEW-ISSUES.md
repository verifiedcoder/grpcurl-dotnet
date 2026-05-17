# Persistent Issues After RESPONSE-OVERVIEW Re-review

Date: 2026-05-16

Repository: `grpcurl-dotnet`

Inputs reviewed:

- `CODE-REVIEW.md`
- `RESPONSE-OVERVIEW.md`
- Current working tree under `Src`, `Tests`, `Scripts`, `Docs`, `.github`, and solution/project files

## Executive Summary

`RESPONSE-OVERVIEW.md` claims that all 16 remediation phases were completed, including a core library split, CI matrix, ValidationRunner, root test fix, `.gitattributes`, lock files, grpcurl parity work, authority override, secret redaction, and 893 passing tests. The current workspace does not contain most of those claimed artifacts, and several original findings still reproduce directly.

The build succeeds, and the integration and Gql2Grpc test projects pass when run from their project folders. However, the root test command still fails under .NET 10/Microsoft.Testing.Platform, the exact command from the response (`dotnet test --solution GrpCurl.Net.slnx`) is invalid on this SDK, and the unit suite still fails on the same Windows CRLF assertion called out in `CODE-REVIEW.md`.

The highest-risk product issue also persists: `invoke` still builds a TLS/mTLS-capable reflection channel and then creates a second RPC channel that omits `CaCertPath`, `ClientCertPath`, `ClientKeyPath`, and `ClientCertPassword`.

## Verification Performed

Commands were run from `E:\DEV\Repos\Personal\verifiedcoder\grpcurl-dotnet` on Windows unless noted. `dotnet build` and `dotnet test` were run outside the sandbox after the sandbox produced a non-diagnostic .NET build failure.

| Check | Result |
|---|---|
| `dotnet build GrpCurl.Net.slnx --no-restore` | Passed, 0 warnings, 0 errors. |
| `dotnet test GrpCurl.Net.slnx --no-build --no-restore --verbosity normal` | Failed with the .NET 10 MTP/VSTest target error for all test projects. |
| `dotnet test --solution GrpCurl.Net.slnx --no-build --no-restore --verbosity normal` | Failed with `MSBUILD : error MSB1001: Unknown switch` for `--solution`. |
| `dotnet test --no-build --no-restore --verbosity normal` in `Tests/GrpCurl.DotNet.Tests.Unit` | Failed: 657 total, 656 succeeded, 1 failed. |
| `dotnet test --no-build --no-restore --verbosity normal` in `Tests/GrpCurl.DotNet.Tests.Integration` | Passed: 89 total. |
| `dotnet test --no-build --no-restore --verbosity normal` in `Tests/Gql2Grpc.Tests` | Passed: 70 total. |

The failing unit test is still `GrpCurl.Net.Tests.Unit.Commands.OutputRendererTests.WriteListServices_Text_OutputsOneServicePerLine`, with actual `"alpha.Foo\r"` vs expected `"alpha.Foo"` at `Tests/GrpCurl.DotNet.Tests.Unit/Commands/OutputRendererTests.cs:20`.

## Claimed Artifacts Not Present

The following claims in `RESPONSE-OVERVIEW.md` do not match the current workspace:

- `CODE-REVIEW-RESPONSE.md` is referenced at `RESPONSE-OVERVIEW.md:64`, but no such file exists.
- `Src/GrpCurl.Net.Core` is claimed at `RESPONSE-OVERVIEW.md:24`, but `Src` contains only `GrpCurl.Net` and `Gql2Grpc`.
- `Scripts/ValidationRunner` is claimed at `RESPONSE-OVERVIEW.md:5` and `:62`, but the path does not exist.
- `.github/workflows/ci.yml` is claimed at `RESPONSE-OVERVIEW.md:6`, but `.github/workflows` is empty.
- Root `global.json`, `.gitattributes`, `Directory.Packages.props`, `packages.lock.json`, and `coverlet.runsettings` are claimed at `RESPONSE-OVERVIEW.md:15`, but none are present.
- `Docs/articles/parity.md` and `Docs/articles/grpcurl-compat.md` are referenced at `RESPONSE-OVERVIEW.md:64`, but neither file exists.
- No code symbols or options exist for claimed implementations such as `AuthorityOverrideHandler`, `SecretRedactor`, `--unsafe-show-secrets`, `--proto-out-dir`, or `--max-stdin-bytes`.

## Persistent Findings

### P0: Root Test Execution Is Still Broken

The root test command still fails because the repository has not opted the solution-level `dotnet test` path into the new .NET 10 Microsoft.Testing.Platform runner. The error is the same class of failure from the original review: `Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later`.

The response's suggested command, `dotnet test --solution GrpCurl.Net.slnx`, is also not valid in this SDK; MSBuild treats `--solution` as an unknown switch.

Impact:

- The claimed "893 deterministic passes" could not be reproduced.
- Contributors still hit a failing root-level test command.
- No CI workflow exists to catch this before review.

Recommendation:

- Add the root-level MTP configuration required by .NET 10, or provide a checked-in cross-platform test runner that invokes each test project correctly.
- Add a real `.github/workflows/ci.yml` that runs the same commands.
- Remove or correct the invalid `dotnet test --solution` instruction.

### P0: Unit Tests Still Fail on Windows CRLF

`Tests/GrpCurl.DotNet.Tests.Unit/Commands/OutputRendererTests.cs:17` still uses `output.TrimEnd().Split('\n')`, leaving `\r` on Windows. The failed assertion remains at `:20`.

Impact:

- The Windows unit suite is not green.
- The original cross-platform line-ending issue persists unchanged.

Recommendation:

- Normalize logical output lines before assertions, for example with `StringReader`, `Environment.NewLine`-aware splitting, or explicit CRLF/LF normalization.
- Add a regression test that proves output assertions are platform-neutral.

### P0: `invoke` Still Drops TLS/mTLS Material on the Actual RPC Channel

`Src/GrpCurl.Net/Commands/InvokeCommandHandler.cs:363-368` sets `CaCertPath`, `ClientCertPath`, `ClientKeyPath`, and `ClientCertPassword` on the reflection channel options. The later RPC `channelOptions2` at `:453-462` does not include any of those values before creating `channel2` at `:464`.

Impact:

- `invoke --cacert --cert --key` can still discover schema successfully and then perform the RPC without the same trust/client identity settings.
- The public TLS/mTLS behavior remains unsafe to claim as fixed.

Recommendation:

- Build one immutable connection options object and reuse it for both reflection and RPC, or reuse one fully configured channel when possible.
- Add an integration test where reflection and RPC both require the custom CA and client cert.

### P1: CI and Cross-Platform Release Gates Are Still Missing

`.github/workflows` exists but contains no workflow files. There is still no root `.gitattributes`, no root `global.json`, no package lock file, and no central package management file.

Impact:

- Windows/Linux/macOS support is still asserted by `README.md:16` but not verified.
- WSL-to-Windows line ending drift remains unguarded.
- Package/release reproducibility remains loose.

Recommendation:

- Add a real CI matrix for `windows-latest`, `ubuntu-latest`, and `macos-latest`.
- Add `.gitattributes` rules for text, shell scripts, PowerShell scripts, certificates, protosets, archives, and images.
- Add root test runner configuration and lock-file restore if the project intends release-grade reproducibility.

### P1: ValidationRunner Is Absent and Bash Validation Remains Unix/WSL-Only

`Scripts/ValidationRunner` does not exist. `Scripts/run-production-validation.sh` still uses Unix tools and paths: `pkill` at `:80-84`, `nc -z` at `:94`, `/tmp/test-export.protoset` at `:232-235`, and Bash glob discovery limited to `0[2-9]*`, `1*`, and `2[0-7]*` at `:513`. That glob still excludes scripts `28` through `32`.

Impact:

- Native Windows users still cannot run the validation path without WSL/Git Bash.
- The claimed "9 cross-platform scenarios" are not present.
- The production validation script still does not cover all demo scripts.

Recommendation:

- Add the claimed cross-platform runner or explicitly document Bash scripts as Unix/WSL-only examples.
- Replace `pkill`, `nc`, and `/tmp` assumptions with a managed runner that owns process lifetime and uses platform APIs.
- Fix script discovery so all intended scripts are validated.

### P1: `--connect-timeout` Is Still Ignored for Plaintext Fast Path

`Src/GrpCurl.Net/Utilities/GrpcChannelFactory.cs:31-33` returns `GrpcChannel.ForAddress` for plaintext channels before creating a `SocketsHttpHandler`. That skips the handler where `ConnectTimeout` is set at `:44`.

Impact:

- Plaintext calls can still ignore `--connect-timeout`.
- The default 10-second timeout comment is misleading for the plaintext fast path.

Recommendation:

- Only use the fast path when no handler-level option is present, or always create the handler.
- Add a unit/integration test that proves plaintext plus `ConnectTimeout` configures the channel through the handler path.

### P1: `--max-time` Still Is Not a Whole-Operation Deadline

In `invoke`, `deadlineCts` is still null at command setup (`Src/GrpCurl.Net/Commands/InvokeCommandHandler.cs:316`), then descriptor loading, reflection lookup, and stdin reads use `deadlineCts?.Token ?? CancellationToken.None` at `:355`, `:388`, and `:444`. The timeout source is only created at `:474`, after schema discovery and request preparation.

Gql2Grpc similarly calculates its RPC deadline only after file reads, variable parsing, descriptor source creation, mapping resolution, and schema construction (`Src/Gql2Grpc/Commands/QueryCommandHandler.cs:209-239`).

Impact:

- Large protosets, slow reflection, blocking stdin, large query/variables files, or expensive descriptor setup can exceed `--max-time`.
- This still diverges from grpcurl-style whole-operation timeout semantics.

Recommendation:

- Parse `--max-time` at command entry and create one linked cancellation source for the whole command.
- Pass the same token through descriptor source creation, file/stdin reads, schema work, request parsing, protoset export, and RPC calls.
- Derive the gRPC deadline from the remaining budget.

### P1: `--authority` Still Does Not Override HTTP/2 `:authority`

`Src/GrpCurl.Net/Utilities/GrpcChannelFactory.cs:53-56` maps `Authority` only to `SslOptions.TargetHost` when `ServerName` is absent. There is no `AuthorityOverrideHandler` or other HTTP/2 `:authority` override path in the current code.

Impact:

- Routing through virtual-hosted gRPC ingress or authority-based test servers remains unsupported despite docs saying `--authority` controls `:authority`.
- `--authority` remains closer to an SNI/certificate-validation fallback than a grpcurl-compatible authority override.

Recommendation:

- Implement an HTTP handler/call path that sets the request authority for both reflection and RPC.
- Keep `--servername` separate for certificate/SNI behavior.
- Add an integration test where server behavior differs by authority.

### P1: TLS Test Server Still Depends on Local Developer Certificate State

`Tests/GrpCurl.Net.TestServer/Program.cs:44` still calls `listenOptions.UseHttps()` without loading an explicit test certificate. No mTLS server toggle or explicit test certificate path is present.

Impact:

- TLS tests can depend on developer machine certificate state.
- CI/Linux/macOS behavior can differ from Windows.
- The test server still cannot prove custom CA, wrong CA, expired cert, server name, and client-certificate flows deterministically.

Recommendation:

- Load explicit certificates from `Tests/TestCertificates`.
- Add mTLS mode and deterministic certificate failure cases.
- Keep tests independent of user-level dev cert stores.

### P1: grpcurl Parity Claims Are Still Not Implemented

The response claims `--proto/-I`, `-bin`, status details, headers/trailers, text format, `--proto-out-dir`, Unix sockets, keepalive, and proto2 groups. Current command options still expose protosets but no proto source/import-path options in `ListCommandHandler`, `DescribeCommandHandler`, or `InvokeCommandHandler`. Searches found no `--proto-out-dir`, `--max-stdin-bytes`, Unix socket, or compatibility handler implementation.

Impact:

- Users with `.proto` source trees still need an external `protoc` step.
- Claimed drop-in grpcurl compatibility is not present in the code.
- Documentation and response claims overstate the actual product surface.

Recommendation:

- Implement the parity features with tests, or document them as unsupported.
- Add a real parity matrix file and keep it tied to test coverage.

### P2: Secret Redaction Is Still Missing

No `SecretRedactor` or `--unsafe-show-secrets` option exists. Verbose output still writes request metadata values directly at `Src/GrpCurl.Net/Commands/InvokeCommandHandler.cs:972-974`, and response headers/trailers at `:993-1017`. Verbose streaming still writes request payload JSON at `:1053-1055` and `:1103-1105`.

Impact:

- Authorization headers, cookies, API keys, tokens, and sensitive request payload fields can still be emitted to stderr/logs.
- The response's secret-redaction claim is not implemented.

Recommendation:

- Redact sensitive metadata keys by default.
- Add an explicit unsafe escape hatch only when users need full diagnostics.
- Add tests for common header names such as `authorization`, `cookie`, `x-api-key`, and `*-token`.

### P2: TLS Hardening Gaps Persist

`Src/GrpCurl.Net/Utilities/GrpcChannelFactory.cs:97` still uses `X509RevocationMode.NoCheck`, and PKCS12 client cert loading still uses `X509KeyStorageFlags.Exportable` at `:118`. PKCS12 detection still depends on `.p12` or `.pfx` filename extensions instead of content.

Impact:

- Revocation checking is disabled even when a custom CA is used.
- Private keys are loaded as exportable.
- Valid PKCS12 content with an unexpected extension is still treated as PEM.

Recommendation:

- Make revocation behavior explicit and configurable.
- Prefer ephemeral/non-exportable key storage where possible.
- Detect PKCS12 by content or give a clearer explicit option.

### P2: Architecture and Packaging Claims Are Not Applied

`Src/GrpCurl.Net/GrpCurl.Net.csproj` still has `<OutputType>Exe</OutputType>` and no `PackAsTool`/`ToolCommandName`. `Src/Gql2Grpc/Gql2Grpc.csproj:18` still references `..\GrpCurl.Net\GrpCurl.Net.csproj`, and `:25` still uses floating `YamlDotNet` version `16.*`. `GrpCurl.Net.csproj` still grants `InternalsVisibleTo` to `Gql2Grpc` and tests. `Docs/articles/gql2grpc-future-work.md:105-112` still describes promoting the shared API as future work.

Impact:

- The claimed `GrpCurl.Net.Core` split is not present.
- Gql2Grpc remains coupled to an executable project's internals.
- Packaging and tool-install docs remain unsupported by project metadata.

Recommendation:

- Split reusable transport/descriptor/invocation code into a real library project.
- Make CLI projects depend on the library instead of using `InternalsVisibleTo`.
- Add real package/tool metadata or narrow the install docs.

### P2: Documentation Drift Persists

Examples:

- `README.md:67` still links to `Docs/api/GrpCurl.Net.DescriptorSources.yml`, which is not present.
- `Docs/articles/authentication.md:101-104` and `Docs/articles/troubleshooting.md:11` still point to `Scripts/generate-certs.sh`; the certificate script is under `Tests/TestCertificates/generate-certs.sh`.
- `Docs/articles/ci-cd.md` remains Bash/Linux-heavy and still uses Linux port checks.
- Response-referenced docs `Docs/articles/parity.md` and `Docs/articles/grpcurl-compat.md` do not exist.

Impact:

- Users following docs can hit missing files or unsupported commands.
- The response's documentation completion claim is not credible in this workspace.

Recommendation:

- Add doc validation in CI once CI exists.
- Correct stale links and command examples.
- Clearly separate supported cross-platform workflows from Unix-only demo scripts.

### P2: Helper Script Supply Chain Risk Persists

`Scripts/install-go.sh` still downloads `go1.22.0.linux-amd64.tar.gz` without a checksum, installs under `/usr/local/go` via `sudo`, installs `grpcurl@latest`, and clones `grpc-go` without pinning a commit (`Scripts/install-go.sh:9-23`, `:51`, `:64-66`).

Impact:

- The script is Linux-only and mutates system state.
- Reproducibility and supply-chain integrity remain weak.
- The response's "hardened" claim is not present.

Recommendation:

- Pin versions/commits and verify checksums.
- Prefer user-scoped install paths or containerized validation.
- Avoid `@latest` in reproducible validation scripts.

## Status Matrix

| Original review area | Current status |
|---|---|
| P0 invoke TLS/mTLS channel bug | Persists. |
| P0 CI absent/root test broken | Persists. `.github/workflows` is empty and root `dotnet test` fails. |
| Windows CRLF unit failure | Persists. |
| Cross-platform gates | Persist. No matrix, no `.gitattributes`, scripts remain Unix-shaped. |
| ValidationRunner/published artifact validation | Not present. |
| Plaintext `--connect-timeout` | Persists by code inspection. |
| Whole-operation `--max-time` | Persists in `invoke` and Gql2Grpc. |
| HTTP/2 `:authority` override | Persists. No override handler present. |
| Proto source/import path support | Not present. |
| Core library split | Not present. |
| Secret redaction | Not present. |
| TLS hardening | Persists. |
| Documentation drift | Persists. |
| install-go hardening | Not present. |

## Recommended Gate Before Accepting the Developer Response

Do not mark the prior review as resolved from `RESPONSE-OVERVIEW.md` alone. Require a concrete patch that adds the claimed artifacts and fixes the reproduced failures. A reasonable acceptance gate is:

1. Root-level build and test commands are valid and pass on this machine.
2. `.github/workflows/ci.yml` exists and runs Windows/Linux/macOS matrix jobs.
3. Unit, integration, and Gql2Grpc tests all pass locally.
4. A TLS/mTLS integration test proves the invoke RPC channel uses the same cert/CA options as reflection.
5. The claimed files (`ValidationRunner`, parity docs, compatibility docs, core library if still claimed) actually exist.
6. Documentation claims match implemented options and project metadata.
