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

## Authentication

The app is protected by **Google OAuth** with an email allowlist — only the
configured Google accounts can sign in. See
[docs/aks-networking.md](docs/aks-networking.md) for the production/AKS specifics.

Three settings drive it:

| Setting | Purpose |
|---------|---------|
| `Authentication:Google:ClientId` | Google OAuth client ID |
| `Authentication:Google:ClientSecret` | Google OAuth client secret |
| `Authentication:AllowedEmails:Emails` (array) or `…:EmailsCsv` (comma-separated) | Permitted account emails |

If the Google credentials are absent, login is disabled and `/login` shows a
notice instead of erroring — the rest of the app still boots.

**Google Cloud Console** — register an OAuth client (type *Web application*) and
add an authorized redirect URI per environment:
- `http://localhost:5000/signin-google` (local)
- `http://<your-host>/signin-google` (deployed)

### Local credentials

- **`dotnet run`** (host) → use user-secrets (loaded only in the `Development` environment):
  ```bash
  cd src/Woistes.Api
  dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_ID"
  dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_SECRET"
  dotnet user-secrets set "Authentication:AllowedEmails:Emails:0" "you@gmail.com"
  ```
- **`docker compose`** (container) → user-secrets don't reach the container, so
  use a `.env` file (see [Docker Compose](#docker-compose) below).
- **AKS** → passed as Helm overrides from GitHub Actions secrets
  (`GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `ALLOWED_EMAILS`).

## Docker Compose

Runs the app with a real SQL Server instance. Data is persisted in a Docker volume.

Copy `.env.example` to `.env` and fill in your Google OAuth credentials (the
`.env` file is gitignored). docker-compose reads it automatically:

```bash
cp .env.example .env   # then edit .env
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
  Woistes.Api.Tests/        # API + auth integration tests (23 tests)
```

## License

See [LICENSE](LICENSE) for details.
