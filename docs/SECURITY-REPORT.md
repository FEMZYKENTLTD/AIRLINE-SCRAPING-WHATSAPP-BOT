# Security Report

## Authentication & Authorization

### JWT Authentication
- Admin API endpoints require JWT Bearer tokens
- Tokens are issued via `/api/auth/login` with username/password
- Token validation: issuer, audience, lifetime, signing key
- Default expiry: 24 hours (configurable via `JWT_EXPIRY_HOURS`)
- Minimum secret length enforced (32+ characters)

### Webhook Security
- **WhatsApp**: HMAC-SHA256 signature verification via `X-Hub-Signature-256` header
  - Uses constant-time comparison to prevent timing attacks
  - Dev mode allows unsigned requests when AppSecret is not configured
- **Telegram**: Secret token validation via `X-Telegram-Bot-Api-Secret-Token` header

### Public vs Protected Endpoints
| Endpoint | Auth Required |
|----------|--------------|
| `GET /` | No (status) |
| `GET /webhook` | No (verification) |
| `POST /webhook` | Signature |
| `POST /telegram` | Secret token |
| `POST /api/auth/login` | No |
| `GET /api/admin/*` | JWT |
| `GET /api/admin/health` | No |

## Secret Management

### External Secrets
All sensitive configuration is loaded from environment variables:
- `WHATSAPP_ACCESS_TOKEN`
- `WHATSAPP_APP_SECRET`
- `TELEGRAM_BOT_TOKEN`
- `AZURE_OPENAI_API_KEY`
- `AMADEUS_CLIENT_SECRET`
- `JWT_SECRET`
- `ADMIN_PASSWORD`

### Source Control
- `.env` is in `.gitignore`
- `.env.example` contains placeholder names only (no real values)
- No secrets committed in source code
- **CRITICAL**: The original `.env.example` contained real WhatsApp credentials. These have been removed and replaced with placeholders.

### Recommendations
- **Rotate immediately**: Any WhatsApp tokens that were in the `.env.example` file in Git history
- Use GitHub repository secrets for CI/CD
- Use a secrets manager for production deployments

## Input Validation

### User Input
- Name validation: regex pattern, length limits (2-100 chars)
- Email validation: regex pattern for standard email format
- IATA codes: exactly 3 alphabetic characters
- Dates: strict `YYYY-MM-DD` format parsing
- Phone numbers: provider-validated format
- Passenger counts: minimum adult requirement, infant-to-adult ratio

### Webhook Payloads
- JSON parsing with try/catch
- Required field extraction with null checks
- Malformed payloads return 200 OK (prevent webhook retries)

## Logging Controls

### Never Logged
- Passwords
- Access tokens
- API keys
- Secret values
- Verification codes
- Payment credentials

### Logged (with redaction)
- Phone numbers (used for session identification)
- Email addresses (used for user identification)
- Message content (truncated for audit)
- API response status codes
- Error messages (without secrets)

## Rate Limiting

Current implementation relies on:
- Provider-level rate limits (Meta, Telegram)
- Database query limits (pagination)
- Message chunking for large responses

Future recommendations:
- Per-user request throttling
- Webhook endpoint rate limiting
- AI endpoint cost control

## Idempotency

- `Message.ProviderMessageId` prevents duplicate message processing
- Database unique constraints prevent duplicate records
- Webhook controllers return 200 OK even for duplicates

## Dependency Security

NuGet packages used:
- Microsoft.AspNetCore.Authentication.JwtBearer 8.0.0
- System.IdentityModel.Tokens.Jwt 7.2.0
- Microsoft.EntityFrameworkCore 8.0.0
- Serilog.AspNetCore 8.0.3

Recommendation: Regularly audit packages with `dotnet list package --vulnerable`

## Known Limitations

1. SQLite doesn't enforce unique constraints in InMemory test provider
2. Rate limiting is not yet implemented at the application level
3. Admin password should use hashed storage in production
4. CORS is not configured (no frontend)
