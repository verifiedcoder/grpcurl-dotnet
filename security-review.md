# Security Review

Date: 2026-05-19

Reviewer posture: principal security review focused on .NET, gRPC transport behavior, descriptor ingestion, command-line input handling, filesystem writes, secret handling, and dependency advisories.

## Scope

Reviewed non-test production projects:

- `Src/Gql2Grpc/Gql2Grpc.csproj`
- `Src/GrpCurl.Net/GrpCurl.Net.csproj`
- `Src/GrpCurl.Net.Core/GrpCurl.Net.Core.csproj`
- `Scripts/ValidationRunner/ValidationRunner.csproj` as supporting repository tooling

Test projects and the test server were not assessed for product security behavior except where package advisory tooling reported solution-wide status.

## Methodology

The review covered manual source inspection of command handlers, gRPC channel creation, TLS/mTLS handling, metadata parsing, reflection and protoset descriptor loading, GraphQL-to-gRPC execution, local file reads/writes, child process launch points, verbose logging, and validation runner process management. Dependency advisories were checked with:

```powershell
dotnet list GrpCurl.Net.slnx package --vulnerable --include-transitive
```

NuGet reported no vulnerable packages for `Gql2Grpc`, `GrpCurl.Net.Core`, `GrpCurl.Net`, or `ValidationRunner` using `https://api.nuget.org/v3/index.json`.

## Executive Summary

No critical authentication bypass, remote code execution, TLS downgrade by default, command injection, or dependency vulnerability was identified. The codebase has several good controls already in place: TLS verification is enabled by default, `--insecure` is explicit, custom CA validation preserves hostname checks, mTLS options are carried through to invocation channels, child processes use `UseShellExecute = false` and `ArgumentList`, request metadata is redacted in verbose output, stdin has a default 16 MiB cap, and `--max-time` bounds `invoke` and `gql2grpc` operations.

The main security concern is filesystem containment for `--proto-out-dir`: reconstructed `.proto` file paths are derived from descriptor file names without validation. Descriptor data can come from remote reflection or user-supplied protosets, so a malicious descriptor name can escape the selected output directory. The remaining findings are hardening issues around unbounded schema ingestion, timeout consistency, ignored stdin limit configuration, and verbose response metadata handling.

## Findings

| ID | Severity | Area | Summary |
| --- | --- | --- | --- |
| SR-001 | High | Filesystem output | `--proto-out-dir` can write outside the requested output directory when descriptor file names contain traversal or rooted paths. |
| SR-002 | Medium | Remote reflection / availability | `list` and `describe` reflection operations lack a total deadline and descriptor resource limits. |
| SR-003 | Low | Local schema input | Protoset and GraphQL/config file reads are unbounded. |
| SR-004 | Low | CLI resource control | `--max-stdin-bytes` is defined but not wired into request generation. |
| SR-005 | Low | Verbose logging | Response metadata is written verbatim in verbose mode and is not redacted like request metadata. |

## Remediation Status

All five findings have been addressed in the current working tree:

- SR-001: `ProtoFileEmitter` now validates descriptor names, rejects traversal/rooted paths, normalizes the output root, and uses atomic create semantics for non-force writes.
- SR-002: `list` and `describe` now accept `--max-time`, pass operation cancellation through descriptor resolution/export paths, and descriptor sources enforce file count, dependency depth, symbol count, and reflection/protoset byte limits.
- SR-003: Local protoset, GraphQL document, GraphQL variables, and mapping configuration reads are bounded before the full file is read.
- SR-004: `invoke --max-stdin-bytes` is parsed, validated, and propagated to unary and streaming stdin request generation.
- SR-005: Verbose response headers and trailers now use the shared metadata redaction policy, with unsafe opt-out support and visible escaping for control characters.

Regression coverage was added for path containment, bounded file reads, protoset limits, command option exposure, stdin limit validation, response metadata redaction, control-character escaping, and Gql2Grpc mapping file limits.

## SR-001: `--proto-out-dir` Can Escape the Output Directory

Severity: High

Affected code:

- `Src/GrpCurl.Net/Commands/InvokeCommandHandler.cs`: `--proto-out-dir` calls `ProtoFileEmitter.WriteAsync(...)`.
- `Src/GrpCurl.Net.Core/Output/ProtoFileEmitter.cs`: `EmitFileAndDependenciesAsync` builds `targetPath` with `Path.Combine(outputDirectory, file.Name)` and writes it with `File.WriteAllTextAsync(...)`.

