# Comprehensive .NET 10 Solution Review

Date: 2026-05-14

Repository: `grpcurl-dotnet`

Scope: `Src/GrpCurl.Net`, `Src/Gql2Grpc`, `Tests`, `Docs`, `Scripts`, solution/project configuration, release/test posture, and feature parity against upstream `grpcurl`.

Reference sources:

- Upstream grpcurl README: https://github.com/fullstorydev/grpcurl
- Upstream grpcurl CLI flags and command flow: https://raw.githubusercontent.com/fullstorydev/grpcurl/master/cmd/grpcurl/grpcurl.go

## Executive Summary

The solution has a strong foundation: the code builds cleanly on .NET 10, the descriptor-source abstraction is directionally right, integration tests cover the core happy paths, and the project already has a serious amount of test and documentation investment. The most important risks are not cosmetic. They are behavioral parity, transport correctness, and release/test operational maturity.

The highest priority product defect is that `grpcurl.net invoke` builds one correctly configured channel for reflection, then builds a second RPC channel that drops custom CA and client-certificate settings. This directly contradicts the public TLS/mTLS claim and can make a secure schema lookup succeed while the actual RPC fails or runs with the wrong client identity.

The second broad risk is that the project is presented as a production-grade grpcurl equivalent, but several grpcurl reference features are absent or only partially implemented: proto source/import-path descriptors, protobuf text request format, binary metadata, actual HTTP/2 authority override, rich status details, and fuller proto2 behavior. Some differences are acceptable product choices, but they need explicit compatibility strategy and documentation.

The third broad risk is operational. There is no CI workflow, root-level `dotnet test` is not configured correctly for .NET 10 Microsoft.Testing.Platform, unit tests currently fail on this machine, and the "production validation" scripts do not validate production artifacts. This makes regression detection too dependent on local convention.

The fourth broad risk is cross-platform maturity. The README claims Windows, Linux, and macOS support, but the current validation workflow is Linux/WSL-shaped, there is no `.gitattributes` policy for line endings or shell-script LF preservation, and the local Windows test run already exposed a CRLF-sensitive assertion. Cross-platform behavior should be treated as a release gate, not a documentation afterthought.

## Review Plan Used

The review was run as five parallel lanes, then synthesized into this document.

1. Architecture and code quality
   - Project structure, executable/library boundaries, internal APIs, descriptor-source design, command handler shape, channel lifecycle, error handling, .NET 10 idioms.

2. Feature implementation and grpcurl parity
   - `list`, `describe`, `invoke`, reflection/protosets, proto sources, TLS/mTLS, headers, deadlines, formats, streaming, output, error behavior, CLI ergonomics, and Gql2Grpc relationship.

3. Security and reliability
   - TLS behavior, certificate handling, metadata/secret exposure, input size handling, timeout/cancellation behavior, dependency posture, supply-chain scripts, DoS risks.

4. Tests and validation
   - Unit/integration coverage, test server fidelity, CI, production validation scripts, MTP setup, coverage enforcement, fixture drift.

5. Documentation and public UX
   - README, DocFX docs, CLI reference, authentication docs, examples, release/install claims, script docs, Gql2Grpc docs, doc/implementation drift.

## Local Verification Performed

Commands were run from `E:\DEV\Repos\Personal\verifiedcoder\grpcurl-dotnet` unless noted.

| Check | Result |
|---|---|
| `dotnet --info` | .NET SDK `10.0.300`, runtime `10.0.8` available. |
| `dotnet build GrpCurl.Net.slnx --no-restore` | Passed, 0 warnings, 0 errors. |
| `dotnet test GrpCurl.Net.slnx --no-build --no-restore` | Failed from repo root due Microsoft.Testing.Platform/VSTest target mismatch. |
| `dotnet test --no-build --no-restore` in `Tests/GrpCurl.DotNet.Tests.Unit` | Failed. A detailed run reported 657 total, 656 succeeded, 1 failed; a stop-on-fail run surfaced two failures before stopping. |
| `dotnet test --no-build --no-restore` in `Tests/GrpCurl.DotNet.Tests.Integration` | Passed, 89 total. |
| `dotnet test --no-build --no-restore` in `Tests/Gql2Grpc.Tests` | Passed, 70 total. |

Observed unit failures:

