using System.Net;
using System.Net.Http.Json;

namespace Woistes.Api.Tests;

public class BrowseEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public BrowseEndpointTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetChildren_InvalidDisk_ReturnsEmptyList()
    {
        var response = await _client.GetAsync("/api/browse/999/children");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<List<EntryDto>>();
        Assert.NotNull(entries);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetChildren_AfterUpload_ReturnsRootEntries()
    {
        using var content = new MultipartFormDataContent();
        var sampleCtf = TryGetSampleCtfPath();
        if (sampleCtf == null) return;
        using var fileStream = File.OpenRead(sampleCtf);
        content.Add(new StreamContent(fileStream), "file", "Boumbo40.ctf");

        var uploadResponse = await _client.PostAsync("/api/catalogues/upload", content);
        var catalogue = await uploadResponse.Content.ReadFromJsonAsync<CatalogueSummaryDto>();

        var detailResponse = await _client.GetAsync($"/api/catalogues/{catalogue!.Id}");
        var detail = await detailResponse.Content.ReadFromJsonAsync<CatalogueDetailDto>();
        var diskId = detail!.Disks[0].Id;

        var response = await _client.GetAsync($"/api/browse/{diskId}/children");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<List<EntryDto>>();
        Assert.NotNull(entries);
        Assert.NotEmpty(entries);
    }

    [Fact]
    public async Task GetChildren_WithParentId_ReturnsSubEntries()
    {
        using var content = new MultipartFormDataContent();
        var sampleCtf = TryGetSampleCtfPath();
        if (sampleCtf == null) return;
        using var fileStream = File.OpenRead(sampleCtf);
        content.Add(new StreamContent(fileStream), "file", "Boumbo40.ctf");

        var uploadResponse = await _client.PostAsync("/api/catalogues/upload", content);
        var catalogue = await uploadResponse.Content.ReadFromJsonAsync<CatalogueSummaryDto>();

        var detailResponse = await _client.GetAsync($"/api/catalogues/{catalogue!.Id}");
        var detail = await detailResponse.Content.ReadFromJsonAsync<CatalogueDetailDto>();
        var diskId = detail!.Disks[0].Id;

        var rootResponse = await _client.GetAsync($"/api/browse/{diskId}/children");
        var rootEntries = await rootResponse.Content.ReadFromJsonAsync<List<EntryDto>>();
        var folder = rootEntries!.FirstOrDefault(e => e.IsDirectory);

        Assert.NotNull(folder); // the sample has root folders

        var childResponse = await _client.GetAsync($"/api/browse/{diskId}/children?parentId={folder.Id}");
        Assert.Equal(HttpStatusCode.OK, childResponse.StatusCode);
        var children = await childResponse.Content.ReadFromJsonAsync<List<EntryDto>>();
        Assert.NotNull(children);
    }

    // Regression: nested subfolder contents must be persisted with correct
    // ParentId so browsing into a folder returns its children. A prior bug
    // discarded the nested tree on import, leaving every folder empty.
    [Fact]
    public async Task GetChildren_DeepTree_PopulatedFolderIsNotEmpty()
    {
        using var content = new MultipartFormDataContent();
        var sampleCtf = TryGetSampleCtfPath();
        if (sampleCtf == null) return;
        using var fileStream = File.OpenRead(sampleCtf);
        content.Add(new StreamContent(fileStream), "file", "Boumbo40.ctf");

        var uploadResponse = await _client.PostAsync("/api/catalogues/upload", content);
        var catalogue = await uploadResponse.Content.ReadFromJsonAsync<CatalogueSummaryDto>();
        var detail = await (await _client.GetAsync($"/api/catalogues/{catalogue!.Id}"))
            .Content.ReadFromJsonAsync<CatalogueDetailDto>();
        var diskId = detail!.Disks[0].Id;

        // Walk down from root until we find a folder that has children, proving
        // nested entries were persisted (not just the root level).
        var foundPopulatedFolder = await FolderWithChildrenExists(diskId, null, depth: 0);
        Assert.True(foundPopulatedFolder, "no folder with persisted children was found");
    }

    private async Task<bool> FolderWithChildrenExists(int diskId, long? parentId, int depth)
    {
        if (depth > 6) return false;
        var url = parentId is null
            ? $"/api/browse/{diskId}/children"
            : $"/api/browse/{diskId}/children?parentId={parentId}";
        var entries = await (await _client.GetAsync(url)).Content.ReadFromJsonAsync<List<EntryDto>>();
        foreach (var folder in entries!.Where(e => e.IsDirectory))
        {
            var childUrl = $"/api/browse/{diskId}/children?parentId={folder.Id}";
            var children = await (await _client.GetAsync(childUrl)).Content.ReadFromJsonAsync<List<EntryDto>>();
            if (children!.Count > 0) return true;
            if (await FolderWithChildrenExists(diskId, folder.Id, depth + 1)) return true;
        }
        return false;
    }

    private static string? TryGetSampleCtfPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "sampleCTF")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir == null ? null : Path.Combine(dir, "sampleCTF", "Boumbo40.ctf");
    }
}

public record EntryDto(long Id, string Name, bool IsDirectory, string FullPath, long Size, DateTime? CreatedDate, DateTime? ModifiedDate);
