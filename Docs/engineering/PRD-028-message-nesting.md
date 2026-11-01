# PRD-028: Bound response-message nesting

Base: `feature/bug-fixes` at `6e06c91a51142a6cc7eae268151976e2e8eda704`.
The shared Core response parser and JSON renderer allow at most 100 nested protobuf message levels,
with the root at depth zero. Attempting to enter level 101 fails with a diagnostic and gRPC `Internal`.
This fixed limit has no CLI flag, Studio setting, workspace field or persisted policy.

## Implementation

`MessageNesting` defines the limit and a typed `MessageNestingException` carrying the limit and phase.
Depth is an integer passed along each active path, with no mutable global or per-call counter. Siblings
receive the same remaining budget, and a failed message cannot affect a later invocation.

`ProtobufReader` checks before entering a nested message or known group, including repeated messages
and implicit map-entry messages. Unknown groups use an explicit bounded skip routine; delegating them
to a fresh `CodedInputStream` would let them reset the effective budget. Unknown non-group fields retain
the existing skip behavior. Unknown group terminators must match and cannot be omitted.

`SimpleDynamicMessage` carries the same logical depth through ordinary, repeated and map JSON rendering,
including `Struct`, `Value` and `ListValue` callbacks. A map entry costs one level and a message-valued
entry costs another. An `Any` field costs one level and its resolved embedded message costs another.
Embedded binary parsing starts at that accumulated depth, and its JSON rendering uses that depth too.
An unresolvable `Any` retains its existing base64 fallback. In-memory cycles exhaust the depth budget.

`DynamicInvoker` translates nesting failures during parsing or rendering into `RpcException` with
`StatusCode.Internal` and the limit/phase diagnostic. The parse diagnostic uses a distinct prefix so the
existing empty-response/cardinality normalizer does not reclassify it as `Unimplemented`. JSON formatting
uses a derived container limit of 202, sufficient for an object and repeated-field array at each accepted
protobuf level. Thus pretty-printing and `--emit-defaults` do not impose the JSON library's lower default
limit on an otherwise accepted response. Formatting finishes before a response string is returned.

CLI failures use the existing error and unsuccessful-exit path. Studio's `InvocationRunner` maps a
stream-preview rendering failure into a terminal error row, so it reaches the normal view-model error
and history handling. Unary failures use the existing error result. Disposing a failed streaming
enumeration cancels the call. There are no new controls or dialogs. Request JSON parsing,
descriptor-graph limits, message-size limits and general
CPU/allocation budgets remain separate. This change does not alter existing root-WKT or group JSON shapes.

## Baseline evidence

In an isolated detached worktree at `6e06c91`, two child-process probes constructed 2,000 levels using
iterative byte encoding. A 512 KiB worker stack makes the failure deterministic without risking the
parent test runner. Ordinary binary nesting and nested `Any` rendering each terminated the child with
`Stack overflow.` and exit code 134. The assertions requiring normal rejection failed as expected.
The [baseline excerpt](evidence/prd-028-baseline.txt) identifies omitted repeated frames. The full trace
and baseline probe source are retained in the local remediation evidence folder.

The fixed `NestingCrashProbeTests` run both probes, require successful child completion after catching
the limit diagnostic, and parse another valid message in that same process.

## Acceptance traceability

| Requirement | Evidence |
|---|---|
| Root zero, levels 99/100 accepted, 101 rejected | `MessageNestingTests.Binary_and_rendering_boundaries_agree`, singular and repeated fields, compact/pretty JSON and defaults on/off |
| Any cannot reset the budget | `Any_and_embedded_binary_share_the_remaining_budget`, `Any_special_WKT_rendering_does_not_reset_depth` |
| Maps and groups consume the same budget | Map-entry boundary tests, known/unknown group boundaries and unknown groups inside a fresh nested stream |
| Recursive WKT rendering and cycles terminate | Exact `Value`/`ListValue` boundaries; ordinary, map, list and struct cycles |
| Wide messages and later calls remain valid | Sibling and independent-call tests, valid calls following failures |
| Process survival | Both isolated baseline failures and the corresponding fixed child-process probes |
| CLI errors without partial response output | Four real-process cases: unary/streaming and binary/Any |
| Studio recovery and stream disposal | Four live HTTP/2 server cases through `InvocationRunner`, including server-observed cancellation of parked streams and a subsequent valid call |
| Existing behavior | Full filtered solution, ViewModels coverage gate, CLI validation, Core and Studio conformance, three-OS CI |

## Verification

Run build and coverage commands sequentially: rebuilding a referenced assembly while coverage tests are
running can invalidate the test process's mapped assembly metadata.

```bash
dotnet restore --locked-mode GrpCurl.Net.slnx
dotnet build GrpCurl.Net.slnx -c Release --no-restore
dotnet test --solution GrpCurl.Net.slnx -c Release --no-build --no-restore -- --filter-not-trait "Category=Performance" --coverlet --coverlet-output-format cobertura --results-directory=/tmp/prd028-coverage
python3 Scripts/check-coverage-gate.py --coverage-dir /tmp/prd028-coverage --package GrpCurl.Net.Studio.ViewModels --min 0.80
dotnet run --project Scripts/ValidationRunner -c Release -- --ci
Scripts/run-conformance.sh
Scripts/run-studio-conformance.sh
docfx Docs/docfx.json --warningsAsErrors
```

The implementation machine uses `DBUS_SESSION_BUS_ADDRESS=unix:path=/tmp/prd028-no-session-bus` for the
solution run, as in the preceding PRD-026/027 fixes. This isolates unrelated desktop session behavior.
Local Linux results do not replace the Windows/macOS CI gates. Delivery evidence, retries and the
independent-review verdict are recorded with the PR and in `PRD-028-Fix-Review.md` alongside the existing
remediation review records. Independent approval and fresh explicit user merge authorization are required.

See [CLI troubleshooting](../articles/troubleshooting.md#response-message-nesting-limit) and
[Studio troubleshooting](../articles/studio/troubleshooting.md#response-message-nesting-limit).
