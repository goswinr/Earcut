#!/bin/bash
# SessionStart hook for Claude Code on the web.
#
# The remote container ships with Node but no .NET SDK, so without this every
# session starts unable to build the library or regenerate Test/test.js's
# dependency (Src/Earcut.fs.js, the Fable output). This installs the SDK, the
# pinned local tools (Fable, fsdocs) and transpiles Src to JS.
#
# Runs asynchronously: the session starts immediately while this works in the
# background. See .claude/settings.json for registration.
set -euo pipefail

echo '{"async": true, "asyncTimeout": 900000}'

# Local machines are expected to have their own .NET setup.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
cd "$PROJECT_DIR"

DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Persist for the session's own shells, so `dotnet` is on PATH for the agent too.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$DOTNET_ROOT\""
    echo "export PATH=\"$DOTNET_ROOT:$DOTNET_ROOT/tools:\$PATH\""
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
    echo 'export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1'
  } >> "$CLAUDE_ENV_FILE"
fi

# .NET SDK 10, matching .github/workflows/test.yml. Idempotent: dotnet-install.sh
# skips the download when the requested SDK band is already present, but checking
# first keeps warm-container startups from touching the network at all.
if ! "$DOTNET_ROOT/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "Installing .NET SDK 10 into $DOTNET_ROOT ..."
  curl -fsSL --retry 3 --retry-delay 2 https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_ROOT" --no-path
  rm -f /tmp/dotnet-install.sh
fi

# Pinned Fable + fsdocs from .config/dotnet-tools.json.
dotnet tool restore

# NuGet restore up front so the first `dotnet build` of the session is offline-fast.
dotnet restore

# Test/test.js imports ../Src/Earcut.fs.js, which only exists once Fable has run.
dotnet fable

echo "Session setup complete: $("$DOTNET_ROOT/dotnet" --version), $(node --version)"
