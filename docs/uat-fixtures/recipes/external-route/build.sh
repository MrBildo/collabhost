#!/usr/bin/env bash
# Build the external-route UAT fixture into docs/uat-fixtures/build/external-route/.
#
# This recipe writes the side-process working directory only. The operator launches
# the side-process explicitly before registration:
#
#   cd docs/uat-fixtures/build/external-route/localhost-http
#   python3 -m http.server 11235
#
# `python -m http.server` serves files by literal name; the `health` file (no
# extension) returns 200 with the file body for `GET /health`.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(cd "$here/../../../.." && pwd)"
src_root="$here/sources"
out_root="$repo_root/docs/uat-fixtures/build/external-route"

mkdir -p "$out_root"
rm -rf "$out_root/localhost-http"
mkdir -p "$out_root/localhost-http"
cp "$src_root/localhost-http/index.html" "$out_root/localhost-http/index.html"
cp "$src_root/localhost-http/health" "$out_root/localhost-http/health"

find "$out_root" -exec touch -d "2026-01-01T00:00:00Z" {} +

echo "external-route fixture built at: $out_root/localhost-http"
echo "ready to launch with: (cd $out_root/localhost-http && python3 -m http.server 11235)"
