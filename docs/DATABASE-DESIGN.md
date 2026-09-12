# Database Design

## Overview

The database uses **SQLite** via Entity Framework Core 8.0 with code-first migrations. SQLite was chosen for:
- Zero-configuration deployment
- File-based storage suitable for the project scale
- Full EF Core support
- Easy development and testing

## Entity Relationship Diagram

```
User (1) ──────< ChannelIdentity (N)
  │
  ├────────< AppSession (N)
  │               │
  │               ├────< Message (N)
  │               │
  │               └────< ServiceRequest (N)
  │
  ├────────< UserPreference (N)
  │
  └────────< Reservation (0..1 via PhoneNumber)
                 │
                 └────── PassengerInfo (1)
                 │
                 └────── PaymentRecord (N)

ChatLog (standalone, legacy)
KnowledgeEntry (standalone)
ConversationInsight (standalone)
AuditLog (standalone)
AiInteractionLog (standalone)
Product (1) ─────< ProductImage (N)
```

## Entity Details

### User
| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | PK, auto-increment | Internal ID |
| ExternalId | string(50) | UNIQUE | Public identifier |
| DisplayName | string(100)? | - | User's display name |
| Email | string(255)? | INDEX | Email address |
| EmailVerified | bool | - | Verification status |
| PhoneNumber | string(50)? | INDEX | Primary phone |
| Status | string(20) | INDEX | Active/Suspended/Deleted |
| CreatedAtUtc | DateTime | - | Creation timestamp |
| UpdatedAtUtc | DateTime | - | Last update |
| LastActivityAtUtc | DateTime? | - | Last activity |
| Metadata | string(1000)? | - | JSON metadata |

### ChannelIdentity
| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | PK | Internal ID |
| UserId | int | FK→User, INDEX | Parent user |
| Channel | string(20) | UNIQUE(Channel, ProviderUserId) | whatsapp/telegram |
| ProviderUserId | string(100) | UNIQUE(Channel, ProviderUserId) | Provider ID |
| DisplayName | string(200)? | - | Channel display name |
| Metadata | string(2000)? | - | Channel-specific JSON |
| CreatedAtUtc | DateTime | - | Link creation time |
| LastSeenAtUtc | DateTime? | - | Last activity |

### AppSession
| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | PK | Internal ID |
| SessionId | string(64) | UNIQUE | Public session ID |
| Channel | string(20) | INDEX | Channel name |
| ConversationId | string(100)? | - | Provider chat/user ID |
| UserId | int? | FK→User, INDEX | Associated user |
| ChannelIdentityId | int? | FK→ChannelIdentity | Channel identity |
| CurrentState | string(50) | - | Workflow state |
| WorkflowStep | string(50)? | - | Current step |
| ContextData | string? | - | Serialized JSON state |
| LastActivityAtUtc | DateTime | INDEX | Activity tracking |
| ExpiresAtUtc | DateTime? | INDEX | 7-day expiry |

### Message
| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | long | PK | Internal ID |
| SessionId | int? | FK→AppSession, INDEX | Associated session |
| UserId | int? | FK→User, INDEX | Associated user |
| Channel | string(20) | INDEX | Channel name |
| ProviderMessageId | string(200)? | INDEX | Idempotency key |
| Direction | string(10) | INDEX | inbound/outbound |
| MessageType | string(20) | - | text/image/audio |
| Content | string? | - | Message text |
| ProviderUserId | string(100)? | - | Provider user ID |
| CorrelationId | string(64)? | INDEX | Trace ID |
| ProcessingStatus | string(20) | - | received/processed/failed |
| ErrorMessage | string(1000)? | - | Error details |
| Timestamp | DateTime | INDEX | Message time |

### ServiceRequest
| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | PK | Internal ID |
| RequestCode | string(30) | UNIQUE | Human-readable code |
| UserId | int? | FK→User, INDEX | Requesting user |
| SessionId | int? | FK→AppSession | Originating session |
| Channel | string(20) | INDEX | Origin channel |
| RequestType | string(50) | INDEX | Request category |
| Status | enum | INDEX | Lifecycle state |
| RequestSummary | string(2000)? | - | User's request text |
| ResultData | string? | - | Result JSON |
| ReservationCode | string(30)? | INDEX | Linked reservation |
| RequiresHumanAgent | bool | - | Escalation flag |
| AssignedAgentId | string(100)? | - | Human agent |
| Priority | string(10) | - | Low/Normal/High/Urgent |
| UsedAI | bool | - | AI involvement |
| AIProvider | string(50)? | - | AI provider name |

### Reservation (Existing)
Preserved with original schema. Key fields:
- ReservationCode (unique), PhoneNumber, AirlineKey, FromAirport, ToAirport
- Status lifecycle: Draft → PendingPayment → PaymentReceived → BookingInProgress → Confirmed → TicketIssued
- Linked to PassengerInfo via PassengerId FK

### AuditLog
| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | long | PK | Internal ID |
| Action | string(100) | INDEX | Event type |
| EntityType | string(100)? | INDEX | Affected entity |
| EntityId | string(50)? | - | Entity identifier |
| ActorId | string(100)? | INDEX | Who performed action |
| Details | string? | - | Additional context JSON |
| IpAddress | string(45)? | - | Source IP |
| Timestamp | DateTime | INDEX | Event time |

### AiInteractionLog
| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | long | PK | Internal ID |
| UserId | int? | INDEX | User involved |
| Provider | string(50) | INDEX | AI provider |
| Model | string(100)? | - | Model/deployment |
| Success | bool | INDEX | Outcome |
| LatencyMs | long | - | Response time |
| ErrorMessage | string(1000)? | - | Failure details |
| UsedFallback | bool | - | Fallback triggered |

## Indexes

Key indexes for query performance:
- `User.ExternalId` (unique)
- `ChannelIdentity.(Channel, ProviderUserId)` (unique)
- `AppSession.SessionId` (unique)
- `AppSession.LastActivityAtUtc` (cleanup queries)
- `Message.ProviderMessageId` (idempotency lookups)
- `ServiceRequest.RequestCode` (unique)
- `Reservation.ReservationCode` (unique)

## Migration Strategy

The application uses `EnsureCreated()` for development simplicity. For production:
1. Use EF Core migrations: `dotnet ef migrations add MigrationName`
2. Apply migrations: `dotnet ef database update`
3. Seed data is safe baseline data only (no credentials)
