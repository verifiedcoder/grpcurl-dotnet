# CI/CD integration

`grpcurl.net` and `gql2grpc` are designed to work cleanly inside bash scripts and CI pipelines. Both honour a POSIX-ish exit code contract and emit structured JSON (or NDJSON) so downstream tools can parse results deterministically.

## Exit code contract

| Code | Meaning | Example trigger |
|---|---|---|
| `0` | Success | Normal completion; for `gql2grpc`, no `errors[]` in the envelope. |
| `1` | Internal | Unhandled exception that didn't fall into a known category. |
| `2` | Usage / JSON-parse | Bad CLI args, invalid JSON in `-d`, missing GraphQL document. |
| `3` | Schema / file | Protoset missing or invalid, symbol not found, refusing to overwrite an existing `--protoset-out` target. |
| `4` | Network | TCP/TLS failure outside the RPC itself. |
| `5` | Timeout | Connect or operation deadline exceeded outside the RPC itself. |
| `64 + grpcStatusCode` | Upstream RPC failed. | `InvalidArgument` (3) → `67`, `NotFound` (5) → `69`, `Unauthenticated` (16) → `80`, `Unavailable` (14) → `78`. |
| `130` | User cancelled (Ctrl+C / SIGINT). | Long-running streams aborted by the pipeline runner. |

The contract is implemented in `GrpCurl.Net.Exceptions.GrpcCommandException` (with the typed envelope from `ErrorRenderer`) and (for Gql2Grpc) `ExceptionTranslator.ExitCodeFor`. Every script example below assumes these codes.

## Bash hygiene

Always prefer:

```bash
#!/usr/bin/env bash
set -euo pipefail
```

- `-e` — exit on any command failure. Crucial for `grpcurl.net` calls so downstream steps don't run on a 401/404.
- `-u` — treat unset variables as errors. Catches `$TOKEN` typos before the CLI is even invoked.
- `-o pipefail` — fail the whole pipe if any segment fails. Prevents `grpcurl.net … | jq …` from swallowing upstream failures.

To continue on failure, wrap in a subshell and explicitly inspect the exit code:

```bash
status=0
output=$(grpcurl.net invoke --plaintext ... ) || status=$?

if (( status == 78 )); then
  echo "upstream unavailable; retrying..."
  sleep 5 && exec "$0" "$@"
elif (( status != 0 )); then
  echo "failed with status $status" >&2
  exit "$status"
fi
```

## Piping `describe` → `invoke`

The `describe --msg-template` output is a JSON template for a message type. Pipe it directly into `invoke -d @` to send a default payload — useful for smoke tests:

```bash
grpcurl.net describe --plaintext --msg-template localhost:9090 my.pkg.MyRequest \
  | grpcurl.net invoke --plaintext -d @ localhost:9090 my.pkg.MyService/DoThing
```

`-d @` reads JSON from stdin. For streaming methods, `@` reads one JSON object per line; for unary, the full stdin is treated as a single document.

## Structured failure parsing

For production pipelines where you want to alert on specific gRPC statuses:

```bash
envelope=$(gql2grpc --mapping gql2grpc.yaml \
  -H "authorization: Bearer ${TOKEN}" \
  api.example.com:443 \
  'query { activeResponses(first: 10) { id } }' ) || true

if echo "$envelope" | jq -e '.errors' > /dev/null; then
  code=$(echo "$envelope" | jq -r '.errors[0].extensions.grpcStatusCode // "unknown"')
  status=$(echo "$envelope" | jq -r '.errors[0].extensions.grpcStatus // "unknown"')
  echo "GraphQL error: $status ($code)" >&2
  # Send to alerting, Slack, etc.
  exit 1
fi

echo "$envelope" | jq '.data.activeResponses'
```

The envelope is always emitted on stdout, even for error responses, so `jq` can parse deterministically.

## Health-check patterns

A fail-fast ping that exits within 3 seconds:

```bash
grpcurl.net invoke --plaintext \
  --connect-timeout 1s \
  --max-time 3s \
  -d '{}' localhost:9090 grpc.health.v1.Health/Check
```

Suitable for Kubernetes `exec` probes. Combine with `--output json` to get machine-readable failure details on stderr while keeping stdout clean.

## GitHub Actions

Install the tool, start the server, run the test:

```yaml
jobs:
  smoke-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'
      - name: Build
        run: dotnet build GrpCurl.Net.slnx
      - name: Start TestServer
        run: |
          dotnet run --project Tests/GrpCurl.Net.TestServer --no-build &
          until curl -sf http://localhost:9090 || ss -tln | grep -q :9090; do sleep 1; done
      - name: Smoke — list services
        run: |
          dotnet run --project Src/GrpCurl.Net --no-build -- \
            list --plaintext localhost:9090
      - name: Smoke — GraphQL bridge
        run: |
          dotnet run --project Src/Gql2Grpc --no-build -- \
            --plaintext --default-service testing.TestService \
            localhost:9090 'query { EmptyCall }' \
            | jq -e '.data.EmptyCall != null'
```

Secrets reach the runner via `${{ secrets.NAME }}` and should be exported as env vars before invoking the CLI:

```yaml
      - name: Authenticated query
        env:
          API_TOKEN: ${{ secrets.API_TOKEN }}
        run: |
          gql2grpc --mapping ./gql2grpc.yaml \
            -H "authorization: Bearer ${API_TOKEN}" \
            api.example.com:443 \
            'query { me { id email } }' \
            | tee response.json
```

## GitLab CI

```yaml
smoke:
  image: mcr.microsoft.com/dotnet/sdk:10.0
  variables:
    GRPC_SERVER: localhost:9090
  script:
    - dotnet build GrpCurl.Net.slnx
    - dotnet run --project Tests/GrpCurl.Net.TestServer --no-build &
    - until ss -tln | grep -q :9090; do sleep 1; done
    - dotnet run --project Src/GrpCurl.Net --no-build -- list --plaintext "$GRPC_SERVER"
```

Secrets come in via CI/CD variables (`$API_TOKEN`, etc.) and substitute into `-H` values the same way.

## Avoiding the "invoked but silent" failure mode

`System.CommandLine` returns exit code 0 when no action fires (e.g. unknown flag parsed as a positional argument). Guard against this with an output assertion rather than trusting the exit code alone:

```bash
response=$(grpcurl.net invoke --plaintext -d '{}' localhost:9090 my.pkg.Svc/Ping)

[[ -n "$response" ]] || { echo "empty response" >&2; exit 1; }

echo "$response" | jq . > /dev/null || { echo "not JSON" >&2; exit 1; }
```

This catches misspelled flags that silently degrade to no-ops.

## Cancellation in pipelines

Runners kill long-running commands with SIGTERM. Both CLIs treat this as a linked cancellation and exit promptly — in streaming mode, the in-flight gRPC call is cancelled and no partial JSON object is emitted after the signal. Pipelines that stream many messages should set a reasonable `--max-time` instead of relying on the runner's global timeout.

```bash
timeout --signal=INT 30s gql2grpc --plaintext ... 'subscription { ... }'
```

`timeout --signal=INT` sends SIGINT (exit code 130 on successful cancellation) rather than SIGKILL, giving the tool a chance to flush the last envelope line.
