# SIWES Technical Documentation

## System Overview

### What Problem Does the System Solve?

The system solves the problem of providing accessible, AI-powered travel assistance through messaging platforms that users already use daily (WhatsApp and Telegram). Instead of requiring users to visit websites or call support, they can interact with an intelligent assistant directly in their messaging app to search flights, manage bookings, and get support.

### Why This Architecture?

**Layered architecture** was chosen because:
1. **Separation of concerns**: Communication channels are independent from business logic
2. **Extensibility**: Adding a new channel (e.g., SMS) requires only a new adapter
3. **Testability**: Business logic can be tested without real messaging providers
4. **Maintainability**: Changes to one layer don't cascade to others

### Why .NET?

ASP.NET Core was chosen because:
1. **Performance**: High-throughput async/await support
2. **EF Core**: Mature ORM with LINQ, migrations, and multiple database providers
3. **Dependency injection**: Built-in DI container
4. **Middleware pipeline**: Clean request processing
5. **Swagger integration**: Automatic API documentation
6. **Cross-platform**: Runs on Windows, Linux, macOS
7. **Industry relevance**: Widely used in enterprise software

### Why Relational Database (SQLite)?

A relational database was chosen because:
1. **Structured data**: Users, sessions, messages have clear relationships
2. **Referential integrity**: Foreign keys ensure data consistency
3. **Query capability**: SQL supports complex queries and joins
4. **EF Core support**: First-class ORM support
5. **Zero configuration**: SQLite requires no server setup

### Why EF Core?

Entity Framework Core provides:
1. **Code-first approach**: Define models in C#, generate database
2. **LINQ queries**: Type-safe database queries
3. **Migrations**: Version-controlled schema changes
4. **Multiple providers**: SQLite for dev, PostgreSQL/SQL Server for production
5. **Change tracking**: Automatic dirty checking
6. **Async operations**: Non-blocking database access

### Why REST APIs?

REST was chosen because:
1. **Stateless**: Each request contains all needed information
2. **HTTP-native**: Works with existing web infrastructure
3. **Swagger support**: Automatic documentation
4. **Client-agnostic**: Works with any HTTP client
5. **Status codes**: Clear success/failure semantics

### Why WhatsApp?

WhatsApp is the most widely used messaging platform globally, with over 2 billion users. The Meta Cloud API provides:
1. Official business messaging
2. Webhook-based message delivery
3. Rich message types (text, image, interactive)
4. Message delivery receipts
5. End-to-end encryption (platform level)

### Why Telegram?

Telegram provides:
1. Open Bot API with excellent documentation
2. No approval process for bot creation
3. Rich features (inline keyboards, commands, groups)
4. Strong developer community
5. Reliable webhook infrastructure

### Why AI (Azure OpenAI)?

AI integration enables:
1. Natural language understanding of user queries
2. Context-aware responses using conversation history
3. Knowledge retrieval from database (RAG)
4. 24/7 availability without human operators
5. Consistent response quality

### Why Service Abstraction?

Interfaces like `ILLMService`, `IWhatsAppService` enable:
1. **Testability**: Mock implementations for unit tests
2. **Replaceability**: Swap AI providers without changing business logic
3. **Fallback**: Wrap with resilience patterns
4. **Single Responsibility**: Each service has one clear purpose

### Why Persistent Sessions?

Database-backed sessions enable:
1. **Durability**: Sessions survive application restarts
2. **7-day expiry**: Based on persisted timestamps, not memory
3. **Audit**: Session history is queryable
4. **Multi-instance**: Sessions shared across server instances

### Why Idempotency?

Webhook providers may deliver the same event multiple times. Without idempotency:
- Duplicate messages could be stored
- Duplicate business actions could be triggered
- Users could receive repeated responses

The `ProviderMessageId` check prevents these issues.

### Why Structured Logging?

Serilog provides:
1. **Structured data**: JSON-formatted log entries
2. **Multiple sinks**: Console, file, external services
3. **Log levels**: Debug, Information, Warning, Error, Fatal
4. **Correlation**: Trace requests across services
5. **Performance**: Minimal overhead with async sinks

### Why JWT Authentication?

JWT tokens provide:
1. **Stateless**: No server-side session storage
2. **Self-contained**: Claims embedded in token
3. **Standard**: Widely supported across platforms
4. **Expiry**: Built-in token lifetime management
5. **Role-based**: Claims support authorization

### Why Docker?

Docker provides:
1. **Consistency**: Same environment dev → production
2. **Isolation**: Application doesn't conflict with host
3. **Reproducibility**: Build once, run anywhere
4. **CI/CD**: Standard artifact format
5. **Scaling**: Container orchestration ready

### Why Automated Tests?

Testing ensures:
1. **Correctness**: Code behaves as expected
2. **Regression**: New changes don't break existing functionality
3. **Documentation**: Tests document expected behavior
4. **Confidence**: Safe refactoring

## Key Technical Decisions

### Database Schema Design
The multi-channel identity model (`User → ChannelIdentity`) allows a single internal user to interact through multiple channels. This prevents duplicate user records when the same person uses both WhatsApp and Telegram.

