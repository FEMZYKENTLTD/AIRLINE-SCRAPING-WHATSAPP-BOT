# WhatsApp & Telegram AI Service Request Management Platform

**A database-backed, AI-enabled, multi-channel service request management platform integrating WhatsApp and Telegram with airline/travel service functionality.**

## Overview

This platform enables users to interact through **WhatsApp** and **Telegram** to:
- Search and book flights using live pricing (Amadeus API + web scraping)
- Manage reservations and cancellations
- Browse a product catalog
- Chat with an AI assistant (Azure OpenAI)
- Escalate to human agents when needed

The system uses a **shared business logic layer** — both WhatsApp and Telegram channels use the same underlying services, database, and AI integration.

## Architecture

```
                WhatsApp (Meta Cloud API)
                        |
                        v
                 Channel Adapter
                        |
                        v
Telegram --------> Application Layer
                        |
                        v
                  Business Services
                        |
            +-----------+-----------+
            |           |           |
            v           v           v
         Database     AI (LLM)   External APIs
            |
            v
      Admin API (JWT)
```

## Features

### Multi-Channel Messaging
- **WhatsApp**: Meta Cloud API with webhook verification, signature validation, message chunking
- **Telegram**: Bot API with webhook processing, commands, and inline responses

### Airline/Travel Services
- Flight search across multiple airlines (Arik Air, Air Peace, Turkish Airlines, Lufthansa)
- Live pricing via Amadeus API
- Deep-link fallback pricing
- Web scraping for additional data
- Reservation management with full lifecycle
- Passenger detail collection
- Cancellation workflow

### AI Integration
- Azure OpenAI with RAG (Retrieval Augmented Generation)
- Knowledge base from database
- User preference learning
- Graceful fallback when AI is unavailable
- AI interaction logging

### Service Request Management
- Full lifecycle tracking (New → Processing → Completed/Failed)
- Human agent escalation
- Priority levels
- Cross-channel traceability

### Admin API
- JWT-authenticated endpoints
- Dashboard overview
- User management
- Session monitoring
- Message history
- Service request management
- Audit log access

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 8 / ASP.NET Core |
| Database | SQLite (EF Core) |
| AI/LLM | Azure OpenAI (GPT-4.1-mini) |
| WhatsApp | Meta Cloud API |
| Telegram | Telegram Bot API |
| Flight API | Amadeus Self-Service |
| Authentication | JWT Bearer |
| Logging | Serilog |
| API Docs | Swagger/OpenAPI |
| Container | Docker |
| CI/CD | GitHub Actions |
| Testing | xUnit, Moq, FluentAssertions |

## Prerequisites

- .NET 8 SDK
- SQLite (included with EF Core)
- WhatsApp Business API credentials (for WhatsApp channel)
- Telegram Bot Token (for Telegram channel)
- Azure OpenAI API key (for AI features)
- Amadeus API credentials (for live flight pricing)

## Quick Start

### 1. Clone the Repository

```bash
git clone https://github.com/FEMZYKENTLTD/AIRLINE-SCRAPING-WHATSAPP-BOT.git
cd AIRLINE-SCRAPING-WHATSAPP-BOT
```

### 2. Configure Environment

```bash
cp .env.example .env
# Edit .env with your credentials
```

### 3. Restore, Build, and Run

```bash
dotnet restore
dotnet build
cd WhatsAppBot
dotnet run
```

The application starts at `http://localhost:5260` (or configured port).

### 4. Verify

```bash
# In another terminal:
curl http://localhost:5260/
curl http://localhost:5260/api/admin/health
# Open Swagger in development:
# http://localhost:5260/swagger
```

### 5. Access Swagger

Open `http://localhost:5260/swagger` in your browser.

## Environment Variables

See `.env.example` for the complete list. Key variables:

| Variable | Required | Description |
|----------|----------|-------------|
| `DATABASE_CONNECTION_STRING` | No | SQLite connection string (default: `Data Source=whatsappbot.db`) |
| `WHATSAPP_ACCESS_TOKEN` | For WhatsApp | Meta Cloud API access token |
| `WHATSAPP_VERIFY_TOKEN` | For WhatsApp | Webhook verification token |
| `WHATSAPP_PHONE_NUMBER_ID` | For WhatsApp | WhatsApp Business phone number ID |
| `WHATSAPP_APP_SECRET` | For WhatsApp | App secret for signature verification |
| `TELEGRAM_BOT_TOKEN` | For Telegram | Telegram Bot API token |
| `TELEGRAM_WEBHOOK_SECRET` | No | Secret for webhook validation |
| `AZURE_OPENAI_ENDPOINT` | For AI | Azure OpenAI endpoint URL |
| `AZURE_OPENAI_API_KEY` | For AI | Azure OpenAI API key |
| `AZURE_OPENAI_DEPLOYMENT` | No | Model deployment name (default: `gpt-4.1-mini`) |
| `AMADEUS_CLIENT_ID` | For flights | Amadeus API client ID |
| `AMADEUS_CLIENT_SECRET` | For flights | Amadeus API client secret |
| `JWT_SECRET` | For admin | JWT signing secret (min 32 chars) |
| `ADMIN_USERNAME` | For admin | Admin login username |
| `ADMIN_PASSWORD` | For admin | Admin login password |

