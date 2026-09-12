# SIWES Project Summary

## Project Title

**WhatsApp and Telegram AI Service Request Management System**

### Subtitle
*A Database-Backed Multi-Channel AI Service Management and Airline Assistance Platform*

## Background

The Student Industrial Work Experience Scheme (SIWES) provides students with practical exposure to industry-relevant technologies. This project demonstrates the application of computer science principles in building a production-quality software system.

## Problem Statement

Businesses and travelers need efficient, accessible ways to search for flights, manage bookings, and receive support. Traditional channels (phone, email, website) have limitations:
- Limited availability (business hours only)
- High operational costs
- Slow response times
- Inconsistent service quality

Additionally, users increasingly prefer messaging platforms (WhatsApp, Telegram) over traditional web interfaces.

## Aim

To design and implement a multi-channel AI service request management platform that allows users to interact through WhatsApp and Telegram for flight search, booking, and customer support, backed by a relational database and AI integration.

## Objectives

1. Design a database schema to support multi-channel user management, persistent sessions, message storage, and service request tracking
2. Implement WhatsApp integration using Meta Cloud API with webhook processing and signature verification
3. Implement Telegram integration using Bot API with webhook handling
4. Integrate AI (Azure OpenAI) for intelligent query handling with graceful fallback
5. Implement flight search and pricing using Amadeus API
6. Build a service request lifecycle management system
7. Implement JWT-authenticated admin API for system monitoring
8. Apply software engineering best practices: testing, documentation, security, and DevOps
9. Document the system architecture, database design, and API surface

## Scope

### Included
- WhatsApp and Telegram message processing
- AI-powered conversational interface
- Flight search and pricing
- Reservation management
- Product catalog
- User management with multi-channel identity
- Persistent session management
- Service request tracking
- Admin API with authentication
- Audit logging
- Comprehensive testing
- CI/CD pipeline
- Docker containerization

### Excluded
- Production deployment to cloud infrastructure
- Real payment processing
- Real WhatsApp Business account integration
- Real Telegram Bot deployment
- Mobile application
- Frontend web dashboard

## Users

- **Travelers**: Search flights, make reservations, get support via WhatsApp/Telegram
- **Customer Service Agents**: Monitor and manage service requests via admin API
- **System Administrators**: Monitor system health, view audit logs, manage users

## Architecture

The system uses a layered architecture:

```
Communication Layer (WhatsApp + Telegram adapters)
          ↓
Application Layer (User, Session, Message, ServiceRequest services)
          ↓
Business Layer (Flight pricing, AI, Reservations, Products)
          ↓
Data Layer (SQLite via EF Core)
          ↓
External APIs (Amadeus, Azure OpenAI, Meta Cloud, Telegram)
```

## Technologies Used

| Category | Technology | Purpose |
|----------|-----------|---------|
| Language | C# 12 | Application development |
| Framework | ASP.NET Core 8 | Web API framework |
| ORM | Entity Framework Core 8 | Database access |
| Database | SQLite | Data persistence |
| AI | Azure OpenAI | Natural language processing |
| Messaging | Meta Cloud API | WhatsApp integration |
| Messaging | Telegram Bot API | Telegram integration |
| Flights | Amadeus API | Flight search/pricing |
| Auth | JWT Bearer | API authentication |
| Logging | Serilog | Structured logging |
| Testing | xUnit | Unit testing |
| Testing | Moq | Mocking framework |
| API Docs | Swagger/OpenAPI | API documentation |
| CI/CD | GitHub Actions | Automated build/test |
| Container | Docker | Deployment packaging |

## Database

### Key Entities
- **User**: Internal user with multi-channel identities
- **ChannelIdentity**: Links users to WhatsApp/Telegram identities
- **AppSession**: Persistent session with 7-day expiry
- **Message**: All inbound/outbound messages with idempotency
- **ServiceRequest**: Full lifecycle tracking
- **Reservation**: Flight reservation with status management
- **AuditLog**: Administrative event tracking
- **AiInteractionLog**: AI usage tracking

### Relationships
- User → ChannelIdentity (1:N)
- User → AppSession (1:N)
- AppSession → Message (1:N)
- ServiceRequest → User (N:1)
- Reservation → PassengerInfo (N:1)

## AI Integration

- **Provider**: Azure OpenAI (GPT-4.1-mini)
- **RAG**: Retrieves relevant knowledge from database before generating responses
- **Fallback**: Deterministic responses when AI is unavailable
- **Safety**: AI cannot fabricate bookings, prices, or confirmations
- **Logging**: All AI interactions tracked with latency and success metrics

## Testing

### Test Categories
1. **Database tests**: Entity creation, relationships, constraints
2. **Service tests**: User, session, conversation, service request, audit
3. **AI tests**: Fallback behavior, response handling
4. **Controller tests**: Webhook processing, model validation

### Test Count: 48 tests covering critical paths

## Security Measures

- JWT authentication for admin endpoints
- HMAC-SHA256 webhook signature verification
- Environment-based secret management
- Input validation on all user inputs
- Structured logging without sensitive data
- Idempotent webhook processing

## Results

The completed system demonstrates:
- A working multi-channel architecture (WhatsApp + Telegram)
- Persistent database-backed sessions and messages
- Service request lifecycle management
- AI integration with graceful degradation
- Flight search and reservation capability
- Admin API for system monitoring
- Comprehensive test coverage
- Professional documentation
- CI/CD pipeline
- Docker support

## Limitations

1. External services (WhatsApp, Telegram, Amadeus, Azure OpenAI) require real credentials for full functionality
2. AI responses depend on the Azure OpenAI service availability
3. Flight pricing accuracy depends on Amadeus API data
4. Web scraping may break if airline websites change structure
5. SQLite is suitable for moderate scale but may need migration for high-volume production

## Future Improvements

1. Replace SQLite with PostgreSQL for production scalability
2. Add real-time WebSocket notifications
3. Implement frontend admin dashboard
4. Add payment gateway integration
5. Support additional messaging channels (SMS, email)
6. Implement machine learning for user preference prediction
7. Add multi-language support
8. Implement end-to-end encryption for sensitive data