- `GrpCurl.Net.Tests.Unit.Commands.OutputRendererTests.WriteListServices_Text_OutputsOneServicePerLine`: expected `"alpha.Foo"` but got `"alpha.Foo\r"` on Windows, so the assertion is newline-sensitive.
- `GrpCurl.Net.Tests.Unit.Utilities.TimingContextTests.PrintSummary_WithMetrics_DoesNotThrow`: threw `ObjectDisposedException: Cannot write to a closed TextWriter`, suggesting console writer/global output state leaks across tests.

Cross-platform observations:

- The workspace is being reviewed on Windows, while the app and scripts were developed in a WSL-oriented workflow. That transition has already exposed a Windows newline failure in the unit suite.
- `README.md:16` claims Windows, Linux, and macOS support, but CI does not prove any of those targets.
- Scripts are Bash-only and use Unix tools and paths such as `pkill`, `nc`, `/tmp`, `chmod`, `sudo`, `wget`, `tar`, and `~/.bashrc`.
- No `.gitattributes` file is present, so shell script line endings, executable metadata expectations, and test fixture line endings can drift when the repo moves between WSL, Windows, and CI.
- The TLS test server uses Kestrel `UseHttps()` without an explicit certificate, which depends on local developer certificate state and can behave differently across Windows, Linux, macOS, and headless CI.

## Severity Model

- P0: Must fix before claiming production readiness or TLS/security correctness.
- P1: High-impact correctness, parity, release, or maintainability issue.
- P2: Important gap that can affect real users or future maintenance.
- P3: Lower-risk quality, docs, hygiene, or roadmap issue.

## Findings

### P0: `invoke` Drops TLS/mTLS Material for the Actual RPC

`InvokeCommandHandler` builds the reflection channel with `CaCertPath`, `ClientCertPath`, `ClientKeyPath`, and `ClientCertPassword` (`Src/GrpCurl.Net/Commands/InvokeCommandHandler.cs:365-368`). It then builds `channelOptions2` for the actual RPC without any of those fields (`Src/GrpCurl.Net/Commands/InvokeCommandHandler.cs:453-464`).

Impact:

- `grpcurl.net invoke --cacert ... --cert ... --key ...` can discover schema successfully and then fail the RPC.
- mTLS client identity can be silently omitted from the actual business request.
- README/docs claim full TLS/mTLS support, but invoke does not deliver it end to end.

Recommendation:

- Reuse one fully configured `GrpcChannel` for reflection and RPC, or build one immutable connection options object and pass it to both channels.
- Add integration tests where both reflection and RPC require custom CA and client certificate auth.
- Treat this as both a correctness and documentation blocker.

### P0: CI Is Absent and the Root Test Command Is Broken

There is no `.github/workflows` directory; the only `.github` file is `.github/copilot-instructions.md`. Test projects opt into Microsoft.Testing.Platform via `TestingPlatformDotnetTestSupport`, but the `global.json` runner opt-in exists only inside individual test project folders. Root-level `dotnet test` therefore fails under .NET 10 with the MTP/VSTest target error.

Unit tests also currently fail locally under the correct MTP invocation. Integration and Gql2Grpc tests pass when run from their project folders.

Impact:

- Contributors will naturally run `dotnet test GrpCurl.Net.slnx`; today that path fails.
- The repo has no automated gate to catch the TLS/mTLS regression, doc drift, unit failures, or script drift.
- Coverage collector packages are referenced but no thresholds or reports are enforced.

Recommendation:

- Add a root-level `global.json` with the MTP runner opt-in, or document and script the exact per-project MTP commands.
- Add GitHub Actions for restore, build, unit tests, integration tests, Gql2Grpc tests, coverage, package/publish smoke tests, and script validation.
- Fix current unit failures and make test output isolation robust on Windows and Linux.

### P1: Cross-Platform Support Is Claimed but Not Gated

`README.md:16` advertises Windows, Linux, and macOS support. The current repo does not have a platform matrix, the root test command fails before tests run, and the Windows unit run exposed a CRLF-sensitive assertion in `Tests/GrpCurl.DotNet.Tests.Unit/Commands/OutputRendererTests.cs:17`.

Impact:

- A WSL-developed change can pass locally while failing for native Windows users.
- Windows-specific path, newline, certificate-store, process, and networking behavior can regress without detection.
- The public cross-platform claim is stronger than the validation evidence behind it.

Recommendation:

