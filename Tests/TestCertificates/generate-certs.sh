#!/bin/bash

# Generate test certificates for TLS/mTLS testing
# Run this script from the TestCertificates directory

set -e

CERT_DIR="$(dirname "$0")"
cd "$CERT_DIR"

# Clean up old certificates
rm -f *.crt *.key *.csr *.srl *.pem

echo "Generating CA certificate..."
openssl genrsa -out ca.key 2048
openssl req -x509 -new -nodes -key ca.key -sha256 -days 3650 \
    -subj "/CN=Test CA/O=GrpCurl Test/C=US" \
    -out ca.crt

echo "Generating server certificate..."
openssl genrsa -out server.key 2048
openssl req -new -key server.key \
    -subj "/CN=localhost/O=GrpCurl Test/C=US" \
    -out server.csr

# Create server certificate with SAN (Subject Alternative Name) for localhost
cat > server_ext.cnf << EOF
authorityKeyIdentifier=keyid,issuer
basicConstraints=CA:FALSE
keyUsage = digitalSignature, nonRepudiation, keyEncipherment, dataEncipherment
subjectAltName = @alt_names

[alt_names]
DNS.1 = localhost
IP.1 = 127.0.0.1
IP.2 = ::1
EOF

openssl x509 -req -in server.csr -CA ca.crt -CAkey ca.key -CAcreateserial \
    -out server.crt -days 3650 -sha256 -extfile server_ext.cnf

echo "Generating client certificate for mTLS..."
openssl genrsa -out client.key 2048
openssl req -new -key client.key \
    -subj "/CN=Test Client/O=GrpCurl Test/C=US" \
    -out client.csr

cat > client_ext.cnf << EOF
authorityKeyIdentifier=keyid,issuer
basicConstraints=CA:FALSE
keyUsage = digitalSignature, keyEncipherment
extendedKeyUsage = clientAuth
EOF

openssl x509 -req -in client.csr -CA ca.crt -CAkey ca.key -CAcreateserial \
    -out client.crt -days 3650 -sha256 -extfile client_ext.cnf

echo "Generating wrong CA certificate..."
openssl genrsa -out wrong-ca.key 2048
openssl req -x509 -new -nodes -key wrong-ca.key -sha256 -days 3650 \
    -subj "/CN=Wrong CA/O=Wrong Org/C=US" \
    -out wrong-ca.crt

echo "Generating expired certificate..."
openssl genrsa -out expired.key 2048
openssl req -new -key expired.key \
    -subj "/CN=Expired Cert/O=GrpCurl Test/C=US" \
    -out expired.csr

# Generate a certificate that expired yesterday
openssl x509 -req -in expired.csr -CA ca.crt -CAkey ca.key -CAcreateserial \
    -out expired.crt -days -1 -sha256 2>/dev/null || \
openssl ca -config /dev/null -in expired.csr -out expired.crt \
    -cert ca.crt -keyfile ca.key -batch -notext \
    -startdate $(date -d "-2 days" "+%y%m%d000000Z") \
    -enddate $(date -d "-1 day" "+%y%m%d000000Z") 2>/dev/null || \
echo "Note: Expired cert generation may require manual adjustment"

# Generate PFX files for .NET (combines cert + key)
echo "Generating PFX files for .NET..."
openssl pkcs12 -export -out server.pfx -inkey server.key -in server.crt \
    -certfile ca.crt -passout pass:testpassword

openssl pkcs12 -export -out client.pfx -inkey client.key -in client.crt \
    -certfile ca.crt -passout pass:testpassword

# Cleanup temporary files
rm -f *.csr *.cnf *.srl

echo ""
echo "Test certificates generated successfully:"
ls -la *.crt *.key *.pfx 2>/dev/null || true
echo ""
echo "Password for PFX files: testpassword"
