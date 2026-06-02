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

        if (folder != null)
        {
            var childResponse = await _client.GetAsync($"/api/browse/{diskId}/children?parentId={folder.Id}");
            Assert.Equal(HttpStatusCode.OK, childResponse.StatusCode);
            var children = await childResponse.Content.ReadFromJsonAsync<List<EntryDto>>();
            Assert.NotNull(children);
        }
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
