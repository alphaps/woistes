using Woistes.Domain;

namespace Woistes.Api.Endpoints;

public static class BrowseEndpoints
{
    public static void MapBrowseEndpoints(this WebApplication app)
    {
        app.MapGet("/api/browse/{diskId:int}/children", async (int diskId, long? parentId, ICatalogueRepository repo) =>
        {
            var entries = await repo.GetChildrenAsync(diskId, parentId);
            return Results.Ok(entries.Select(e => new EntryDto(
                e.Id, e.Name, e.IsDirectory, e.FullPath,
                e.Size, e.CreatedDate, e.ModifiedDate)));
        });
    }
}

public record EntryDto(long Id, string Name, bool IsDirectory, string FullPath, long Size, DateTime? CreatedDate, DateTime? ModifiedDate);
