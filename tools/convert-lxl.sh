#!/bin/sh
# Thin wrapper; tools must run from the repo root.
cd "$(dirname "$0")/.." || exit 1
exec dotnet run --project tools -- convert-lxl "$@"
