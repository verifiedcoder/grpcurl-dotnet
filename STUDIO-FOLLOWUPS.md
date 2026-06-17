# GrpCurl.Net Studio — tracked follow-up work

Work deliberately deferred during Studio PRs, captured here so it stays visible (not silent
tech debt) and can be scheduled. Each item names the PR/epic it came from, the spec reference,
and its MoSCoW priority. Remove an item when it ships; add one whenever a PR defers something.

| # | Item | From | Spec | Priority |
|---|------|------|------|----------|
| 1 | **Replay: restore headers + options.** Replay rebuilds body + connection + method only. It should also restore headers and options, with a "value required" marker on redacted-literal headers and re-resolution of secret-typed / `${VAR}` headers. Needs a fuller invocation pre-fill API (`IDocumentHost.OpenInvocation` takes only connection/method/json today). Partly unblocked by **E3.2 Environments**. | E3.3 PR-C (#62) | FR-123, SPEC-040 §5.2 | M |
| 2 | **Workspace file-locking / multi-instance.** A PID lock file, a read-only "Locked by PID … on host since …" banner when a second instance opens the same workspace, and a "Take over" action. Not implemented — E3.1's scope was schema / open-save-recent / autosave / dirty. | E3.1 | SPEC-040 §"concurrency" | M |
| 3 | **Per-connection history purge on connection delete.** Offer a checkbox when deleting a connection to also purge history entries whose snapshot `address`+`name` match. | E3.3 | FR-126 (Could) | C |
| 4 | **Deprecated-mapping test coverage.** `DescriptorService` maps `Deprecated` from descriptor options; the `deprecated = true` branch is covered at the model/VM/view level but not end-to-end via a deprecated symbol in the TestServer proto (would need stub regeneration). | CU-4 | FR-059 | minor |
| 5 | **History binary index.** `history.index` (line-offset + summary-column cache) is deferred; the NDJSON file is read directly as the source of truth, which is fine for the ≤1000-entry v1 cap. Revisit only if large-history performance becomes an issue. | E3.3 PR-A | SPEC-040 §5 | minor |
| 6 | **Spec reconciliation — redaction marker.** SPEC-040 §5.1 (normative data shape) uses `[redacted]`; FR-121 / AC-13 use `«redacted»`. Studio stores `[redacted]` (ASCII, greppable, avoids JSON unicode-escaping). Reconcile the two specs to `[redacted]`. | E3.3 PR-A | SPEC-040 §5.1 vs FR-121 | doc |
| 7 | **Active-environment-aware header previews.** `HeaderRowViewModel.ResolvedPreview` (FR-066) resolves `${VAR}` against the **OS only**; it does not consult the active workspace environment, and switching environments in the status bar does not refresh open header previews. PR-B delivered the switcher + send-time resolution (the send path is correct via `InvocationRunner`→`IEnvironmentService`); the live preview cross-wiring needs `IEnvironmentService` threaded into the invocation document + header rows with a re-raise on `ActiveChanged`. | E3.2 PR-B | FR-133 (preview clause) · FR-066 | S |
