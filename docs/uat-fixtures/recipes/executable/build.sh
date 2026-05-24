#!/usr/bin/env bash
# Build the executable UAT fixtures into docs/uat-fixtures/build/executable/.
#
# Variants per the per-type README:
#   single-binary/      - one Go binary at root, listens on $PORT
#   multiple-binaries/  - two Go binaries at root (sorted: aaa, bbb)
#   looks-like-dotnet/  - copy of the dotnet-app/self-contained/ build output
#                         (drives the §3.3 IsManagedDotnet nudge banner)
#
# Cross-OS:
#   Windows: produces `*.exe` (`ListExecutablesAtRoot` keys off .exe extension).
#   Linux:   produces extensionless binary with execute bit (`HasExecutableBit`
#            is the gate).
#
# Determinism:
#   `-trimpath` strips $GOPATH and $HOME from the binary.
#   `-buildvcs=false` avoids stamping git revision into the binary.
#   Same Go toolchain + same machine => byte-identical binary.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(cd "$here/../../../.." && pwd)"
src_root="$here/sources"
out_root="$repo_root/docs/uat-fixtures/build/executable"

# Detect host OS for the binary extension.
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) bin_ext=".exe" ;;
  *) bin_ext="" ;;
esac

mkdir -p "$out_root"

# --- single-binary/ ---
sb="$out_root/single-binary"
rm -rf "$sb"
mkdir -p "$sb"
(
  cd "$src_root"
  GOFLAGS="" go build -trimpath -buildvcs=false -ldflags='-s -w -buildid=' \
    -o "$sb/uat-executable$bin_ext" ./...
)
if [[ -z "$bin_ext" ]]; then
  chmod +x "$sb/uat-executable"
fi

# --- multiple-binaries/ ---
mb="$out_root/multiple-binaries"
rm -rf "$mb"
mkdir -p "$mb"
# Same binary built under two different names (sorted: aaa < bbb).
cp "$sb/uat-executable$bin_ext" "$mb/aaa$bin_ext"
cp "$sb/uat-executable$bin_ext" "$mb/bbb$bin_ext"
if [[ -z "$bin_ext" ]]; then
  chmod +x "$mb/aaa" "$mb/bbb"
fi

# --- looks-like-dotnet/ ---
ld="$out_root/looks-like-dotnet"
rm -rf "$ld"
mkdir -p "$ld"
src_dn="$repo_root/docs/uat-fixtures/build/dotnet-app/self-contained"
if [[ ! -d "$src_dn" ]]; then
  echo "warn: dotnet-app/self-contained/ not built yet - run the dotnet-app recipe first."
  echo "      looks-like-dotnet/ will be left empty."
else
  cp -R "$src_dn/." "$ld/"
fi

find "$out_root" -exec touch -d "2026-01-01T00:00:00Z" {} + 2>/dev/null || true

echo "executable fixtures built at: $out_root"
[[ -z "$bin_ext" ]] && echo "  (Linux/macOS: binaries have execute bit set)"
