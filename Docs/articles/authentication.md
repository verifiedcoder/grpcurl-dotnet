# Authentication recipes

Header-based authentication patterns for `grpcurl.net invoke` and `gql2grpc`. Both CLIs share the same header machinery — `-H "name: value"`, `--rpc-header`, `--reflect-header` — and both expand `${ENV_VAR}` references via `GrpcChannelFactory.ExpandEnvironmentVariables`, so secrets stay out of shell history when sourced from the environment.

Scopes:

- `-H` applies the header to both reflection calls and the RPC itself.
- `--rpc-header` applies only to the RPC (the normal choice for auth).
- `--reflect-header` applies only to reflection calls (rare; use when reflection requires a different token).

## Bearer token

The standard OAuth2 / OpenID Connect pattern:

```bash
export API_TOKEN='<your bearer token>'
grpcurl.net invoke --plaintext \
  --rpc-header "authorization: Bearer ${API_TOKEN}" \
  -d '{"id": "abc"}' \
  api.example.com:443 my.pkg.MyService/GetThing
```

gRPC header names are case-insensitive but conventionally lowercase. `authorization: Bearer …` is the HTTP convention; many gRPC services accept it unchanged.

## API key

Typical patterns include `x-api-key` or a vendor-specific header. Same substitution works:

```bash
export MY_API_KEY='...'
grpcurl.net invoke --plaintext \
  --rpc-header "x-api-key: ${MY_API_KEY}" \
  -d '{}' localhost:9090 my.pkg.MyService/Ping
```

With Gql2Grpc, bake it into the mapping file instead of the CLI if the same key applies to every call:

```yaml
operations:
  - graphqlField: listThings
    method: ListThings
    arguments:
      $selection: { fieldMask: read_mask }
      apiKey: { literal: "${MY_API_KEY}" }  # sent as the `api_key` request field
```

The literal is env-expanded at config load, so the actual secret never lands in logs or the mapping file itself.

## Cookie authentication (session token)

The motivating scenario for `gql2grpc`: enterprise gRPC backends fronted by identity servers that issue session cookies. `gql2grpc` forwards the cookie header verbatim:

```bash
export SESSION_COOKIE='mySessionCookie'
gql2grpc --mapping gql2grpc.yaml \
  -H "cookie: .tmc.ac.session=${SESSION_COOKIE}" \
  api.example.com:443 \
  'query($first: Int) { activeResponses(first: $first) { id } }' \
  --var first=10
```

Multiple cookies on one header:

```bash
-H "cookie: .tmc.ac.session=${SESSION_COOKIE}; XSRF-TOKEN=${XSRF}"
```

Separate values with `; ` (semicolon-space). The HTTP cookie format is preserved end-to-end through gRPC metadata.

## Mutual TLS (mTLS)

`grpcurl.net` and `gql2grpc` both support client-certificate authentication via PEM and PKCS12.

### PEM — separate cert and key

```bash
grpcurl.net invoke \
  --cacert ca.pem \
  --cert client.pem \
  --key client.key \
  --authority my-service.internal \
  -d '{}' my-service.internal:443 my.pkg.MyService/Ping
```

The private key file must be unencrypted — passphrases require PKCS12 below.

### PKCS12 — single container

```bash
grpcurl.net invoke \
  --cacert ca.pem \
  --cert client.p12 \
  --cert-password "${P12_PASSPHRASE}" \
  -d '{}' my-service.internal:443 my.pkg.MyService/Ping
```

`.pfx` and `.p12` are both accepted — the extension determines nothing, the format is detected from the file content.

### Generating test certificates

The repo ships `Tests/TestCertificates/generate-certs.sh` (and a PowerShell sibling `generate-certs.ps1`), which uses `openssl` to produce a self-signed CA, server cert, client cert, and matching `.p12` bundles for integration testing. Run once:

