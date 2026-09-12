# Local Development Guide

## Prerequisites

| Software | Version | Purpose |
|----------|---------|---------|
| .NET 8 SDK | 8.0+ | Build and run |
| Git | any | Version control |
| Docker | optional | Container testing |

## Quick Start

```bash
# 1. Clone
git clone https://github.com/FEMZYKENTLTD/AIRLINE-SCRAPING-WHATSAPP-BOT.git
cd AIRLINE-SCRAPING-WHATSAPP-BOT

# 2. Configure
cp .env.example .env
# Edit .env with your credentials (see SECRET-SETUP-GUIDE.md)

# 3. Restore & Build
dotnet restore
dotnet build

# 4. Run
cd WhatsAppBot
dotnet run
```

The application starts at `http://localhost:5260` (or check console output).

## Project Structure

```
AIRLINE-SCRAPING-WHATSAPP-BOT/
├── WhatsAppBot.sln                    # Solution file
├── WhatsAppBot/                       # Main application
│   ├── WhatsAppBot.csproj
│   ├── Program.cs                     # Entry point
│   ├── Controllers/                   # API controllers
│   ├── Data/                          # EF Core context
│   ├── Models/                        # Domain entities
│   ├── Services/                      # Business logic
│   ├── Extensions/                    # Configuration helpers
│   ├── Dockerfile
│   ├── appsettings.json
│   └── Properties/
├── WhatsAppBot.Tests/                 # Test project
│   ├── WhatsAppBot.Tests.csproj
│   ├── Database/
│   ├── Services/
│   └── Controllers/
├── docs/                              # Documentation
├── .github/workflows/ci.yml          # CI/CD
├── .env.example
├── .gitignore
└── README.md
```

## Database Setup

The application uses SQLite via EF Core. The database file (`whatsappbot.db`) is created automatically on first run.

```bash
# Database is created via EnsureCreated() on startup
# No manual migration steps needed for development

# To use EF Core migrations (optional):
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate --project WhatsAppBot
dotnet ef database update --project WhatsAppBot
```

## Running the Application

```bash
cd WhatsAppBot
dotnet run
```

### Verify Startup

1. **Root endpoint**: http://localhost:5260/
   - Returns JSON with service info and feature list

2. **Swagger** (Development only): http://localhost:5260/swagger
   - Interactive API documentation
   - Try admin endpoints by clicking "Authorize" and entering your JWT

3. **Health check**: http://localhost:5260/api/admin/health
   - Returns `{ "status": "Healthy", "database": "Connected" }`

4. **WhatsApp webhook verification**:
   ```bash
   curl "http://localhost:5260/webhook?hub.mode=subscribe&hub.verify_token=YOUR_TOKEN&hub.challenge=test123"
   ```
   Should return: `test123`

## Running Tests

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --verbosity normal

# Run specific test class
dotnet test --filter "FullyQualifiedName~UserServiceTests"

# Run specific test
dotnet test --filter "FindOrCreateByChannel_CreatesNewUser_WhenNotExists"
```

## Webhook Development

### WhatsApp (Meta Cloud API)

For local development, use [ngrok](https://ngrok.com) to expose your local server:

```bash
# Install ngrok
ngrok http 5260

# Copy the HTTPS URL (e.g., https://abc123.ngrok.io)
# In Facebook Developer Portal:
#   WhatsApp → Configuration → Webhook
#   Callback URL: https://abc123.ngrok.io/webhook
#   Verify Token: (your WHATSAPP_VERIFY_TOKEN value)
```

### Telegram

```bash
# Set webhook to your ngrok URL
curl -X POST "https://api.telegram.org/bot{YOUR_TOKEN}/setWebhook" \
  -d "url=https://abc123.ngrok.io/telegram" \
  -d "secret_token=YOUR_SECRET"
```

## Docker

```bash
# Build
docker build -t airline-platform -f WhatsAppBot/Dockerfile WhatsAppBot/

# Run
docker run -p 8080:8080 --env-file .env airline-platform

# Verify
curl http://localhost:8080/
curl http://localhost:8080/api/admin/health
```

## Troubleshooting

### "The framework 'Microsoft.AspNetCore.App' version '8.0.0' was not found"
Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0

### "SQLite Error: no such table"
Delete `whatsappbot.db` and restart the application (development only).

### "JWT secret is too short or default"
Set `JWT_SECRET` in `.env` to a string of at least 32 characters:
```bash
JWT_SECRET=$(openssl rand -base64 32)
```

### "Meta send failed: 401"
Check that `WHATSAPP_ACCESS_TOKEN` is valid and not expired.

### Telegram bot not responding
1. Verify `TELEGRAM_BOT_TOKEN` is correct
2. Check webhook is set: `curl "https://api.telegram.org/bot{TOKEN}/getWebhookInfo"`
3. Ensure your server is publicly accessible (use ngrok)
