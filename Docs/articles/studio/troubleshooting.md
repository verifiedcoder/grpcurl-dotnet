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

Studio stores secret values in the OS secret store when one is available. If the platform store can't be
reached, it falls back to a less-durable backend and surfaces that in **Settings**. Imported workspaces
keep secret *references* but not the values — Studio prompts you to supply missing secrets on import, or
you can re-enter them per environment variable.

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
