# Security Policy

## Phase 2 hardening summary (2026-09)

- **CI secret scanning** on every push (`scripts/secret-scan.sh`): pattern
  scan of the entire tracked tree for private keys, cloud/CI/Slack/Stripe/
  Telegram credentials, raw JWTs, and secret-named assignments. Documented
  placeholders are allow-listed; anything else fails the pipeline.
- **CI config validation** on every push (`scripts/validate-config.sh`):
  no `.env`/DB/key files tracked, required config sections present,
  `.env.example` ↔ `Program.cs` variable consistency, no real-looking
  values in `appsettings.json`.
- **Well-known JWT default secret is rejected**: `appsettings.json` ships a
  placeholder `Jwt:Secret`. If the configured secret is missing, shorter
  than 32 characters, or equal to the placeholder, authentication is
  disabled entirely (every `[Authorize]` endpoint returns 401) and
  `POST /api/auth/login` returns **503** — a forgotten rotation can never
  result in a publicly-known signing key.
- **Constant-time credential comparison** in the admin login endpoint
  (`CryptographicOperations.FixedTimeEquals`) to avoid user enumeration
  via response timing.
- **Security audit events**: `WEBHOOK_REJECTED` (bad HMAC / bad Telegram
  secret), `WEBHOOK_DUPLICATE`, `USER_CREATED`,
  `SERVICE_REQUEST_ESCALATED`, `AI_REQUEST`/`AI_FAILURE` (via
  `AiInteractionLog`) — all in the `AuditLog` table.
- **HMAC verification uses constant-time comparison** and runs before any
  payload is processed; rejected payloads are audited with the client IP.
- **Optional providers never block startup**: WhatsApp/Telegram/Azure
  OpenAI/Amadeus/Stripe are all optional. Missing credentials produce
  logged warnings and clean runtime fallbacks — the platform (and its
  security checks) remain fully operational.

## Secret Management

### Local Development
1. Copy `.env.example` to `.env`
2. Fill in real values only in `.env`
3. `.env` is in `.gitignore` — never commit it
4. Never paste secrets into chat, logs, or documentation

### Production
Use a secrets manager:
- Azure Key Vault
- AWS Secrets Manager
- HashiCorp Vault

### GitHub Actions
Store secrets in: Repository → Settings → Secrets and variables → Actions

---

## Credential Rotation

### When to Rotate
- Immediately if credentials were exposed in Git history
- Periodically (every 90 days recommended)
- When team members leave
- After security incidents

### How to Rotate
1. Generate new credentials at the provider
2. Update `.env` (local) and GitHub secrets (CI/CD)
3. Restart the application
4. Verify functionality
5. Revoke old credentials at the provider

---

## Exposed Credentials Response

**The repository previously contained real WhatsApp credentials in the `.env.example` file.**

These credentials were committed in the repository's Git history at commit `f52164c`.

### Required Actions
1. **IMMEDIATELY** go to Facebook Developer Portal
2. Navigate to your app → WhatsApp → API Setup
3. Generate a **new** access token
4. The old token is considered **compromised**
5. Update your `.env` and GitHub secrets with the new token
6. Consider setting a new verify token as well

---

## Webhook Security

### WhatsApp
- HMAC-SHA256 signature verification via `X-Hub-Signature-256` header
- Uses constant-time comparison (`CryptographicOperations.FixedTimeEquals`)
- Dev mode allows unsigned requests when AppSecret is not configured

### Telegram
- Secret token validation via `X-Telegram-Bot-Api-Secret-Token` header
- Optional — only enforced when `TELEGRAM_WEBHOOK_SECRET` is set

---

## JWT Security

- Minimum secret length: 32 characters
- **The well-known default/placeholder secret is treated as unconfigured**
  (authentication disabled + login 503) — see Phase 2 summary above
- Tokens expire after 24 hours (configurable)
- Validates issuer, audience, lifetime, and signing key
- Uses HMAC-SHA256 algorithm
- Login compares username and password in constant time

### Production Recommendations
- Use asymmetric keys (RSA) for JWT in production
- Implement token refresh mechanism
- Add rate limiting to authentication endpoint

---

## Admin Security

- Admin endpoints require JWT authentication
- `[Authorize]` attribute protects all admin routes
- Admin credentials stored as environment variables
- Health endpoint is public (no sensitive data exposed)

---

## Logging Policy

### Never Logged
- Passwords
- Access tokens
- API keys
- JWT secrets
- Webhook secrets
- Payment credentials
- Verification codes

### Logged (with care)
- Phone numbers (for session identification)
- Email addresses (for user identification)
- Message content (truncated for audit)
- API response codes
- Error messages (without secrets)

---

## Database Security

- SQLite file stored locally (not exposed via API)
- EF Core parameterized queries (no SQL injection)
- Cascade deletes configured appropriately
- Unique constraints prevent duplicate records

---

## Dependency Security

Audit packages periodically:
```bash
dotnet list package --vulnerable
```

---

## Rate Limiting (Recommended for Production)

Not yet implemented at the application level. Recommended additions:
- Per-user webhook rate limiting
- Admin endpoint rate limiting
- AI endpoint cost control
- Login attempt throttling