## API Endpoints

### Public
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/` | Service status and info |
| GET | `/webhook` | WhatsApp webhook verification |
| POST | `/webhook` | WhatsApp message receiver |
| POST | `/telegram` | Telegram webhook receiver |
| POST | `/api/auth/login` | Admin authentication |

### Admin (JWT Required)
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/admin/dashboard` | System overview |
| GET | `/api/admin/users` | List users |
| GET | `/api/admin/users/{id}` | Get user details |
| GET | `/api/admin/sessions` | List sessions |
| GET | `/api/admin/messages` | List messages |
| GET | `/api/admin/service-requests` | List service requests |
| PUT | `/api/admin/service-requests/{code}/status` | Update request status |
| POST | `/api/admin/service-requests/{code}/escalate` | Escalate request |
| GET | `/api/admin/reservations` | List reservations |
| GET | `/api/admin/audit` | Audit logs |
| GET | `/api/admin/knowledge` | Knowledge entries |
| GET | `/api/admin/health` | Health check |

## Docker

```bash
docker build -t airline-platform -f WhatsAppBot/Dockerfile .
docker run -p 8080:8080 --env-file .env airline-platform
```

## Testing

```bash
dotnet test
```

59 tests covering:
- Database persistence and relationships
- User management (creation, channel identity, lookup)
- Session management (7-day expiry, persistence, cleanup)
- Conversation/message handling (inbound, outbound, idempotency)
- Service request lifecycle (creation, status, escalation, completion)
- AI fallback behavior (success, failure, safe responses)
- Audit logging
- Webhook controller logic (Telegram, model validation)

## Project Structure

```
WhatsAppBot/
├── Controllers/
│   ├── WhatsAppWebhookController.cs    # WhatsApp webhook
│   ├── TelegramWebhookController.cs    # Telegram webhook
│   ├── AdminController.cs              # Admin API
│   └── AuthController.cs               # JWT authentication
├── Data/
│   ├── AppDbContext.cs                 # EF Core context
│   └── DbSeeder.cs                     # Seed data
├── Models/
│   ├── User.cs                         # Internal user
│   ├── ChannelIdentity.cs              # Multi-channel identity
│   ├── AppSession.cs                   # Persistent sessions
│   ├── Message.cs                      # Message storage
│   ├── ServiceRequest.cs               # Service request lifecycle
│   ├── AuditLog.cs                     # Audit trail
│   ├── AiInteractionLog.cs             # AI tracking
│   ├── Flights/                        # Flight domain models
│   ├── Passengers/                     # Passenger details
│   ├── Payments/                       # Payment records
│   ├── Reservations/                   # Reservation models
│   └── Learning/                       # Knowledge & preferences
├── Services/
│   ├── Implementations/
│   │   ├── UserService.cs
│   │   ├── PersistentSessionService.cs
│   │   ├── ConversationService.cs
│   │   ├── ServiceRequestService.cs
│   │   ├── AuditService.cs
│   │   ├── ResilientLlmService.cs
│   │   ├── TelegramService.cs
│   │   ├── MetaWhatsAppService.cs
│   │   └── AzureOpenAiService.cs
│   ├── Interfaces/                     # Service contracts
│   ├── Flights/                        # Flight pricing
│   ├── Reservations/                   # Reservation management
│   ├── Learning/                       # AI learning
│   ├── Media/                          # Image/voice/vision
│   ├── Scraping/                       # Web scraping
│   └── Automation/                     # Booking automation
├── Extensions/                         # Configuration helpers
└── Properties/

WhatsAppBot.Tests/
├── Database/                           # Database tests
├── Services/                           # Service unit tests
└── Controllers/                        # Controller tests
```

## Documentation

Detailed documentation is available in the `/docs` directory:

- [Architecture](docs/ARCHITECTURE.md)
- [Database Design](docs/DATABASE-DESIGN.md)
- [API Documentation](docs/API-DOCUMENTATION.md)
- [Security Report](docs/SECURITY-REPORT.md)
- [Test Report](docs/TEST-REPORT.md)
- [Deployment Guide](docs/DEPLOYMENT.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Feature Matrix](docs/FEATURE-MATRIX.md)
- [SIWES Project Summary](docs/SIWES-PROJECT-SUMMARY.md)

## Security

- JWT authentication for admin endpoints
- WhatsApp webhook signature verification (HMAC-SHA256)
- Telegram webhook secret token validation
- Environment-based credential management (no secrets in source)
- Input validation on all user inputs
- Structured logging without sensitive data exposure
- Idempotency for webhook processing

## License

Proprietary — FEMZYK ENTERPRISES LTD