`file.Name` comes from `FileDescriptor.Name`. That descriptor graph may originate from server reflection or a protoset. The emitter treats the name as a trusted relative path and does not reject `..` segments, absolute paths, drive-qualified Windows paths, UNC paths, or alternate directory separators before creating directories and writing the reconstructed file.

Impact: A user who runs `invoke --proto-out-dir <dir>` against a malicious reflection server, or against a malicious protoset, could write files outside `<dir>` with the privileges of the CLI process. Without `--force`, existing files are not overwritten, which reduces impact. With `--force`, the same path traversal can overwrite accessible files. The written content is reconstructed `.proto` text rather than arbitrary binary data, but this still breaks the expected filesystem boundary.

Recommendation:

- Resolve the output root with `Path.GetFullPath(outputDirectory)`.
- Reject descriptor names that are empty, rooted, drive-qualified, UNC-style, contain `..` path segments, contain invalid path characters, or normalize outside the output root.
- After combining and normalizing, enforce that the final path starts with the normalized output root plus a directory separator.
- Prefer `FileMode.CreateNew` for non-force writes to close time-of-check/time-of-use races.
- Add tests using a malicious protoset descriptor name such as `../escape.proto`, `/tmp/escape.proto`, `C:\temp\escape.proto`, and `safe/../../escape.proto`.

## SR-002: Reflection Discovery Lacks Total Deadline and Descriptor Limits

Severity: Medium

Affected code:

- `Src/GrpCurl.Net/Commands/ListCommandHandler.cs`: `DescriptorSourceFactory.CreateAsync(...)` is called with `CancellationToken.None`.
- `Src/GrpCurl.Net/Commands/DescribeCommandHandler.cs`: `DescriptorSourceFactory.CreateAsync(...)` is called with `CancellationToken.None`.
- `Src/GrpCurl.Net.Core/DescriptorSources/ReflectionSource.cs`: reflection responses are parsed and cached without explicit byte, file-count, dependency-depth, or total-descriptor limits.

`invoke` and `gql2grpc` have total operation budget handling via `--max-time`, but `list` and `describe` only expose connection timeout. After the connection is established, a slow or hostile reflection service can keep discovery work open. Reflection also accepts descriptor bytes from the peer and parses them into dictionaries and `FileDescriptor` graphs without an application-level ceiling. The reflection resolver is also less defensive than the protoset resolver: `ProtosetSource.ResolveFileDescriptor(...)` tracks the current dependency path and detects circular imports, while `ReflectionSource.ResolveFileDescriptor(...)` recursively resolves dependencies without the same cycle guard.

Impact: A malicious or broken reflection server can cause CLI hangs, excessive memory use, excessive CPU use, or recursion failure during `list`/`describe`. This is availability-focused rather than confidentiality or integrity-impacting, but it is remotely triggerable when users inspect untrusted endpoints.

Recommendation:

- Add `--max-time` to `list` and `describe`, matching the semantics already documented for `invoke` and `gql2grpc`.
- Pass a linked operation token through descriptor loading, symbol lookup, protoset export, and proto emission.
- Apply explicit limits for reflection descriptor bytes, descriptor count, dependency depth, and total loaded symbols. Reasonable defaults can be configurable for unusually large schemas.
- Share the circular-dependency guard used by `ProtosetSource` with `ReflectionSource`.
- Add tests for a reflection stream that never completes, oversized descriptor responses, and cyclic dependency descriptors.

## SR-003: Protoset and GraphQL/Config File Reads Are Unbounded

Severity: Low

Affected code:

- `Src/GrpCurl.Net.Core/DescriptorSources/ProtosetSource.cs`: `File.ReadAllBytesAsync(filePath, ...)` reads an entire protoset before parsing.
- `Src/Gql2Grpc/Commands/QueryCommandHandler.cs`: query and variables files are read fully with `File.ReadAllTextAsync(...)`.
- `Src/Gql2Grpc/Configuration/MappingConfigLoader.cs`: mapping files are read fully with `File.ReadAllTextAsync(...)`.

These are local operator-supplied files, so the trust boundary is narrower than remote reflection. Still, a huge or intentionally malformed file can drive avoidable memory pressure before validation occurs.

