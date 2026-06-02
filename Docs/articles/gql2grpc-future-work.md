# Gql2Grpc future-work backlog

This document captures functionality that is deliberately out of scope for the initial `Gql2Grpc` production release but should be revisited as the tool matures. Each entry lists motivation, rough shape of the work, prerequisites, and risks so a future contributor can pick it up cold.

---

## 1. Proto-annotation-based mapping discovery

**Motivation.** The YAML mapping file is expressive but places the maintenance burden on each consumer. For teams that own their `.proto` files, annotations let the gRPC service definition be the single source of truth — add a GraphQL-binding option to the `.proto`, recompile, and the mapping updates automatically.

**Proposed shape.**
- Define a custom `FileOptions` / `MethodOptions` extension in a new `gql2grpc/options.proto` (served alongside `repo/Src/Gql2Grpc/` as a shipped file). Fields: `graphql_field`, `graphql_operation_type`, `argument_mappings`, `response_unwrap`, `field_mask_argument`.
- At descriptor load time, inspect each `MethodDescriptor`'s `CustomOptions` via `FieldDescriptor.GetExtension`. Convert found options into synthetic `MappingEntry` records and merge them into `MappingConfig` with lowest precedence (explicit YAML still wins).
- Document the workflow: annotate the proto, recompile with `protoc --include_imports`, ship the resulting protoset (or enable reflection), and omit `--mapping` from the CLI.

**Prerequisites.**
- Promote `google.protobuf.MessageOptions`, `MethodOptions`, `FileOptions` access helpers to Gql2Grpc (may already be available through the `Google.Protobuf` package transitively).
- A stable option number registered via Google's extension range or an internal range.
- Decide on option ownership: vendored into `GrpCurl.Net` or a separate shipped module.

**Risks.**
- Extension numbers collide if two tools claim the same range — coordinate with any internal gRPC standards body.
- Users must recompile their proto stack to benefit, which friction-gates adoption.

---

## 2. Client-streaming and bidirectional RPC mapping

**Motivation.** Today the plan maps only unary (queries, mutations) and server-streaming (subscriptions). Some enterprise APIs expose genuinely bidi operations (e.g., live chat, multi-event telemetry uploads) that neither idiomatic GraphQL nor unary gRPC represent cleanly.

**Proposed shape.**
- Support an `operationType: bidirectional` extension in the mapping file, opting into a new CLI mode where stdin is parsed as NDJSON GraphQL mutations and each line becomes a request on an open bidi stream. Responses are emitted as NDJSON envelopes.
- Client-streaming `operationType: clientStreaming` accepts NDJSON requests on stdin, returns a single envelope on close.
- Both modes bypass parallel execution — a single streaming operation dominates the invocation.

**Prerequisites.**
- Wire `DynamicInvoker.InvokeClientStreamingAsync` and `InvokeDuplexStreamingAsync` into the `GrpcTransport` adapter.
- Design cancellation semantics: stdin EOF = half-close; SIGINT = full cancel.
- Decide whether to keep this CLI-only or expose an embedding API.

**Risks.**
- GraphQL clients don't natively express streaming requests — the tool is useful only as a CLI/bridge, not as a drop-in GraphQL endpoint.
- NDJSON framing mismatches are easy to introduce; parser needs to be strict.

---

## 3. Persisted queries, complexity limits, rate limiting, caching

**Motivation.** Production GraphQL gateways typically enforce a persisted-query allowlist, a cost limit per query, per-consumer rate limits, and response caching to protect upstream gRPC services.

**Proposed shape.**
- `--persisted-queries <path>` — hash-to-document map, rejects unknown hashes.
- `--max-complexity <n>` — walks the resolved selection tree, assigns cost weights from the mapping file (`operations[].cost: N`), rejects over-budget queries.
- `--rate-limit <rps>` — simple token-bucket per address / per `-H` identity.
- `--cache <strategy>` — `off` (default), `memory`, or `file:<path>`; keyed on (method, normalised request JSON, `-H Cookie`).

