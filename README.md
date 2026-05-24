# Woistes

A modern, web-based replacement for [WhereIsIt?](http://www.whereisit-soft.com) — a file cataloguing software originally developed for Windows 95/NT (1997-2001).

Upload legacy WhereIsIt `.CTF` catalogue files from the browser, browse their contents visually, and search across all catalogued files.

## Features

- **CTF Import** — parse WhereIsIt? binary catalogue files (v3.00) and persist to SQL Server
- **Tree Browser** — Explorer-like navigation through catalogue > disk > folder hierarchy
- **Search** — glob-pattern search across all imported catalogues with pagination
- **REST API** — upload, browse, and search endpoints

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 10 |
| Frontend | Blazor Server |
| Backend | ASP.NET Core Minimal API |
| ORM | Entity Framework Core |
| Database | SQL Server 2022 |
| Deployment | Docker / Kubernetes (AKS) |

## Getting Started

```bash
# Build
dotnet build Woistes.sln

# Run tests
dotnet test

# Run the API (in-memory database)
dotnet run --project src/Woistes.Api
```

The app starts at `http://localhost:5000` by default. Without a connection string it uses an in-memory database (data is lost on restart).

## Docker Compose

Runs the app with a real SQL Server instance. Data is persisted in a Docker volume.

```bash
docker compose up --build
```

The app will be available at `http://localhost:5000`. SQL Server is exposed on port `1433` (sa password: `Woistes_Dev_123!`).

To stop and remove containers:

```bash
docker compose down
```

To also delete the database volume:

```bash
docker compose down -v
```

## Helm (Kubernetes)

Deploy to a Kubernetes cluster using the Helm chart in `k8s/`.

```bash
# Build and load the Docker image (e.g., for a local cluster like minikube/kind)
docker build -t woistes:latest .

# Install the chart
helm install woistes ./k8s

# Or with custom values
helm install woistes ./k8s \
  --set image.repository=myregistry.azurecr.io/woistes \
  --set image.tag=1.0.0 \
  --set sqlserver.password='MySecurePassword!' \
  --set ingress.enabled=true \
  --set ingress.host=woistes.example.com

# Check status
helm status woistes
kubectl get pods

# Upgrade after changes
helm upgrade woistes ./k8s

# Uninstall
helm uninstall woistes
```

The chart deploys:
- App deployment with health checks
- ClusterIP service (port 80 -> 8080)
- SQL Server StatefulSet with persistent storage
- Secret for the SA password / connection string
- Optional ingress (disabled by default)

## Project Structure

```
src/
  Woistes.Domain/           # Domain entities, interfaces
  Woistes.Infrastructure/   # EF Core, SQL Server, repositories
  Woistes.CtfParser/        # CTF binary format parser
  Woistes.Api/              # Web API + Blazor UI (single host)
tests/
  Woistes.CtfParser.Tests/  # Parser unit tests (25 tests)
  Woistes.Api.Tests/        # API integration tests (14 tests)
```

## License

See [LICENSE](LICENSE) for details.