- Add a CI matrix for `windows-latest`, `ubuntu-latest`, and `macos-latest` that runs restore, build, unit tests, integration tests, Gql2Grpc tests, and packaging smoke tests.
- Add explicit tests for CRLF/LF output normalization, Windows paths with spaces/backslashes, relative paths, temp paths, and case-insensitive file systems where relevant.
- Treat platform-specific failures as release blockers until the README support claim is narrowed or the matrix is green.

### P1: Validation and Helper Scripts Are Unix/WSL-Only

`Scripts/README.md:3` describes the scripts as Bash scripts and `Scripts/README.md:98` tells users to run `chmod +x *.sh`. `Scripts/run-production-validation.sh` uses `pkill`, `nc -z`, `/tmp/test-export.protoset`, and Bash globs (`Scripts/run-production-validation.sh:80`, `:94`, `:232`, `:513`). `Scripts/install-go.sh` downloads `go*.linux-amd64.tar.gz`, writes under `/usr/local/go`, edits `~/.bashrc`, and builds `/tmp/interop_server` (`Scripts/install-go.sh:10`, `:21`, `:32`, `:66`).

Impact:

- Native Windows users cannot run the documented validation flow without WSL or Git Bash.
- macOS users may have different `nc`, `pkill`, path, and certificate behavior than the scripts assume.
- CI script reuse is limited because the validation path depends on shell tools instead of the .NET runtime already required by the project.

Recommendation:

- Either document the scripts as Unix/WSL-only examples, or replace the production validation entry point with a cross-platform .NET or PowerShell runner.
- Use `Path.GetTempPath()` or shell-independent temp handling instead of hard-coded `/tmp`.
- Replace `nc` port checks and `pkill` cleanup with a managed test harness that owns child process lifetime.
- Ensure every production validation script is discovered intentionally; the current `2[0-7]*.sh` glob excludes later scripts.

### P1: TLS Test Server Depends on Platform Developer Certificate State

`Tests/GrpCurl.Net.TestServer/Program.cs:34` calls `UseHttps()` without an explicit certificate when `--tls` is provided. That lets Kestrel choose the local development certificate configuration.

Impact:

- TLS validation can pass on a developer machine and fail on CI, Linux containers, or macOS machines without a trusted dev certificate.
- The test server cannot reliably exercise custom CA, server name, and mTLS flows across platforms.
- Certificate-store behavior differs significantly between Windows, Linux, and macOS.

Recommendation:

- Load explicit test certificates from `Tests/TestCertificates` when the test server starts in TLS mode.
- Add deterministic fixtures for trusted CA, wrong CA, expired cert, server-name override, and client certificate auth.
- Keep tests independent of the user-level certificate store.

### P1: `--connect-timeout` Is Ignored for Ordinary Plaintext Channels

`GrpcChannelFactory.Create` returns early for plaintext channels when there are no TLS options (`Src/GrpCurl.Net/Utilities/GrpcChannelFactory.cs:31`). That branch also matches when `ConnectTimeout` is supplied, so the configured `SocketsHttpHandler.ConnectTimeout` is skipped.

Impact:

- Scripts targeting a blackholed plaintext endpoint can hang beyond the advertised/default timeout posture.
- This undermines the agent/script guidance that timeouts prevent indefinite blocking.

Recommendation:

- Only use the fast path when no handler-level options are set, or always create a `SocketsHttpHandler`.
- Add tests proving plaintext plus `--connect-timeout` installs and honors a handler timeout.

### P1: `--max-time` Is Not a Total Operation Deadline

In `invoke`, `deadlineCts` is still null during protoset loading, reflection schema lookup, and unary stdin reads (`Src/GrpCurl.Net/Commands/InvokeCommandHandler.cs:316`, `:355`, `:388`, `:444`). It is only created before RPC invocation (`:474`). Gql2Grpc similarly computes the RPC deadline after query/variables files, mapping load, and descriptor source creation.

Impact:

- Slow reflection, huge protosets, large query/variables files, or blocking input can outlive `--max-time`.
- This differs from grpcurl's documented "maximum total time the operation can take" semantics.

Recommendation:

- Parse `--max-time` at command entry.
- Create one linked CTS for Ctrl+C, command cancellation, and max-time.
- Pass the token through descriptor loading, file reads, stdin reads, parsing where possible, protoset export, and RPC calls.
- Derive the gRPC deadline from the same remaining budget.

### P1: `--authority` Does Not Set HTTP/2 `:authority`

