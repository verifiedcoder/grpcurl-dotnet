# PRD-027: Explicit TLS bypass consent during workspace import

Base: `feature/bug-fixes` at `a88cec13abfe3508480b5d614a01d6f5e61a3945`.
Scope: Import Workspace only. Open, Open Recent, Reload and startup retain their TLS behavior.
See the [user guide](../articles/studio/workspace-import-tls.md).

## Implementation

`WorkspaceImportDialogViewModel` replaces the generic confirmation. It retains item counts, destination
names and missing-secret information and renders typed TLS rows for every incoming profile requesting
verification bypass or specifying a custom CA. Each row carries source ID, original and destination names,
requested bypass, selected outcome and CA path. Duplicate names remain distinguishable. Eligible checkboxes
start unchecked on every import. Cancel returns no decision and cancels the whole import.

`WorkspaceImportDecision` defensively copies the selected source IDs and binds them by object identity to
the exact `WorkspacePreparation` reviewed after PRD-026 approval. Creation and commit reject foreign
preparations and unknown, duplicate or ineligible selections. Import rejects missing, empty, padded,
control-bearing or duplicate profile IDs, including absent or repeated JSON `id` properties. IDs remain
opaque ordinal strings; legacy non-UUID IDs are supported. Existing open behavior remains unchanged.

`WorkspaceMerger.Merge` validates source IDs, copies each incoming profile/connection/environment, applies
bypass selections by source identity, and only then generates destination IDs and remaps connections.
No decision or omitted merger selections mean verification enabled. Existing destination profiles keep
their settings. Both summary and commit use this policy. CA, revocation, client-certificate and secret-reference
configuration are retained. No descriptor or certificate content is read by the summary or merger.

`JsonWorkspaceStore.CommitImportAsync` validates the decision and the preparation before constructing the
final model. Existing document-fingerprint, resolved-target and approval-generation checks still run
immediately before persistence. PRD-026 grants transfer to the final IDs, paths and document fingerprint.
The UI collects optional secrets before commit and writes them only after successful workspace commit and
refresh. A workspace write followed by failed approval persistence still refreshes the committed workspace,
reports the partial result and leaves supplied secrets unwritten. Unsaved destinations remain dirty until saved.

No workspace schema, exported consent or persistent TLS trust mechanism is added. Reimporting requires
fresh choices. Persisted TLS settings, editor consent and insecure banners retain their normal behavior.
PRD-057 secret-reference ownership and general transaction improvements remain separate.

## Baseline evidence

`WorkspaceImportTlsBaselineTests.cs` was copied into an isolated detached worktree at `a88cec1`.
All three assertions failed: direct store import persisted `insecureSkipVerify: true` with no decision;
the summary omitted certificate-verification details; and it omitted the custom CA path.
The [captured output](evidence/prd-027-baseline.txt) records the failures. These same tests run in the fixed suite.

## Acceptance traceability

| Requirement | Evidence |
|---|---|
| No silent bypass; unchecked/subset/all outcomes | `WorkspaceImportTlsBaselineTests`, `WorkspaceImportTlsTests`, `WorkspaceImportFlowTests` |
| Per-profile identity, names, CA and selected outcome | `Merger_applies_only_selected_source_IDs_without_mutating_inputs`, rendered `WorkspaceImportUiTests` |
| Cancel preserves current workspace and secrets | `Import_orders_file_approval_TLS_consent_and_secret_writes_and_reports_failures`, `Cancel_discards_checked_choices_and_cancels_the_entire_import` |
| Immutable, preparation-bound decision; invalid identities/selections rejected | `Decision_is_immutable_and_cannot_be_reused_for_another_preparation`, `Invalid_decisions_are_rejected_at_creation`, `Malformed_document_identities_never_reach_import_consent` |
| Preview/commit/restart consistency and safe direct callers | `Dialog_preview_commit_and_restart_agree_and_reimport_starts_unchecked`, baseline persistence test, merger defaults |
| Approval transfer, changed bytes, retargeting and revocation | `Changed_source_or_path_approval_cannot_commit_selected_bypass`, restart tests, existing `WorkspaceTrustTests` |
| Persistence failure, partial save and unsaved destination | `Workspace_write_failure_preserves_active_and_persisted_destination`, `Approval_write_failure_reports_committed_workspace_and_retains_selected_TLS_outcome`, unsaved-destination test |
| No premature secret writes or referenced-content consumption | Flow test holds the CA file unavailable to .NET readers, uses invalid certificate contents, asserts no descriptor loads and no secrets before commit |
| Open/recent/reload/startup compatibility, editor consent, banners | Extended `WorkspaceApprovalFlowTests`, existing `TlsProfileEditorViewModelTests` and `MainWindowViewModelTests` |

## Verification and delivery

Local verification: locked restore and Release build passed with zero warnings/errors. The filtered
solution run passed 2,423 tests with 14 platform-specific skips and no failures. ViewModels line coverage
was 5,059/5,545 (91.2%), above the 80% gate. The rendered dialog and view-location suite passed 24 tests.
CLI validation passed all ten scenarios; core and Studio conformance each passed 634/634.
DocFX with warnings as errors passed with zero warnings/errors. Complete three-OS CI results will be recorded when available.
The implementer's acceptance evidence is not independent review approval. Independent review and fresh
explicit user merge authorization are required before merge.

Screenshots are generated by actual Avalonia controls with Skia rendering, not mockups:
[unchecked choices](../assets/studio/workspace-import-tls-defaults.png) and
[selected bypass](../assets/studio/workspace-import-tls-selected.png).

## Reproduction

From the nested checkout:

```bash
dotnet restore --locked-mode GrpCurl.Net.slnx
dotnet build GrpCurl.Net.slnx -c Release --no-restore
dotnet test --solution GrpCurl.Net.slnx -c Release --no-build --no-restore -- --filter-not-trait "Category=Performance" --coverlet --coverlet-output-format cobertura --results-directory=/tmp/prd027-coverage
python3 Scripts/check-coverage-gate.py --coverage-dir /tmp/prd027-coverage --package GrpCurl.Net.Studio.ViewModels --min 0.80
dotnet test --project Tests/GrpCurl.Net.Studio.Tests.UI -c Release --no-build --no-restore -- --filter-class '*WorkspaceImportUiTests'
dotnet run --project Scripts/ValidationRunner -c Release -- --ci
Scripts/run-conformance.sh
Scripts/run-studio-conformance.sh
docfx Docs/docfx.json --warningsAsErrors
```

On the implementation machine, full solution tests use
`DBUS_SESSION_BUS_ADDRESS=unix:path=/tmp/prd027-no-session-bus` to isolate desktop session behavior already
documented in PRD-026. Set `GRPCN_IMPORT_SCREENSHOT_DIR` to an output directory for UI screenshot capture.
Linux results do not substitute for Windows/macOS CI evidence.
