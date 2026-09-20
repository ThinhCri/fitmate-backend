# ===== Stage 1: Build =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Direct csproj restore — no solution file dependency (avoids Fitmate.sln/.slnx path assumptions).
COPY src/Fitmate.Domain/Fitmate.Domain.csproj src/Fitmate.Domain/
COPY src/Fitmate.Application/Fitmate.Application.csproj src/Fitmate.Application/
COPY src/Fitmate.Infrastructure/Fitmate.Infrastructure.csproj src/Fitmate.Infrastructure/
COPY src/Fitmate.Api/Fitmate.Api.csproj src/Fitmate.Api/
RUN dotnet restore src/Fitmate.Api/Fitmate.Api.csproj

# Copy only API tree (Domain → Application → Infrastructure → Api). Tests are excluded from runtime build.
COPY src/ src/

RUN dotnet publish src/Fitmate.Api/Fitmate.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ===== Stage 2: Runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install curl for HEALTHCHECK. MUST run before USER fitmate + clean apt cache to keep image small.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Non-root user. Pre-create DataProtection key ring dir with writable perms so named volume mount preserves keys across recreate.
RUN groupadd --gid 1001 --system fitmate \
    && useradd --uid 1001 --system --gid fitmate --home /home/fitmate --shell /usr/sbin/nologin fitmate \
    && mkdir -p /home/fitmate/.aspnet/DataProtection-Keys \
    && chown -R fitmate:fitmate /home/fitmate /app

COPY --from=build /app/publish .

HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=5 \
    CMD curl -fsS http://localhost:8080/health/ready || exit 1

USER fitmate
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "Fitmate.Api.dll"]
