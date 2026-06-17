#!/usr/bin/env bash
# Runs the local-only Ferret.Dev.IntegrationTests project (excluded from Ferret.slnx / CI).
# Needs Docker. Set OPENAI_API_KEY to also run the live OpenAI provider test.
# Extra args are forwarded to `dotnet test`, e.g.:
#   scripts/test-dev.sh --filter "FullyQualifiedName~Ollama"
set -euo pipefail
cd "$(dirname "$0")/.."
exec dotnet test tests/Ferret.Dev.IntegrationTests/Ferret.Dev.IntegrationTests.csproj -c Release "$@"
