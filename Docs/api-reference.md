# API Reference

Public API documentation for the libraries backing `grpcn` and `gql2grpc`.

The reference is generated from XML doc comments in source. Internal helpers, tests, and auto-generated protocol types are excluded via `filterConfig.yml`. Browse types via the API sidebar, or pick a landing namespace below.

## Namespaces

### GrpCurl.Net

- **`GrpCurl.Net.DescriptorSources`** — [`IDescriptorSource`](xref:GrpCurl.Net.DescriptorSources.IDescriptorSource), [`ReflectionSource`](xref:GrpCurl.Net.DescriptorSources.ReflectionSource), [`ProtosetSource`](xref:GrpCurl.Net.DescriptorSources.ProtosetSource), [`WellKnownTypeRegistry`](xref:GrpCurl.Net.DescriptorSources.WellKnownTypeRegistry). The descriptor abstraction used by every CLI command and by Gql2Grpc.
- **`GrpCurl.Net.Exceptions`** — [`GrpcCommandException`](xref:GrpCurl.Net.Exceptions.GrpcCommandException). Exit-code contract is `64 + grpcStatusCode` for RPC errors, `130` for Ctrl+C, `1` for general errors. See [CI/CD integration](articles/ci-cd.md#exit-code-contract) for the full table.

### Gql2Grpc

- **`Gql2Grpc.Configuration`** — mapping-file records: [`MappingConfig`](xref:Gql2Grpc.Configuration.MappingConfig), [`MappingEntry`](xref:Gql2Grpc.Configuration.MappingEntry), [`ArgumentRule`](xref:Gql2Grpc.Configuration.ArgumentRule) hierarchy (`Rename`, `PathRule`, `Literal`, `SkipArgument`), [`MethodKind`](xref:Gql2Grpc.Configuration.MethodKind), [`ResponseShaping`](xref:Gql2Grpc.Configuration.ResponseShaping). Loader and resolver: [`MappingConfigLoader`](xref:Gql2Grpc.Configuration.MappingConfigLoader), [`MappingResolver`](xref:Gql2Grpc.Configuration.MappingResolver).
- **`Gql2Grpc.GraphQL`** — parsed document surface: [`GraphQLDocument`](xref:Gql2Grpc.GraphQL.GraphQLDocument), [`GraphQLOperation`](xref:Gql2Grpc.GraphQL.GraphQLOperation), [`GraphQLOperationType`](xref:Gql2Grpc.GraphQL.GraphQLOperationType), [`ResolvedSelection`](xref:Gql2Grpc.GraphQL.ResolvedSelection), [`SelectionResolver`](xref:Gql2Grpc.GraphQL.SelectionResolver), [`VariableCoercer`](xref:Gql2Grpc.GraphQL.VariableCoercer), [`GraphQLError`](xref:Gql2Grpc.GraphQL.GraphQLError), [`GraphQLDocumentParser`](xref:Gql2Grpc.GraphQL.GraphQLDocumentParser).
- **`Gql2Grpc.Introspection`** — schema synthesis: [`GraphQLSchemaBuilder`](xref:Gql2Grpc.Introspection.GraphQLSchemaBuilder), [`IntrospectionExecutor`](xref:Gql2Grpc.Introspection.IntrospectionExecutor).
- **`Gql2Grpc.Response.RootFieldResult`** — per-field outcome record consumed by the response builder and streaming writer. [`RootFieldResult`](xref:Gql2Grpc.Response.RootFieldResult).

## Related articles

- [CLI Reference](articles/cli-reference.md)
- [Architecture](articles/architecture.md) — module layout and dataflow
- [Gql2Grpc mapping file](articles/gql2grpc-mapping.md)
- [Gql2Grpc cookbook](articles/gql2grpc-cookbook.md)