Docs claim `--authority` controls `:authority` and TLS server name. The implementation only maps it to `SslOptions.TargetHost` when `--servername` is absent (`Src/GrpCurl.Net/Utilities/GrpcChannelFactory.cs:51-56`). RPC and reflection call options do not carry a host/authority override.

Impact:

- Virtual-hosted gRPC services, ingress gateways, and test scenarios that route by `:authority` will not behave as documented.
- `--authority` is currently closer to an SNI/certificate validation knob than a true grpcurl authority override.

Recommendation:

- Plumb authority into both reflection and RPC calls using the appropriate gRPC/.NET mechanism for host override.
- Keep `--servername` as certificate validation/SNI override.
- Add integration tests where server behavior changes based on authority.

### P1: Missing Proto Source and Import Path Descriptor Source

The CLI exposes only `--protoset` for offline schema loading. Upstream grpcurl supports `-proto` plus `-import-path`/`-I`, which is a major flow when reflection is disabled and no protoset exists.

Impact:

- Users with normal `.proto` source trees cannot use this tool without running `protoc` separately.
- The docs and abstraction suggest descriptor extensibility, but feature parity stops at reflection/protosets.

Recommendation:

- Add a `ProtoSource` descriptor implementation.
- Support repeatable `--proto` and `--import-path`/`-I` options.
- Decide whether to shell out to `protoc`, embed a parser/compiler path, or document a strict external dependency.
- Add parity tests for imports, well-known types, multiple proto roots, and missing import diagnostics.

### P1: Executable Project Is Serving as a Private Shared SDK

`Src/GrpCurl.Net/GrpCurl.Net.csproj` is an executable (`OutputType` `Exe`) but `Gql2Grpc` references it as a project dependency and consumes internals through `InternalsVisibleTo` (`Src/GrpCurl.Net/GrpCurl.Net.csproj:4`, `:33-36`; `Src/Gql2Grpc/Gql2Grpc.csproj:18`).

Impact:

- CLI, reusable core, test internals, and Gql2Grpc integration are all coupled to one assembly boundary.
- Public API, internal API, and executable concerns are blurred.
- This makes packaging, trimming, testing, and long-term extension harder.

Recommendation:

- Split a `GrpCurl.Net.Core` library for descriptor sources, invocation, protobuf dynamic message handling, channel creation, protoset export, and output-neutral errors.
- Keep `GrpCurl.Net` as a thin CLI shell.
- Have `Gql2Grpc` consume the core library through explicit public/internal APIs instead of the CLI executable.

### P1: Reflection Channels Are Owned but Not Disposed

`List`, `Describe`, and `Invoke` create `ReflectionSource(..., ownsChannel: true)` but do not dispose the source (`Src/GrpCurl.Net/Commands/ListCommandHandler.cs:347`, `DescribeCommandHandler.cs:363`, `InvokeCommandHandler.cs:383`). `Gql2Grpc` has a better pattern in `DescriptorSourceFactory`, which reuses one channel and disposes it via `IAsyncDisposable`.

Impact:

- Short-lived CLI processes hide most leaks, but tests, embedded use, and future long-running modes can leak sockets/handlers.
- The bug reinforces duplicated lifecycle logic across commands.

Recommendation:

- Move the `DescriptorSourceFactory` pattern into shared core.
- Reuse the channel for reflection and RPC where possible.
- Ensure all descriptor sources with owned resources are disposed deterministically.

### P1: Production Validation Does Not Validate Production Artifacts

`Scripts/run-production-validation.sh` uses `dotnet run --project` for the CLI, builds only `GrpCurl.Net` in Release, starts the test server in default Debug, and does not exercise scripts `28-32` for Gql2Grpc. Demo scripts hard-code project/debug execution patterns.

Impact:

- The validation name overstates what is being tested.
- Release publish, tool packaging, native/RID behavior, executable names, and Gql2Grpc are not truly validated.

Recommendation:

- Publish release artifacts first, then run those binaries.
- Include `Gql2Grpc` and all demo scripts.
- Add `dotnet publish` smoke tests for supported RIDs.
- Fail the script on doc/script expectation mismatches.

### P1: Test Server Does Not Model Important grpcurl Interop Behavior

`test.proto` defines fields such as `response_size`, `fill_username`, `fill_oauth_scope`, and `response_status`, and scripts rely on them. The test service implementation only partially honors these semantics. Production validation and demos expect behavior that is not actually implemented.

