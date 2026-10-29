# PRD-039: Clear transient plaintext and reject corrupt reference indexes

Base: `feature/bug-fixes` at `e925e8e` (PRD-013). No public interface, settings schema, storage location,
encryption, or serialized-format changes. Review approval and fresh explicit merge authorization are
required before merging.

## Implementation

Fallback AES-GCM encryption/decryption and Windows DPAPI protection/unprotection now enclose each
application-owned plaintext array in `try/finally` immediately after allocation/return from DPAPI.
`CryptographicOperations.ZeroMemory` runs on success and every exceptional exit. Encryption input is
cleared before ciphertext encoding and persistence; decryption output is cleared after conversion to
the returned string. Internal, instance-scoped checkpoints let tests retain actual array references
and inject failures inside those protected lifetimes. No global hooks or buffer logging are used.

This boundary excludes immutable strings and cryptographic-library copies. macOS cleanup is unchanged;
native Secret Service internals are outside scope. PRD-005's cached key, operation gate, disposal-time
zeroization, facade admission accounting, and deferred backend disposal are preserved.

`ReadIndex` now has one interpretation: a missing file means empty; an existing file must contain a
JSON array of unique, non-null, non-whitespace strings. Comparisons are ordinal. Valid references,
including leading/trailing whitespace around nonblank text, case differences, and distinct Unicode
representations, remain unchanged. Reads never repair or rewrite content. Invalid mutation references
are rejected before filesystem/backend side effects.

Set/delete acquire the existing index gate for preflight validation, release it before backend calls,
then reacquire it to re-read and update the index. Invalid JSON, wrong types, invalid entries, and
duplicates use sanitized `SecretStorageException` messages with backup recovery instructions. An index
that becomes corrupt after the backend committed is preserved and the error reports the completed
secret change. Persistence failures retain PRD-013's commit-point semantics. Listing fails on corruption;
readable backend secrets remain accessible; fallback missing-reference decisions require a valid index.
Storage errors never authorize fallback (PRD-006).

## Verification

`SecretPlaintextBufferTests` observes nonzero encryption inputs/decrypted outputs and retains the real
arrays to verify clearing. It covers Unicode/empty values, persisted round trips, immediate allocation
failure, failure before string conversion, cryptographic failure, authentication failure, persistence
failure, shared-key reuse, and disposal zeroization. DPAPI cases execute on Windows and explicitly skip
elsewhere. Seeded decryption destinations ensure an allocation-failure test detects missing cleanup.

`SecretIndexTests` covers the accepted and rejected shapes, exact text preservation, preflight rejection,
corruption between backend commit and index update, persistence failure, continued reads, fallback
guards, and the existing environment editor's sanitized error/input-retention boundary. A backend callback
lists references to prove preflight does not hold the index gate across backend calls. These tests use
APIs present at `e925e8e` for baseline comparison. Existing PRD-005/006/013 suites remain regression gates.

Validation results and delivery evidence are recorded with the PR. The required gates are locked
restore, Release build, filtered solution tests, ViewModels line coverage at least 80%, documentation,
and the complete Linux/Windows/macOS CI matrix.

Local Linux validation: locked restore and Release build passed with zero warnings/errors; the filtered
solution with coverage passed 2,270 tests with 12 platform skips and zero failures. ViewModels coverage
was 4,904/5,382 = 91.1%. DocFX passed with warnings treated as errors. Against `e925e8e`, the unchanged
index suite had 26 expected failures and six passes. Removing only transient-buffer cleanup in an
isolated copy produced seven expected failures, two passes, and seven Windows skips. One passing case
uses an empty buffer; the other exercises AES's own authentication-failure clearing. Windows DPAPI
execution and all three operating systems remain required CI gates, not inferred from Linux results.

## Recovery and remaining boundaries

Close Studio and restore a valid `secret-keyrefs.json` from backup before retrying; restore a backup
consistent with the backend when a partial operation is reported. No repair UI, reconstruction, or
guessed ownership is introduced. A missing index remains indistinguishable from first use.

Separate remaining concerns:

- Index authentication/reference ownership: valid JSON is not proof of provenance. Deletion, valid
  malicious edits, and imported-reference collisions are not solved (see the separate PRD-057 work).
- Multi-file consistency: there is no transaction spanning backend data, index, and workspace; no
  cross-process locking or automatic reconciliation. Permission and atomic single-file protection do
  not make those updates transactional.

PRD-013 already provides private create-time permissions, hardening/verification, link rejection, and
atomic replacement for these files. Its evidence comprises mode/ACL inspections and same-account tests;
it does **not** claim an actual second-account access test. This change retains that disclosure and
does not repeat the original historical index-permission claim as a current defect.
