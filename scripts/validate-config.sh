#!/usr/bin/env bash
# ============================================================================
# scripts/validate-config.sh
#
# Static configuration validation for the repository (NO live credentials
# required, NO network access required):
#
#   1. appsettings.json / appsettings.Development.json are valid JSON
#   2. No duplicated keys within the same JSON object
#   3. Every environment variable READ by the code is documented in
#      .env.example (with a small allow-list for ASP.NET Core framework vars)
#   4. Every variable DOCUMENTED in .env.example is actually read by the code
#      (detects stale/obsolete variables)
#   5. No variable names collide under different configuration mappings
#
# Exit code 0 = valid, 1 = problems found.
# ============================================================================
set -uo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

FAIL=0
FAIL_STEP() { echo "❌ FAIL: $1"; FAIL=1; }
OK()        { echo "✅ $1"; }

echo "=============================================="
echo " Configuration validation"
echo "=============================================="

# ── 1+2) JSON validity and duplicate keys ───────────────────────────────────
python3 - <<'PYEOF'
import json, sys

def walk(node, path, errors):
    if isinstance(node, dict):
        seen = {}
        for k, v in node.items():
            if k in seen:
                errors.append(f"duplicate key '{k}' at {path or '/'}")
            seen[k] = True
            walk(v, f"{path}/{k}", errors)
    elif isinstance(node, list):
        for i, v in enumerate(node):
            walk(v, f"{path}[{i}]", errors)

ok = True
for f in ["WhatsAppBot/appsettings.json", "WhatsAppBot/appsettings.Development.json"]:
    try:
        with open(f) as fh:
            data = json.load(fh)
    except FileNotFoundError:
        continue
    except json.JSONDecodeError as e:
        print(f"❌ FAIL: {f} is not valid JSON: {e}")
        ok = False
        continue
    errors = []
    walk(data, "", errors)
    if errors:
        for e in errors:
            print(f"❌ FAIL: {f}: {e}")
        ok = False
    else:
        print(f"✅ {f}: valid JSON, no duplicate keys")

sys.exit(0 if ok else 1)
PYEOF
[ $? -eq 0 ] || FAIL=1

# ── 3+4) env var sync: code <-> .env.example ────────────────────────────────
FRAMEWORK_ALLOWLIST='^ASPNETCORE_'

# Variables read in code: Env("X") helper + GetEnvironmentVariable("X")
mapfile -t CODE_VARS < <(
    {
        grep -rhoE 'Env\("[A-Z0-9_]+"\)' WhatsAppBot/Program.cs 2>/dev/null | sed -E 's/Env\("([A-Z0-9_]+)"\)/\1/'
        grep -rhoE 'GetEnvironmentVariable\("[A-Z0-9_]+"\)' WhatsAppBot/ --include='*.cs' 2>/dev/null | sed -E 's/GetEnvironmentVariable\("([A-Z0-9_]+)"\)/\1/'
    } | sort -u
)

# Variables documented in .env.example (KEY=value lines, non-comment)
mapfile -t EXAMPLE_VARS < <(
    grep -vE '^\s*(#|$)' .env.example 2>/dev/null | cut -d'=' -f1 | sed -E 's/[[:space:]]+$//' | sort -u
)

if [ ! -f .env.example ]; then
    FAIL_STEP ".env.example is missing"
else
    MISSING_IN_EXAMPLE=0
    for v in "${CODE_VARS[@]}"; do
        if ! printf '%s\n' "${EXAMPLE_VARS[@]}" | grep -qx "$v" && ! echo "$v" | grep -qE "$FRAMEWORK_ALLOWLIST"; then
            echo "❌ FAIL: '$v' is read in code but not documented in .env.example"
            MISSING_IN_EXAMPLE=1
            FAIL=1
        fi
    done
    [ "$MISSING_IN_EXAMPLE" -eq 0 ] && OK "all code-read env vars are documented in .env.example"

    STALE=0
    for v in "${EXAMPLE_VARS[@]}"; do
        if ! printf '%s\n' "${CODE_VARS[@]}" | grep -qx "$v" && ! echo "$v" | grep -qE "$FRAMEWORK_ALLOWLIST"; then
            echo "❌ FAIL: '$v' is documented in .env.example but never read by the code (stale variable)"
            STALE=1
            FAIL=1
        fi
    done
    [ "$STALE" -eq 0 ] && OK "no stale/obsolete env vars in .env.example"

    # duplicate env var names in .env.example
    DUP=$(grep -vE '^\s*(#|$)' .env.example | cut -d'=' -f1 | sed -E 's/[[:space:]]+$//' | sort | uniq -d)
    if [ -n "$DUP" ]; then
        echo "❌ FAIL: duplicated env var names in .env.example:"
        echo "$DUP"
        FAIL=1
    else
        OK "no duplicated env var names in .env.example"
    fi
fi

# ── 5) no env var mapped to two different configuration keys ───────────────
DUP_MAPPINGS=$(grep -rhoE 'Env\("[A-Z0-9_]+"\)\s*\?\?\s*builder\.Configuration\[[^]]+\]' WhatsAppBot/Program.cs 2>/dev/null \
    | sed -E 's/Env\("([A-Z0-9_]+)"\).*Configuration\["([^"]+)"\]/\1 => \2/' \
    | sort | awk -F' => ' '{print $1}' | sort | uniq -d)
if [ -n "$DUP_MAPPINGS" ]; then
    echo "❌ FAIL: the same env var is mapped to multiple configuration keys:"
    echo "$DUP_MAPPINGS"
    FAIL=1
else
    OK "no conflicting env var → config key mappings"
fi

echo "=============================================="
if [ "$FAIL" -eq 0 ]; then
    echo " ✅ Configuration validation PASSED"
else
    echo " ❌ Configuration validation FAILED"
fi
echo "=============================================="

exit "$FAIL"