Impact: Local denial of service when an automation, CI job, or wrapper process feeds unexpectedly large schema, query, variables, or mapping files to the tools.

Recommendation:

- Introduce explicit maximum file sizes for protosets, GraphQL documents, variables files, and mapping files.
- Fail before reading the full content when `FileInfo.Length` exceeds the cap.
- Consider separate caps for descriptor artifacts and text inputs, with documented overrides for large schemas.

## SR-004: `--max-stdin-bytes` Is Ignored

Severity: Low

Affected code:

- `Src/GrpCurl.Net/Commands/InvokeCommandHandler.cs`: `maxStdinBytesOpt` is defined and added to the command, but the parsed value is not retrieved or passed into `ExecuteAsync`.
- `GenerateRequests(...)` has a `maxStdinBytes` parameter with a 16 MiB default, but client-streaming and bidirectional-streaming call sites invoke it without supplying the CLI value.

The default 16 MiB stdin cap still provides useful protection. The issue is that users cannot tighten the cap for constrained environments or raise it intentionally for known-safe large streaming payloads, despite the documented option.

Impact: Resource-control expectations do not match runtime behavior. In CI or agent contexts, an operator may believe a lower limit is being enforced when the process still accepts up to the default cap.

Recommendation:

- Parse `maxStdinBytesOpt`, validate it is positive, and pass it through `ExecuteAsync` to all `GenerateRequests(...)` call sites.
- Add arrange/act/assert tests for default, custom small cap, and invalid cap values.

## SR-005: Verbose Response Metadata Is Written Verbatim

Severity: Low

Affected code:

- `Src/GrpCurl.Net/Commands/InvokeCommandHandler.cs`: `WriteVerboseResponseHeaders(...)` and `WriteVerboseResponseTrailers(...)` write non-binary metadata as `$"{entry.Key}: {entry.Value}"`.
- `Src/GrpCurl.Net.Core/Utilities/SecretRedactor.cs`: request metadata has a redaction helper, but response metadata does not use the same policy.

Verbose request metadata output redacts headers such as `authorization`, `cookie`, `*-token`, `*-secret`, and `*-bin`. Response headers and trailers are emitted directly. If a server returns sensitive metadata such as `set-cookie`, token-like headers, or terminal control characters accepted by the gRPC stack, verbose logs can expose or render remote-controlled content.

Impact: Secret exposure or log/terminal confusion in verbose troubleshooting sessions, especially in CI logs collected by other systems. The user must opt into verbose output, so severity is low.

Recommendation:

- Reuse `SecretRedactor` for response metadata by default.
- Add an explicit opt-out if raw response metadata is needed for troubleshooting.
- Encode or visibly escape control characters before writing metadata to a terminal.
- Consider showing binary metadata as redacted by default, or as base64 only under the unsafe/raw mode.

## Positive Security Controls Observed

- TLS is the default transport when no scheme is supplied; plaintext requires `--plaintext`.
- `--insecure` is explicit and, when verbose mode is enabled, produces a warning.
- Custom CA validation uses `X509ChainTrustMode.CustomRootTrust`, preserves hostname mismatch rejection, and defaults revocation checks to online mode.
- mTLS client certificate options are included in the shared channel options used for reflection and business RPCs.
- `--authority` affects HTTP/2 authority separately from `--servername`/SNI.
- Header binary metadata must be valid base64.
- Request metadata redaction is enabled by default in verbose mode.
- `protoc` execution uses `UseShellExecute = false` and `ArgumentList`, mitigating shell injection through proto paths and import paths.
- Temporary protoset generation uses a GUID-based filename and `FileMode.CreateNew`.
- `--protoset-out` refuses to overwrite existing files unless `--force` is supplied.
- `invoke` and `gql2grpc` use linked cancellation tokens for total operation deadlines when `--max-time` is supplied.

## Operational Notes

- `ProtoSource` intentionally resolves `protoc` from `PATH`. This is normal CLI behavior and not a finding by itself, but users should avoid running the tool with an untrusted `PATH` when compiling `.proto` files.
- `ValidationRunner` launches only `dotnet` and published local binaries using `UseShellExecute = false` and `ArgumentList`. Its recursive temp-directory cleanup is scoped to a GUID-named directory it creates under the OS temp path.
