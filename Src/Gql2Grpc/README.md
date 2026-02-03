# gQL2gRPC

An experimental GraphQL to gRPC query proxy.

## Supported Features

- Query operations (no mutations yet)
- Relay pagination: first, after, before
- Filtering: filter argument
- Sorting: orderBy argument
- GraphQL JSON response format: { "data": { ... } }
- Error handling: GraphQL-style errors

## Usage

### Basic query

``` bash
dotnet run --project Src/Gql2Grpc -c Release -- \
  --plaintext \
  --protoset /path/to/service.protoset \
  --grpcurl /path/to/GrpCurl.Net.csproj \
  localhost:8080 \
  'query { activeResponses(first: 10) { pageInfo { totalItems } } }'
```

### With filter

``` bash
dotnet run --project Src/Gql2Grpc -c Release -- \
  --plaintext \
  --protoset /path/to/service.protoset \
  --grpcurl /path/to/GrpCurl.Net.csproj \
  localhost:8080 \
  'query { activeResponses(filter: "status = ARS_ACTIVE") { pageInfo { totalItems } } }'
```
