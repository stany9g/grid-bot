# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

GridBot is a cryptocurrency trading bot built as a .NET Aspire distributed application using .NET 10.0 and containers. The application is designed to automate cryptocurrency trading operations while following best practices for distributed systems.

The project follows the .NET Aspire architecture pattern with service defaults, orchestration, and multiple project components.

## Development Principles

**KISS - Keep It Simple Stupid**

This project emphasizes simplicity and clean code above all else:

- **Avoid Complexity**: If a simpler solution exists, use it. Don't over-engineer.
- **Clean Code**: Code should be self-documenting and easy to understand.
- **Minimal Abstractions**: Only create abstractions when they provide clear value.
- **Direct Solutions**: Prefer straightforward implementations over clever ones.
- **Question Complexity**: If something feels complex, it probably is. Simplify it.

When adding features or making changes:
- Choose the simplest approach that solves the problem
- Favor readability over premature optimization
- Avoid adding layers of indirection without clear benefit
- Keep functions and classes focused on a single responsibility

## Architecture

### Project Structure

The solution consists of four main projects:

1. **GridBot.AppHost** - Aspire orchestration host that defines and manages the distributed application
   - Configures service discovery, health checks, and dependencies
   - Defines Redis cache resource
   - Orchestrates startup order: Redis → ApiService → Web
   - Entry point: `AppHost.cs`

2. **GridBot.ApiService** - Backend API service
   - Minimal API with OpenAPI support
   - Exposes `/weatherforecast` endpoint (sample)
   - Uses service defaults for telemetry, health checks, and resilience
   - Entry point: `Program.cs`

3. **GridBot.Web** - Blazor Server frontend
   - Interactive server-side Blazor components
   - Consumes ApiService via `WeatherApiClient`
   - Uses Redis for output caching
   - Service discovery resolves `https+http://apiservice`
   - Entry point: `Program.cs`
   - Components located in `GridBot.Web/Components/`

4. **GridBot.ServiceDefaults** - Shared library for cross-cutting concerns
   - Configures OpenTelemetry (metrics, traces, logs)
   - Provides service discovery and HTTP client defaults
   - Defines health check endpoints (`/health`, `/alive`)
   - Adds standard resilience handlers
   - Key file: `Extensions.cs`

### Service Communication

- Web → ApiService: Service discovery via `https+http://apiservice` scheme (prefers HTTPS, falls back to HTTP)
- Web uses Redis for output caching
- All services report health via `/health` endpoint
- AppHost configures `.WaitFor()` dependencies to ensure proper startup sequence

### Key Patterns

- All services call `builder.AddServiceDefaults()` to register Aspire defaults
- OpenTelemetry instrumentation is automatic for ASP.NET Core, HTTP clients, and runtime metrics
- Health checks use tags (`live`) to distinguish liveness from readiness
- HTTP clients get resilience and service discovery by default

## Development Commands

### Running the Application

Run the entire distributed application via the AppHost:
```bash
dotnet run --project GridBot.AppHost
```

This starts:
- Aspire Dashboard (provides observability UI)
- Redis container
- ApiService
- Web frontend

Access points:
- Aspire Dashboard: Check terminal output for dashboard URL
- Web frontend: `https://gridbot.dev.localhost:17018` or `http://gridbot.dev.localhost:15225`

### Building

Build entire solution:
```bash
dotnet build GridBot.slnx
```

Build specific project:
```bash
dotnet build GridBot.ApiService/GridBot.ApiService.csproj
```

### Running Individual Services

Run ApiService standalone:
```bash
dotnet run --project GridBot.ApiService
```

Run Web frontend standalone:
```bash
dotnet run --project GridBot.Web
```

Note: Running services standalone bypasses Aspire orchestration. Service discovery won't work without AppHost.

### Restore Dependencies

```bash
dotnet restore
```

### Clean Build Artifacts

```bash
dotnet clean
```

## Testing

Currently no test projects exist. When adding tests:
- Run tests: `dotnet test`
- Run specific test: `dotnet test --filter "FullyQualifiedName~TestName"`
- Run tests with coverage: `dotnet test --collect:"XPlat Code Coverage"`

## Configuration

### Service Defaults Behavior

`GridBot.ServiceDefaults` automatically configures:
- OpenTelemetry exporters (OTLP if `OTEL_EXPORTER_OTLP_ENDPOINT` is set)
- Service discovery for HTTP clients
- Standard resilience patterns (retries, circuit breakers, timeouts)
- Health check endpoints (only in Development environment)

### Environment Variables

Key Aspire environment variables (set by AppHost):
- `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT`: Environment name
- `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL`: OpenTelemetry endpoint
- `OTEL_EXPORTER_OTLP_ENDPOINT`: Enables OTLP exporter if set

### User Secrets

AppHost uses user secrets (ID: `c35c3631-20fe-4062-8c51-1664d9a47322`):
```bash
dotnet user-secrets set "key" "value" --project GridBot.AppHost
```

## Important Implementation Details

### Service Discovery URLs

When referencing services from Web or other clients, use the format:
- `https+http://servicename` - Prefers HTTPS, falls back to HTTP
- Service name matches the AppHost registration (e.g., `apiservice`)

### Health Checks

- `/health` - All health checks must pass (readiness)
- `/alive` - Only checks tagged with "live" (liveness)
- Health checks only exposed in Development environment

### Adding New Services

When adding a new project:
1. Reference `GridBot.ServiceDefaults`
2. Call `builder.AddServiceDefaults()`
3. Call `app.MapDefaultEndpoints()` for health checks
4. Register in `AppHost.cs` using `builder.AddProject<>()`
5. Configure dependencies with `.WithReference()` and `.WaitFor()`

### OpenTelemetry

Telemetry is automatically collected for:
- ASP.NET Core requests (excluding health endpoints)
- HTTP client calls
- Runtime metrics (GC, thread pool, etc.)

View telemetry in the Aspire Dashboard when running via AppHost.

## Technology Stack

- .NET 10.0
- .NET Aspire 13.0.0 (container orchestration and service management)
- Blazor Server (Interactive)
- Minimal APIs
- Redis (via Aspire.Hosting.Redis)
- Containers (managed via Aspire)
- OpenTelemetry
- Service Discovery & Resilience

## Notes

- The solution uses Visual Studio's XML-based solution format (`.slnx`)
- Bootstrap 5 is included in `GridBot.Web/wwwroot/lib/bootstrap/`
- Current implementation includes sample weather forecast functionality as temporary scaffolding - this will be replaced with cryptocurrency trading features
