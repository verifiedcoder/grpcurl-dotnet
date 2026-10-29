# Troubleshooting

## "Locked by another instance" banner

Studio takes an advisory lock on the open workspace so two running instances don't clobber each other's
saves. If another live instance holds the lock, you'll see a **Locked by PID … on … since …** banner and
autosave is paused. Close the other instance, or click **Take over** to seize the lock (the previous
holder degrades to read-only).

## "This workspace file is read-only" banner

The workspace file is read-only on disk, so Studio can't autosave. Your edits are kept in memory — use
**File → Save As** to write them somewhere writable, or clear the file's read-only attribute.

## `protoc` not found

The **Proto** descriptor source compiles `.proto` files by shelling out to `protoc`. If it isn't on your
`PATH`, descriptor loading fails with install guidance. Install the Protocol Buffers compiler (or set its
path in **Settings → protoc**), or switch the connection to **Reflection** or **Protoset**, which need no
external tools.

## Secrets aren't being remembered

Studio prefers the OS keystore. If it is locked or unavailable, secret operations fail with an actionable
message; Studio does not silently retry against file storage. Unlock the keychain or start a Secret Service
provider, then retry the operation. For an unavailable startup backend, use **Settings → Security → Retry
native keystore**.

If no native backend can be used, **Enable encrypted-file fallback…** offers an explicit confirmation of
its weaker protection. The choice is saved locally and the fallback remains visible in Settings for the
session. An administrator can forbid fallback. Existing installations that previously used automatic
fallback must now consent once; existing files are retained. Native references absent from the selected
file backend remain unavailable rather than being treated as deleted or overwritten. No secrets migrate
between backends automatically; restore native access and restart Studio to use those references.

See [secret storage policy](secret-storage-policy.md) for managed configuration and recovery details.

## "Secret storage could not protect or access its files"

Check ownership, permissions, available disk space, and filesystem support for private files in the
Studio application-data directory (`GrpCurlNet.Studio` under the current user's application-data folder).
Studio requires a private directory (`0700`) and secret files (`0600`) on Unix; on Windows it requires a
protected ACL with only the current user and SYSTEM. Filesystems or redirected profiles that cannot
establish and retain these protections cause a visible storage error. Repair access, then retry the
operation, or **Retry native keystore** if the startup DPAPI probe failed. Storage failures do not enable
fallback, even with saved consent. Other files in the shared directory are not recursively hardened.
On Unix, initializing fallback key inputs also requires a filesystem that supports atomic hard-link
publication; unsupported filesystems produce the same storage error.

Replace any symlink/reparse-point directory or secret file with a regular directory/file in the existing
location before retrying. Studio refuses links before changing their permissions or reading their contents.

If key inputs are missing or invalid, close Studio and restore `secrets.salt`, the generated `machine.id`
(where used), and `secrets.json` from the same backup on the original machine and user account. Do not
remove the salt or generated ID to try to repair an existing ciphertext file. Linux system machine IDs
are read-only inputs and are not altered by Studio.

A failed save before atomic replacement retains the prior complete file. After replacement, the new
complete file is authoritative. Abandoned `.secret-*.tmp` files are ignored; they are not a recovery
source. File contents are flushed, but directory-entry durability during power loss depends on the
filesystem. Backups remain necessary.

If an error says **the secret change completed, but its reference index could not be updated**, the
secret change already committed. Close Studio, repair access if necessary, and restore a consistent
backup before retrying. For deletion from fallback, an old index entry can still report a missing
stored reference. These files are not a single transaction. Do not assume a failed multi-file operation
rolled back the secret change.

## "The secret-reference index is invalid or unreadable"

Close Studio and restore a valid `secret-keyrefs.json` from backup in its existing application-data
directory, then retry. Studio requires an array of unique, nonblank string references. It preserves
valid reference text exactly and refuses malformed JSON, nulls, other types, and duplicate entries.
It does not repair, reconstruct, or rewrite an invalid index. Do not delete it or replace it with an
empty array to bypass the error: a missing index is indistinguishable from first use and can lose the
record of references belonging to another backend.

While the index is corrupt, listing and all secret writes/deletes fail. Secrets the selected backend
can still read remain accessible. Missing-reference checks in fallback also fail because Studio cannot
determine reference ownership. This error never enables fallback. If the error reports that the secret
change completed, restore a backup consistent with the backend's state; the backend change was not
rolled back. For native keystores, use the corresponding platform backup/recovery facilities.

## The INSECURE banner won't go away

A tab is using a TLS profile with certificate verification disabled. That's intended for testing only and
exposes traffic to interception. Click **Review connection…** on the banner to open the offending
connection, and attach a profile that verifies the server certificate.

## Updates

When **Settings → Updates → check on launch** is enabled, Studio compares your version against the latest
GitHub Release and shows a status-bar link if a newer one exists. There is **no in-app update** — the link
opens the [Releases page](https://github.com/verifiedcoder/grpcurl-dotnet/releases) so you download and
replace the app yourself (see [Install](install.md)). If the check finds nothing, it stays silent; it is
offline-safe and never blocks startup.

## A large response feels sluggish to scroll

Above ~4 MiB the response viewer drops syntax highlighting and code folding to stay responsive — the body
is still shown in full, just without colour. This is expected for very large payloads.

## Still stuck?

Open an issue from **Help → Report an Issue**, or file one directly on the
[issue tracker](https://github.com/verifiedcoder/grpcurl-dotnet/issues/new).
