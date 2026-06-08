# syntax=docker/dockerfile:1
ARG PROJECT=CveNet.Orchestrator

# ── Build stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG PROJECT
WORKDIR /src

# Copy solution and all project files first to leverage layer caching
COPY CveNet.sln ./
COPY src/CveNet.Shared/CveNet.Shared.csproj                   src/CveNet.Shared/
COPY src/CveNet.Orchestrator/CveNet.Orchestrator.csproj        src/CveNet.Orchestrator/
COPY src/CveNet.PromptParser/CveNet.PromptParser.csproj        src/CveNet.PromptParser/
COPY src/CveNet.CveSearch/CveNet.CveSearch.csproj              src/CveNet.CveSearch/
COPY src/CveNet.Prioritization/CveNet.Prioritization.csproj    src/CveNet.Prioritization/
COPY src/CveNet.Report/CveNet.Report.csproj                    src/CveNet.Report/

RUN dotnet restore "src/${PROJECT}/${PROJECT}.csproj"

# Copy all source
COPY . .

RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Runtime stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
ARG PROJECT
WORKDIR /app

# Non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish .

# Kestrel listens on 8080 (non-privileged, AKS-friendly)
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

# ARG doesn't survive into exec-form ENTRYPOINT; use shell form so the variable expands.
ENTRYPOINT dotnet ${PROJECT}.dll