```bash
# Linux / macOS / WSL
bash Tests/TestCertificates/generate-certs.sh

# Windows (PowerShell 7+)
pwsh Tests/TestCertificates/generate-certs.ps1
```

The test suite regenerates `client.pfx` on demand from the checked-in PEM pair (see `Tests/GrpCurl.DotNet.Tests.Unit/Utilities/GrpcChannelFactoryTests.EnsureClientPfx`), so you don't need `openssl` locally to run unit tests.

### TLS hardening defaults

GrpCurl.Net applies these defaults whenever `--cacert` or `--cert` is supplied:

| Setting | Default | Override |
|---|---|---|
| Revocation policy | `Online` (fetches CRL / OCSP) | `--revocation-mode offline\|nocheck` |
| PKCS12 private-key storage | `EphemeralKeySet` (key never persists) | `--exportable-key` |
| Cert format detection | Content-based — PKCS12 is tried first, PEM fallback if it fails | (none) |

Use `--revocation-mode nocheck` only against self-signed fixtures that lack a CRL distribution point. Production deployments should leave the default `Online` so revoked certs are rejected.

### Verbose output and secrets

When `--verbose` (`-v`) prints request metadata it redacts sensitive header values by default:

```text
authorization: [REDACTED]
x-api-key: [REDACTED]
cookie: [REDACTED]
trace-bin: [REDACTED]
```

Patterns that always redact: `authorization`, `cookie`, `set-cookie`, `proxy-authorization`, `x-api-key`, `x-auth-token`, `x-access-token`, `x-csrf-token`, `x-amz-security-token`, and any header whose final segment is `-token`, `-secret`, `-password`, `-credential`, `-signature`/`-sig`, `-nonce`, `-jwt`, `-api-key`/`-api_key`. All `*-bin` metadata is redacted too because the base64 payload is opaque.

Pass `--unsafe-show-secrets` to opt out of redaction (e.g. when piping `-v` output through a sanitiser of your own).

## OAuth2 / service-account flow

For service-account authentication (client_credentials grant), obtain a token out-of-band and pass it as a bearer header:

```bash
# Fetch a token from your identity provider
ACCESS_TOKEN=$(curl -s -X POST https://idp.example.com/oauth2/token \
  -d grant_type=client_credentials \
  -d client_id="${CLIENT_ID}" \
  -d client_secret="${CLIENT_SECRET}" \
  | jq -r .access_token)

# Pass it to every subsequent gRPC call
grpcurl.net invoke \
  --rpc-header "authorization: Bearer ${ACCESS_TOKEN}" \
  -d '{"query": "..."}' \
  api.example.com:443 my.pkg.SearchService/Search
```

If tokens expire mid-script, wrap the invocation in a refresh loop. For long-running subscriptions in `gql2grpc`, pre-fetch a longer-lived token or accept that the stream terminates at token expiry.

## Gql2Grpc: reflection vs RPC with different credentials

When reflection and the RPC itself require different headers (e.g. reflection is anonymous but the RPC requires a user token):

```bash
gql2grpc --plaintext \
  --reflect-header "x-internal: reflection" \
  --rpc-header "authorization: Bearer ${USER_TOKEN}" \
  localhost:9090 'query { ... }'
```

`-H` would apply to both; the scoped flags keep the two channels separate even though they share the same `GrpcChannel`.

## Secret hygiene

- Use environment variables and `${VAR}` expansion — keep secrets out of shell history (`history` or `.bash_history`).
- For CI pipelines, load secrets from the runner's secrets store and export as env vars. See [CI/CD integration](ci-cd.md).
- Avoid embedding secrets in `--mapping` files that are committed to git. Use `${VAR}` references in `literal:` rules instead.
- `gql2grpc --vv` echoes the outbound request JSON on stderr. If a secret flows through a `literal` rule, it *will* appear in verbose logs — mute `--vv` in production scripts, or scope the verbose output to a file you control.