Impact:

- Message-size, status/error, and response-fill scenarios can pass superficially while not testing the intended behavior.
- grpcurl comparison coverage is weaker than the scripts suggest.

Recommendation:

- Either implement the grpc interop test service behavior more faithfully or revise scripts/docs to match the current server.
- Add integration tests asserting `response_size`, `response_status`, and fill flags affect responses and errors.

### P2: Verbose Output Can Leak Credentials and Payload Secrets

`invoke -v` prints every outgoing metadata key/value pair in `WriteVerboseMethodInfo` (`Src/GrpCurl.Net/Commands/InvokeCommandHandler.cs:956` onward). Gql2Grpc `--vv` logs translated request JSON (`Src/Gql2Grpc/Execution/OperationExecutor.cs:227`). Docs only partially warn about the GraphQL request JSON case.

Impact:

- CI logs and terminal captures can expose bearer tokens, cookies, API keys, mapped literal secrets, and sensitive request fields.

Recommendation:

- Redact sensitive metadata by default: `authorization`, `cookie`, `x-api-key`, `*-token`, `*-secret`, `grpc-status-details-bin`, etc.
- Consider payload-field redaction for mapped literal secrets.
- Add an explicit `--show-secrets` or `--unsafe-show-secrets` opt-in for raw values.

### P2: TLS Hardening Gaps

Custom CA validation disables revocation checking (`Src/GrpCurl.Net/Utilities/GrpcChannelFactory.cs:97`). PKCS12 client certificates are loaded with `X509KeyStorageFlags.Exportable` (`:116-118`). Authentication docs also claim PKCS12 content detection, while implementation branches on `.p12`/`.pfx` extension (`:111-112`).

Impact:

- Revoked certificates remain accepted under `--cacert`.
- Client private keys are more exposed than needed inside the process/key store.
- Documentation can mislead users with extensionless or mislabeled cert files.

Recommendation:

- Prefer `EphemeralKeySet` and avoid `Exportable` unless explicitly requested.
- Make revocation mode configurable and document default/offline tradeoffs.
- Align docs with extension-based behavior or implement content detection.

### P2: Local Input Handling Is Largely Unbounded

Protosets are loaded via `File.ReadAllBytesAsync` (`Src/GrpCurl.Net/DescriptorSources/ProtosetSource.cs:73`). Gql2Grpc query and variables files are read wholly into memory (`Src/Gql2Grpc/Commands/QueryCommandHandler.cs:210`, `:343`). Concatenated JSON parsing copies full request input into a UTF-8 byte array and materializes a list of messages.

Impact:

- CI/agent workflows that process untrusted or large local files can be driven into memory pressure or long CPU work.

Recommendation:

- Add configurable max input sizes for protosets, query files, variables files, stdin, mapping files, and message counts.
- Stream stdin parsing for repeated messages.
- Fail early with clear usage errors.

### P2: Streaming `stdin` Is Less Capable Than Inline JSON

Inline streaming data supports arrays and concatenated JSON values. For `-d @` on client/bidi streaming, the implementation reads one JSON object per line and stops on a blank line. This rejects pretty-printed JSON arrays and differs from grpcurl's multi-value stream behavior.

Recommendation:

- Feed stdin through the same multi-value JSON decoder used for inline data.
- Support arrays, concatenated objects, and pretty-printed payloads until EOF.

### P2: Protobuf Text Format Is Missing

Upstream grpcurl has separate `-format json|text` request/response formatting behavior. This project only has `--output text|json`, which controls human vs machine envelopes, while request data is always JSON.

Recommendation:

- Add a payload format option distinct from envelope output.
- Support protobuf text request parsing and text response formatting, or explicitly document JSON-only scope.

### P2: Metadata and Error Parity Is Incomplete

Metadata creation stores only string values, so binary `-bin` metadata is not supported. RPC error envelopes preserve status code/detail but do not decode rich status details or expose headers/trailers consistently.

Recommendation:

- Support base64 binary metadata for `*-bin` headers.
- Capture and expose response headers/trailers for all call types.
- Decode `grpc-status-details-bin` when available.

### P2: Proto2 and Legacy Descriptor Support Is Partial

Groups throw in `ProtobufReader`/`ProtobufWriter`. JSON output comments and behavior assume proto3 default semantics. grpcurl can operate against proto2 descriptors and presence-heavy APIs.

