#!/usr/bin/env bash
# ============================================================================
# scripts/scan-secrets.sh
#
# High-confidence secret scanner for the repository's CURRENT tree.
#
# Scans for:
#   - GitHub / Slack / Google / AWS / Stripe tokens
#   - Private key blocks (RSA/EC/OPENSSH/PGP)
#   - Meta/WhatsApp access tokens (EAAG... format)
#   - Telegram bot tokens (digits:token format)
#   - Signed JWTs committed to source
#   - Generic key=value assignments with long high-entropy-looking values
#   - Tracked .env files and database files
#
# Allow-listing keeps false positives low:
#   - .env.example is skipped (must contain no values)
#   - lines containing CHANGE_ME / example / <placeholder> are skipped
#   - comment lines are skipped
#
# Exit code 0 = clean, 1 = findings.
# ============================================================================
set -uo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

FAIL=0

echo "=============================================="
echo " Secret scan (current tree)"
echo "=============================================="

# ── 1) .env must never be tracked ───────────────────────────────────────────
if git ls-files --error-unmatch -- .env >/dev/null 2>&1; then
    echo "❌ FAIL: '.env' is tracked in Git. Remove it: git rm --cached .env"
    FAIL=1
else
    echo "✅ .env is not tracked"
fi

# ── 2) Database files must never be tracked ─────────────────────────────────
DB_FILES="$(git ls-files | grep -E '\.(db|sqlite|sqlite3)$' || true)"
if [ -n "$DB_FILES" ]; then
    echo "❌ FAIL: database files tracked in Git:"
    echo "$DB_FILES"
    FAIL=1
else
    echo "✅ No database files tracked"
fi

# ── 3) Pattern scan over tracked text files ─────────────────────────────────
# Collect tracked files, excluding binaries and the (empty-values) example file.
mapfile -t FILES < <(git ls-files | grep -vE '\.(png|jpe?g|gif|ico|pdf|zip|nupkg|snupkg|db|sqlite3?|wasm|dll|exe|so|dylib|pyc|class)(-shm|-wal)?$' | grep -v '^\.env\.example$' || true)

# Regexes. Each entry: "label|pattern"
PATTERNS=(
    "GitHub personal access token|ghp_[A-Za-z0-9]{36,}"
    "GitHub fine-grained token|github_pat_[A-Za-z0-9_]{22,}"
    "GitHub OAuth/refresh token|gh[ousr]_[A-Za-z0-9]{36,}"
    "Slack token|xox[baprs]-[A-Za-z0-9-]{10,}"
    "Google API key|AIza[0-9A-Za-z_-]{35}"
    "AWS access key ID|AKIA[0-9A-Z]{16}"
    "Stripe live secret key|sk_live_[A-Za-z0-9]{10,}"
    "Stripe live restricted key|rk_live_[A-Za-z0-9]{10,}"
    "Stripe live publishable key|pk_live_[A-Za-z0-9]{10,}"
    "Private key block|-----BEGIN [A-Z ]*PRIVATE KEY( BLOCK)?-----"
    "Meta/WhatsApp access token|EAAG[A-Za-z0-9]{30,}"
    "Telegram bot token|[0-9]{6,12}:[A-Za-z0-9_-]{35,}"
    "Committed signed JWT|eyJ[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{8,}"
    "Generic credential assignment|((api[_-]?key|apikey|secret|token|password|passwd|pwd|client[_-]?secret|access[_-]?key)[\"']?\s*[:=]\s*[\"']?[A-Za-z0-9+/_=.-]{24,})"
)

ALLOWLIST_RE='CHANGE_ME|change_me|<[^>]+>|__[^_]+__|%s|\$\{|\$\(|sample|Sample|SAMPLE|Example|EXAMPLE|example|placeholder|PLACEHOLDER|xxxx|XXXX|dummy|Dummy|DO_NOT_USE|do_not_use'

for entry in "${PATTERNS[@]}"; do
    label="${entry%%|*}"
    pattern="${entry#*|}"

    matches="$(grep -rInE "$pattern" -- "${FILES[@]}" 2>/dev/null || true)"
    [ -z "$matches" ] && continue

    while IFS= read -r line; do
        [ -z "$line" ] && continue
        # grep -rInE output format: file:lineno:content
        file="${line%%:*}"
        rest="${line#*:}"
        lineno="${rest%%:*}"
        content="${rest#*:}"
        [ "$lineno" = "$rest" ] && content="$rest"

        # Allow-list: placeholder-ish lines
        if echo "$content" | grep -qE "$ALLOWLIST_RE"; then
            continue
        fi
        # Allow-list: comment-only lines
        if echo "$content" | grep -qE '^\s*(//|#|<!--|/\*|\*)'; then
            continue
        fi

        # Redact the matched value: keep the key, mask the value
        masked="$(echo "$content" | sed -E "s/($pattern)/[REDACTED]/" | cut -c1-160)"
        echo "❌ FAIL: possible $label in $file (line $lineno)"
        echo "        $masked"
        FAIL=1
    done <<< "$matches"
done

if [ "$FAIL" -eq 0 ]; then
    echo "=============================================="
    echo " ✅ Secret scan PASSED — no likely secrets found"
    echo "=============================================="
else
    echo "=============================================="
    echo " ❌ Secret scan FAILED — review findings above"
    echo "    If a finding is a confirmed real secret: rotate it immediately"
    echo "    and add a targeted allow-list comment for any known false positive."
    echo "=============================================="
fi

exit "$FAIL"
