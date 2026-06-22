# First run

This walkthrough goes from a freshly installed Studio to your first gRPC call.

> _📷 Screenshot: the welcome empty state with the "Add Connection" button._

## 1. Add a connection

On first launch Studio shows a welcome screen. Click **Add Connection** (or press `Ctrl+N`'s workspace
menu → the sidebar's **+**) to open the connection editor.

> _📷 Screenshot: the connection editor with address, transport, and descriptor source._

Fill in:

- **Name** — a label for the connection (e.g. `local`).
- **Address** — `host:port` (e.g. `localhost:50051`).
- **Transport** — **Plaintext** for local/dev servers, or **TLS** with a certificate profile for secured
  ones (see [workflows → TLS](workflows.md#tls-and-mtls)).
- **Descriptor source** — how Studio learns the schema:
  - **Reflection** — query the server's reflection service (nothing else to configure).
  - **Protoset** — point at a pre-compiled `.protoset` file.
  - **Proto** — compile `.proto` files (needs `protoc`; see
    [troubleshooting](troubleshooting.md#protoc-not-found)).

Save the connection. It appears in the sidebar and Studio loads its schema.

## 2. Browse the schema

Select the connection. The **service explorer** fills with its services and message types. Type in the
filter box to narrow the tree. Selecting a method shows its signature in the inspector; double-clicking
a method opens a request tab for it.

> _📷 Screenshot: the service explorer with a method selected and its signature in the inspector._

Tip: `Ctrl+L` jumps focus to the filter box; `Ctrl+T` opens a request tab for the selected method.

## 3. Compose and invoke

In the request tab, edit the **request body** (JSON, with completion and inline validation). Add request
**headers** in the grid below, set a **deadline** if you want one, then **Invoke** (`Ctrl+Enter`).

> _📷 Screenshot: an invocation tab showing the request body, headers, and the response._

The response renders in the lower pane; the **Timing** tab breaks the call into channel, descriptor, and
call phases. For streaming methods you get **Start**/**Stop** controls and a live message log instead of a
single response. Cancel an in-flight call or stream with `Ctrl+.`.

## Next steps

- Save the request for later (it joins the connection in the sidebar) — see
  [workflows → saved requests](workflows.md#saved-requests).
- Reuse the call from the shell with **Copy as CLI** — see
  [workflows → copy as CLI](workflows.md#copy-as-cli).
- Parameterise hosts and secrets with [environments](workflows.md#environments-and-secrets).
