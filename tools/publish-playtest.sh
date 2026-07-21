#!/bin/sh
cd "$(dirname "$0")/.." || exit 1
exec dotnet run --project tools -- publish-playtest "$@"
