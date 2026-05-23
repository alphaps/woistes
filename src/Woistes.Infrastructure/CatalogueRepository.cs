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

    public async Task<Catalogue> AddAsync(Catalogue catalogue, CancellationToken ct = default)
    {
        _db.Catalogues.Add(catalogue);
        await _db.SaveChangesAsync(ct);
        return catalogue;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var catalogue = await _db.Catalogues.FindAsync([id], ct);
        if (catalogue is not null)
        {
            _db.Catalogues.Remove(catalogue);
            await _db.SaveChangesAsync(ct);
        }
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
        var sqlPattern = pattern.Replace('*', '%').Replace('?', '_');

        var query = _db.Entries.Where(e => EF.Functions.Like(e.Name, sqlPattern));

        if (catalogueId.HasValue)
        {
            var diskIds = _db.Disks
                .Where(d => d.CatalogueId == catalogueId.Value)
                .Select(d => d.Id);
            query = query.Where(e => diskIds.Contains(e.DiskId));
        }

        return query;
    }
}