Recommendation:

- Decide whether invocation is proto3-only. If so, document it prominently.
- If proto2 parity is a goal, add required-field validation, presence semantics, extensions, defaults, and group support.

### P2: Command Handlers Are Too Large and Duplicate Core Concerns

`InvokeCommandHandler`, `DescribeCommandHandler`, and `ListCommandHandler` each define options, parse values, validate, build descriptor sources, render output, handle timing, and translate exceptions. Descriptor graph resolution and symbol indexing are duplicated between `ReflectionSource` and `ProtosetSource`. `DynamicInvoker.cs` combines invocation, JSON parsing/writing, dynamic message state, and async stream extensions.

Impact:

- Fixes such as TLS/mTLS, timeout, authority, and disposal must be made in multiple places.
- New features like `--proto` and text format will be harder to add safely.

Recommendation:

- Extract option records and shared option builders.
- Introduce `DescriptorSourceResolver`, `ConnectionOptions`, `InvocationPipeline`, and central exception translation.
- Extract `DescriptorGraphBuilder` and `SymbolIndex`.
- Split `DynamicInvoker.cs` into invocation, dynamic message, JSON parser, JSON writer, and binary codec pieces.

### P2: Gql2Grpc Option Coverage Is Thin

The CLI exposes protosets, TLS/mTLS, deadlines, message limits, split reflection/RPC headers, variables, raw output, strict selection, and introspection. Tests mostly cover plaintext, mapping, `-H`, and default-service behavior. There is no deep unit test area for introspection despite substantial schema synthesis code.

Recommendation:

- Add end-to-end tests for `--protoset`, TLS flags, `--max-time`, `--max-msg-sz`, `--raw`, variables files, `--strict-selection`, split headers, and failure paths.
- Add introspection unit tests for `__type`, `__typename`, enums, input types, lists/non-null, directives, type overrides, and unknown type behavior.

### P2: Documentation Has Multiple Implementation Drifts

Examples:

- README API link points to `Docs/api/GrpCurl.Net.DescriptorSources.yml`, but generated API YAML is not tracked; authored landing page is `Docs/api-reference.md`.
- Docs mention a root `global.json`, but only test-project-local `global.json` files exist.
- DocFX docs mention hand-authored `Docs/api/index.md`, which is absent.
- Authentication docs say certificate generation is under `Scripts/generate-certs.sh`, but the script is under `Tests/TestCertificates/generate-certs.sh`.
- Authentication docs say PKCS12 format is detected from content; implementation uses extension.
- Gql2Grpc cookbook documents `--introspection off`, but the CLI exposes a boolean `--introspection` defaulting true, not a tested on/off parser.
- Error suggestions in list/describe use wrong executable/order examples.

Recommendation:

- Add doc tests or script-lint checks for file paths and command examples.
- Align docs after product decisions on packaging, proto source support, authority, text format, and TLS behavior.

### P2: Line Endings and File Metadata Are Unmanaged

No `.gitattributes` file is present. In a WSL-to-Windows workflow, this leaves Bash script LF endings, generated fixture endings, and executable-bit expectations to each contributor's Git configuration. The observed unit failure in `OutputRendererTests` shows that newline assumptions are already leaking into tests.

Impact:

- Bash scripts can become CRLF and fail under Linux/WSL with confusing interpreter errors.
- Tests can pass on LF-only environments and fail on Windows because assertions split on `\n` without removing `\r`.
- Script executable bits are not portable to Windows and should not be the only documented execution path.

Recommendation:

- Add `.gitattributes` with explicit policies, for example `* text=auto`, `*.sh text eol=lf`, `*.ps1 text eol=crlf`, and binary rules for `.pfx`, `.crt`, `.key`, `.protoset`, images, and archives.
- Normalize tests through `StringReader`, `SplitLines`, or explicit CRLF/LF normalization before asserting logical lines.
- Prefer `bash script.sh` or `dotnet`/PowerShell validation entry points in docs where executable bits are not guaranteed.

### P3: Dependency and Release Reproducibility Is Loose

There is no root SDK pin, no `Directory.Packages.props`, and no `packages.lock.json`. `Gql2Grpc` uses floating `YamlDotNet` version `16.*`.

Recommendation:

- Pin package versions exactly.
- Enable NuGet lock-file restore for release builds.
- Consider central package management.
- Add scheduled dependency/advisory checks.

