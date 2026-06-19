#!/usr/bin/env bash
#
# publish.sh — build the zero-budget GitHub-Release artifacts for one RID.
#
# Produces, into <staging>/dist, self-contained (no .NET prerequisite) binaries:
#   * grpcn      CLI  — single-file
#   * gql2grpc   CLI  — single-file
#   * GrpCurl.Net Studio — folder archive (a .app bundle on osx-*)
#
# Archive format follows the RID: win-* -> .zip, everything else -> .tar.gz
# (tar preserves the executable bit; zip is friendlier on Windows).
#
# Usage: publish.sh <rid> <version> [staging-dir]
#   <rid>          win-x64 | win-arm64 | osx-x64 | osx-arm64 | linux-x64 | linux-arm64
#   <version>      X.Y.Z   (no leading v)
#   [staging-dir]  default: artifacts/release
#
# Env:
#   GIT_SHA   short commit sha for InformationalVersion (default: `git rev-parse --short HEAD`)
#
# Notes:
#   * PublishReadyToRun is OFF — R2R cannot cross-gen to a foreign arch and 3/6 RIDs cross-publish.
#   * RestorePackagesWithLockFile is forced OFF for publish so RID-specific runtime packs never
#     churn the committed packages.lock.json; the locked-mode gate lives in the build-test job.
set -euo pipefail

RID="${1:?usage: publish.sh <rid> <version> [staging-dir]}"
VERSION="${2:?usage: publish.sh <rid> <version> [staging-dir]}"
STAGING="${3:-artifacts/release}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

GIT_SHA="${GIT_SHA:-$(git rev-parse --short HEAD 2>/dev/null || echo local)}"
INFO_VERSION="${VERSION}+${GIT_SHA}"

case "$RID" in
  win-*) EXE=".exe"; FMT="zip" ;;
  *)     EXE="";     FMT="tar" ;;
esac

PUB="$STAGING/publish/$RID"
STAGE="$STAGING/stage/$RID"
DIST="$STAGING/dist"
rm -rf "$PUB" "$STAGE"
mkdir -p "$PUB" "$STAGE" "$DIST"

COMMON=(
  -c Release
  -r "$RID"
  --self-contained true
  -p:Version="$VERSION"
  -p:InformationalVersion="$INFO_VERSION"
  -p:IncludeSourceRevisionInInformationalVersion=false
  -p:PublishReadyToRun=false
  -p:RestoreLockedMode=false
  --nologo
  -v minimal
)

# archive <src-dir> <artifact-basename> — packs the CONTENTS of <src-dir> at the archive root.
archive() {
  local src="$1" base="$2"
  if [ "$FMT" = "zip" ]; then
    (cd "$src" && zip -qr -X "$ROOT/$DIST/$base.zip" .)
    echo "  -> $DIST/$base.zip"
  else
    tar -czf "$DIST/$base.tar.gz" -C "$src" .
    echo "  -> $DIST/$base.tar.gz"
  fi
}

publish_cli() {
  local proj="$1" asm="$2" friendly="$3"
  local out="$PUB/$friendly"
  echo "==> publishing CLI $friendly ($RID)"
  dotnet publish "$proj" "${COMMON[@]}" \
    -p:PublishSingleFile=true \
    -p:EnableCompressionInSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    --output "$out"

  local s="$STAGE/$friendly"
  rm -rf "$s"; mkdir -p "$s"
  cp "$out/$asm$EXE" "$s/$friendly$EXE"
  chmod +x "$s/$friendly$EXE"
  archive "$s" "$friendly-$RID-$VERSION"
}

echo "### publish.sh  rid=$RID  version=$VERSION  info=$INFO_VERSION"

publish_cli "Src/GrpCurl.Net/GrpCurl.Net.csproj" "GrpCurl.Net" "grpcn"
publish_cli "Src/Gql2Grpc/Gql2Grpc.csproj"        "Gql2Grpc"    "gql2grpc"

echo "==> publishing Studio ($RID)"
STUDIO_OUT="$PUB/studio"
dotnet publish "Src/GrpCurl.Net.Studio/GrpCurl.Net.Studio.csproj" "${COMMON[@]}" --output "$STUDIO_OUT"
rm -f "$STUDIO_OUT/"*.pdb || true

case "$RID" in
  osx-*)
    APP_PARENT="$STAGE/studio"
    rm -rf "$APP_PARENT"; mkdir -p "$APP_PARENT"
    bash "$ROOT/Scripts/package/make-macos-app.sh" \
      "$STUDIO_OUT" "$VERSION" "$APP_PARENT/GrpCurl.Net Studio.app"
    archive "$APP_PARENT" "GrpCurlNetStudio-$RID-$VERSION"
    ;;
  *)
    STUDIO_STAGE="$STAGE/studio/GrpCurlNetStudio-$RID-$VERSION"
    rm -rf "$STAGE/studio"; mkdir -p "$STUDIO_STAGE"
    cp -R "$STUDIO_OUT/." "$STUDIO_STAGE/"
    archive "$STAGE/studio" "GrpCurlNetStudio-$RID-$VERSION"
    ;;
esac

echo "### done: artifacts in $DIST"
ls -1 "$DIST"
