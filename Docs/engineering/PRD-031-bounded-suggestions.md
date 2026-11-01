# PRD-031: Bound GraphQL mapping suggestion costs

Base: `feature/bug-fixes` at `84f00a40648c7f5bd91944c299f3e4f4a71f53cb`.
Studio's descriptor-aware GraphQL mapping validation now bounds the cost of each typo comparison.
The fixed maximum is **256 UTF-16 code units** per operand; the maximum edit distance remains **3**.
Longer symbols remain valid for normal resolution. Only optional suggestions are omitted.

## Implementation

`GraphQlService.Closest` rejects oversized targets before enumerating candidates or allocating scratch
space. It skips oversized candidates and length differences greater than three without inspecting their
characters. Names are neither truncated nor normalized. Comparisons retain ordinal UTF-16 semantics,
unit insertion/deletion/substitution costs, closest-match ranking and the first candidate on a tie.
The first exact match ends enumeration.

Two 257-element integer spans are allocated on the stack once per lookup (2,056 bytes total). Each
comparison initializes a row and evaluates only the distance-three diagonal band, at most seven cells
per row. Boundary sentinels prevent stale cells from previous rows or candidates entering the band.
A row whose minimum exceeds three ends the comparison. No quadratic matrix or per-candidate scratch
allocation remains. Enumeration stays streaming with no sorting or candidate-count cutoff.

The existing validation token reaches service, method, argument-path, selection-mask-path and response
unwrap suggestions. Cancellation is checked before enumeration and between candidates. There is no
public API, configuration, mapping-schema or CLI change; the internal helper gains an optional token.

Comparison memory is fixed and work is bounded per candidate. Total lookup work still scales with
candidate count, and an arbitrary enumerable must cooperate with enumeration/cancellation. Descriptor
loading, retained schema memory, diagnostic-string construction and broader resource policies remain
separate. This is not a constant-allocation guarantee for the complete network/validation pipeline.

## Baseline and acceptance evidence

The [isolated baseline probe](evidence/prd-031-baseline.txt) at `84f00a4` compares a preallocated 2 MiB
candidate with `Service`. After warming the matcher, `GC.GetAllocatedBytesForCurrentThread` measured
**67,108,968 bytes** allocated by one lookup. The assertion requiring less than 32 KiB failed. The probe
uses a short target to demonstrate the defect without exhausting process memory.

| Requirement | Tests |
|---|---|
| Oversized names do not allocate a distance matrix | `SuggestionAllocationTests`, same probe on baseline and fix |
| Fixed scratch allocation across many candidates | 10,000 eligible 256-character candidates, warmed synchronous allocation measurement excluding fixture construction |
| Exact boundary and streaming behavior | Lengths 255/256/257; oversized target never enumerates; oversized candidate followed by a valid match; exact match stops enumeration |
| Compatibility and band correctness | Empty inputs, ordinal/case behavior, first ties and closer later matches; deterministic differential checks against a reference matrix, including surrogate pairs and band-edge edits |
| Cancellation reaches every caller | Before/during enumeration and all five mapping suggestion paths |
| Real reflection and long-symbol resolution | HTTP/2 reflection fixture advertising a 2 MiB service name; validation completes within a 30-second test deadline and suggests the ordinary service; synthetic long service/method/field names resolve normally |
| Field and method diagnostics | Argument, mask and unwrap suggestions remain for ordinary names; oversized typos still produce configuration errors without suggestions |

The test project uses an explicit Core assembly alias for reflection protocol types because the test
server dependencies also expose types with the same namespace. The fixture uses real protobuf reflection
frames over HTTP/2, without adding packages or changing package locks.

## Verification and delivery

Run build/coverage commands sequentially to avoid overwriting assemblies used by a running test process.

```bash
dotnet restore --locked-mode GrpCurl.Net.slnx
dotnet build GrpCurl.Net.slnx -c Release --no-restore
dotnet test --project Tests/GrpCurl.Net.Studio.Tests.Unit -c Release --no-build --no-restore -- --filter-class '*Suggestion*'
dotnet test --solution GrpCurl.Net.slnx -c Release --no-build --no-restore -- --filter-not-trait "Category=Performance" --coverlet --coverlet-output-format cobertura --results-directory=/tmp/prd031-coverage
python3 Scripts/check-coverage-gate.py --coverage-dir /tmp/prd031-coverage --package GrpCurl.Net.Studio.ViewModels --min 0.80
dotnet run --project Scripts/ValidationRunner -c Release -- --ci
Scripts/run-conformance.sh
Scripts/run-studio-conformance.sh
docfx Docs/docfx.json --warningsAsErrors
```

On the implementation workstation, the full solution uses
`DBUS_SESSION_BUS_ADDRESS=unix:path=/tmp/prd031-no-session-bus` to isolate desktop session behavior, as in
PRD-026/027/028. The PR and local `PRD-031-Fix-Review.md` record final Linux results, three-OS CI and any
retries. Independent approval and fresh explicit user merge authorization remain required.

See [Studio troubleshooting](../articles/studio/troubleshooting.md#graphql-mapping-errors-have-no-typo-suggestion).
