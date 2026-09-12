# Testing Strategy

## Principles

1. **Deterministic.** All tests run offline against the InMemory EF provider
   or temp SQLite files. No external credentials, no network.
2. **CI is the test machine.** Tests run in GitHub Actions on every push
   (see `docs/CI-CD.md`). TRX results are uploaded as artifacts even when a
   run fails.
3. **Runtime proof, not just unit proof.** `PlatformSmokeTests` boots the
   real application host (`WebApplicationFactory<Program>`) and the Docker
   smoke job boots the real container — both without any provider
   credentials.
4. **Every fixed bug gets a regression test** where practical.

## Test projects

| Project | Frameworks | Purpose |
|---------|-----------|---------|
| `WhatsAppBot.Tests` | xUnit 2.6.6, FluentAssertions 6.12, Moq 4.20, EFCore.InMemory 8.0, Mvc.Testing 8.0 | Unit + integration + runtime smoke |

## Test suites

### Database & schema — `Database/DatabaseTests`
Schema creation, relationships, unique channel-identity behavior, session
expiry model, service-request status transitions, audit + AI interaction
log persistence.

### Shared services — `Services/*Tests`

| Suite | Covers |
|-------|--------|
| `UserServiceTests` | find-or-create per (channel, provider), `Created` flag, profile updates, pagination, count |
| `PersistentSessionServiceTests` | get-or-create, 7-day expiry default, touch/refresh, channel isolation, expired cleanup, `ResetAsync` |
| `ConversationServiceTests` | inbound/outbound persistence, provider-message-id idempotency, ordering, counts |
| `ServiceRequestServiceTests` | unique `SR-` codes, New→Processing→Completed, escalate, failure, filters, status counts |
| `AuditServiceTests` | audit entry creation, filtering by action/entity, null-safety |
| `ResilientLlmServiceTests` | deterministic fallback content (never fabricates a booking), null-message handling, `AiInteractionLog` persistence |
| `IntentRouterTests` | deterministic flight-intent routing (intent verb + flight vocabulary, route patterns, IATA pairs, active-flow passthrough, non-flight → AI) |
| `AmadeusProviderTests` | OAuth 401/500, offers 429/empty → null; successful offers → cheapest exact price; pipeline falls back instead of throwing on provider exceptions |
| `PaymentServiceTests` | **never fabricates success**; unconfigured provider → Cancelled + auditable record; provider event transitions (Succeeded/Failed + reasons); input validation |
| `MultiChannelConsistencyTests` | the same shared user/session/message/service-request/audit layer behaves identically for WhatsApp and Telegram (single user per channel identity, channel-isolated sessions, shared idempotency, identical SR lifecycle, one audit trail) |

### Webhook adapters — `Controllers/*Tests`
- **WhatsApp**: Meta verify handshake (accept/reject), HMAC signature
  reject (401 + `WEBHOOK_REJECTED` audit) and accept, duplicate provider
  message → no reprocessing (`WEBHOOK_DUPLICATE` audit, no user lookup, no
  LLM call), full valid-message round-trip (persist inbound + outbound,
  reply via WhatsApp), `FlowContext` persistence round-trip incl. corrupt
  JSON recovery.
- **Telegram**: controller construction, session/enum model invariants.

### Runtime smoke — `Integration/PlatformSmokeTests`
Boots the **real host** (Program.cs) with zero provider credentials and a
temp SQLite database:
- app boots without any external provider credentials
- `GET /health` → 200 Healthy
- `GET /health/ready` → 200 with `database: Connected`
- `GET /` → 200 with endpoint map
- `GET /webhook` verify: wrong token → 401; correct token → challenge echo
- `POST /webhook`: invalid HMAC → 401; valid HMAC → 200
- `POST /telegram`: wrong secret → 401; correct secret → 200
- `GET /api/admin/*` without/with garbage token → 401
- `POST /api/auth/login` with the default placeholder JWT secret → 503
  (a well-known default must never sign tokens)
- full DI graph resolves (scoped validation enabled in the test host)

## Docker smoke (CI)

In addition to the in-process smoke tests, the CI `docker-build-and-smoke`
job runs the actual container image and performs the same endpoint checks
(health, readiness, HMAC reject, Telegram secret reject, admin 401).

## What is NOT covered (honest status)

- **Live airline website scraping/booking automation** — requires real
  external sites, Playwright browsers, and CAPTCHA environments. Status:
  *implemented, external live verification required.* Not run in CI (no
  network dependence by design).
- **Real Amadeus API calls** — CI tests the failure modes and the happy
  path with mocked HTTP; a live sandbox call requires credentials.
- **Real WhatsApp/Telegram delivery** — the outbound senders are covered by
  contract-level tests (payload shape, chunking, chunk labels); actual
  delivery needs a verified Meta app / BotFather bot.
- **Stripe live payments** — only the unconfigured-path safety is tested
  (never fabricates success). Sandbox testing requires a test key.

## Running tests locally (optional)

CI runs everything; local runs are optional:

```bash
dotnet test WhatsAppBot.sln -c Release \
  --logger "console;verbosity=detailed" \
  --logger "trx;LogFileName=test-results.trx"
```
