# Common workflows

## Saved requests

Once you've composed a request, save it so it sticks around. A saved request appears under its connection
in the sidebar; opening it restores the body, headers, options, and deadline exactly. Edits to an open
saved request show a dirty marker on the tab until you save again. Saved requests live in the workspace
file, so they travel with it.

## Environments and secrets

Environments let you swap hosts, tokens, and other values without editing each request. Define variables
in an environment (e.g. `HOST`, `TOKEN`) and reference them as `${HOST}` in addresses, headers, and
bodies. Switch the active environment from the status-bar **switcher** (`Ctrl+E`); the change applies to
subsequent sends, while an in-flight call keeps the environment it was sent with.

> _📷 Screenshot: the environment switcher open in the status bar, with the manager dialog behind it._

Mark a variable **secret** to keep its value out of the workspace file. Secret values are stored in the
OS secret store where available and referenced indirectly; the **save guard** warns you before writing a
plain-text secret to disk, and **export** produces a secret-free copy. When you import a workspace, Studio
offers to supply any missing secret values inline.

## TLS and mTLS

For TLS connections, attach a **certificate profile** (a CA, and optionally a client cert/key for mTLS).
Profiles are reusable across connections. A profile that disables certificate verification shows a
non-dismissable **INSECURE** banner on any tab using it — verification-off is for testing only.

## History and replay

Every call is recorded in **History** (`Ctrl+H`) — method, connection, status, duration, timestamp.
Filter by text (`< 100 ms` over large histories), connection, category, or kind, and **pin** entries you
return to. **Replay** re-opens a call as a fresh draft (secret header values are redacted and prompted
for). History capture can be toggled in **Settings → History**.

> _📷 Screenshot: the history tab with the filter bar and a row's Replay/Pin actions._

## GraphQL

The GraphQL tab proxies a GraphQL query, mutation, or subscription to gRPC using the bundled `Gql2Grpc`
engine. It has a GraphQL editor with grammar highlighting and descriptor-aware completion, a variables
editor, per-field parallel progress, an introspection schema viewer, and a **mapping designer** with a
live resolution preview. See the [Gql2Grpc cookbook](../gql2grpc-cookbook.md) for the mapping format.

> _📷 Screenshot: the GraphQL tab with a query, variables, and the response._

## Copy as CLI

Any invocation (gRPC or GraphQL) can be copied as an equivalent `grpcn` / `gql2grpc` command via **Copy
as CLI** — handy for scripts, CI, or sharing a repro. Secret values render as `${VAR}` references rather
than literals. Pick the shell dialect (POSIX / PowerShell) in **Settings → General**.

## Workspaces

A workspace bundles your connections, saved requests, environments, and TLS profiles into one
`.gcnws.json` file. Use **File → New / Open / Save / Save As**, **Import** to merge another workspace in,
or **Export** for a secret-free copy to share. Recently opened workspaces are under **File → Open Recent**.
Studio can reopen your last workspace and tabs on launch (**Settings → General → Startup**).
