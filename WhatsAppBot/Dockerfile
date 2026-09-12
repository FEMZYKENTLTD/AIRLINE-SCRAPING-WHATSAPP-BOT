# ═══════════════════════════════════════════════════════════════════
# Multi-stage Dockerfile for Airline Service Management Platform
# ═══════════════════════════════════════════════════════════════════

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies (cached layer)
COPY ["WhatsAppBot.csproj", "./"]
RUN dotnet restore

# Copy all source code and publish
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Install curl for health check and create non-root user for security
RUN apt-get update && apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/* && \
    groupadd -r appuser && useradd -r -g appuser -d /app -s /sbin/nologin appuser

WORKDIR /app

# Create logs directory with proper permissions
RUN mkdir -p /app/logs && chown -R appuser:appuser /app

# Copy published output
COPY --from=build /app/publish .

# Switch to non-root user
USER appuser

# Expose HTTP port
EXPOSE 8080

# Configure ASP.NET to listen on all interfaces
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/ || exit 1

ENTRYPOINT ["dotnet", "WhatsAppBot.dll"]
