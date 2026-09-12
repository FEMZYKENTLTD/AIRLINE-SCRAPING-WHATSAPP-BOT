# Feature Matrix — Truthful Status

Status legend:
- **CI VERIFIED** — built, tested, and runtime-checked in GitHub Actions.
- **CI VERIFIED — EXTERNAL PROVIDER TEST REQUIRED** — the code path is
  verified in CI with mocked/absent providers; a live call with real
  credentials is still required to certify the integration end-to-end.
- **STATICALLY VERIFIED** — code reviewed; no automated runtime proof yet.
- **NOT READY** — incomplete or blocked.

## Core platform

| Feature | Status | Evidence |
|---------|--------|----------|
| Multi-channel architecture (shared services) | CI VERIFIED | `MultiChannelConsistencyTests` — WhatsApp & Telegram through one user/session/message/SR/audit layer |
| WhatsApp webhook (verify + HMAC) | CI VERIFIED | `WhatsAppWebhookTests` (401 on bad HMAC, 200 on valid), Docker smoke 401 check |
| Telegram webhook (secret token) | CI VERIFIED | `PlatformSmokeTests` + Docker smoke (401 on wrong secret) |
| Single user, multiple channel identities | CI VERIFIED | `MultiChannelConsistencyTests`, unique index `{Channel, ProviderUserId}` |
| Persistent DB sessions (7-day expiry, configurable) | CI VERIFIED | `PersistentSessionServiceTests`, `SessionCleanupService` on the persistent store |
| Restart-safe conversation flow (flight state survives restart) | CI VERIFIED | `FlowContext` round-trip test (incl. corrupt-JSON recovery) |
| Message persistence with provider IDs | CI VERIFIED | `ConversationServiceTests` |
| Webhook idempotency (duplicate delivery) | CI VERIFIED | `WhatsAppWebhookTests` duplicate test — no reprocessing, `WEBHOOK_DUPLICATE` audit |
| Service request lifecycle (New→Processing→Completed + Escalated/Failed/Cancelled) | CI VERIFIED | `ServiceRequestServiceTests`, `MultiChannelConsistencyTests` |
| Human agent escalation (/agent on both channels) | CI VERIFIED (logic) — EXTERNAL PROVIDER TEST REQUIRED (notification to agents) | SR escalation + audit tested; no agent notification channel exists yet |
| Audit logging (no secrets logged) | CI VERIFIED | `AuditServiceTests` + audit events in webhook tests |
| AI interaction logging (AI_REQUEST/AI_FAILURE) | CI VERIFIED | `AiInteractionLog` persisted by `ResilientLlmService` |
| Admin API + JWT | CI VERIFIED | `PlatformSmokeTests` (401 without/garbage token, 503 with placeholder secret); live token flow = EXTERNAL PROVIDER TEST REQUIRED |
| `/health` + `/health/ready` (no external creds needed) | CI VERIFIED | in-process + Docker smoke |
| Docker image (multi-stage, non-root, HEALTHCHECK) | CI VERIFIED | `docker-build-and-smoke` job |
| EF Core migrations | NOT READY — `EnsureCreated()` in use; migration tooling planned | `AppDbContextFactory` in place; no `Migrations/` yet |

## Airline / travel (domain preserved)

| Feature | Status | Evidence |
|---------|--------|----------|
| Flight search conversation (numbered flow: airline→mode→route→dates→pax→confirm) | CI VERIFIED (flow logic) — EXTERNAL PROVIDER TEST REQUIRED (live pricing) | flow preserved 1:1 from Phase 1; pricing mocked in tests |
| Free-text flight intent → shared flight service (both channels) | CI VERIFIED (routing) | `IntentRouterTests`, wired in both controllers |
| Amadeus API pricing | CI VERIFIED — EXTERNAL PROVIDER TEST REQUIRED | `AmadeusProviderTests`: 401/500/429/empty/malformed degrade to null; happy path returns cheapest exact price. Live sandbox call needs credentials |
| Deep-link pricing fallback | CI VERIFIED | deterministic URL builder; tested via pipeline fallback test |
| Scrape-based pricing fallback | CI VERIFIED (unit) — EXTERNAL PROVIDER TEST REQUIRED (live sites) | provider tested; live scraping not run in CI by design |
| Reservation management + status transitions | CI VERIFIED | `DatabaseTests`, admin endpoints |
| Passenger details collection + attachment | CI VERIFIED (flow logic) | booking flow steps + `AttachPassengerAsync` |
| Booking automation (Playwright per airline) | STATICALLY VERIFIED — EXTERNAL LIVE VERIFICATION REQUIRED | browser automation cannot run deterministically in CI; Playwright 1.52 referenced |
| Cancellation workflow | CI VERIFIED (flow logic) — EXTERNAL LIVE VERIFICATION REQUIRED (airline-site cancellation) | flow + SR tracking tested; `ExecuteCancellationAsync` uses scoped DI (restart-safe) |
| Catalog sync (airline pages → Products) | CI VERIFIED (pipeline logic) — EXTERNAL PROVIDER TEST REQUIRED (live sites) | `CatalogSyncService` upsert logic; per-airline config-driven scraper registered; scraping disabled in CI by design |
| Payments (Stripe) | CI VERIFIED — EXTERNAL PROVIDER TEST REQUIRED (sandbox) | `PaymentServiceTests`: never fabricates success; unconfigured → Cancelled + audited. Live sandbox needs a test key |

## AI / LLM

| Feature | Status | Evidence |
|---------|--------|----------|
| Azure OpenAI chat assistant | CI VERIFIED — EXTERNAL PROVIDER TEST REQUIRED (real calls) | `AzureOpenAiService` with graceful "not configured" response; no live call in CI |
| Deterministic AI fallback (no fabricated actions) | CI VERIFIED | `ResilientLlmServiceTests` — fallback never claims a booking |
| AI intent router (business actions via app services, never via LLM) | CI VERIFIED | `IntentRouterTests`; router output drives app services, not raw actions |
| RAG knowledge retrieval | STATICALLY VERIFIED | `KnowledgeService` keyword path works; semantic path needs embeddings (external) |
| User preference learning | STATICALLY VERIFIED | phone-keyed preferences; single-User mapping pending |

## Infrastructure

| Feature | Status | Evidence |
|---------|--------|----------|
| GitHub Actions pipeline (build/test/TRX/security/docker-smoke) | CI VERIFIED | this very pipeline |
| Secret scanning in CI | CI VERIFIED | `scripts/secret-scan.sh` runs on every push |
| Config validation in CI | CI VERIFIED | `scripts/validate-config.sh` runs on every push |
| Structured logging (Serilog) with correlation | STATICALLY VERIFIED | Serilog file sink; correlation IDs logged where applicable |
| Rate limiting | NOT READY | recommended in SECURITY.md; not yet implemented |
| Swagger (dev only) | STATICALLY VERIFIED | registered when `ASPNETCORE_ENVIRONMENT=Development` |
