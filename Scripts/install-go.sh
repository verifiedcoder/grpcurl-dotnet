#!/bin/bash
# =============================================================================
# Script: install-go.sh
# Purpose: Install Go and set up environment for grpcurl testing
# =============================================================================

set -e

GO_VERSION="1.22.0"
GO_TARBALL="go${GO_VERSION}.linux-amd64.tar.gz"
GO_URL="https://go.dev/dl/${GO_TARBALL}"

echo "=== Installing Go ${GO_VERSION} ==="

# Download Go
echo "Downloading Go from ${GO_URL}..."
cd /tmp
wget -q --show-progress "${GO_URL}"

# Remove previous installation and extract
echo "Installing to /usr/local/go..."
sudo rm -rf /usr/local/go
sudo tar -C /usr/local -xzf "${GO_TARBALL}"

# Clean up
rm -f "${GO_TARBALL}"

# Set up PATH for current session
export PATH=$PATH:/usr/local/go/bin
export PATH=$PATH:$HOME/go/bin

# Add to .bashrc if not already present
if ! grep -q '/usr/local/go/bin' ~/.bashrc 2>/dev/null; then
    echo "" >> ~/.bashrc
    echo "# Go environment" >> ~/.bashrc
    echo 'export PATH=$PATH:/usr/local/go/bin' >> ~/.bashrc
    echo 'export PATH=$PATH:$HOME/go/bin' >> ~/.bashrc
    echo "Added Go paths to ~/.bashrc"
fi

# Create go bin directory
mkdir -p $HOME/go/bin

# Verify installation
echo ""
echo "=== Verifying Installation ==="
go version

echo ""
echo "=== Installing grpcurl ==="
go install github.com/fullstorydev/grpcurl/cmd/grpcurl@latest

echo ""
echo "=== Verifying grpcurl ==="
$HOME/go/bin/grpcurl --version

echo ""
echo "=== Building gRPC-Go Interop Server ==="
if [ -d "/tmp/grpc-go" ]; then
    echo "Removing existing /tmp/grpc-go..."
    rm -rf /tmp/grpc-go
fi

git clone --depth 1 https://github.com/grpc/grpc-go.git /tmp/grpc-go
cd /tmp/grpc-go/interop/server
go build -o /tmp/interop_server .

echo ""
echo "=== Verifying Interop Server ==="
ls -la /tmp/interop_server

echo ""
echo "=============================================="
echo "Installation Complete!"
echo ""
echo "Components installed:"
echo "  - Go ${GO_VERSION} at /usr/local/go"
echo "  - grpcurl at $HOME/go/bin/grpcurl"
echo "  - gRPC-Go interop server at /tmp/interop_server"
echo ""
echo "To use in current shell, run:"
echo "  source ~/.bashrc"
echo ""
echo "Or start a new terminal session."
echo "=============================================="