### Session State Machine
The session uses string-based states (`New`, `Onboarding_Name`, `Verified`) rather than enums, allowing flexible state transitions without code changes for new workflows.

### AI Safety
The `ResilientLlmService` wrapper ensures:
- AI failures never crash the application
- Fallback responses are deterministic and helpful
- AI cannot independently execute business actions (bookings, payments)
- All AI interactions are logged for monitoring

### Webhook Processing Pattern
```
Webhook received
  → Validate signature/secret
  → Parse payload
  → Check idempotency (ProviderMessageId)
  → Persist message
  → Process business logic
  → Generate response
  → Send response
  → Log audit trail
  → Return 200 OK (always)
```

## Defense Questions and Answers

### Q: What problem does the system solve?
A: The system provides an AI-powered travel and service management platform accessible through WhatsApp and Telegram, allowing users to search flights, manage bookings, and get support through their preferred messaging app.

### Q: Explain the architecture.
A: The system uses a layered architecture where WhatsApp and Telegram are communication channels (adapters) that translate provider-specific formats into a common application model. Business services handle flight search, reservations, and AI interactions. Data is persisted in a SQLite database via EF Core.

### Q: Explain the database.
A: The database has 12 main entities. Users have channel identities (WhatsApp/Telegram). Sessions track conversation state with 7-day expiry. Messages store all interactions with idempotency keys. Service requests track the full lifecycle from creation to completion.

### Q: Why did you use EF Core?
A: EF Core provides type-safe database access through LINQ, automatic change tracking, migration support for schema evolution, and works with multiple database providers. It integrates naturally with ASP.NET Core's dependency injection.

### Q: What is a webhook?
A: A webhook is an HTTP callback - when an event occurs (e.g., new WhatsApp message), the provider sends an HTTP POST request to our endpoint with the event data. We process the message and respond.

### Q: How does WhatsApp communicate with your backend?
A: Meta's Cloud API sends HTTP POST requests to our `/webhook` endpoint whenever a message arrives. We verify the HMAC-SHA256 signature to ensure the request is authentic, parse the JSON payload, extract the message, process it, and send a reply via the Meta Cloud API.

### Q: How does Telegram communicate with your backend?
A: Telegram sends HTTP POST requests to our `/telegram` endpoint with update objects containing messages. We validate the secret token header, extract the message text and user info, process the command or query, and respond via the Telegram Bot API.

### Q: How do you prevent duplicate webhook processing?
A: Each message has a unique provider message ID (e.g., `wamid` for WhatsApp, `message_id` for Telegram). Before processing, we check if a message with that ID already exists in our database. If so, we skip processing.

### Q: How does the AI integration work?
A: User messages are sent to Azure OpenAI's chat completions API. Before calling the AI, we search our knowledge database for relevant context (RAG). The AI response is returned to the user. If AI is unavailable, deterministic fallback responses are provided.

### Q: What happens if OpenAI is unavailable?
A: The `ResilientLlmService` wrapper catches exceptions and returns helpful fallback responses that suggest relevant commands (`/flight`, `/products`, etc.). The application continues functioning for all non-AI features.

### Q: How do you secure API keys?
A: All sensitive values are stored as environment variables, never in source code. The `.env.example` file contains only placeholder names. The `.env` file is in `.gitignore`. For production, we recommend a secrets manager.

### Q: How do you authenticate administrators?
A: Admins authenticate via `/api/auth/login` with username/password and receive a JWT token. Subsequent requests include the token in the Authorization header. The JWT includes claims for username and role.

### Q: How does the 7-day session expiry work?
A: Each session has an `ExpiresAtUtc` field. When a user interacts, `Touch()` updates this to `DateTime.UtcNow + 7 days`. A background service periodically marks expired sessions. This is based on persisted database timestamps, not in-memory timers.

### Q: How are users identified?
A: Users are identified by their channel-specific identifier (WhatsApp phone number, Telegram user ID). We look up or create a `ChannelIdentity` linking to an internal `User` record.

### Q: How are conversations stored?
A: Every inbound and outbound message is persisted in the `Messages` table with channel, direction, content, provider message ID, and timestamps. Sessions link to their messages.

### Q: How do you handle errors?
A: Controller-level try/catch blocks return appropriate HTTP responses. Service-level exceptions are logged with structured context. Webhook handlers always return 200 OK to prevent provider retries. AI failures trigger deterministic fallback.

### Q: How do you test the system?
A: We use xUnit with Moq for mocking dependencies and FluentAssertions for readable assertions. Tests cover database persistence, service logic, AI fallback behavior, session management, and webhook processing.

### Q: What was the most difficult technical problem?
A: Ensuring idempotent webhook processing while maintaining conversation state. Webhooks can arrive out of order or multiple times, but we need consistent session state and no duplicate business actions.

### Q: What would you improve?
A: Add PostgreSQL for production, implement WebSocket notifications, build a frontend dashboard, add payment gateway integration, implement rate limiting, and add end-to-end tests with real provider test environments.

### Q: What did you personally implement?
A: See CONTRIBUTION-MATRIX.md for a detailed breakdown. Key contributions include the multi-channel architecture, Telegram integration, persistent session management, service request domain, admin API, AI fallback system, testing suite, and documentation.
