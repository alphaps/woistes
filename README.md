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

# Run the API
dotnet run --project src/Woistes.Api
```

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
