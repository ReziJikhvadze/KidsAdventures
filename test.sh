#!/bin/zsh
# The reliable full-suite run for this machine. Healthy time: under a minute.
#
# Why this script exists: `dotnet test` with its implicit build deadlocks on this
# machine when orphaned testhost processes from earlier runs are still around —
# the new run sits at 0% CPU forever and looks like a "55-minute test suite".
# The suite itself is ~1350 tests in ~50 seconds. So: sweep the corpses, build
# once, run without the implicit build, and let --blame-hang kill any host that
# stalls instead of waiting on it.
set -e

# 1. Kill test hosts that are doing nothing (0.0% CPU) — corpses from old runs.
for pid in $(ps aux | grep -E "testhost" | grep -v grep | awk '$3 == "0.0" {print $2}'); do
  kill -9 "$pid" 2>/dev/null || true
done

# 2. Build once, explicitly.
dotnet build KidsAdventures.sln

# 3. Run without rebuilding; abort any test host silent for 3 minutes.
dotnet test Tests/Adventrya.Story.Tests --no-build --blame-hang --blame-hang-timeout 3m "$@"
