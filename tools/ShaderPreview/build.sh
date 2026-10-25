#!/usr/bin/env bash
# Cross-compiles the shared HLSL atmosphere/cloud core to GLSL ES 3.0 for the browser preview.
# Requires glslangValidator and spirv-cross (apt: glslang-tools spirv-cross).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="${1:-$here/out}"
mkdir -p "$out"
glslangValidator -D -V -S frag -e main --auto-map-bindings --auto-map-locations "$here/preview.hlsl" -o "$out/preview.spv"
spirv-cross "$out/preview.spv" --es --version 300 --flatten-ubo --output "$out/preview.frag"
spirv-cross "$out/preview.spv" --reflect --output "$out/preview.json"
echo "compiled -> $out/preview.frag"
