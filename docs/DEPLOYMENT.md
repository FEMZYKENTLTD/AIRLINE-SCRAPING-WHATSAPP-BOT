# Deployment Guide

## Local Development

### Prerequisites
- .NET 8 SDK
- Git

### Setup
```bash
# Clone
git clone https://github.com/FEMZYKENTLTD/AIRLINE-SCRAPING-WHATSAPP-BOT.git
cd AIRLINE-SCRAPING-WHATSAPP-BOT

# Configure
cp .env.example .env
# Edit .env with your credentials

# Build and run
dotnet restore
dotnet build
cd WhatsAppBot
dotnet run
```

The app starts at `http://localhost:5260` by default.

### Running Tests
```bash
dotnet test
```

### Swagger
Navigate to `http://localhost:5260/swagger` in development mode.

## Docker

### Build
```bash
docker build -t airline-platform -f WhatsAppBot/Dockerfile .
```

### Run
```bash
docker run -p 8080:8080 \
  -e DATABASE_CONNECTION_STRING="Data Source=/data/whatsappbot.db" \
  -e WHATSAPP_ACCESS_TOKEN="your-token" \
  -e TELEGRAM_BOT_TOKEN="your-token" \
  -v $(pwd)/data:/data \
  airline-platform
```

### Environment Variables
Pass all required environment variables via `--env-file`:
```bash
docker run -p 8080:8080 --env-file .env -v $(pwd)/data:/data airline-platform
```

## Production Considerations

### Database
For production, consider migrating from SQLite to PostgreSQL:
1. Add `Npgsql.EntityFrameworkCore.PostgreSQL` package
2. Update `Program.cs` connection string configuration
3. Generate and apply migrations

### Secrets Management
- Use a secrets manager (Azure Key Vault, AWS Secrets Manager)
- Never commit `.env` files
- Rotate credentials regularly
- Use GitHub repository secrets for CI/CD

### Health Checks
The `/api/admin/health` endpoint reports:
- Application status
- Database connectivity

### Logging
Logs are written to:
- Console (development)
- `logs/whatsapp-bot-*.log` (file)

For production, consider adding:
- Structured log aggregation (ELK, Seq, Application Insights)
- Alert monitoring

## Webhook Configuration

### WhatsApp (Meta Cloud API)
1. Create a Meta Business app
2. Configure webhook URL: `https://your-domain.com/webhook`
3. Set verify token (must match `WHATSAPP_VERIFY_TOKEN`)
4. Subscribe to `messages` events

### Telegram
Set webhook via API:
```bash
curl -X POST "https://api.telegram.org/bot<YOUR_TOKEN>/setWebhook" \
  -d "url=https://your-domain.com/telegram" \
  -d "secret_token=YOUR_SECRET"
```

## GitHub Actions CI/CD

The workflow runs on push/PR to `main`:
1. Restore dependencies
2. Build solution
3. Run tests
4. Build Docker image

Configure repository secrets for any credentials needed during CI.
