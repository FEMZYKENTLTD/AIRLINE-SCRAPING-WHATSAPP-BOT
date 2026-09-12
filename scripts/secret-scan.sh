#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Secret scanner (CI)
#
# Scans every tracked file in the repository for likely committed secrets:
#   • private key blocks
#   • AWS access/secret keys
#   • GitHub tokens (ghp_/gho_/github_pat_)
#   • Slack tokens
#   • Stripe live keys
#   • Telegram bot tokens (digits:35-char-token)
#   • raw JWTs (eyJ... segments)
#   • well-known secret variable names with long assigned values
#
# Allow-listed: empty values and documented placeholders (CHANGE_ME_*,
# REPLACE_ME, "your*"/"YOUR*", example/placeholder text, "ci-smoke-test-*",
# URLs and connection-string values) so .env.example and docs keep working.
#
# NOTE: historical exposure of a WhatsApp token in old Git history is NOT
# caught here (this scans the current tree only). That exposure is documented
# in docs/SECURITY.md and the token still must be rotated.
#
# Exit code: 0 = clean, 1 = potential secrets found.
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
REPORT_DIR="artifacts/security"
mkdir -p "$REPORT_DIR"
REPORT="$REPORT_DIR/secret-scan-report.txt"
RAW="$(mktemp)"
FILTERED="$(mktemp)"

echo "═══ SECRET SCAN (current Git tree) ═══"

FILE_LIST=$(git ls-files)
trap 'rm -f "$RAW" "$FILTERED"' EXIT

# Uses PCRE (grep -P) so inline (?i) flags work. Available on GitHub runners.
scan_grep() {
  local label="$1" pattern="$2"
  local hits
  hits=$(echo "$FILE_LIST" | xargs -r grep -nIPH "$pattern" 2>/dev/null || true)
  if [ -n "$hits" ]; then
    {
      echo "── $label ──"
      echo "$hits"
      echo
    } >> "$RAW"
  fi
}

# 1. Private key material
scan_grep "Private key blocks" \
  "-----BEGIN (RSA|EC|DSA|OPENSSH|PGP|ENCRYPTED) ?PRIVATE KEY"

# 2. AWS credentials
scan_grep "AWS access key IDs" \
  "AKIA[0-9A-Z]{16}"
scan_grep "AWS secret assignments" \
  "(?i)aws_secret_access_key[\"'=[:space:]]+[A-Za-z0-9/+=]{40}"

# 3. GitHub / CI tokens
scan_grep "GitHub tokens" \
  "(ghp_|gho_|ghu_|ghs_|ghr_|github_pat_)[A-Za-z0-9]{20,}"

# 4. Slack tokens
scan_grep "Slack tokens" \
  "xox[baprs]-[A-Za-z0-9-]{10,}"

# 5. Stripe live keys
scan_grep "Stripe live keys" \
  "(sk_live_|rk_live_|pk_live_)[A-Za-z0-9]{10,}"

# 6. Telegram bot tokens: 8-10 digits, colon, 35 chars [A-Za-z0-9_-]
scan_grep "Telegram bot token pattern" \
  "[0-9]{8,10}:[A-Za-z0-9_-]{35}"

# 7. Raw JWTs (three base64url segments)
scan_grep "Raw JWT tokens" \
  "eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}"

# 8. Well-known secret variable names with long, non-placeholder values.
#    Catches lines like:  JWT_SECRET=<real>,  ADMIN_PASSWORD=<real>,
#    WHATSAPP_ACCESS_TOKEN=<real>,  "ApiKey": "real",  etc.
scan_grep "Secret-named assignments" \
  "(?i)(whatsapp_access_token|whatsapp_app_secret|telegram_bot_token|telegram_webhook_secret|azure_openai_api_key|azure_speech_key|amadeus_client_secret|stripe_secret_key|stripe_webhook_secret|captcha_api_key|jwt_secret|admin_password|\"?api_?key\"?|\"?access_?token\"?|\"?app_?secret\"?|\"?bot_?token\"?|\"?webhook_?secret\"?|\"?client_?secret\"?|\"?secret\"?|\"?password\"?)[:=][[:space:]]*\"?[A-Za-z0-9+/=_\\-]{16,}"

# ── Filter documented placeholders / obvious non-secrets ────────────────────
ALLOWLIST='CHANGE_ME|REPLACE_ME|placeholder|your[_-]?[A-Za-z0-9_-]*|YOUR[_-]?[A-Za-z0-9_-]*|example[_-]?(token|secret|key|password|string|value)?|ci-smoke-test|Data Source=|https?://[^ ]*|your_generated_secret|generated_secret|strong_password|random_string'
if [ -s "$RAW" ]; then
  grep -vE "$ALLOWLIST" "$RAW" | awk '
    /^── .* ──$/ { if (pending && kept) print buf; pending=1; kept=0; buf=$0"\n"; next }
    { if (pending) { buf = buf $0 "\n"; if ($0 !~ /^[[:space:]]*$/) kept=1 } }
    END { if (pending && kept) print buf }
  ' > "$FILTERED"
else
  : > "$FILTERED"
fi

# Count remaining finding categories (label lines, not echoed hit lines)
if [ -s "$FILTERED" ]; then
  # Keep only category headers and their following hit lines; drop empties later
  REMAINING_CATEGORIES=$(grep -cE '^── .* ──$' "$FILTERED" || true)
else
  REMAINING_CATEGORIES=0
fi

echo
echo "── Summary ──"
{
  echo "Secret Scan Report"
  echo "Date: $(date -u '+%Y-%m-%d %H:%M:%S UTC')"
  echo "Commit: $(git rev-parse HEAD 2>/dev/null || echo unknown)"
  echo "Scanned files: $(echo "$FILE_LIST" | wc -l)"
  echo "Raw hits: $( [ -s "$RAW" ] && wc -l < "$RAW" || echo 0 )"
  echo "Findings after allow-list filter: $REMAINING_CATEGORIES"
  echo
  echo "NOTE: A WhatsApp access token was exposed in an older commit (Phase 1 finding)."
  echo "It has been removed from the current tree; the token still requires rotation."
  echo "See docs/SECURITY.md."
} > "$REPORT"

if [ "$REMAINING_CATEGORIES" -gt 0 ]; then
  cat "$FILTERED"
  cat "$FILTERED" >> "$REPORT"
  echo "✖ Potential secrets detected. Review the report: $REPORT"
  exit 1
fi

echo "✔ No committed secrets detected in the current tree (after allow-list filter)."
echo "✔ Report written to $REPORT"
exit 0
