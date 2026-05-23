namespace Woistes.Domain;

public interface ICatalogueRepository
{
    Task<Catalogue?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<Catalogue>> GetAllAsync(CancellationToken ct = default);
    Task<Catalogue> AddAsync(Catalogue catalogue, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<List<CatalogueEntry>> GetChildrenAsync(int diskId, long? parentId, CancellationToken ct = default);
    Task<List<CatalogueEntry>> SearchAsync(string pattern, int? catalogueId, int skip, int take, CancellationToken ct = default);
    Task<int> SearchCountAsync(string pattern, int? catalogueId, CancellationToken ct = default);
}
