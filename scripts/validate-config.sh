#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Configuration validation (CI, no real secrets required)
#
# Detects:
#   • .env or other secret files accidentally tracked in Git
#   • database files accidentally tracked in Git
#   • malformed appsettings JSON
#   • missing required configuration sections in appsettings.json
#   • environment variables used by Program.cs that are missing from .env.example
#   • environment variables in .env.example that Program.cs never reads (stale)
#   • real-looking secret values committed in appsettings.json
#
# Exit code: 0 = pass, 1 = fail. A report is written to artifacts/security/.
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
REPORT_DIR="artifacts/security"
mkdir -p "$REPORT_DIR"
REPORT="$REPORT_DIR/config-validation-report.txt"

PASS=0
FAIL=0

pass() { echo "  ✔ $1"; PASS=$((PASS+1)); }
fail() { echo "  ✖ $1"; FAIL=$((FAIL+1)); }

echo "═══ CONFIGURATION VALIDATION ═══"
echo

# ── 1. No secret / local files tracked in Git ───────────────────────────────
echo "[1] Checking that no local secret/data files are tracked in Git..."
TRACKED_BAD=$(git ls-files | grep -E '^\.env$|(^|/)\.env\.(local|prod|production|staging)$|\.env\.([^.]*\.)?db$|\.db$|\.sqlite$|\.sqlite3$|id_rsa|\.pem$|\.pfx$|\.key$' | grep -v '^\.env\.example$' || true)
if [ -z "$TRACKED_BAD" ]; then
  pass "No .env / database / key files tracked in Git"
else
  fail "The following sensitive-looking files are tracked in Git:"
  echo "$TRACKED_BAD" | sed 's/^/      /'
fi

# ── 2. appsettings JSON must be valid ───────────────────────────────────────
echo
echo "[2] Validating appsettings JSON files..."
for f in WhatsAppBot/appsettings.json WhatsAppBot/appsettings.Development.json; do
  if [ -f "$f" ]; then
    if python3 -c "import json,sys; json.load(open('$f'))" 2>/dev/null; then
      pass "$f is valid JSON"
    else
      fail "$f is NOT valid JSON"
    fi
  fi
done

# ── 3. Required configuration sections present ──────────────────────────────
echo
echo "[3] Checking required configuration sections in appsettings.json..."
python3 - <<'PYEOF'
import json, sys

with open("WhatsAppBot/appsettings.json") as f:
    cfg = json.load(f)

required_sections = {
    "MetaWhatsApp": ["GraphBase", "ApiVersion", "PhoneNumberId", "AccessToken", "VerifyToken", "AppSecret"],
    "Telegram": ["BotToken", "WebhookSecret"],
    "AzureOpenAI": ["Endpoint", "Deployment", "ApiKey", "ApiVersion"],
    "Amadeus": ["Enabled", "BaseUrl", "ClientId", "ClientSecret"],
    "Jwt": ["Secret", "Issuer", "Audience", "ExpiryHours"],
    "Admin": ["Username", "Password"],
    "Session": ["TimeoutMinutes", "CleanupIntervalMinutes"],
    "Scraping": ["Enabled", "Airlines"],
    "FlightPricing": ["EnableAmadeus", "EnableDeepLinkFallback", "EnableScrapeFallback"],
    "LLM": ["MaxTokens", "Temperature", "MaxHistoryMessages"],
}

errors = 0
for section, keys in required_sections.items():
    if section not in cfg:
        print(f"  ✖ Missing section: {section}")
        errors += 1
        continue
    for key in keys:
        if key not in cfg[section]:
            print(f"  ✖ Missing key: {section}:{key}")
            errors += 1

if errors == 0:
    print("  ✔ All required configuration sections/keys are declared")
sys.exit(1 if errors else 0)
PYEOF
if [ $? -eq 0 ]; then PASS=$((PASS+1)); else FAIL=$((FAIL+1)); fi

# ── 4. Program.cs ↔ .env.example consistency ────────────────────────────────
echo
echo "[4] Checking Program.cs ↔ .env.example consistency..."
# Env vars consumed by the application
USED_VARS=$(grep -oE 'Env\("[A-Z0-9_]+"\)' WhatsAppBot/Program.cs | sed -E 's/Env\("([A-Z0-9_]+)"\)/\1/' | sort -u)
MISSING_IN_EXAMPLE=0
for var in $USED_VARS; do
  if ! grep -qE "^${var}=" .env.example; then
    fail ".env.example is missing variable used by Program.cs: $var"
    MISSING_IN_EXAMPLE=1
  fi
done
[ "$MISSING_IN_EXAMPLE" = "0" ] && pass "Every env var used by Program.cs is documented in .env.example"

# Stale vars in .env.example that Program.cs never reads (informational list)
EXAMPLE_VARS=$(grep -oE '^[A-Z0-9_]+=' .env.example | sed 's/=$//' | sort -u)
STALE=0
for var in $EXAMPLE_VARS; do
  case "$var" in
    ASPNETCORE_ENVIRONMENT|ASPNETCORE_URLS) continue ;;  # runtime, not app code
  esac
  if ! echo "$USED_VARS" | grep -qx "$var" && \
     ! grep -rqE "\"$var" WhatsAppBot/ --include="*.cs" && \
     ! grep -rqE "GetEnvironmentVariable\(\"$var\"\)" WhatsAppBot/ --include="*.cs"; then
    echo "  ⚠ .env.example declares '$var' which is not referenced by application code (review or remove)"
    STALE=1
  fi
done
[ "$STALE" = "0" ] && pass "No stale variables detected in .env.example"

# ── 5. appsettings must not contain real-looking secrets ────────────────────
echo
echo "[5] Checking appsettings.json for real-looking secret values..."
SECRET_LEAK=$(python3 - <<'PYEOF'
import json, re

with open("WhatsAppBot/appsettings.json") as f:
    cfg = json.load(f)

secret_keys = ["AccessToken", "ApiKey", "AppSecret", "BotToken", "WebhookSecret",
               "ClientId", "ClientSecret", "Secret", "Password"]
PLACEHOLDERS = ("CHANGE_ME", "REPLACE_ME", "xxx", "your-", "your_")
leaks = []

def walk(section, node):
    if isinstance(node, dict):
        for k, v in node.items():
            walk(f"{section}.{k}" if section else k, v)
    elif isinstance(node, str):
        base = section.split(".")[-1]
        if base in secret_keys and node.strip():
            if not any(p in node.upper() for p in PLACEHOLDERS):
                leaks.append(section)

walk("", cfg)
if leaks:
    print("\n".join(leaks))
PYEOF
)
if [ -z "$SECRET_LEAK" ]; then
  pass "No real-looking secrets committed in appsettings.json"
else
  fail "Possible real secrets committed in appsettings.json (values must be empty or placeholders):"
  echo "$SECRET_LEAK" | sed 's/^/      /'
fi

# ── Summary ─────────────────────────────────────────────────────────────────
echo
echo "═══ RESULT: $PASS passed, $FAIL failed ═══"
{
  echo "Configuration Validation Report"
  echo "Date: $(date -u '+%Y-%m-%d %H:%M:%S UTC')"
  echo "Commit: $(git rev-parse HEAD 2>/dev/null || echo unknown)"
  echo
  echo "PASS=$PASS FAIL=$FAIL"
} > "$REPORT"

[ "$FAIL" = "0" ] || exit 1
echo "Configuration validation passed."
