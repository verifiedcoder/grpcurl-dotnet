# History privacy and recovery

History keeps call metadata by default: method, connection, status, timing, counts, and secret-name-redacted headers. Request bodies, GraphQL documents, and free-text server errors require **Settings → History → Capture request bodies and error details**. Response bodies have a separate opt-in. Existing settings without the new request-capture setting get the safer default. Disabling capture stops new records; it does not delete existing history.

## Capture and redaction

With capture enabled, Studio parses JSON and replaces secret-named properties recursively, including properties in arrays, with `[redacted]`. **History redact fields** adds case-insensitive, exact property names, separated by commas. Built-in secret classification still applies. Redaction happens before the UTF-8 byte cap.

GraphQL documents retain operation structure and variable references, but every scalar literal (including defaults, enums, numbers, booleans, nulls, and block strings) is replaced and comments are removed. GraphQL variable values and streaming message bodies are not recorded. Invalid JSON/GraphQL and unsupported body formats are omitted. Response capture uses the same JSON field redaction. Free-text errors cannot be reliably field-redacted: opting into request capture also accepts this risk. Names, addresses, method names, and unclassified header values may still identify sensitive systems or data. Redaction is not a guarantee that arbitrary payloads contain no secrets.

Omitted, redacted, and truncated request bodies cannot be replayed. A complete unmodified JSON request or legacy request can still open a draft, subject to the existing connection-resolution rules. Replay never sends automatically. New GraphQL snapshots are for inspection, not replay.

## Storage and keys

`history.ndjson` remains in Studio's application-data directory. Each line is an authenticated AES-GCM encrypted record (`enc1:` envelope) with a fresh nonce. The random history key is stored through Studio's selected secret store under `studio/v1/history/at-rest-key`. There is no independent history password or implicit fallback authorization. If native storage is unavailable, follow [secret storage policy](secret-storage-policy.md); fallback still requires explicit consent or administrator authorization. Failed capture does not prevent an invocation completing. The History tab displays a sanitized recovery notice when storage cannot be used.

History and its stable `history.ndjson.lock` file use owner-only access on Unix (directory 0700, files 0600). Windows uses protected ACLs granting access to the current user and SYSTEM. Atomic migration/retention rewrites use private temporary files, flush them before replacement, and clean up owned temporary files on failure. This reuses PRD-013's permission and replacement implementation. Encryption does not defend against another process running as the same user, a compromised OS, or old plaintext backups.

The history key is re-resolved for each operation; cached rows cannot bypass key loss or an unavailable secret store. Application-owned key and plaintext byte arrays are cleared on success and exceptions. Immutable strings, displayed rows, library copies, and original request/response objects are outside this cleanup boundary.

## Upgrading existing history

The first read or mutation automatically converts valid legacy plaintext records, including mixed plaintext/encrypted files, into an encrypted file using atomic replacement. Existing bodies, IDs, and pins are preserved; current capture preferences do not erase old content. Until migration commits, the old file remains. A failure after replacement may leave the encrypted replacement committed. Old backups, copies, and exported files are not modified.

An unfinished final record from an interrupted append can be discarded; a valid record without a final newline is preserved. Complete malformed records, malformed JSON, and authenticated-record failures stop the operation and preserve the file. A syntactically incomplete encrypted tail cannot always be distinguished from truncation damage. Per-record authentication does not authenticate record ordering, the presence of every record, or the file as a whole.

Cooperating Studio processes serialize key initialization and history file operations using a stable OS file lock. A busy operation times out with a retry message. Closing or terminating a process releases the held lock; leave the lock file in place. Older Studio versions do not participate in this protocol: close them before upgrading and do not run them against the new history file.

## Retention, deletion, export, and backup

The default retention limits are 1,000 entries and 50 MiB of encrypted history. On append, oldest unpinned entries are evicted until limits are met; pinned records can exceed either limit. The default per-body/error snapshot cap is 256 KiB, applied after redaction without splitting a Unicode scalar. Policy ceilings and pin limits are separate outstanding work (PRD-014).

**Delete selected** and **Clear unpinned** require a readable history/key. **Clear all** is explicitly confirmed and can remove unreadable history without decrypting it. These operations remove the local file/records, not backups, snapshots, or other copies, and do not promise secure erasure. Clear all retains the history key and stable lock file. If the key value itself is malformed, restore it or explicitly remove the history key reference in Settings → Security after clearing history and closing other Studio instances.

**Export** first warns that the destination is plaintext. It exports the selected stored records, including any preserved legacy payloads, to a private file. It revalidates storage before exporting and rejects destinations in Studio's storage directory. Export does not grant new capture consent. Review the file before sharing it, and do not attach it automatically to support reports. The built-in diagnostics text bundle does not include the history file. Manually copied application-data directories can contain history and secret-store files; treat them as sensitive even when encrypted.

For backup, close all Studio instances and capture a consistent history and secret-store backup. Native keys need their platform's backup/recovery facilities; the fallback requires matching `secrets.json`, `secrets.salt`, `machine.id`, and `secret-keyrefs.json` on the original machine/account. A history file alone is not a portable backup. History and key/index files are not one transaction. Loss of a file, whole-file rollback, record deletion/reordering, and cross-file inconsistency are not resolved by this change.

## Machine policy

Administrators can restrict new capture through a separate `history-policy.json`, read at startup alongside the existing secret policy:

| Platform | Path |
|---|---|
| Windows | `%ProgramData%\GrpCurlNet.Studio\history-policy.json` |
| macOS | `/Library/Application Support/GrpCurlNet.Studio/history-policy.json` |
| Linux | `/etc/grpcurl-studio/history-policy.json` |

```json
{
  "history": {
    "enabled": true,
    "captureRequestBodies": false,
    "captureResponses": false
  }
}
```

Each `false` prohibits that capture; `true` or an absent flag leaves the user's preference in control. Setting `enabled` to `false` disables all new history records. Missing files and `{}` impose no restrictions. The maximum policy size is 4 KiB. Wrong types, duplicates, unknown properties, malformed or unreadable files disable new history persistence and show an administrator recovery notice. Restore a valid policy and restart Studio.

Restrictions apply in both the recorder and store, and the corresponding Settings controls are disabled with a managed notice. Stored user preferences are preserved, so a permissive policy cannot create consent. Existing records remain available to read, migrate, export, or explicitly delete; this policy does not purge historical data. User settings cannot override it, but this is an application policy, not a security boundary against a hostile local administrator or modified client.

## When history is unavailable

Restore secret-storage access and a consistent history/key backup, then retry. If the History tab reports a missing key, restore the original key rather than creating a replacement for encrypted data. Authentication or complete-record corruption is never treated as an empty history. No automatic reconstruction or guessed recovery is attempted. **Clear all** remains available if you choose to discard history instead. Close other Studio processes before restoration; do not edit encrypted records or delete the lock file while Studio is running.
