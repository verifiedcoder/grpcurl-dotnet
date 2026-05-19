#requires -Version 7.0
# Generate test certificates for TLS/mTLS testing on Windows.
# Equivalent to generate-certs.sh. Requires openssl on PATH (Git for Windows
# ships it, or install via "winget install ShiningLight.OpenSSL").

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$certDir = Split-Path -Parent $PSCommandPath
Set-Location $certDir

if (-not (Get-Command openssl -ErrorAction SilentlyContinue)) {
    throw "openssl was not found on PATH. Install OpenSSL (Git for Windows / winget ShiningLight.OpenSSL) and retry."
}

# Clean up old artefacts.
Get-ChildItem -Path $certDir -Include *.crt, *.key, *.csr, *.srl, *.pem, *.pfx -Recurse -Force `
    | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host 'Generating CA certificate...'
openssl genrsa -out ca.key 2048
openssl req -x509 -new -nodes -key ca.key -sha256 -days 3650 `
    -subj '/CN=Test CA/O=GrpCurl Test/C=US' `
    -out ca.crt

Write-Host 'Generating server certificate...'
openssl genrsa -out server.key 2048
openssl req -new -key server.key `
    -subj '/CN=localhost/O=GrpCurl Test/C=US' `
    -out server.csr

@'
authorityKeyIdentifier=keyid,issuer
basicConstraints=CA:FALSE
keyUsage = digitalSignature, nonRepudiation, keyEncipherment, dataEncipherment
subjectAltName = @alt_names

[alt_names]
DNS.1 = localhost
IP.1 = 127.0.0.1
IP.2 = ::1
'@ | Set-Content -Path server_ext.cnf -Encoding ASCII

openssl x509 -req -in server.csr -CA ca.crt -CAkey ca.key -CAcreateserial `
    -out server.crt -days 3650 -sha256 -extfile server_ext.cnf

Write-Host 'Generating client certificate for mTLS...'
openssl genrsa -out client.key 2048
openssl req -new -key client.key `
    -subj '/CN=Test Client/O=GrpCurl Test/C=US' `
    -out client.csr

@'
authorityKeyIdentifier=keyid,issuer
basicConstraints=CA:FALSE
keyUsage = digitalSignature, keyEncipherment
extendedKeyUsage = clientAuth
'@ | Set-Content -Path client_ext.cnf -Encoding ASCII

openssl x509 -req -in client.csr -CA ca.crt -CAkey ca.key -CAcreateserial `
    -out client.crt -days 3650 -sha256 -extfile client_ext.cnf

Write-Host 'Generating wrong CA certificate...'
openssl genrsa -out wrong-ca.key 2048
openssl req -x509 -new -nodes -key wrong-ca.key -sha256 -days 3650 `
    -subj '/CN=Wrong CA/O=Wrong Org/C=US' `
    -out wrong-ca.crt

Write-Host 'Generating expired certificate...'
openssl genrsa -out expired.key 2048
openssl req -new -key expired.key `
    -subj '/CN=Expired Cert/O=GrpCurl Test/C=US' `
    -out expired.csr

openssl x509 -req -in expired.csr -CA ca.crt -CAkey ca.key -CAcreateserial `
    -out expired.crt -days -1 -sha256

Write-Host 'Generating PFX files for .NET...'
openssl pkcs12 -export -out server.pfx -inkey server.key -in server.crt `
    -certfile ca.crt -passout pass:testpassword
openssl pkcs12 -export -out client.pfx -inkey client.key -in client.crt `
    -certfile ca.crt -passout pass:testpassword

Get-ChildItem -Path $certDir -Include *.csr, *.cnf, *.srl -Recurse -Force `
    | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Test certificates generated successfully:'
Get-ChildItem -Path $certDir -Include *.crt, *.key, *.pfx | Format-Table -AutoSize
Write-Host ''
Write-Host 'Password for PFX files: testpassword'