**Prerequisites.**
- Gql2Grpc needs a consumer-identity primitive to key rate limits and caches — likely a configurable `-H` header name (`--identity-header x-user-id`).
- Decide whether these belong in Gql2Grpc itself or in a separate "Gql2Grpc.Server" wrapper.

**Risks.**
- CLI-as-gateway is a different shape than CLI-as-developer-tool; blurring the two creates a sprawling surface.
- File-based cache invalidation is notoriously hard; in-memory scope is per-process and has limited value for a short-lived CLI.

---

## 4. DataLoader-style N+1 batching

**Motivation.** GraphQL queries that fetch nested related entities (`users { id posts { title } }`) currently fan out as `users.length` independent RPCs. A DataLoader pattern collapses these into one bulk RPC (`BatchGetPosts(user_ids)`), matching how GraphQL servers usually implement joins.

**Proposed shape.**
- Extend `MappingEntry` with `batchBy: user_id` and a companion `batchMethod: BatchGetPosts`.
- During execution, nested selections with matching `batchBy` are collected across sibling parents; a single RPC fetches the union; results are re-scattered by key.
- Expose an explicit `batchWindow: 10ms` to balance latency vs. batch size.

**Prerequisites.**
- The nested gRPC method must accept a list of keys and return results keyed back — this is a schema contract consumers must enforce.
- Requires rethinking the response pipeline to correlate streamed bulk results with individual selection nodes.

**Risks.**
- Introduces implicit, cross-field coordination that is hard to debug when mappings are misconfigured.
- Error mapping becomes thornier — a single batch failure poisons every consumer of the batched data.

---

## 5. Full GraphQL interface / union type resolution

**Motivation.** The first-pass introspection synthesises unions only from proto `oneof` with message-typed branches and does not attempt GraphQL interfaces. Schemas with polymorphic return types (e.g., `Node` interface, search-result unions) currently can't be expressed.

**Proposed shape.**
- Add `abstractTypes:` block to the mapping file, listing GraphQL interfaces / unions with member type names and their discriminator (proto field or `Any` type URL).
- At response-shaping time, inspect the `@type` on `google.protobuf.Any` fields, look up the concrete proto message, and emit `__typename` plus the expected GraphQL object fields.
- Interface support requires field presence checks so non-matching members are pruned, not rendered as `null`.

**Prerequisites.**
- Most real APIs use `google.protobuf.Any` for polymorphism; the rewrite already maps `Any` to a custom scalar, so this builds on top.
- Deep introspection coverage: interface types must appear in `__schema.types[]` with their `possibleTypes`.

**Risks.**
- GraphQL client expectations are strict — partial interface implementations generate confusing errors at the client.
- Any-typed fields without a registered mapping should degrade to the custom scalar; avoid crashing the query.

---

## 6. `InternalsVisibleTo` escape — promote GrpCurl.Net APIs to public

**Motivation.** The rewrite relies on `InternalsVisibleTo "Gql2Grpc"` to reach `DynamicInvoker`, `ProtosetSource`, and `GrpcChannelFactory`. If Gql2Grpc ever ships as a separate NuGet package or is consumed by third parties, the `internal` coupling breaks.

**Proposed shape.**
- Carve out a public API surface on `GrpCurl.Net`: `IGrpcClient` (unary/streaming), `IDescriptorSource` (already public), `GrpcChannelBuilder` (fluent config).
- Mark the existing `internal` types as obsolete-internal, have the new public surface delegate.
- Update Gql2Grpc to consume the public API only; remove the `InternalsVisibleTo` entries.

**Prerequisites.**
- Design review on the public shape — avoid exposing `SimpleDynamicMessage` directly if we can wrap it behind `IMessage`-returning methods.
- Update `GrpCurl.Net`'s version and document the new library contract.

**Risks.**
- Locks in a public API commitment that's harder to evolve than `internal`.
- Either project may need a major-version bump.

---

## 7. Observability follow-ups

- Add a Grafana/OpenTelemetry section when the gRPC client gains built-in tracing (GrpCurl.Net doesn't ship tracing today).

---

## Tracking

Each item should become a GitHub issue (or equivalent tracker) when work starts, labelled `area:gql2grpc` and `enhancement`. Cross-link the issue back to this document and update the entry with the issue number once filed.
