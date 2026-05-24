#!/usr/bin/env bash
# Build the static-site UAT fixtures into docs/uat-fixtures/build/static-site/.
# Idempotent: rerunning produces byte-identical output.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(cd "$here/../../../.." && pwd)"
src_root="$here/sources"
out_root="$repo_root/docs/uat-fixtures/build/static-site"

variants=(basic with-config-json spa-bundle)

mkdir -p "$out_root"

for v in "${variants[@]}"; do
  dst="$out_root/$v"
  rm -rf "$dst"
  mkdir -p "$dst"
done

# basic: as-is
cp -R "$src_root/basic/." "$out_root/basic/"

# with-config-json: basic + config.json
cp -R "$src_root/basic/." "$out_root/with-config-json/"
cp "$src_root/with-config-json/config.json" "$out_root/with-config-json/config.json"

# spa-bundle: its own shape (index.html + assets/)
cp -R "$src_root/spa-bundle/." "$out_root/spa-bundle/"

# Pin mtimes for byte-stable tar/zip downstream (no impact on per-file SHA, but stabilises archive hashes).
find "$out_root" -exec touch -d "2026-01-01T00:00:00Z" {} +

echo "static-site fixtures built at: $out_root"
