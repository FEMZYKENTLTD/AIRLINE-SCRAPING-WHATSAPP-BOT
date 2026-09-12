# ═══════════════════════════════════════════════════════════════════════════
# Multi-stage Dockerfile — Multi-Channel AI Service Management Platform
#
#   Stage 1 (build)    : .NET 8 SDK — restore + publish (Release)
#   Stage 2 (runtime)  : ASP.NET Core 8 runtime, non-root user, /health check
#
# The image is intentionally runnable WITHOUT any external provider
# credentials (WhatsApp/Telegram/Azure OpenAI/Amadeus are all optional).
# ═══════════════════════════════════════════════════════════════════════════

# ── Stage 1: Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies (cached layer)
COPY ["WhatsAppBot.csproj", "."]
RUN dotnet restore

# Copy all source code and publish
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Install curl for the health check and create a non-root user for security
RUN apt-get update && apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/* && \
    groupadd -r appuser && useradd -r -g appuser -d /app -s /sbin/nologin appuser

WORKDIR /app

# Data + logs directories with proper permissions
# (SQLite database lives in /app/data, Serilog files in /app/logs)
RUN mkdir -p /app/logs /app/data && chown -R appuser:appuser /app

# Copy published output
COPY --from=build /app/publish .

# Switch to non-root user
USER appuser

# Expose HTTP port
EXPOSE 8080

# Configure ASP.NET to listen on all interfaces
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Default database location (override with DATABASE_CONNECTION_STRING)
ENV DATABASE_CONNECTION_STRING="Data Source=/app/data/whatsappbot.db"

# Health check — liveness only (does NOT require external providers)
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "WhatsAppBot.dll"]
