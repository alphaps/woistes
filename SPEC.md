# Woistes - Project Specification

## Overview

**Woistes** is a modern, web-based replacement for [WhereIsIt?](http://www.whereisit-soft.com), a file cataloguing software originally developed by Robert Galle (1997-2001) for Windows 95/NT.

The initial objective is to **open legacy WhereIsIt CTF catalogue files from the browser**, browse their contents visually (directory tree, file lists), search for items, and store the parsed data in SQL Server for fast querying. Volume scanning and new cataloguing will come in a later phase.

Woistes runs as a C# / .NET Core application deployed on Azure AKS (Kubernetes) using distroless containers, with a SQL Server backend for persistent storage.

---

## Implementation Status

### Done

- **Domain model** (`Woistes.Domain`): `Catalogue`, `Disk`, `CatalogueEntry` entities
- **CTF Parser** (`Woistes.CtfParser`): fully working binary parser for CTF v3.00 files
  - Parses all 5 file entry marker types (0x001C, 0x002C, 0x0058, 0x000C, 0x0048)
  - **Pre-order tree model** (verified against the WhereIsIt GUI): each disk section is a flat file-entry run followed by a directory-record block; the tree is rebuilt via a depth stack where each folder consumes its `directFileCount` files. This replaced an earlier incorrect "global file index" model that truncated files and left populated folders empty.
  - Three directory-record type variants (0x0C/0x18/0x2C) with distinct payload lengths (40/36/41 bytes)
  - Detects disk boundaries via filesystem type string scanning
  - Resync logic to recover from alignment gaps in the file region
  - **28 passing tests** covering header parsing, file entries, directory tree, sizes, full paths, multi-disk support, and exact GUI-verified counts for `120 Go.CTF` disk 0 (17,195 files / 1,083 folders; root 13 files + 10 folders; "New Setups" 5 files + 2 subfolders)
- **EF Core data layer** (`Woistes.Infrastructure`): DbContext, entity configurations (with indexes on Name/FullPath/ParentId), `ICatalogueRepository` + SQL Server implementation with search (LIKE), tree browsing, and DI extension method
  - **Import persists the full entry tree in a single `SaveChanges`**: stamps `DiskId` recursively and adds the root entries; EF tracks the graph via the self-referencing `Children` navigation and assigns `ParentId` automatically. (Gotcha fixed: the prior code set `Children = []` before saving, discarding every nested entry so all folders browsed as empty; a per-level save approach was correct but did tens of thousands of round-trips — one graph save is both correct and fast.)
  - **Large-import tuning**: a catalogue like `120 Go.CTF` imports ~106k entries (95,505 files + 10,247 folders across 8 disks). Three levers keep this fast: (1) `AutoDetectChangesEnabled = false` around the bulk `AddRange` — EF's per-operation rescan is O(n²) and dominates otherwise; (2) SQL Server `MaxBatchSize(1000)` — fewer round-trips; (3) a `Logging` config (`Microsoft.EntityFrameworkCore: Warning`, `Microsoft.AspNetCore: Warning`). (Gotcha: default Information-level EF logging in Development logged ~106k SQL statements per import — looked like a runaway loop/flood, and writing that many lines to container stdout is itself a major slowdown.)
- **Target framework upgrade**: all projects migrated from net8.0 to net10.0
- **ASP.NET Core Web API** (`Woistes.Api`): minimal API with endpoints for CTF upload/import, catalogue CRUD, tree browsing (lazy-load children by disk/parent), and paginated search with glob patterns. **34 integration tests** using WebApplicationFactory + InMemory DB (incl. auth, logout/denied, deep-tree browse regression, import progress).

- **Import progress reporting**: `AddAsync` accepts an optional `IProgress<ImportProgress>` and saves one disk per `SaveChanges`, reporting `(DisksSaved, DisksTotal, EntriesSaved, EntriesTotal)`. The Blazor Upload page shows a live progress bar (import of a large catalogue is ~tens of seconds, dominated by the DB save, not parsing). Reporting is opt-in so the REST endpoint and tests are unaffected.

- **Blazor Web UI** (merged into `Woistes.Api`): Blazor Server components for catalogue dashboard, CTF upload (with import progress bar), tree browser with breadcrumbs, and paginated search. Served alongside the REST API from a single host.

- **Docker & Kubernetes**: multi-stage Dockerfile, docker-compose (app + SQL Server), Helm chart with deployment, service, StatefulSet SQL Server, secrets, and optional ingress. Auto-migration on startup.

- **CI/CD pipeline** (GitHub Actions): `test` (gates build) → `buildImage` → `deploy` to AKS via Helm bake, old image cleanup. OIDC auth with federated credentials (no secrets stored).
- **Code coverage** (GitHub Actions, informational): a `coverage` job runs in parallel with `test`, collects coverage via `coverlet.collector` (`--collect:"XPlat Code Coverage" --settings coverlet.runsettings`), renders an HTML + Markdown report with ReportGenerator (posted to the job summary, uploaded as an artifact). It is `continue-on-error` and in no `needs` chain, so it never blocks build/deploy. Kept off the gating `test` job because instrumentation slows the parser tests (~12s → ~2m). `coverlet.runsettings` excludes generated/untestable code (EF migrations, model snapshot, design-time factory) and skips auto-properties so the number reflects real logic. **Run locally** (with `sampleCTF/` present) merged coverage is ~82% line / ~69% branch: parser 99.7%, endpoints/config 100%, repository ~56–85%.

  Two coverage caveats, both accepted:
  - **Parser ~0% on CI by design.** The CtfParser tests need the sample `.CTF` files, which are intentionally **not committed because they contain personal information** (real file/folder names and paths from the user's own drives). On CI those files are absent so the parser tests no-op (they still pass — they early-return), leaving `Woistes.CtfParser` at ~0% on the CI report. Parser coverage is only meaningful locally. (A tell: the 28 parser tests run in <100ms on CI vs ~12s locally — they aren't actually parsing anything.) Future option: a small hand-crafted CTF fixture with synthetic (PII-free) data could be committed to give the parser real CI coverage.
  - **Blazor Razor components ~0%.** Integration tests exercise the API endpoints, not the rendered UI. bUnit component tests would close this if UI coverage becomes a priority.

  `TestResults/`, `CoverageReport/`, and `*.cobertura.xml` are gitignored.
- **Helm chart hardened for AKS**: ingressClassName for NGINX, SQL Server securityContext (runAsUser 0, fsGroup 10001) for Azure container runtime compatibility.
- **Google OAuth authentication**: cookie-based auth with Google login, email allowlist middleware (403 for non-permitted emails). Allowed emails configured via Kubernetes secret (CSV) or appsettings (array). User-secrets for local dev. Google ClientId/ClientSecret/AllowedEmails passed as Helm overrides from GitHub Actions secrets (`GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `ALLOWED_EMAILS`). 7 new auth tests.
- **Logout**: `POST /logout` (sidebar shows signed-in email + Sign out button), redirects to an anonymous `/loggedout` confirmation page that links back to login. Login challenge sends `prompt=select_account` so users can sign in with a different Google account.
- **Access-denied UX**: a non-allowlisted (but Google-authenticated) user is redirected to an anonymous `/denied` page naming their account, with a "Sign out & switch account" button — instead of a bare 403 that trapped them. The allowlist middleware exempts `/denied`, `/login`, `/logout`, `/loggedout`, `/health` so blocked users can escape. 5 auth tests total for logout + denied flow.
- **Public access via NGINX ingress on AKS**: documented in [docs/aks-networking.md](docs/aks-networking.md) — covers the LB health-probe path fix and the HTTP-only OAuth cookie workaround.
- **Configurable ingress class**: `ingress.className` value (defaults to `nginx` for AKS; set to `traefik` for Rancher Desktop / k3s) so the same chart works locally and in the cloud.
- **Resilient startup migration** (`DatabaseInitializer`): tolerates concurrent "database/object already exists" errors (rolling-deploy race where two pods migrate at once) and retries transient failures, instead of crash-looping. 5 new tests.

### Next

- **TLS / HTTPS** for the public endpoint (cert-manager + Let's Encrypt), then revert the HTTP-only OAuth cookie workaround
- Items from "Future Phases" section

---

## Goals

1. **Import and browse legacy CTF files** - upload WhereIsIt `.CTF` catalog files from the browser, parse them, store in SQL Server, and browse/search their contents
2. **Modern web UI** - responsive, accessible, no desktop installation required
3. **Cloud-native architecture** - containerized, horizontally scalable, infrastructure-as-code
4. **Future-ready** - data model supports eventual volume scanning, but scanning is not in initial scope

---

## Architecture

### Infrastructure

| Component | Technology |
|-----------|-----------|
| Container runtime | Azure AKS (Kubernetes) |
| Application image | .NET 10 on distroless container (`mcr.microsoft.com/dotnet/runtime-deps`) |
| Database | SQL Server on AKS with PVC-backed persistent storage |
| Ingress | NGINX Ingress Controller or Azure Application Gateway |

### Application Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Blazor Server or Blazor WebAssembly (TBD) |
| Backend API | ASP.NET Core Web API |
| ORM | Entity Framework Core |
| Database | SQL Server 2022 |
| Authentication | None (single-user, local trust) - future: Azure AD / Entra ID |

### Deployment Topology

```
[Browser] --> [Ingress/LB] --> [Woistes Pod(s)] --> [SQL Server Pod]
                                                         |
                                                    [PVC (Azure Disk)]
```

---

## Core Features (Phase 1)

### 1. CTF File Import

- Upload WhereIsIt? `.CTF` catalog files from the browser
- Parse the binary CTF format (version "Catalog 3.00") server-side
- Extract all available metadata: directory tree, file names, sizes, timestamps, volume info
- Persist parsed data into SQL Server
- Show import progress and report any parse errors/warnings
- Support all sample files (small USB key catalogues through large 100+ disc collections)

### 2. Browsing & Navigation

- Explorer-like tree view (catalogue > disk > folder hierarchy)
- List view with sortable columns (name, size, date modified, type, full path)
- Breadcrumb navigation
- Detail/properties panel for selected items (file metadata, volume info)
- Lazy-load tree nodes for large catalogues (20 MB+ CTF files with 30k+ entries)

### 3. Search

- Quick search by filename pattern (glob-style wildcards)
- Search criteria: name, size range, date range, file extension, path contains
- Search across all imported catalogues or scoped to a specific one
- Results displayed as a flat list with full path context
- Paginated results

### 4. Catalogue Management

- List all imported catalogues with summary stats (disk count, file count, total size)
- Rename or delete imported catalogues
- View per-disk metadata (volume label, filesystem, capacity, scan date)

---

## Data Model (High-Level)

```
Catalogue                           (one per imported CTF file)
  ├── Name, SourceFileName, ImportedDate, FileCount, FolderCount
  └── Disks[]
       ├── VolumeLabel, SerialNumber, FilesystemType
       ├── MediaType, TotalSize, FreeSpace, OriginalScanDate
       └── RootFolder
            └── Entries[] (recursive)
                 ├── Name, IsDirectory, FullPath
                 ├── Size, CreatedDate, ModifiedDate
                 └── Children[] (if directory)
```

The model is intentionally flat for Phase 1 (no disk groups, categories, flags, or descriptions — those can be added when the CTF parser matures and later features need them).

---

## CTF File Format (Reverse-Engineered)

Fully reverse-engineered from analysis of WhereIsIt? 3.x `.CTF` binary files.

### Header (fixed structure)

| Offset | Size | Description |
|--------|------|-------------|
| 0x00 | 12 | Magic: `"Catalog 3.00"` (ASCII, null-padded) |
| 0x0C | 4 | Unknown uint32 (flags/version?) |
| 0x10 | 2 | Disk count N (uint16 LE) |
| 0x12 | N×2 | Disk ID table: array of uint16 (1-based) |
| +0 | 2 | Catalogue name length (uint16 LE) |
| +2 | varies | Catalogue name (ASCII, length-prefixed) |

### Disk Headers (in-band markers)

Disk boundaries are identified by scanning for filesystem type strings in the binary. Each disk header has the format:

```
[label_len: 1 byte] [label: N bytes] [fs_len: 1 byte] [fs_name: M bytes]
```

Recognized filesystem types: `FAT`, `FAT32`, `exFAT`, `NTFS`, `CDFS`, `UDF`.

Some disks (e.g., TrueCrypt volumes) may lack a filesystem marker entirely and will not be parseable.

### File Entry Records

Five marker types, all sharing the same prefix structure:

```
[marker: 2 bytes LE] [name_len: 1 byte] [name: N bytes] [metadata: variable]
```

| Marker | Metadata size | Fields |
|--------|--------------|--------|
| `0x001C` | 16 bytes | modTime(2) + modDate(2) + creTime(2) + creDate(2) + accTime(2) + accDate(2) + size(4) |
| `0x002C` | 16 bytes | same layout as `0x001C` |
| `0x0058` | 12 bytes | modTime(2) + modDate(2) + accTime(2) + accDate(2) + size(4) |
| `0x000C` | 17 bytes | attributes(1) + modTime(2) + modDate(2) + creTime(2) + creDate(2) + accTime(2) + accDate(2) + size(4) |
| `0x0048` | 13 bytes | modTime(2) + modDate(2) + 5 unidentified bytes + size(4) — rare; layout not fully reverse-engineered |

- All timestamps are **DOS date/time format** (same as FAT filesystem)
- The attribute byte in `0x000C` entries corresponds to DOS file attributes (system, hidden, archive, etc.)
- Within a disk section, **all file entries come first** (a single flat run, in pre-order tree order), **then** the directory-record block. Files are not interleaved with directory records.

### Directory Records

Directory records form a contiguous block after the file entries. Three type variants exist, differing only in payload length:

```
[02 00] [type: 1 byte] [00] [depth: 1 byte] [name_len: 2 bytes LE]
[name: N bytes] [payload]
```

| Type byte | Payload length (after name) |
|-----------|------------------------------|
| `0x0C` | 40 bytes |
| `0x18` | 36 bytes |
| `0x2C` | 41 bytes |

Payload fields (uint32 LE, from start of payload): `[nulls][f1][f2][directFileCount][...][DOS date-time + size]`.

| Field | Description |
|-------|-------------|
| `depth` | Nesting level (1 = top-level directory, 2+ = subdirectory) |
| `directFileCount` | payload offset +12: number of files directly in this folder (`0xFFFFFFFF` sentinel ⇒ 0) |
| `f1`, `f2` | Large cumulative counters (reach ~170k for a 17k-file disk; likely byte offsets). **Not used** for tree reconstruction. |

### Tree Reconstruction (the key insight)

The earlier model — treating `f1`/`f2` as a "global file index / file count" — was **wrong** and produced corrupt trees. The verified model:

- Files are stored in **pre-order depth-first traversal**: the disk's root files first, then folder-by-folder.
- The directory records are also in pre-order, each carrying its `depth` and `directFileCount`.
- Rebuild by walking the dir records with a **depth stack**; each folder consumes its next `directFileCount` files from the flat list. The leading files consumed by no folder (`totalFiles − Σ directFileCount`) are the disk's root files.

### Overall File Structure

```
[Header]
[Metadata section: ~100 bytes of per-catalogue counts and scan names]
[Disk 1: header + [flat file-entry run] + [directory-record block]]
[Disk 2: ...]
...
[Disk N: ...]
```

### Known Sample Files

| File | Size | Disks | Files Parsed | Description |
|------|------|-------|-------------|-------------|
| Boumbo40.ctf | 1.6 MB | 4 | ~49,000 | Kingston USB + NTFS drives |
| 120 Go.CTF | 5.0 MB | 8 | ~36,000 | 120 GB hard drive partitions |
| Mes CD 1.CTF | 14.9 MB | 111 | Large | CD collection |
| Mes CD 2.CTF | 5.2 MB | 133 | Large | CD collection continued |
| mypassport1000.CTF | 20.3 MB | 4* | ~306,000 | WD My Passport 1 TB (NTFS) |

*Header declares 5 disks but one (TC volume) has no FS marker and cannot be parsed.

---

## Deliverables

1. **CTF Parser library** (standalone class library):
   - Binary reader for CTF format v3.00
   - Map CTF structures to domain model
   - Validation and error reporting for malformed files
   - Unit tests against all sample CTF files

2. **ASP.NET Core Web API** with REST endpoints for:
   - CTF file upload and import
   - Catalogue listing and deletion
   - Tree browsing (get children of a node, paginated)
   - Search (filename pattern, filters)

3. **Blazor Web UI** with:
   - Dashboard showing imported catalogues
   - File upload / import page
   - Tree browser (Explorer-like, lazy-loaded)
   - Search page with filters and results table
   - Responsive layout (desktop + tablet)

4. **Entity Framework Core data layer** with:
   - SQL Server provider
   - Migrations
   - Efficient hierarchical data queries (recursive CTEs or HierarchyId)

5. **Docker container**:
   - Multi-stage build (SDK for build, distroless for runtime)
   - Non-root execution
   - Health check endpoint

### Project Structure

```
src/
  Woistes.Domain/           # Domain entities, interfaces
  Woistes.Infrastructure/   # EF Core, SQL Server, repositories
  Woistes.CtfParser/        # CTF binary format parser library
  Woistes.Api/              # ASP.NET Core Web API (hosts Blazor + API)
  Woistes.Web/              # Blazor frontend (Razor components)
tests/
  Woistes.CtfParser.Tests/  # CTF parser unit tests (uses sampleCTF files)
  Woistes.Api.Tests/        # API integration tests
k8s/
  deployment.yaml           # App deployment
  sqlserver.yaml            # SQL Server StatefulSet + PVC
  ingress.yaml              # Ingress rules
  configmap.yaml            # App configuration
  secrets.yaml              # Connection strings (sealed/external)
Dockerfile
docker-compose.yml          # Local dev (app + SQL Server)
```

---

## Future Phases (Out of Scope)

- **Volume scanning** - local agent/service that scans actual disks and pushes results to the API
- **Plugin system** - extensible metadata extractors (MP3 tags, EXIF, PDF info, Office metadata)
- **Multi-user / multi-tenant** support
- **Authentication** (Azure AD / Entra ID)
- **Full-text search** with SQL Server FTS or Elasticsearch
- **Thumbnail storage** (Azure Blob or PVC)
- **Diff/changelog** - compare current scan vs. previous to show what changed
- **Reporting & export** (CSV, Excel, print-friendly views)
- **Duplicate detection** (by name, size, hash across catalogues)
- **Archive browsing** (list contents of ZIP/RAR/7z within catalogued entries)

---

## Technical Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| .NET version | .NET 10 | Latest runtime, matches installed SDK |
| Container base | `mcr.microsoft.com/dotnet/runtime-deps` (distroless) | Minimal attack surface, small image |
| ORM | EF Core | First-class .NET support, migrations, LINQ |
| Hierarchy storage | Adjacency list (ParentId) + materialized FullPath column | Simple, fast path-based search; recursive CTE for tree expansion |
| Frontend | Blazor Server | Single language stack (C#), shared models, no WASM download penalty |
| CTF parsing | Manual binary reader (`BinaryReader` / `Span<byte>`) | No existing library; format is proprietary and undocumented |
| File upload | Streaming upload via `IFormFile` with size limit (~50 MB) | Largest sample is 20 MB; keeps memory bounded |
| Kubernetes SQL Server | StatefulSet with PVC | Persistent storage survives pod restarts |

---

## References

- WhereIsIt? 3.03b by Robert Galle (2001) 
- WhereIsIt DescAPI 2.0 - `WhereIsIt/DescAPI/DescAPI.h`
- [Azure AKS Documentation](https://learn.microsoft.com/en-us/azure/aks/)
- [SQL Server on Kubernetes](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-kubernetes-best-practices)
- [Distroless .NET containers](https://learn.microsoft.com/en-us/dotnet/core/docker/container-images)
