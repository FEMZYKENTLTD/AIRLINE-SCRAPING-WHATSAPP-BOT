# System Architecture

## Overview

The Airline Service Management Platform follows a **layered architecture** with clear separation of concerns. Communication channels (WhatsApp, Telegram) are adapters that translate provider-specific formats into a common application model.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                     COMMUNICATION LAYER                         │
│  ┌──────────────────┐  ┌──────────────────┐                    │
│  │  WhatsApp         │  │  Telegram         │                    │
│  │  Webhook          │  │  Webhook          │                    │
│  │  Controller       │  │  Controller       │                    │
│  └────────┬──────────┘  └────────┬──────────┘                    │
│           │                      │                              │
│           └──────────┬───────────┘                              │
│                      ▼                                          │
│  ┌───────────────────────────────────────┐                      │
│  │       APPLICATION SERVICES            │                      │
│  │  IUserService                         │                      │
│  │  IPersistentSessionService            │                      │
│  │  IConversationService                 │                      │
│  │  IServiceRequestService               │                      │
│  │  IAuditService                        │                      │
│  └────────────────────┬──────────────────┘                      │
│                       ▼                                         │
│  ┌────────────────────────────────────────┐                     │
│  │        BUSINESS SERVICES               │                     │
│  │  ILLMService (ResilientLlmService)     │                     │
│  │  FlightPricingService                  │                     │
│  │  IReservationService                   │                     │
│  │  IProductCatalogService                │                     │
│  │  IKnowledgeService                     │                     │
│  └───────┬────────────┬───────────────┬───┘                     │
│          ▼            ▼               ▼                          │
│  ┌───────────┐ ┌───────────┐ ┌───────────────────┐              │
│  │ Database  │ │ AI (LLM)  │ │ External APIs     │              │
│  │ (SQLite)  │ │ (Azure    │ │ (Amadeus, Stripe, │              │
│  │ EF Core   │ │  OpenAI)  │ │  Meta Cloud)      │              │
│  └───────────┘ └───────────┘ └───────────────────┘              │
└─────────────────────────────────────────────────────────────────┘
```

## Layer Descriptions

### 1. Communication Layer
- **WhatsAppWebhookController**: Handles Meta Cloud API webhook verification (GET) and message reception (POST). Validates HMAC-SHA256 signatures, parses payloads, manages the in-memory session state machine.
- **TelegramWebhookController**: Handles Telegram Bot API updates. Validates webhook secret, extracts messages, uses persistent sessions and shared services.

### 2. Application Services
- **IUserService**: Creates/links internal users with channel-specific identities (multi-channel identity model).
- **IPersistentSessionService**: Database-backed sessions with 7-day inactivity expiration. Tracks workflow state across restarts.
- **IConversationService**: Persists all inbound/outbound messages with idempotency support via provider message IDs.
- **IServiceRequestService**: Manages service request lifecycle from creation to completion/escalation.
- **IAuditService**: Logs administrative and security events.

### 3. Business Services
- **ILLMService**: AI abstraction wrapped by `ResilientLlmService` for graceful fallback.
- **FlightPricingService**: Orchestrates multiple pricing providers (Amadeus API, Deep Link, Scrape).
- **IReservationService**: Creates and manages flight reservations with proper state transitions.

### 4. Data Layer
- **AppDbContext**: EF Core context with SQLite provider. Defines all entity relationships, indexes, and constraints.
- **Models**: Domain entities with proper validation attributes and navigation properties.

### 5. External Integrations
- **Meta Cloud API**: WhatsApp message sending via HTTP API.
- **Telegram Bot API**: Telegram message sending and webhook management.
- **Azure OpenAI**: AI chat completions with RAG.
- **Amadeus API**: Flight offer search and pricing.

## Design Decisions

### Why Layered Architecture?
The layered architecture ensures:
- Channels are interchangeable (add a new channel without changing business logic)
- Business logic is testable independently of the communication layer
- Database can be swapped without affecting the API surface
- AI provider can be changed behind the abstraction

### Why Both In-Memory and Persistent Sessions?
- **WhatsApp**: Uses the existing `InMemorySessionService` for backward compatibility. The WhatsApp controller manages a rich state machine (FlightStep enum) that works well with in-memory state.
- **Telegram**: Uses the new `PersistentSessionService` backed by the database, supporting session persistence across application restarts and 7-day expiration.

### Why Idempotency?
Webhook providers (Meta, Telegram) may deliver the same event multiple times. The `ConversationService` checks `ProviderMessageId` before processing to prevent duplicate business actions.

### Why ResilientLlmService?
AI services are external and can fail. The `ResilientLlmService` wrapper:
- Catches exceptions from the underlying LLM service
- Returns deterministic fallback responses
- Never crashes the application
- Suggests relevant commands based on user input
- Does NOT fabricate bookings, prices, or confirmations
