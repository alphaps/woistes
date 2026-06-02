using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Woistes.Domain;
using Woistes.Infrastructure;

namespace Woistes.Api.Tests;

public class CatalogueEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CatalogueEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetCatalogues_ReturnsOkWithList()
    {
        var response = await _client.GetAsync("/api/catalogues");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var catalogues = await response.Content.ReadFromJsonAsync<List<CatalogueSummaryDto>>();
        Assert.NotNull(catalogues);
    }

    [Fact]
    public async Task GetCatalogue_NotFound_Returns404()
    {
        var response = await _client.GetAsync("/api/catalogues/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCatalogue_NotFound_Returns404()
    {
        var response = await _client.DeleteAsync("/api/catalogues/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostCatalogue_Upload_ReturnsCatalogue()
    {
        using var content = new MultipartFormDataContent();
        var sampleCtf = TryGetSampleCtfPath();
        if (sampleCtf == null) return;
        using var fileStream = File.OpenRead(sampleCtf);
        content.Add(new StreamContent(fileStream), "file", "Boumbo40.ctf");

        var response = await _client.PostAsync("/api/catalogues/upload", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var catalogue = await response.Content.ReadFromJsonAsync<CatalogueSummaryDto>();
        Assert.NotNull(catalogue);
        Assert.True(catalogue.Id > 0);
        Assert.Equal("Boumbo40", catalogue.Name);
        Assert.True(catalogue.FileCount > 0);
        Assert.True(catalogue.DiskCount > 0);
    }

    [Fact]
    public async Task GetCatalogue_AfterUpload_ReturnsCatalogueWithDisks()
    {
        using var content = new MultipartFormDataContent();
        var sampleCtf = TryGetSampleCtfPath();
        if (sampleCtf == null) return;
        using var fileStream = File.OpenRead(sampleCtf);
        content.Add(new StreamContent(fileStream), "file", "Boumbo40.ctf");

        var uploadResponse = await _client.PostAsync("/api/catalogues/upload", content);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<CatalogueSummaryDto>();

        var response = await _client.GetAsync($"/api/catalogues/{uploaded!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var catalogue = await response.Content.ReadFromJsonAsync<CatalogueDetailDto>();
        Assert.NotNull(catalogue);
        Assert.Equal("Boumbo40", catalogue.Name);
        Assert.NotEmpty(catalogue.Disks);
    }

    [Fact]
    public async Task DeleteCatalogue_AfterUpload_Succeeds()
    {
        using var content = new MultipartFormDataContent();
        var sampleCtf = TryGetSampleCtfPath();
        if (sampleCtf == null) return;
        using var fileStream = File.OpenRead(sampleCtf);
        content.Add(new StreamContent(fileStream), "file", "Boumbo40.ctf");

        var uploadResponse = await _client.PostAsync("/api/catalogues/upload", content);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<CatalogueSummaryDto>();

        var deleteResponse = await _client.DeleteAsync($"/api/catalogues/{uploaded!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/catalogues/{uploaded.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    private static string? TryGetSampleCtfPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "sampleCTF")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir == null ? null : Path.Combine(dir, "sampleCTF", "Boumbo40.ctf");
    }
}

public record CatalogueSummaryDto(int Id, string Name, string SourceFileName, DateTime ImportedDate, int FileCount, int FolderCount, int DiskCount);
public record CatalogueDetailDto(int Id, string Name, string SourceFileName, DateTime ImportedDate, int FileCount, int FolderCount, List<DiskDto> Disks);
public record DiskDto(int Id, string VolumeLabel, string FilesystemType, int DiskIndex, long TotalSize, long FreeSpace);
