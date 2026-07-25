# GrpCurl.Net Studio

GrpCurl.Net Studio is a desktop client for exploring and calling gRPC servers — the graphical companion
to the `grpcn` CLI. It speaks JSON instead of binary protobuf, discovers services via server reflection,
protoset files, or `.proto` sources, and supports all four streaming shapes.

This guide covers Studio specifically. For the command-line tools see the
[CLI Reference](../cli-reference.md); for the GraphQL-to-gRPC proxy see the
[Gql2Grpc cookbook](../gql2grpc-cookbook.md).

- [Install Studio](install.md) — download, verify, and run on Windows, macOS, and Linux.
- [First run](first-run.md) — from an empty workspace to your first invocation.
- [Keyboard shortcuts](keyboard-shortcuts.md) — the complete shortcut map.
- [Common workflows](workflows.md) — environments, history, saved requests, GraphQL, copy-as-CLI.
- [Troubleshooting](troubleshooting.md) — workspace locks, read-only files, `protoc`, secrets, updates.
- [Accessibility charter](accessibility-charter.md) — the per-release manual accessibility pass.

## The window at a glance

> _📷 Screenshot: the Studio shell with the sidebar, a request tab, the inspector, and the console labelled._

Studio is organised into zones you can show or hide independently (and cycle with `F6`):

- **Sidebar** (left) — your **connections** and their **saved requests** on top, the **service explorer**
  (the selected connection's services, methods, and message types) below. The explorer's filter box
  narrows the tree as you type.
- **Document tabs** (centre) — each open request, describe, GraphQL, history, or settings view is a tab.
  The active tab's editor is where you compose the request body.
- **Inspector** (right) — context for the current selection: a method's signature, a streamed message,
  or a completed call's timing breakdown.
- **Console** (bottom) — a running log of calls and workspace events.
- **Status bar** — the active environment switcher, the workspace name (with a dirty marker), the app
  version, and any update or banner notices.

Toggle the sidebar with `Ctrl+B`, the console with `Ctrl+J`, the inspector with `Ctrl+I`, or enter
focus mode (`Ctrl+Shift+M`) to collapse them all and maximise the document area.

## Screenshot capture checklist

Screenshots in this guide are placeholders pending capture on a real desktop. To fill them in, grab the
images below (light theme, default window size) and save them under `Docs/images/studio/`, then replace
the corresponding `_📷 Screenshot: …_` note with `![caption](../../images/studio/<file>.png)`:

- [ ] `shell.png` — the whole window with each zone visible (this page).
- [ ] `welcome.png` — the first-run empty state ([first run](first-run.md)).
- [ ] `add-connection.png` — the connection editor ([first run](first-run.md)).
- [ ] `explorer.png` — the service explorer with a method selected ([first run](first-run.md)).
- [ ] `invoke.png` — an invocation tab with a response ([first run](first-run.md)).
- [ ] `environments.png` — the environment switcher + manager ([workflows](workflows.md)).
- [ ] `history.png` — the history tab with filters ([workflows](workflows.md)).
- [ ] `graphql.png` — the GraphQL tab ([workflows](workflows.md)).
