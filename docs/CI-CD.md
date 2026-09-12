# CI/CD Pipeline

## Overview

GitHub Actions is the **only** build machine for this project. No local .NET
toolchain, Docker, or other setup is required to run the pipeline — GitHub's
hosted runners provide everything.

**Every push to any branch** and **every pull request to `main`** runs the
full pipeline.

```
push (any branch) / PR → main
        │
        ▼
┌─────────────────────┬─────────────────────┬──────────────────────────┐
│  build-and-test     │  security           │  docker-build-and-smoke  │
│  .NET 8 (GitHub     │  • config validation │  needs: build-and-test   │
│  Actions SDK)       │    (scripts/         │  • docker build          │
│  • restore          │      validate-config │  • container start       │
│  • build (Release)  │      .sh)            │  • /health + readiness   │
│  • test (TRX +      │  • secret scanning   │  • HMAC reject (401)     │
│    console)         │    (scripts/         │  • Telegram secret       │
│  • publish          │      secret-scan.sh) │    reject (401)          │
│  • artifacts:       │  • artifacts: report │  • admin auth (401)      │
│    test-results,    │                      │  • container logs        │
│    publish output   │                      │  • artifacts: logs/meta  │
└─────────────────────┴─────────────────────┴──────────────────────────┘
```

## Jobs

### 1. Build & Test (`.NET 8, Release`)

| Step | Command | Fails when |
|------|---------|------------|
| Setup | `actions/setup-dotnet@v4` (8.0.x) | toolchain unavailable |
| Restore | `dotnet restore WhatsAppBot.sln` | package resolution error |
| Build | `dotnet build WhatsAppBot.sln -c Release` | any compile error |
| Test | `dotnet test ... --logger trx;LogFileName=test-results.trx` | any test failure |
| Publish | `dotnet publish WhatsAppBot/WhatsAppBot.csproj` | publish error |

Compile and test failures are additionally surfaced as **check-run
annotations** (readable via the Actions API) so they are visible even when
raw logs are unavailable.

### 2. Security & Config Validation

Runs `scripts/validate-config.sh` and `scripts/secret-scan.sh` (bash +
python3 + grep, both available on the runner):

- no `.env`, database, or key files tracked in Git
- appsettings JSON is valid and declares every required section
- every env var used by `Program.cs` is documented in `.env.example` (and
  vice-versa — stale vars are reported)
- no real-looking secrets committed in appsettings.json
- pattern scan of the entire tracked tree for private keys, AWS/GitHub/
  Slack/Stripe/Telegram credentials, raw JWTs, and high-entropy values on
  secret-named assignments (documented placeholders are allow-listed)

### 3. Docker Build & Smoke Test

- Builds `WhatsAppBot/Dockerfile` (multi-stage: `sdk:8.0` → `aspnet:8.0`,
  non-root user, `HEALTHCHECK` on `/health`).
- Starts the container **without any external provider credentials**
  (WhatsApp, Telegram, Azure OpenAI, Amadeus all optional).
- Waits for `GET /health` (60 s budget), then verifies:
  - `GET /` → 200 (service banner)
  - `GET /health/ready` → 200 (database connected)
  - `POST /webhook` with invalid HMAC → **401** (proves the webhook
    controller constructs — catches DI defects — and rejects bad payloads)
  - `POST /telegram` with wrong secret → **401**
  - `GET /api/admin/dashboard` without a token → **401**
- Captures `docker logs` and image metadata as artifacts (uploaded on
  success and failure).

## Artifacts

| Artifact | Contents | Retention |
|----------|----------|-----------|
| `test-results-<run>` | TRX test results (uploaded even on failure) | 30 d |
| `publish-output-<run>` | `dotnet publish` output | 14 d |
| `security-report-<run>` | config validation + secret scan reports | 30 d |
| `docker-smoke-<run>` | container logs, health responses, image metadata | 30 d |

## Guarantees

- **No `continue-on-error`** on required steps. Artifact uploads use
  `if: always()` (conditional execution) so evidence is preserved when a
  step fails — they never mask a failure.
- **No `|| true`** to hide failures.
- **No real secrets** in CI: the smoke container runs with only
  `ci-smoke-test-*` placeholder values and a temp database.
- **Deterministic tests**: InMemory EF provider + temp SQLite files; no
  network calls (scraping disabled in the test host).

## Configuration

The workflow is fully declarative — no repository secrets are consumed by
the pipeline. Optional provider credentials are added at *deployment* time
(see `docs/DEPLOYMENT.md`), never in CI.

## Troubleshooting

- **Build fails** → read the check-run annotations (each compiler error is
  annotated) or the `Build solution (Release)` step log.
- **Tests fail** → the `test-results-*` artifact contains the full TRX;
  failing test names are also annotated.
- **Docker smoke fails** → the `docker-smoke-*` artifact contains
  `app-smoke-logs.txt` (the application's stdout, incl. Serilog).
