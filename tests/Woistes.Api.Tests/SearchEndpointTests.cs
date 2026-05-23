using System.Net;
using System.Net.Http.Json;

namespace Woistes.Api.Tests;

public class SearchEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SearchEndpointTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Search_NoPattern_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/search");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_NoResults_ReturnsEmptyPage()
    {
        var response = await _client.GetAsync("/api/search?pattern=zzzznonexistent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SearchResultDto>();
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Search_AfterUpload_FindsFiles()
    {
        using var content = new MultipartFormDataContent();
        var sampleCtf = GetSampleCtfPath();
        using var fileStream = File.OpenRead(sampleCtf);
        content.Add(new StreamContent(fileStream), "file", "Boumbo40.ctf");

        await _client.PostAsync("/api/catalogues/upload", content);

        var response = await _client.GetAsync("/api/search?pattern=*.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SearchResultDto>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.True(result.TotalCount > 0);
    }

    [Fact]
    public async Task Search_WithPagination_RespectsSkipAndTake()
    {
        using var content = new MultipartFormDataContent();
        var sampleCtf = GetSampleCtfPath();
        using var fileStream = File.OpenRead(sampleCtf);
        content.Add(new StreamContent(fileStream), "file", "Boumbo40.ctf");

        await _client.PostAsync("/api/catalogues/upload", content);

        var response = await _client.GetAsync("/api/search?pattern=*&skip=0&take=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SearchResultDto>();
        Assert.NotNull(result);
        Assert.True(result.Items.Count <= 5);
    }

    private static string GetSampleCtfPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "sampleCTF")))
            dir = Directory.GetParent(dir)?.FullName;
        return Path.Combine(dir!, "sampleCTF", "Boumbo40.ctf");
    }
}

public record SearchResultDto(List<EntryDto> Items, int TotalCount, int Skip, int Take);
