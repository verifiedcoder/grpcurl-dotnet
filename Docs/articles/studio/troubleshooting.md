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
