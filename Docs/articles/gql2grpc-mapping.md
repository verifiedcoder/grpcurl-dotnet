# gql2grpc mapping file

The mapping file tells `gql2grpc` how each GraphQL root field routes to a gRPC service and method. It is YAML by default; `.json` files are also accepted (the extension selects the parser).

Pass it with `--mapping <path>`. When no mapping file is supplied, convention-based resolution kicks in: GraphQL field `activeResponses` → gRPC method `ActiveResponses` on `--default-service`.

## Full schema

```yaml
version: 1                          # reserved for future forward-compatible parsing

defaults:
  service: company.product.v1.Service # default gRPC service for entries without `service:`
  argumentAliases:                    # default Relay aliases (merged with built-ins)
    first: page_size                  # built-in
    last: page_size                   # built-in
    after: after_cursor               # built-in
    before: before_cursor             # built-in
    orderBy: order_by                 # built-in
    pageSize: page_size               # built-in (echo)
    # … add project-specific aliases here; user-supplied keys override built-ins.
  convention:
    listMethodPrefix: ""              # prepended when convention synthesises a method name
    pascalCaseFieldNames: true        # activeResponses -> ActiveResponses on convention fallback
  introspection:
    schemaName: Company.Product       # label stamped onto the synthesised __Schema description
    typeOverrides:                    # protoFqn -> GraphQL type name
      company.product.v1.User: UserRecord

operations:
  - graphqlField: activeResponses     # required — GraphQL field as selected by the client
    operationType: query              # required — query | mutation | subscription
    service: company.product.v1.ResponseService   # optional — overrides defaults.service
    method: ListActiveResponses       # required — gRPC method name (simple, not FQN)
    kind: unary                       # optional — unary (default) | serverStreaming
    arguments:
      filter: filter                  # string rename
      first: { path: page.size }      # dotted path
      tenantId: { literal: "${TENANT_ID}" }   # constant literal, env-expanded at load time
      pageToken: { skip: true }       # drop even if the caller supplied it
      $selection: { fieldMask: read_mask }    # derive a google.protobuf.FieldMask from the selection
    response:
      unwrap: items                   # strip `{ items: [...] }` wrapper before projection
```

The same shape expressed as JSON works identically. Arrays and objects map one-to-one; YAML's inline `{ path: x }` is equivalent to JSON's `{"path": "x"}`.

## Argument rules

Rules are evaluated per-argument in this order:

| Rule form | Behaviour |
|---|---|
| `<arg>: <field>` (string) | Rename the GraphQL argument to the given protobuf field. Applied only when the caller supplies the argument. |
| `<arg>: { path: "a.b.c" }` | Set a nested path on the request message. Intermediate objects are created as needed. |
| `<arg>: { path: "." }` | Spread an object-valued argument onto the request root. Designed for GraphQL `input` types whose fields already match the request message's shape. |
| `<arg>: { literal: "value" }` | Always set the field to `value`, even when the caller omits the argument. `${VAR}` references are expanded at config-load time. |
| `<arg>: { skip: true }` | Drop the argument — overrides convention-based fallback. |
| `<arg>: { rename: "field" }` | Equivalent to the string shorthand; useful inside objects when other keys are needed. |
| `$selection: { fieldMask: <path> }` | Pseudo-argument. Produces a `google.protobuf.FieldMask` derived from the GraphQL selection set and writes it to `<path>` in the request. See [FieldMask projection](#fieldmask-projection). |

Arguments with no matching rule fall through to `defaults.argumentAliases`, then to a snake_case conversion of the GraphQL name. `userId` → `user_id`; `firstName` → `first_name`.

### Spread rule (`path: "."`)

The spread rule is the idiomatic way to handle GraphQL `input` types — a GraphQL convention where complex request shapes are passed as a single named argument:

```graphql
mutation CreateUser {
  createUser(input: { name: "Alice", email: "a@example.com" }) { id }
}
```

With this mapping entry:

```yaml
- graphqlField: createUser
  operationType: mutation
  method: CreateUser
  arguments:
    input: { path: "." }
```

the translator spreads the GraphQL `input` object onto the root of the gRPC request, producing `{"name": "Alice", "email": "a@example.com"}`. The protobuf JSON parser accepts both `camelCase` and `snake_case` field names, so no further rewriting is typically needed.

If you need both spread and field-level renaming in the same entry, declare individual rules for the renamed fields alongside the spread:

```yaml
arguments:
  input: { path: "." }        # spreads input.name -> name, input.email -> email
  # override one field that needs a different target
  input.phoneNumber: { path: "contact.phone_number" }
```

## Convention fallback

When no `operations` entry matches the incoming root field, `gql2grpc` synthesises one:

1. Method name: `PascalCase(graphqlField)`, optionally prefixed by `defaults.convention.listMethodPrefix` (set it to `"List"` to get `ListActiveResponses` from `activeResponses`).
2. Service: CLI `--default-service` takes precedence over `defaults.service`. At least one must be set — otherwise resolution fails with a GraphQL error.
3. `kind`: `unary` for queries and mutations; `serverStreaming` for subscriptions.

## Precedence

Highest wins:

1. CLI `--default-service` > `defaults.service`.
2. Explicit `operations` entry > convention fallback.
3. Per-entry `arguments` rule > `defaults.argumentAliases` > snake_case conversion of the GraphQL argument name.

## Validation

The loader rejects at load time with `InvalidDataException`:

- Duplicate `(graphqlField, operationType)` pairs.
- Missing required keys (`graphqlField`, `method`).
- Unknown `operationType` or `kind` values.
- Missing `service` on an entry when `defaults.service` is also unset.

Descriptor-level existence checks (service/method present in the discovered descriptor set) happen per-operation at execution time and surface as GraphQL errors in the response envelope rather than startup failures. This means a broken mapping entry only affects the one field that uses it — other fields in the same document keep working.

## Introspection defaults

`defaults.introspection.schemaName` labels the synthesised schema description. It appears on `__Schema.description` — useful so a GraphiQL/Altair user sees which backend they are browsing.

`defaults.introspection.typeOverrides` is a map of fully-qualified proto message names to custom GraphQL type names. Use it when two proto files declare messages with the same simple name (`v1.User` and `v2.User`) or when you want to hide the proto naming from GraphQL consumers.

### What becomes a GraphQL type?

Per-proto-kind rules (full table in `Src/Gql2Grpc/Introspection/TypeMappings.cs`):

- **Message** → GraphQL `ObjectType`. A matching `*Input` `InputObjectType` is synthesised automatically for mutation inputs.
- **Enum** → GraphQL `EnumType` with values as-is (no case conversion).
- **Scalars** — per the protobuf → GraphQL canon: `string`/`bytes` → `String`; `bool` → `Boolean`; `int32`/`sint32`/`sfixed32` → `Int`; `float`/`double` → `Float`; `int64`/`uint32`/`uint64`/`fixed32`/`fixed64`/`sint64`/`sfixed64` → `String` (GraphQL `Int` is 32-bit per spec).
- **Repeated `T`** → `[T!]`.
- **`map<K, V>`** → `[KeyValuePair_<K>_<V>!]` — a synthesised helper list type per `(K, V)` pair.
- **`oneof`** → GraphQL union of the message-typed branches (scalar branches skipped with a verbose warning).
- **Well-known types** map to the nearest GraphQL representation: `Timestamp`, `Duration`, `FieldMask` → `String` (canonical protobuf JSON); `Any` → `AnyScalar` (custom); `Struct`/`Value`/`ListValue` → `JsonScalar` (custom); `StringValue`/`Int32Value`/... wrappers → the wrapped GraphQL scalar, nullable; `Empty` → `Boolean` placeholder.

Root types `Query`, `Mutation`, `Subscription` are assembled by grouping mapping entries by `operationType`. Convention-derived fields do not appear in introspection (they have no declared metadata).

### Built-in directives

The synthesised schema advertises three directives so GraphQL clients pass validation:

- `@include(if: Boolean!) on FIELD | FRAGMENT_SPREAD | INLINE_FRAGMENT`
- `@skip(if: Boolean!) on FIELD | FRAGMENT_SPREAD | INLINE_FRAGMENT`
- `@deprecated(reason: String = "No longer supported") on FIELD_DEFINITION | ENUM_VALUE`

## FieldMask projection

The pseudo-argument `$selection: { fieldMask: <path> }` translates the GraphQL selection into a `google.protobuf.FieldMask` at `<path>` in the request. Leaf selections become dotted snake_case paths:

| GraphQL selection | FieldMask value |
|---|---|
| `{ id name }` | `"id,name"` |
| `{ id payload { body size } }` | `"id,payload.body,payload.size"` |
| `{ firstName lastName }` | `"first_name,last_name"` |

Combine with `response: { unwrap: items }` for efficient list-endpoint APIs — the server sees exactly which fields were asked for and can skip filling the rest.

## Environment variables

Any string value in the YAML is eligible for `${VAR}` substitution, resolved at load time via `GrpcChannelFactory.ExpandEnvironmentVariables`. Missing variables fail load-time validation with a clear error message. This is the recommended way to inject credentials:

```yaml
arguments:
  apiKey: { literal: "${MY_API_KEY}" }
  tenantId: { literal: "${TENANT_ID}" }
```

Note that `-H "name: value"` header values on the CLI also support `${VAR}` expansion via the same mechanism. See [Authentication recipes](authentication.md) for worked patterns.

## Related articles

- [Gql2Grpc cookbook](gql2grpc-cookbook.md) — worked examples with copy-paste-ready commands.
- [CLI Reference § gql2grpc](cli-reference.md#gql2grpc-command) — every CLI option.
- [Gql2Grpc future work](gql2grpc-future-work.md) — deferred backlog (proto annotation discovery, N+1 batching, abstract type resolution).
