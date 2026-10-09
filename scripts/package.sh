#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="0.1.0"
stage_dir="$(mktemp -d)"
trap 'rm -rf "$stage_dir"' EXIT

if ! command -v dotnet >/dev/null 2>&1; then
  printf 'The .NET SDK is required to build this plugin.\n' >&2
  exit 1
fi
if ! command -v zip >/dev/null 2>&1; then
  printf 'The zip utility is required to create the Thunderstore package.\n' >&2
  exit 1
fi

dotnet build "$root_dir/SubspaceTransport.csproj" --configuration Release

mkdir -p "$stage_dir/BepInEx/plugins"
cp "$root_dir/bin/Release/net472/SubspaceTransport.dll" "$stage_dir/BepInEx/plugins/"
cp "$root_dir/manifest.json" "$root_dir/README.md" "$root_dir/icon.png" "$stage_dir/"

mkdir -p "$root_dir/dist"
(
  cd "$stage_dir"
  zip -qr "$root_dir/dist/SubspaceTransport-$version.zip" .
)
printf 'Created %s\n' "$root_dir/dist/SubspaceTransport-$version.zip"
