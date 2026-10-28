# PRD-013: Secure, atomic secret-file persistence

Base: `feature/bug-fixes` at `c05d8cf`. The previous fallback and DPAPI saves truncated their final files;
permissions were applied late, incompletely, or best-effort. The index's predictable temporary file also
lacked private creation permissions.

## Implementation and boundaries

`SecureSecretFile` serves fallback ciphertext, DPAPI ciphertext, the reference index, and fallback key
inputs. The existing shared Studio directory is protected before access (`0700` on Unix; protected
current-user/SYSTEM ACL on Windows). Existing secret files are tightened and verified. Linked directories
and targets are rejected before hardening or content access. No storage migration or format change occurs.

Temporary files have exclusive, unpredictable names in the destination directory and are private at
creation (`0600` or explicit protected Windows ACLs). Contents are flushed to disk, then the stream closes
before atomic replacement. Replacement is the commit point. Failures before it preserve the old file;
after it the complete new file is authoritative. Cleanup removes only a temporary file created by that
operation. Reads ignore abandoned temporary files. Directory-entry durability across power loss depends
on the filesystem and is not guaranteed here.

Salt and generated machine ID publication never overwrites an existing destination. Competing creators
read the committed winner. Missing or malformed key inputs with an existing ciphertext file produce a
recovery error, including with a cached key. System machine IDs stay read-only. HKDF inputs, AES-GCM blob
encoding, DPAPI scope, JSON shapes, and filenames are unchanged.

Windows ACL construction, create-time protection, and inspection use the .NET 10 reference-pack APIs.
No package is added. Applying an existing directory's DACL uses `SetFileSecurityW` deliberately:
[the standard ACL setter propagates changes to existing children](https://learn.microsoft.com/en-us/windows/win32/api/aclapi/nf-aclapi-setnamedsecurityinfow),
while [this local setter does not](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-setfilesecurityw).
This keeps unrelated files in the shared directory unchanged. A regression checks their ACLs as well
as the tightened directory and target. File DACLs use the managed setter; no directory traversal occurs.

`SecretStorageException` reuses existing UI boundaries but is explicitly excluded from unavailable-backend
classification. Raw OS errors, paths, and credentials are not included. Probe/storage failures do not
permit fallback even with saved authorization. Index security failures escape tolerant index parsing.
When backend data committed but updating the index failed, the error reports that partial operation.
There is no cross-process coordination or transaction spanning data and index. PRD-005 gates, admission
accounting, deferred disposal, and key zeroization remain unchanged.

## Verification

- Deterministic checkpoints cover creation, partial write, flush, close, and replacement, including a
  post-commit failure. The old file remains readable before replacement; the new file is complete after.
- Permission inspection runs immediately after directory and temporary-file creation, and under `umask 000`
  in a separate POSIX CI process. Existing broad Unix permissions and Windows explicit/inherited rules
  are tightened. Failure to establish/retain protection is surfaced; linked and dangling targets fail.
- Tests cover all three persistence callers, initialization races for each key input, restart stability,
  invalid/missing inputs, legacy ciphertext, harmless abandoned files, exclusive creation ownership,
  partial index errors, UI recovery messages, and fallback consent/block/disposal regressions.
- Six tests that compile unchanged against `c05d8cf` all fail there: directory/file read hardening,
  missing salt, malformed salt, wrong-length salt, swallowed index access failure, and index mode.
- Initial local Release build: zero warnings/errors. Filtered solution run with coverage: 2,229 passed,
  four platform skips, zero failures; ViewModels 4,904/5,382 = 91.1% (80% floor). After the Windows
  directory-scope correction and one verification-failure test, focused `umask 000` run: 185 passed,
  three macOS skips, zero failures (the Windows-only DPAPI case was subsequently marked an explicit skip).
  Full final-head three-OS results are recorded on the PR.

These are mode/ACL inspections and same-account functional tests. No actual second-account access test
was performed. Windows DPAPI/ACL execution requires the Windows CI leg; Linux results alone do not
establish it.

## Remaining work

PRD-039 remains open for transient plaintext hygiene and remaining index concerns. This change resolves
its index/file-permission overlap only. Tolerant malformed-index parsing, authenticated reference
ownership, and transactional workspace/secret editing are not redesigned. Review approval and explicit
merge authorization are required before merging this fix.
