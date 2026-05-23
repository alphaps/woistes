using Woistes.Domain;

namespace Woistes.Api.Endpoints;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/search", async (string? pattern, int? catalogueId, int? skip, int? take, ICatalogueRepository repo) =>
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return Results.BadRequest("pattern query parameter is required");

            var actualSkip = skip ?? 0;
            var actualTake = Math.Min(take ?? 50, 200);

            var items = await repo.SearchAsync(pattern, catalogueId, actualSkip, actualTake);
            var totalCount = await repo.SearchCountAsync(pattern, catalogueId);

            return Results.Ok(new SearchResultDto(
                items.Select(e => new EntryDto(
                    e.Id, e.Name, e.IsDirectory, e.FullPath,
                    e.Size, e.CreatedDate, e.ModifiedDate)).ToList(),
                totalCount, actualSkip, actualTake));
        });
    }
}

public record SearchResultDto(List<EntryDto> Items, int TotalCount, int Skip, int Take);
