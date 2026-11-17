# CatCar - Auth Function (`catcar-auth-function`)

Serverless Azure Function responsible for customer authentication via CPF in the CatCar car repair shop management platform.

---

## Dedicated Serverless Authentication Architecture

```mermaid
flowchart LR
    Client[Client / Azure APIM] -->|POST /api/auth/customer| Trigger[Azure Function HTTP Trigger<br/>.NET 10 Isolated Worker]
    Trigger --> Validate[Normalize document number<br/>CPF check-digit validation]
    Validate --> Lookup[Least-privilege read-only lookup]
    Lookup --> Customers[(PostgreSQL<br/>service_operations.customers)]
    Lookup -->|Active customer| Issuer[HMAC-SHA256 JWT Token Issuer]
    Issuer -->|Customer JWT: sub, customer_id, role| Client
```

The Function only authenticates active customer records. It normalizes the submitted CPF, rejects an invalid check digit before querying the database, uses the dedicated read-only connection for `service_operations.customers`, and issues a short-lived CatCar API JWT only after a successful lookup.

---

## API & Postman

- **Customer authentication endpoint:** `POST /api/auth/customer`
- **Interactive endpoint page (local Function host):** [http://localhost:7071/api/auth/customer](http://localhost:7071/api/auth/customer)
- **Platform Swagger UI:** [http://localhost:5000/swagger](http://localhost:5000/swagger)
- **Platform Scalar API Reference:** [http://localhost:5000/docs](http://localhost:5000/docs)
- **Versioned Postman collection:** [`CatCar_Platform.postman_collection.json`](https://github.com/RiseOn-CatCar/catcar-platform/blob/main/docs/postman/CatCar_Platform.postman_collection.json)
- **Postman environment template:** [`CatCar_Platform.postman_environment.json`](https://github.com/RiseOn-CatCar/catcar-platform/blob/main/docs/postman/CatCar_Platform.postman_environment.json)

Authenticate a customer directly against the local Function host:

```bash
curl --request POST http://localhost:7071/api/auth/customer \
  --header 'Content-Type: application/json' \
  --data '{"documentNumber":"529.982.247-25"}'
```

When testing the production routing boundary, submit the same request to the APIM base URL:

```bash
curl --request POST "$APIM_URL/api/auth/customer" \
  --header 'Content-Type: application/json' \
  --header "X-Correlation-Id: $(uuidgen)" \
  --data '{"documentNumber":"529.982.247-25"}'
```

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

- **Pull requests** build and test the Function and validate its Terraform root.
- The single Azure release deploys a Function image pinned by its SHA-256 digest. Terraform requires `<foundation-acr>.azurecr.io/catcar-auth-function@sha256:<64 lowercase hex>` and owns the Container App; foundation owns its internal environment and database owns Key Vault.

Configure the Azure OIDC identifiers and shared Terraform backend coordinates. The foundation and database states must already exist, and deployed resources and state ownership must be verified before the first apply. The Function Container App name comes from the foundation state. Its default storage name is `stcatauth<suffix>`, where the suffix is the first eight hex characters of SHA-256 over `<subscription-id>:catcar` (for example, `3c4128ef`). Set `storage_account_name` to adopt an existing account, import it into its existing Terraform resource address, and review the plan before applying. Review other naming changes for replacements as well. State and Terraform plans can contain sensitive data and must not be published.

The Function preserves a valid `X-Correlation-Id` GUID, or generates one when absent or invalid, and echoes it in the response. It does not log document numbers or bearer tokens.
