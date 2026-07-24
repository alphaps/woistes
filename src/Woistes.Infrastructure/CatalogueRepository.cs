using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Woistes.Domain;

namespace Woistes.Infrastructure;

public class CatalogueRepository : ICatalogueRepository
{
    private readonly WoistesDbContext _db;

    public CatalogueRepository(WoistesDbContext db)
    {
        _db = db;
    }

    public async Task<Catalogue?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.Catalogues
            .Include(c => c.Disks)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<List<Catalogue>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Catalogues
            .Include(c => c.Disks)
            .OrderByDescending(c => c.ImportedDate)
            .ToListAsync(ct);
    }

    public async Task<Catalogue> AddAsync(Catalogue catalogue, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        var treesByDiskIndex = catalogue.Disks
            .Select((d, i) => (Index: i, Roots: d.Entries.ToList()))
            .ToList();
        foreach (var disk in catalogue.Disks)
            disk.Entries = [];

        _db.Catalogues.Add(catalogue);
        await _db.SaveChangesAsync(ct);

        int entriesTotal = treesByDiskIndex.Sum(t => t.Roots.Sum(CountTree));
        int disksTotal = treesByDiskIndex.Count;
        progress?.Report(new ImportProgress(0, disksTotal, 0, entriesTotal));

        var isSqlServer = _db.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer";

        if (isSqlServer)
        {
            var connection = (SqlConnection)_db.Database.GetDbConnection();
            await BulkInsertAsync(connection, catalogue, treesByDiskIndex, entriesTotal, disksTotal, progress, ct);
        }
        else
        {
            await EfInsertFallbackAsync(catalogue, treesByDiskIndex, entriesTotal, disksTotal, progress, ct);
        }

        return catalogue;
    }

    private static async Task BulkInsertAsync(
        SqlConnection connection,
        Catalogue catalogue,
        List<(int Index, List<CatalogueEntry> Roots)> treesByDiskIndex,
        int entriesTotal, int disksTotal,
        IProgress<ImportProgress>? progress,
        CancellationToken ct)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        int entriesSaved = 0;

        for (int i = 0; i < treesByDiskIndex.Count; i++)
        {
            var (index, roots) = treesByDiskIndex[i];
            var diskId = catalogue.Disks[index].Id;

            var flat = new List<CatalogueEntry>();
            var parentMap = new Dictionary<CatalogueEntry, CatalogueEntry?>();
            foreach (var root in roots)
                FlattenTreeWithParent(root, null, flat, parentMap);

            if (flat.Count == 0)
            {
                progress?.Report(new ImportProgress(i + 1, disksTotal, entriesSaved, entriesTotal));
                continue;
            }

            long nextId = await AllocateIdsAsync(connection, flat.Count, ct);

            foreach (var entry in flat)
            {
                entry.Id = nextId++;
                entry.DiskId = diskId;
            }

            foreach (var entry in flat)
            {
                var parent = parentMap[entry];
                entry.ParentId = parent?.Id;
            }

            using var bulkCopy = new SqlBulkCopy(connection)
            {
                DestinationTableName = "Entries",
                BatchSize = 10000,
            };
            bulkCopy.ColumnMappings.Add(nameof(CatalogueEntry.Id), "Id");
            bulkCopy.ColumnMappings.Add(nameof(CatalogueEntry.DiskId), "DiskId");
            bulkCopy.ColumnMappings.Add(nameof(CatalogueEntry.ParentId), "ParentId");
            bulkCopy.ColumnMappings.Add(nameof(CatalogueEntry.Name), "Name");
            bulkCopy.ColumnMappings.Add(nameof(CatalogueEntry.IsDirectory), "IsDirectory");
            bulkCopy.ColumnMappings.Add(nameof(CatalogueEntry.FullPath), "FullPath");
            bulkCopy.ColumnMappings.Add(nameof(CatalogueEntry.Size), "Size");
            bulkCopy.ColumnMappings.Add(nameof(CatalogueEntry.CreatedDate), "CreatedDate");
            bulkCopy.ColumnMappings.Add(nameof(CatalogueEntry.ModifiedDate), "ModifiedDate");

            using var reader = new EntryDataReader(flat);
            await bulkCopy.WriteToServerAsync(reader, ct);

            entriesSaved += flat.Count;
            progress?.Report(new ImportProgress(i + 1, disksTotal, entriesSaved, entriesTotal));
        }
    }

    private static async Task<long> AllocateIdsAsync(SqlConnection connection, int count, CancellationToken ct)
    {
        // Grab the first ID, then advance the sequence by (count - 1) more so
        // we own [first .. first + count - 1]. ALTER SEQUENCE + NEXT VALUE is
        // safe here because imports don't run concurrently.
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DECLARE @first bigint = NEXT VALUE FOR [dbo].[EntryIdSequence];
            IF @count > 1
            BEGIN
                DECLARE @remaining int = @count - 1;
                DECLARE @sql nvarchar(200);
                SET @sql = N'ALTER SEQUENCE [dbo].[EntryIdSequence] INCREMENT BY ' + CAST(@remaining AS nvarchar(20));
                EXEC sp_executesql @sql;
                DECLARE @last bigint = NEXT VALUE FOR [dbo].[EntryIdSequence];
                EXEC sp_executesql N'ALTER SEQUENCE [dbo].[EntryIdSequence] INCREMENT BY 1';
            END
            SELECT @first;
            """;
        cmd.Parameters.Add(new SqlParameter("@count", count));
        var result = await cmd.ExecuteScalarAsync(ct);
        return (long)result!;
    }

    private static void FlattenTreeWithParent(
        CatalogueEntry entry,
        CatalogueEntry? parent,
        List<CatalogueEntry> flat,
        Dictionary<CatalogueEntry, CatalogueEntry?> parentMap)
    {
        flat.Add(entry);
        parentMap[entry] = parent;
        foreach (var child in entry.Children)
            FlattenTreeWithParent(child, entry, flat, parentMap);
    }

    private async Task EfInsertFallbackAsync(
        Catalogue catalogue,
        List<(int Index, List<CatalogueEntry> Roots)> treesByDiskIndex,
        int entriesTotal, int disksTotal,
        IProgress<ImportProgress>? progress,
        CancellationToken ct)
    {
        int entriesSaved = 0;
        var autoDetect = _db.ChangeTracker.AutoDetectChangesEnabled;
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            for (int i = 0; i < treesByDiskIndex.Count; i++)
            {
                var (index, roots) = treesByDiskIndex[i];
                var diskId = catalogue.Disks[index].Id;
                int diskEntryCount = 0;
                foreach (var root in roots)
                {
                    StampDiskId(root, diskId);
                    diskEntryCount += CountTree(root);
                }
                _db.Entries.AddRange(roots);
                _db.ChangeTracker.DetectChanges();
                await _db.SaveChangesAsync(ct);

                entriesSaved += diskEntryCount;
                progress?.Report(new ImportProgress(i + 1, disksTotal, entriesSaved, entriesTotal));
            }
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    private static void StampDiskId(CatalogueEntry entry, int diskId)
    {
        entry.DiskId = diskId;
        foreach (var child in entry.Children)
            StampDiskId(child, diskId);
    }

    private static int CountTree(CatalogueEntry entry)
    {
        int n = 1;
        foreach (var child in entry.Children)
            n += CountTree(child);
        return n;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var diskIds = await _db.Disks
            .Where(d => d.CatalogueId == id)
            .Select(d => d.Id)
            .ToListAsync(ct);

        if (diskIds.Count > 0)
        {
            int deleted;
            do
            {
                deleted = await _db.Entries
                    .Where(e => diskIds.Contains(e.DiskId))
                    .Where(e => !_db.Entries.Any(child => child.ParentId == e.Id))
                    .Take(5000)
                    .ExecuteDeleteAsync(ct);
            } while (deleted > 0);
        }

        await _db.Disks.Where(d => d.CatalogueId == id).ExecuteDeleteAsync(ct);
        await _db.Catalogues.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task<List<CatalogueEntry>> GetChildrenAsync(int diskId, long? parentId, CancellationToken ct = default)
    {
        return await _db.Entries
            .Where(e => e.DiskId == diskId && e.ParentId == parentId)
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name)
            .ToListAsync(ct);
    }

    public async Task<List<CatalogueEntry>> SearchAsync(string pattern, int? catalogueId, int skip, int take, CancellationToken ct = default)
    {
        var query = BuildSearchQuery(pattern, catalogueId);
        return await query
            .OrderBy(e => e.FullPath)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> SearchCountAsync(string pattern, int? catalogueId, CancellationToken ct = default)
    {
        var query = BuildSearchQuery(pattern, catalogueId);
        return await query.CountAsync(ct);
    }

    private IQueryable<CatalogueEntry> BuildSearchQuery(string pattern, int? catalogueId)
    {
        var likePattern = pattern.Replace('*', '%').Replace('?', '_');

        IQueryable<CatalogueEntry> query;
        try
        {
            query = _db.Entries.Where(e => EF.Functions.Like(e.Name, likePattern));
            _ = query.Take(0).ToList();
        }
        catch (InvalidOperationException)
        {
            query = _db.Entries.AsEnumerable()
                .Where(e => MatchesGlob(e.Name, pattern))
                .AsQueryable();
        }

        if (catalogueId.HasValue)
        {
            var diskIds = _db.Disks
                .Where(d => d.CatalogueId == catalogueId.Value)
                .Select(d => d.Id)
                .ToList();
            query = query.Where(e => diskIds.Contains(e.DiskId));
        }

        return query;
    }

    private static bool MatchesGlob(string name, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
