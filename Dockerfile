# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AI.ChatAgent.sln ./
COPY src/AI.ChatAgent/AI.ChatAgent.csproj             src/AI.ChatAgent/
COPY tests/AI.ChatAgent.Tests/AI.ChatAgent.Tests.csproj tests/AI.ChatAgent.Tests/

RUN dotnet restore AI.ChatAgent.sln

COPY . .

WORKDIR /src/src/AI.ChatAgent
RUN dotnet publish AI.ChatAgent.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ── Stage 2: Test ─────────────────────────────────────────────────────────────
FROM build AS test
WORKDIR /src
RUN dotnet test tests/AI.ChatAgent.Tests/AI.ChatAgent.Tests.csproj \
    --no-restore \
    --filter "Category!=Integration" \
    --logger "console;verbosity=normal"

# ── Stage 3: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd --gid 1000 appgroup && \
    useradd --uid 1000 --gid 1000 --no-create-home --shell /bin/false appuser

RUN mkdir -p /app/logs /app/SampleData/PDFs /app/SampleData/Files && \
    chown -R appuser:appgroup /app

COPY --from=build /app/publish .
COPY --chown=appuser:appgroup src/AI.ChatAgent/SampleData ./SampleData

USER appuser

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["dotnet", "AI.ChatAgent.dll"]