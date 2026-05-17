# Test Certificates

These certificates back the TLS and mTLS integration tests. They are **for testing
only** and must never be reused in any production context.

The full chain is:

| File | Purpose |
|---|---|
| `ca.crt` / `ca.key` | Test root CA used to sign both the server and client certs. |
| `server.crt` / `server.key` | Valid server cert with SANs for `localhost`, `127.0.0.1`, `::1`. Long expiry (3650 days). |
| `client.crt` / `client.key` | Valid client cert with `extendedKeyUsage = clientAuth`, signed by `ca.crt`. Used for mTLS tests. |
| `wrong-ca.crt` / `wrong-ca.key` | A second, unrelated CA used to drive negative tests (server presents this and the client's `--cacert` rejects it). |
| `expired.crt` / `expired.key` | Already-expired leaf cert used to drive validation failures. |
| `server.pfx` / `client.pfx` | PKCS12 bundles built from the PEM files. Password: `testpassword`. Generated on demand by the scripts. |

## Regenerating

```bash
# Linux / macOS / WSL
./generate-certs.sh

# Windows (PowerShell 7)
./generate-certs.ps1
```

Both scripts require `openssl` on PATH. The PowerShell script uses ASCII-encoded
config files to stay compatible with the OpenSSL config parser.

The .NET test server (`Tests/GrpCurl.Net.TestServer/Program.cs`) loads `server.crt`
+ `server.key` (PEM) directly via `X509Certificate2.CreateFromPemFile`. There is no
runtime dependency on the developer certificate store, so TLS tests behave the same
on Windows, Linux, macOS, and headless CI.
