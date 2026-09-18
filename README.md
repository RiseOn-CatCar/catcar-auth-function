# CatCar - Auth Function (`catcar-auth-function`)

Serverless Azure Function responsible for customer authentication via CPF in the CatCar car repair shop management platform.

---

## Architecture & Responsibilities

- **Function Trigger**: HTTP Trigger (`POST /api/auth/customer`)
- **Authentication**: Validates customer CPF and active status directly against the `service_operations.customers` PostgreSQL table using a least-privilege read-only connection.
- **JWT Issuance**: Issues signed asymmetric/symmetric JWT access tokens (Audience: `CatCar.Api`, Issuer: `CatCar`) containing `sub` (customer ID), `name`, `email`, and role claims.
- **Runtime**: .NET 10.0 isolated worker process hosted on Azure Container Apps / Azure Functions.

```
┌───────────────┐        POST /api/auth/customer        ┌───────────────────────┐
│               ├──────────────────────────────────────►│                       │
│    Client     │                                       │  CatCar.AuthFunction  │
│ (Via APIM/Web)│◄──────────────────────────────────────┤  (Azure Function .NET)│
│               │        200 OK + JWT Bearer Token      └───┬───────────────┬───┘
└───────────────┘                                           │               │
                                              SELECT active │               │ Sign Token
                                              FROM customers│               ▼
                                                            ▼        ┌─────────────┐
                                                    ┌───────────────┐│ HMAC-SHA256 │
                                                    │  PostgreSQL   ││ Signing Key │
                                                    │(service_ops)  │└─────────────┘
                                                    └───────────────┘
```

---

## Project Structure

```
.
├── src/
│   ├── CatCar.AuthFunction/        # Azure Function Isolated Worker HTTP endpoints
│   └── CatCar.ServiceDefaults/     # Shared OpenTelemetry & resilience extensions
├── tests/
│   └── CatCar.AuthFunction.Tests/  # Unit tests for authentication and JWT claims
├── CatCar.AuthFunction.slnx        # Solution manifest
├── Directory.Build.props           # Common MSBuild settings
├── Directory.Packages.props        # Central Package Management (CPM)
├── Dockerfile                      # Multi-stage production container build
└── .github/workflows/ci-cd.yml     # Automated CI/CD pipeline
```

---

## Local Development & Testing

### Prerequisites
- .NET 10.0 SDK
- Docker (optional, for container runs)

### Build & Run Tests
```bash
# Restore dependencies
dotnet restore CatCar.AuthFunction.slnx

# Run unit tests
dotnet test CatCar.AuthFunction.slnx -c Release
```

---

## CI/CD Pipeline

- **Pull Requests**: Runs build and unit test suite.
- **Push to `develop`**: Builds and pushes container image to Azure Container Registry (ACR) and deploys to **Homologation** environment (`catcar-auth-function` Container App).
- **Push to `main`**: Builds and pushes container image to ACR and deploys to **Production** environment.
