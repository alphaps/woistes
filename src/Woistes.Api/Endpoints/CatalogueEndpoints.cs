using Woistes.CtfParser;
using Woistes.Domain;

namespace Woistes.Api.Endpoints;

public static class CatalogueEndpoints
{
    public static void MapCatalogueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/catalogues");

        group.MapGet("/", async (ICatalogueRepository repo) =>
        {
            var catalogues = await repo.GetAllAsync();
            return Results.Ok(catalogues.Select(c => new CatalogueSummaryDto(
                c.Id, c.Name, c.SourceFileName, c.ImportedDate,
                c.FileCount, c.FolderCount, c.Disks.Count)));
        });

        group.MapGet("/{id:int}", async (int id, ICatalogueRepository repo) =>
        {
            var catalogue = await repo.GetByIdAsync(id);
            if (catalogue is null)
                return Results.NotFound();

            return Results.Ok(new CatalogueDetailDto(
                catalogue.Id, catalogue.Name, catalogue.SourceFileName,
                catalogue.ImportedDate, catalogue.FileCount, catalogue.FolderCount,
                catalogue.Disks.Select(d => new DiskDto(
                    d.Id, d.VolumeLabel, d.FilesystemType,
                    d.DiskIndex, d.TotalSize, d.FreeSpace)).ToList()));
        });

        group.MapPost("/upload", async (HttpRequest request, ICtfParser parser, ICatalogueRepository repo) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart form data");

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest("No file uploaded");

            using var stream = file.OpenReadStream();
            var catalogue = parser.Parse(stream, file.FileName);
            catalogue.ImportedDate = DateTime.UtcNow;

            var saved = await repo.AddAsync(catalogue);

            var dto = new CatalogueSummaryDto(
                saved.Id, saved.Name, saved.SourceFileName,
                saved.ImportedDate, saved.FileCount, saved.FolderCount,
                saved.Disks.Count);

            return Results.Created($"/api/catalogues/{saved.Id}", dto);
        }).DisableAntiforgery();

        group.MapDelete("/{id:int}", async (int id, ICatalogueRepository repo) =>
        {
            var catalogue = await repo.GetByIdAsync(id);
            if (catalogue is null)
                return Results.NotFound();

            await repo.DeleteAsync(id);
            return Results.NoContent();
        });
    }
}

public record CatalogueSummaryDto(int Id, string Name, string SourceFileName, DateTime ImportedDate, int FileCount, int FolderCount, int DiskCount);
public record CatalogueDetailDto(int Id, string Name, string SourceFileName, DateTime ImportedDate, int FileCount, int FolderCount, List<DiskDto> Disks);
public record DiskDto(int Id, string VolumeLabel, string FilesystemType, int DiskIndex, long TotalSize, long FreeSpace);
