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
        // Save the catalogue + disks first to obtain their generated ids.
        var treesByDiskIndex = catalogue.Disks
            .Select((d, i) => (Index: i, Roots: d.Entries.ToList()))
            .ToList();
        foreach (var disk in catalogue.Disks)
            disk.Entries = [];

        _db.Catalogues.Add(catalogue);
        await _db.SaveChangesAsync(ct);

        int entriesTotal = treesByDiskIndex.Sum(t => t.Roots.Sum(CountTree));
        int disksTotal = treesByDiskIndex.Count;
        int entriesSaved = 0;
        progress?.Report(new ImportProgress(0, disksTotal, 0, entriesTotal));

        // Persist one disk's tree per SaveChanges so progress advances per disk.
        // EF tracks each tree via the self-referencing Children navigation and
        // assigns ParentId automatically. AutoDetectChanges is disabled because
        // its per-operation rescan is O(n^2) over tracked entities.
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

        return catalogue;
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
            await _db.Entries.Where(e => diskIds.Contains(e.DiskId)).ExecuteDeleteAsync(ct);

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
