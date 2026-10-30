# Workspace file approvals

Studio checks file references when you import, open, open a recent workspace, reload from disk, or restore the startup workspace. Descriptor protosets, proto sources, descriptor lookup directories, CA certificates, client certificates, and private keys all follow the same rules.

Files beneath the selected workspace's directory are usable automatically. References outside it require individual approval. The dialog shows the connection or TLS profile, purpose, original spelling, absolute path, and target. Every checkbox starts unchecked. Cancel, or leave any required reference unapproved, to keep your current workspace and its edits. No referenced file contents are read during this review.

Symbolic links and Windows junctions are resolved using filesystem metadata. An external location is not probed before consent. A link that leads to another external target can require another review before Studio inspects that target. Unresolvable, inaccessible, unsupported, or cyclic links fail closed with instructions to repair the path. Ordinary missing files can stay configured after approval; using them reports the normal missing-file error.

A link target containing parent traversal after a named directory, such as `nested/../file.proto`, is rejected because `nested` could itself be a link. Recreate that link with an absolute canonical target. Leading `../` segments in a relative link target remain supported.

Directory approval permits descriptor lookup beneath that directory. Linked entries escaping it need separate approval. `protoc` also searches beside each source file, so external source lookup directories appear in the dialog. Studio does not implicitly add the process working directory to guarded compiler searches. Keep lookup directories narrow: assessment refuses trees exceeding 20,000 entries.

## Approval lifecycle

Approvals live in private, atomically replaced `workspace-trust.json` in Studio's application-data directory. They are never exported or embedded in workspace documents. Each record binds the canonical workspace location, SHA-256 fingerprint of its document bytes, approved paths, and final resolved targets.

Moving or copying the document, editing it outside Studio, or retargeting its links requires a fresh assessment. Studio's own admitted saves can update the fingerprint for the approved reference set. Adding an external reference requires new consent. Import transfers admitted references to the destination after connection/profile IDs and portable paths are transformed. Exported copies carry no approval.

Startup restoration waits until the shell can show any required approval. Cancelling leaves an empty, unsaved workspace and retains the original file. Approval failures never classify a workspace as corrupt or quarantine it.

## Revoke and recover

Use **Settings → Security → Workspace file approvals** to review approved workspace locations. **Revoke** removes one workspace's approval; **Revoke all** clears every record, including malformed approval JSON when storage is accessible. **Refresh approvals** updates the list after another instance changes it. Your workspace edits are retained.

Descriptor and TLS access revalidate authorization before reading. Cached request descriptors cannot bypass revocation. Running operations observe revocation through a cancellation monitor; their descriptor sessions and channels are disposed as they unwind. Other Studio instances check the same local records before admitting new access. Reopen the workspace to approve access again.

A missing approval file grants no approvals. Malformed or inaccessible storage also grants none and reports recovery guidance. Restore directory access and retry, or explicitly **Revoke all** to reset readable but malformed storage. Do not remove a lock file while another Studio instance is using it.

If Studio reports **the workspace was saved, but its approvals were not saved**, the workspace write already committed. There is no transaction spanning the workspace and approval files. Repair approval storage and reopen the saved workspace to approve access again; the error does not mean the workspace write was rolled back.

## Protection boundary

This is application-level protection against file references in shared workspaces. Approval authorizes a location, not the safety or permanent contents of a file. It is not a filesystem sandbox against a hostile process running as the same user and racing file replacements or changing local approval storage.

Insecure-TLS import consent (PRD-027), imported secret-reference ownership (PRD-057), recent-list network probes (PRD-033), and general workspace transaction improvements remain separate work.
