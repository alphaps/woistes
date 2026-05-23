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
  - Parses all 3 file entry marker types (0x001C, 0x0058, 0x000C)
  - Handles interleaved entries and scattered directory definition blocks
  - Builds directory tree from global file indices
  - Detects disk boundaries via filesystem type string scanning
  - Resync logic to recover from alignment gaps
  - **25 passing tests** covering header parsing, file entries, directory tree, sizes, full paths, and multi-disk support across all 5 sample files
- **EF Core data layer** (`Woistes.Infrastructure`): DbContext, entity configurations (with indexes on Name/FullPath/ParentId), `ICatalogueRepository` + SQL Server implementation with search (LIKE), tree browsing, and DI extension method
- **Target framework upgrade**: all projects migrated from net8.0 to net10.0
- **ASP.NET Core Web API** (`Woistes.Api`): minimal API with endpoints for CTF upload/import, catalogue CRUD, tree browsing (lazy-load children by disk/parent), and paginated search with glob patterns. **14 integration tests** using WebApplicationFactory + InMemory DB.

- **Blazor Web UI** (merged into `Woistes.Api`): Blazor Server components for catalogue dashboard, CTF upload, tree browser with breadcrumbs, and paginated search. Served alongside the REST API from a single host.

### Next

- **Docker & Kubernetes**: Dockerfile, docker-compose for local dev, k8s manifests

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

Three marker types, all sharing the same prefix structure:

```
[marker: 2 bytes LE] [name_len: 1 byte] [name: N bytes] [metadata: variable]
```

| Marker | Metadata size | Fields |
|--------|--------------|--------|
| `0x001C` | 16 bytes | modTime(2) + modDate(2) + creTime(2) + creDate(2) + accTime(2) + accDate(2) + size(4) |
| `0x0058` | 12 bytes | modTime(2) + modDate(2) + accTime(2) + accDate(2) + size(4) |
| `0x000C` | 17 bytes | attributes(1) + modTime(2) + modDate(2) + creTime(2) + creDate(2) + accTime(2) + accDate(2) + size(4) |

- All timestamps are **DOS date/time format** (same as FAT filesystem)
- Entries of all three types are **interleaved** within a disk section (not grouped by type)
- The attribute byte in `0x000C` entries corresponds to DOS file attributes (system, hidden, archive, etc.)

### Directory Definition Records

Directory entries use a 4-byte marker prefix and describe folder structure + file assignment:

```
[02 00] [type: 1 byte (0x0C or 0x2C)] [00] [depth: 1 byte] [name_len: 2 bytes LE]
[name: N bytes] [nulls: 4 bytes] [start_index: 4 bytes LE] [file_count: 4 bytes LE]
[subdir_count: 4 bytes LE] [trailing_metadata: 24 bytes]
```

| Field | Description |
|-------|-------------|
| `depth` | Nesting level (1 = top-level directory, 2+ = subdirectory) |
| `start_index` | Index into the **global cumulative file list** (across all disks) |
| `file_count` | Number of file entries assigned to this directory |
| `subdir_count` | Number of immediate subdirectories |
| `0xFFFFFFFF` | Sentinel value for empty/virtual directories (no files assigned) |

### Overall File Structure

```
[Header]
[Metadata section: ~100 bytes of per-catalogue counts and scan names]
[Disk 1: header + interleaved file entries + directory definitions]
[Disk 2: header + interleaved file entries + directory definitions]
...
[Disk N: header + entries + dirs]
```

Within each disk section, file entries and directory definition blocks are interleaved — directory sections can appear between runs of file entries.

Directory `start_index` values reference the **global file list** (cumulative index across all disks), not per-disk indices.

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

## To Do (Phase 2)

- **Helm chart** for deployment
- **CI/CD pipeline** (GitHub Actions)

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
