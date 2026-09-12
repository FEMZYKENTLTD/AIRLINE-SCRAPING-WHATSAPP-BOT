# Security Policy

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
- Tokens expire after 24 hours (configurable)
- Validates issuer, audience, lifetime, and signing key
- Uses HMAC-SHA256 algorithm

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