### P3: Helper Script Supply Chain Is Risky

`Scripts/install-go.sh` downloads Go, removes `/usr/local/go` with sudo, installs `grpcurl@latest`, and clones grpc-go without pinning commits/checksums.

Recommendation:

- Verify checksums/signatures.
- Pin grpcurl and grpc-go versions.
- Avoid sudo where possible.
- Mark the script as development bootstrap only, not production automation.

## grpcurl Feature Parity Matrix

| Reference capability | Current status | Notes |
|---|---|---|
| `list` services/methods | Mostly implemented | Reflection and protoset supported. CLI shape differs. |
| `describe` services/messages/enums/methods | Partially implemented | Proto-like output exists, but fidelity for proto2/options/comments/extensions is limited. |
| Invoke unary/server-stream/client-stream/bidi | Implemented | Core paths covered, but stdin streaming behavior is narrower than grpcurl. |
| Reflection descriptor source | Implemented | Uses v1alpha reflection. Lifecycle/disposal issues in GrpCurl CLI. |
| Protoset descriptor source | Implemented | Multiple protosets supported, but input size unbounded and fixture drift not checked. |
| Proto source files with import paths | Missing | Major parity gap: no `--proto`, `--import-path`, `-I`. |
| TLS and plaintext | Partially implemented | `invoke` drops TLS/mTLS options on RPC channel; plaintext timeout bug. |
| mTLS | Broken for invoke RPC path | Reflection channel has certs; RPC channel omits them. |
| `--authority` | Partially implemented | SNI/TargetHost only, not HTTP/2 authority override. |
| `--servername` | Implemented as TLS target host | Needs conflict behavior and tests vs authority. |
| `-H`, `--rpc-header`, `--reflect-header` | Implemented for strings | Binary metadata missing; split behavior needs more tests. |
| Environment expansion | Implemented by default | Upstream grpcurl gates this behind `-expand-headers`; this project always expands. Document as intentional divergence. |
| `--max-time` | Partial | Applies to RPC deadline, not whole operation. |
| `--connect-timeout` | Partial | Ignored for simple plaintext channel path. |
| `--max-msg-sz` | Implemented | Needs broader negative/e2e coverage and docs alignment. |
| JSON request/response format | Implemented | Dynamic message implementation is substantial, but proto2/edge cases remain. |
| Protobuf text format | Missing | Upstream `-format text` parity gap. |
| Rich RPC status details | Missing/partial | Status code/detail only; no rich details decode. |
| Response headers/trailers | Partial | Unary verbose path has some support; all call types/errors need parity. |
| `--protoset-out` | Implemented | Force/no-overwrite behavior documented. |
| `--proto-out-dir` | Missing | Upstream export capability not present. |
| Unix sockets | Missing | Upstream supports Unix socket addressing on Unix variants. |
| ALTS/xDS/keepalive/SSLKEYLOGFILE | Missing or partial | Keepalive option exists in core options but not surfaced consistently. |
| grpcurl-compatible CLI shape | Missing by design | Current subcommands are usable, but not drop-in compatible. |

## Security Review Summary

Primary risks:

- mTLS/custom CA omission on actual invoke RPC.
- Authority mismatch between documentation and actual HTTP/2 behavior.
- Verbose metadata/request logging can expose secrets.
- Timeout and input-size limits are incomplete, enabling hanging or resource pressure.
- Certificate revocation is disabled for custom CA and PKCS12 keys are exportable.
- Bootstrap scripts use unpinned/unverified external tooling.

Security strengths:

- TLS, custom CA, mTLS, and certificate loading are at least centrally modeled in `GrpcChannelFactory`.
- Error categories and exit codes are structured enough to be automation-friendly.
- Header env-var expansion helps keep secrets out of shell history.

## Test Coverage Review Summary

Strengths:

- Large unit suite around dynamic messages, protobuf reader/writer, descriptor sources, channel parsing, command helpers, and error rendering.
- Integration suite covers real in-process gRPC invocation and reflection.
- Gql2Grpc has meaningful parser/translation/projection and end-to-end coverage.

Gaps:

- Root-level test command fails on .NET 10/MTP.
- Unit suite currently fails locally.
- No CI or coverage threshold.
- TLS/mTLS is not tested against real RPCs with checked-in CA/client cert fixtures.
- `--authority`, plaintext timeout, `--max-time` total deadline, and header split behavior are not sufficiently tested.
- Proto source/import-path parity, protobuf text format, binary metadata, rich status details, and proto2 behavior have no meaningful coverage because the features are absent or partial.
- Protoset fixtures can drift from `test.proto`.

