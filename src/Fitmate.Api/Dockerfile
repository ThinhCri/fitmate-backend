# ===== Stage 1: Build =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore theo layer: copy csproj trước để cache nuget.
COPY Fitmate.sln ./
COPY src/Fitmate.Domain/Fitmate.Domain.csproj src/Fitmate.Domain/
COPY src/Fitmate.Application/Fitmate.Application.csproj src/Fitmate.Application/
COPY src/Fitmate.Infrastructure/Fitmate.Infrastructure.csproj src/Fitmate.Infrastructure/
COPY src/Fitmate.Api/Fitmate.Api.csproj src/Fitmate.Api/
COPY tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj tests/Fitmate.UnitTests/
RUN dotnet restore src/Fitmate.Api/Fitmate.Api.csproj

# Copy toàn bộ source.
COPY src/ src/
COPY tests/ tests/

# Build (Release).
RUN dotnet publish src/Fitmate.Api/Fitmate.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ===== Stage 2: Runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Non-root user.
RUN groupadd --gid 1001 --system fitmate \
    && useradd --uid 1001 --system --gid fitmate fitmate \
    && mkdir -p /app/data \
    && chown -R fitmate:fitmate /app

COPY --from=build /app/publish .

# Healthcheck (override trong compose).
HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=5 \
    CMD curl -fsS http://localhost:8080/health/ready || exit 1

USER fitmate
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "Fitmate.Api.dll"]
