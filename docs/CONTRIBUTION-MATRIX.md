# Contribution Matrix

This document separates existing repository functionality from work implemented/refactored during this project.

## Existing Repository Functionality (Pre-existing)

| Component | File(s) | Status |
|-----------|---------|--------|
| WhatsApp webhook controller | `Controllers/WhatsAppWebhookController.cs` | Improved |
| AppDbContext (original entities) | `Data/AppDbContext.cs` | Extended |
| DbSeeder | `Data/DbSeeder.cs` | Preserved |
| UserSession (in-memory) | `Models/UserSession.cs` | Extended |
| UserState enum | `Models/UserState.cs` | Preserved |
| ChatLog model | `Models/ChatLog.cs` | Preserved |
| ChatMessage model | `Models/ChatMessage.cs` | Preserved |
| Product model | `Models/Product.cs` | Preserved |
| ProductImage model | `Models/ProductImage.cs` | Preserved |
| Reservation model | `Models/Reservations/Reservation.cs` | Preserved |
| ReservationStatus enum | `Models/Reservations/ReservationStatus.cs` | Preserved |
| PassengerInfo model | `Models/Passengers/PassengerDetails.cs` | Preserved |
| PaymentRecord model | `Models/Payments/PaymentRecord.cs` | Preserved |
| PaymentStatus enum | `Models/Payments/PaymentStatus.cs` | Preserved |
| Flight models | `Models/Flights/` | Extended |
| KnowledgeEntry model | `Models/Learning/KnowledgeEntry.cs` | Preserved |
| UserPreference model | `Models/Learning/UserPreference.cs` | Preserved |
| ConversationInsight model | `Models/Learning/ConversationInsight.cs` | Preserved |
| InMemorySessionService | `Services/Implementations/InMemorySessionService.cs` | Preserved |
| MetaWhatsAppService | `Services/Implementations/MetaWhatsAppService.cs` | Preserved |
| AzureOpenAiService | `Services/Implementations/AzureOpenAiService.cs` | Preserved |
| ChatLogService | `Services/Implementations/ChatLogService.cs` | Preserved |
| ProductCatalogService | `Services/Implementations/ProductCatalogService.cs` | Preserved |
| CatalogSyncService | `Services/Implementations/CatalogSyncService.cs` | Preserved |
| ReservationService | `Services/Reservations/ReservationService.cs` | Preserved |
| FlightPricingService | `Services/Flights/FlightPricingService.cs` | Preserved |
| AmadeusFlightPricingProvider | `Services/Flights/AmadeusFlightPricingProvider.cs` | Preserved |
| DeepLinkFlightPricingProvider | `Services/Flights/DeepLinkFlightPricingProvider.cs` | Preserved |
| FlightConversationService | `Services/Flights/FlightConversationService.cs` | Refactored |
| SessionCleanupService | `Services/SessionCleanupService.cs` | Preserved |
| KnowledgeService | `Services/Learning/KnowledgeService.cs` | Preserved |
| EmbeddingService | `Services/Learning/EmbeddingService.cs` | Preserved |
| Media services | `Services/Media/` | Preserved |
| CAPTCHA services | `Services/CaptchaSolver/` | Preserved |
| Automation services | `Services/Automation/` | Preserved |
| Scraping services | `Services/Scraping/` | Preserved |
| ConfigurationExtensions | `Extensions/ConfigurationExtensions.cs` | Preserved |
| Program.cs | `Program.cs` | Significantly improved |
| Dockerfile | `Dockerfile` | Improved |
| .env.example | `.env.example` | Security fixed |
| appsettings.json | `appsettings.json` | Preserved |

## New Implementations (This Project)

| Component | File(s) | Description |
|-----------|---------|-------------|
| User model | `Models/User.cs` | Internal user entity |
| ChannelIdentity model | `Models/ChannelIdentity.cs` | Multi-channel identity linking |
| AppSession model | `Models/AppSession.cs` | Persistent session with 7-day expiry |
| Message model | `Models/Message.cs` | Persistent message storage |
| ServiceRequest model | `Models/ServiceRequest.cs` | Service request lifecycle |
| ServiceRequestStatus enum | `Models/ServiceRequest.cs` | Status state machine |
| AuditLog model | `Models/AuditLog.cs` | Audit trail |
| AiInteractionLog model | `Models/AiInteractionLog.cs` | AI interaction tracking |
| FlightConversationStep enum | `Models/Flights/FlightConversationStep.cs` | Missing enum (created) |
| IUserService | `Services/Interfaces/IUserService.cs` | User management contract |
| UserService | `Services/Implementations/UserService.cs` | User management implementation |
| IPersistentSessionService | `Services/Interfaces/IPersistentSessionService.cs` | Persistent session contract |
| PersistentSessionService | `Services/Implementations/PersistentSessionService.cs` | DB-backed sessions |
| IConversationService | `Services/Interfaces/IConversationService.cs` | Message persistence contract |
| ConversationService | `Services/Implementations/ConversationService.cs` | Message persistence |
| IServiceRequestService | `Services/Interfaces/IServiceRequestService.cs` | Service request contract |
| ServiceRequestService | `Services/Implementations/ServiceRequestService.cs` | Service request implementation |
| IAuditService | `Services/Interfaces/IAuditService.cs` | Audit contract |
| AuditService | `Services/Implementations/AuditService.cs` | Audit implementation |
| ITelegramService | `Services/Interfaces/ITelegramService.cs` | Telegram contract |
| TelegramService | `Services/Implementations/TelegramService.cs` | Telegram Bot API client |
| IChannelAdapter | `Services/Interfaces/IChannelAdapter.cs` | Channel abstraction |
| ResilientLlmService | `Services/Implementations/ResilientLlmService.cs` | AI fallback wrapper |
| TelegramWebhookController | `Controllers/TelegramWebhookController.cs` | Telegram webhook handler |
| AdminController | `Controllers/AdminController.cs` | Admin API |
| AuthController | `Controllers/AuthController.cs` | JWT authentication |
| AppDbContext (new entities) | `Data/AppDbContext.cs` | Extended with 7 new entities |
| Test project | `WhatsAppBot.Tests/` | 48 unit/integration tests |
| CI/CD workflow | `.github/workflows/ci.yml` | GitHub Actions pipeline |
| Documentation | `docs/` | 10+ documentation files |

## Modifications to Existing Files

| File | Changes |
|------|---------|
| `Program.cs` | Added JWT auth, Swagger, new service registrations, Telegram config, health endpoint |
| `AppDbContext.cs` | Added 7 new entity DbSets and their configurations |
| `UserSession.cs` | Added `ConversationFlightStep` property |
| `FlightConversationService.cs` | Fixed to use `ConversationFlightStep`, added passenger parsing |
| `FlightStep.cs` | No changes |
| `FlightPricingMode.cs` | No changes |
| `WhatsAppWebhookController.cs` | Fixed constructor (added imageService, visionService params), added Media using |
| `.env.example` | Removed real credentials, added Telegram, JWT, admin variables |
| `Dockerfile` | Added non-root user, health check, multi-stage improvements |
| `.gitignore` | Added database files, test results, IDE files |
| `WhatsAppBot.sln` | Added test project reference |
| `WhatsAppBot.csproj` | Added JWT, Swagger, health check, test packages |
