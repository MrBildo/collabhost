#!/usr/bin/env bash
# Build the nodejs-app UAT fixtures into docs/uat-fixtures/build/nodejs-app/.
#
# Strategy:
# - The fixture server uses Node stdlib (`http`) only - no runtime dep on `express`.
#   The `express` dep is declared in package.json purely for probe-panel coverage
#   (NodeData.dependencies count + notable list).
# - `node_modules/` is NOT populated by this recipe. The runbook's nodejs-app walk
#   does not depend on a working `npm install` - PackageJson discovery reads the
#   declared `start` script; the probe extractor reads `package.json` directly.
#   If a future fixture needs an actually-installed dep, populate node_modules in
#   the recipe (vendored or `npm ci`'d against a pinned lockfile).
#
# Idempotent: rerunning produces byte-identical output.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(cd "$here/../../../.." && pwd)"
src_root="$here/sources"
out_root="$repo_root/docs/uat-fixtures/build/nodejs-app"

variants=(with-start-script no-start-script malformed-package-json)

mkdir -p "$out_root"
for v in "${variants[@]}"; do
  dst="$out_root/$v"
  rm -rf "$dst"
  mkdir -p "$dst"
  cp -R "$src_root/$v/." "$dst/"
done

find "$out_root" -exec touch -d "2026-01-01T00:00:00Z" {} +

echo "nodejs-app fixtures built at: $out_root"
