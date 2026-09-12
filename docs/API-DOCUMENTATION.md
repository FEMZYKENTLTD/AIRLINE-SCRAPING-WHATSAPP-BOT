# API Documentation

## Base URL
```
http://localhost:5260
```

## Authentication

Admin endpoints require JWT Bearer token:
```
Authorization: Bearer <token>
```

Obtain a token via `/api/auth/login`.

---

## Public Endpoints

### GET / — Service Status
Returns service information and available features.

**Response (200):**
```json
{
  "service": "FEMZYK ENTERPRISES - Multi-Channel AI Service Platform",
  "version": "3.0",
  "status": "Running",
  "channels": ["WhatsApp", "Telegram"],
  "endpoints": {
    "webhook_whatsapp": "/webhook",
    "webhook_telegram": "/telegram",
    "admin": "/api/admin",
    "swagger": "/swagger"
  }
}
```

### GET /webhook — WhatsApp Verification
Meta's webhook verification endpoint.

**Query Parameters:**
| Parameter | Required | Description |
|-----------|----------|-------------|
| hub.mode | Yes | Must be "subscribe" |
| hub.verify_token | Yes | Must match configured verify token |
| hub.challenge | Yes | Challenge string to echo back |

**Response (200):** Returns challenge string as text/plain
**Response (401):** Token mismatch

### POST /webhook — WhatsApp Messages
Receives incoming WhatsApp messages.

**Headers:**
- `X-Hub-Signature-256`: HMAC-SHA256 signature (verified if AppSecret configured)

**Response:** Always 200 OK

### POST /telegram — Telegram Updates
Receives incoming Telegram updates.

**Headers:**
- `X-Telegram-Bot-Api-Secret-Token`: Secret token (verified if configured)

**Body:** Telegram Update JSON

**Response:** Always 200 OK

### POST /api/auth/login — Admin Login
Authenticate and receive JWT token.

**Request:**
```json
{
  "username": "admin",
  "password": "your-password"
}
```

**Response (200):**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "expiresIn": 86400,
  "tokenType": "Bearer"
}
```

**Response (401):** Invalid credentials

---

## Admin Endpoints (JWT Required)

### GET /api/admin/dashboard
System overview with counts.

**Response (200):**
```json
{
  "users": { "total": 42 },
  "sessions": { "active": 15 },
  "messages": { "total": 1234 },
  "serviceRequests": { "New": 3, "Processing": 2, "Completed": 50 },
  "reservations": { "total": 25 },
  "payments": { "total": 10 }
}
```

### GET /api/admin/users
List users with search and pagination.

**Query Parameters:**
| Parameter | Default | Description |
|-----------|---------|-------------|
| skip | 0 | Offset |
| take | 20 | Limit (max 100) |
| search | - | Search term |

### GET /api/admin/users/{id}
Get user details with channel identities.

### GET /api/admin/sessions
List sessions.

**Query Parameters:**
| Parameter | Default | Description |
|-----------|---------|-------------|
| skip | 0 | Offset |
| take | 20 | Limit |
| channel | - | Filter by channel |

### GET /api/admin/messages
List messages.

**Query Parameters:**
| Parameter | Default | Description |
|-----------|---------|-------------|
| skip | 0 | Offset |
| take | 50 | Limit (max 200) |
| channel | - | whatsapp/telegram |
| direction | - | inbound/outbound |

### GET /api/admin/service-requests
List service requests.

**Query Parameters:**
| Parameter | Default | Description |
|-----------|---------|-------------|
| skip | 0 | Offset |
| take | 20 | Limit |
| status | - | New, Processing, Completed, etc. |
| requestType | - | FlightSearch, AiAssistance, etc. |
| channel | - | whatsapp/telegram |

### PUT /api/admin/service-requests/{requestCode}/status
Update service request status.

**Request:**
```json
{
  "status": "Processing",
  "message": "Searching for flights"
}
```

### POST /api/admin/service-requests/{requestCode}/escalate
Escalate to human agent.

**Request:**
```json
{
  "reason": "Complex booking request",
  "agentId": "agent-001"
}
```

### GET /api/admin/reservations
List reservations with optional status filter.

### GET /api/admin/audit
List audit logs.

### GET /api/admin/knowledge
List knowledge entries.

### GET /api/admin/health
Health check (no auth required).

**Response (200):**
```json
{
  "status": "Healthy",
  "database": "Connected",
  "timestamp": "2026-09-11T12:00:00Z",
  "version": "3.0.0"
}
```

---

## Error Responses

All endpoints return standard error formats:

**400 Bad Request:**
```json
{ "error": "Validation failed" }
```

**401 Unauthorized:**
```json
{ "error": "Invalid credentials" }
```

**404 Not Found:**
```json
{ "error": "Resource not found" }
```

**500 Internal Server Error:**
```json
{ "error": "Internal server error" }
```
