#!/usr/bin/env bash
# Build the dotnet-app UAT fixtures into docs/uat-fixtures/build/dotnet-app/.
#
# Variants per the per-type README:
#   framework-dependent/            - normal `dotnet publish` (no -p:PublishSingleFile)
#   self-contained/                 - --self-contained -p:PublishSingleFile=true
#   self-contained-pdb-stripped/    - self-contained + -p:DebugType=none
#
# Pinned versions live in sources/UatDotnetFixture.csproj. NuGet restore against
# the configured feeds is required on first run (subsequent runs hit local cache).
#
# Reproducibility:
#   - <Deterministic>true</Deterministic> in csproj (deterministic compile).
#   - <PathMap> normalises embedded source paths.
#   - mtimes pinned post-build.
#   The published .pdb has a content-derived MVID and timestamp; bit-for-bit
#   reproducibility across machines requires identical SDK patch versions.
#   Same-machine repeated runs are byte-identical post-mtime-pin for the file
#   bodies (the GUID-named .deps.json entries are deterministic per the SDK).
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(cd "$here/../../../.." && pwd)"
src_root="$here/sources"
out_root="$repo_root/docs/uat-fixtures/build/dotnet-app"

# Detect host RID (publishing self-contained requires a RID).
detect_rid() {
  case "$(uname -s)" in
    Linux*)   echo "linux-x64" ;;
    Darwin*)  echo "osx-x64" ;;
    MINGW*|MSYS*|CYGWIN*) echo "win-x64" ;;
    *)        echo "linux-x64" ;;
  esac
}
rid="$(detect_rid)"

mkdir -p "$out_root"

build_variant() {
  local name="$1"
  shift
  local dst="$out_root/$name"
  rm -rf "$dst"
  mkdir -p "$dst"

  echo ">>> $name (RID=$rid)"
  dotnet publish "$src_root/UatDotnetFixture.csproj" \
    --configuration Release \
    --output "$dst" \
    --nologo \
    --verbosity quiet \
    "$@"
}

build_variant framework-dependent

build_variant self-contained \
  --runtime "$rid" \
  --self-contained true \
  -p:PublishSingleFile=true

build_variant self-contained-pdb-stripped \
  --runtime "$rid" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -p:StaticWebAssetsEnabled=false

# Stable mtimes for archive-hash stability. (Per-file bytes are determined by
# the SDK; this only normalises the surrounding metadata.)
find "$out_root" -exec touch -d "2026-01-01T00:00:00Z" {} + 2>/dev/null || true

echo "dotnet-app fixtures built at: $out_root"