## Documentation Review Summary

Strengths:

- README and DocFX docs are unusually rich for a CLI project.
- Agent/script usage, JSON output envelopes, exit-code contracts, and Gql2Grpc docs are thoughtful.
- Learn Protobuf series is a strong onboarding asset.

Gaps:

- Docs overclaim TLS/mTLS behavior until the invoke channel bug is fixed.
- Install/tool command naming is not backed by `PackAsTool`, `ToolCommandName`, release docs, or publish instructions.
- Several file paths and generated API references are stale.
- Documentation does not clearly separate intentional divergence from grpcurl compatibility gaps.
- Generated `Docs/_site` exists locally but is not tracked; docs should make generated-vs-source state explicit.

## Recommended Remediation Roadmap

### Immediate Stabilization

1. Fix `invoke` RPC channel TLS/mTLS propagation by reusing one connection options model or one channel.
2. Fix root-level `.NET 10` test invocation by adding root MTP config or a test driver script.
3. Fix failing unit tests and test global console writer isolation.
4. Add Windows/Linux/macOS CI for build/test/coverage basics.
5. Add `.gitattributes` and normalize line-ending-sensitive tests and scripts.
6. Correct TLS/mTLS and cross-platform docs or hold release claims until fixed.

### Transport Correctness

1. Fix plaintext `--connect-timeout`.
2. Make `--max-time` a true total operation budget.
3. Implement true HTTP/2 authority override.
4. Add real TLS/mTLS integration fixture using checked-in CA/server/client certs.
5. Redact secrets from verbose output by default.

### Parity and Product Scope

1. Decide if this is a drop-in grpcurl-compatible CLI or a .NET-inspired CLI.
2. If parity is a goal, add `--proto`, `--import-path`, protobuf text format, binary metadata, rich status details, `--proto-out-dir`, and compatibility routing/aliases.
3. If parity is not a goal, document unsupported features and intentional divergences prominently.

### Architecture

1. Split `GrpCurl.Net.Core` from the CLI executable.
2. Promote `DescriptorSourceFactory`/channel lifecycle into shared core.
3. Extract descriptor graph/index code.
4. Split dynamic invocation/message/JSON/binary codec responsibilities.
5. Centralize command option records and exception translation.

### Release and Docs

1. Add tool/package/publish configuration and document real command names.
2. Pin dependencies and SDK, enable lock-file restore for release.
3. Convert production validation to test published release artifacts.
4. Replace or supplement Bash-only validation with a cross-platform .NET or PowerShell runner.
5. Add docs/example validation in CI.
6. Refresh DocFX source references and remove stale generated-path claims.

## Positive Signals

- The codebase already uses nullable reference types, modern C# constructs, generated regex, `X509CertificateLoader`, and structured command option modeling.
- `IDescriptorSource` is the right high-level abstraction for reflection/protoset/proto sources.
- Structured JSON envelopes and exit code contracts are strong for automation and agents.
- Gql2Grpc reuses the gRPC transport model instead of shelling out, which is the right long-term direction once the core/CLI split is cleaned up.
- The test server plus protosets/cert fixtures give the project the raw material needed for much stronger parity tests.

## Suggested First Issue List

1. Fix `InvokeCommandHandler` to reuse the fully configured channel/options for RPC and reflection.
2. Add root `global.json` MTP opt-in and CI workflow.
3. Fix failing unit tests.
4. Add Windows/Linux/macOS CI matrix coverage before preserving the cross-platform README claim.
5. Add `.gitattributes` and fix CRLF/LF-sensitive tests.
6. Replace Bash-only validation assumptions or document them as Unix/WSL-only.
7. Add TLS/mTLS integration fixture and tests for `--cacert`, `--cert`, `--key`, wrong CA, expired cert, `--servername`, and `--authority`.
8. Fix `GrpcChannelFactory` plaintext fast path so `ConnectTimeout` is honored.
9. Make `--max-time` apply from command start.
10. Redact sensitive headers in verbose output.
11. Decide and document grpcurl parity scope.
12. Add proto source/import-path support or explicitly declare it out of scope.
13. Split reusable core from CLI executable.
