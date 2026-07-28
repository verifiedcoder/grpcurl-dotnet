#!/usr/bin/env bash
#
# runtime-pack.sh — locate the .NET runtime pack(s) a self-contained publish embedded.
#
# Sourced by publish.sh (to ship the runtime's own licence + third-party notice) and by
# generate-sbom.sh (to record the pack in the SBOM). A self-contained archive is mostly runtime by
# byte count, and the runtime pack is NOT part of the NuGet project graph — it never appears in
# packages.lock.json or project.assets.json — so it has to be read out of the publish's deps.json,
# which names it exactly (`runtimepack.Microsoft.NETCore.App.Runtime.<rid>/<version>`).
#
# Functions:
#   runtime_packs <project-csproj> <rid>   -> lines of "Id/Version" (empty + non-zero if none found)
#   runtime_pack_dir <Id> <Version>        -> the pack directory on disk
#   runtime_pack_nupkg <Id> <Version>      -> the .nupkg path, if the pack came from the NuGet cache

# The deps.json under obj/ exists for every publish flavour, including single-file (where the
# published copy is bundled into the executable rather than left on disk).
runtime_packs() {
  local proj="$1" rid="$2" deps
  deps="$(find "$(dirname "$proj")/obj/Release" -path "*/$rid/*.deps.json" -type f 2>/dev/null | head -1)"
  if [ -z "$deps" ]; then
    echo "runtime-pack: no deps.json for $proj ($rid) — publish it first" >&2
    return 1
  fi
  grep -oE '"runtimepack\.[A-Za-z0-9._-]+/[^"]+"' "$deps" \
    | tr -d '"' \
    | sed 's/^runtimepack\.//' \
    | sort -u
}

_dotnet_root() {
  if [ -n "${DOTNET_ROOT:-}" ] && [ -d "$DOTNET_ROOT" ]; then
    echo "$DOTNET_ROOT"
    return 0
  fi
  local exe
  exe="$(command -v dotnet 2>/dev/null)" || return 1
  # Resolve symlinks where the platform can; a plain dirname is right on the GitHub runners.
  exe="$(readlink -f "$exe" 2>/dev/null || echo "$exe")"
  dirname "$exe"
}

runtime_pack_dir() {
  local id="$1" ver="$2" lower cand root
  lower="$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')"
  root="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
  for cand in "$root/$lower/$ver" "$(_dotnet_root 2>/dev/null)/packs/$id/$ver"; do
    if [ -d "$cand" ]; then
      echo "$cand"
      return 0
    fi
  done
  echo "runtime-pack: cannot find $id $ver (looked in $root and the SDK packs folder)" >&2
  return 1
}

runtime_pack_nupkg() {
  local id="$1" ver="$2" dir lower
  dir="$(runtime_pack_dir "$id" "$ver")" || return 1
  lower="$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')"
  if [ ! -f "$dir/$lower.$ver.nupkg" ]; then
    # Packs restored into the NuGet cache keep their .nupkg; one resolved from the SDK's packs/
    # folder does not, and then there is no NuGet-recorded artifact to hash.
    echo "runtime-pack: $dir has no $lower.$ver.nupkg to hash" >&2
    return 1
  fi
  echo "$dir/$lower.$ver.nupkg"
}
