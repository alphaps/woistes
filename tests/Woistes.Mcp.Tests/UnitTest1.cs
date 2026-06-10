using System.Net;

namespace Woistes.Mcp.Tests;

public class CatalogueToolsTests
{
    private static CatalogueTools CreateTools(MockHttpHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://test-api") };
        return new CatalogueTools(http);
    }

    [Fact]
    public async Task ListCatalogues_CallsCorrectEndpoint()
    {
        var handler = new MockHttpHandler("[]");
        var tools = CreateTools(handler);

        await tools.ListCatalogues();

        Assert.Equal("/api/catalogues/", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
    }

    [Fact]
    public async Task ListCatalogues_ReturnsResponseBody()
    {
        var json = """[{"id":1,"name":"Test"}]""";
        var handler = new MockHttpHandler(json);
        var tools = CreateTools(handler);

        var result = await tools.ListCatalogues();

        Assert.Equal(json, result);
    }

    [Fact]
    public async Task GetCatalogue_CallsCorrectEndpoint()
    {
        var handler = new MockHttpHandler("{}");
        var tools = CreateTools(handler);

        await tools.GetCatalogue(42);

        Assert.Equal("/api/catalogues/42", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task BrowseFolder_WithoutParentId_CallsRootEndpoint()
    {
        var handler = new MockHttpHandler("[]");
        var tools = CreateTools(handler);

        await tools.BrowseFolder(7);

        Assert.Equal("/api/browse/7/children", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Null(handler.LastRequest.RequestUri.Query is "" ? null : handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task BrowseFolder_WithParentId_IncludesQueryParam()
    {
        var handler = new MockHttpHandler("[]");
        var tools = CreateTools(handler);

        await tools.BrowseFolder(7, parentId: 123);

        Assert.Equal("/api/browse/7/children", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("parentId=123", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task SearchFiles_PassesPatternEncoded()
    {
        var handler = new MockHttpHandler("""{"items":[],"totalCount":0,"skip":0,"take":50}""");
        var tools = CreateTools(handler);

        await tools.SearchFiles("*.mp3");

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("pattern=", query);
        Assert.Equal("/api/search", handler.LastRequest.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task SearchFiles_WithAllParams_IncludesAllQueryParams()
    {
        var handler = new MockHttpHandler("""{"items":[],"totalCount":0,"skip":10,"take":25}""");
        var tools = CreateTools(handler);

        await tools.SearchFiles("report*", catalogueId: 3, skip: 10, take: 25);

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("pattern=report", query);
        Assert.Contains("catalogueId=3", query);
        Assert.Contains("skip=10", query);
        Assert.Contains("take=25", query);
    }

    [Fact]
    public async Task SearchFiles_PatternWithSpecialChars_IsUrlEncoded()
    {
        var handler = new MockHttpHandler("""{"items":[],"totalCount":0,"skip":0,"take":50}""");
        var tools = CreateTools(handler);

        await tools.SearchFiles("my file?.txt");

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.DoesNotContain(" ", handler.LastRequest.RequestUri.AbsoluteUri.Split('?')[1].Replace("%20", "X"));
    }

    [Fact]
    public async Task GetCatalogue_ThrowsOnNotFound()
    {
        var handler = new MockHttpHandler("Not Found", HttpStatusCode.NotFound);
        var tools = CreateTools(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => tools.GetCatalogue(999));
    }
}

public class MockHttpHandler : HttpMessageHandler
{
    private readonly string _responseBody;
    private readonly HttpStatusCode _statusCode;

    public HttpRequestMessage? LastRequest { get; private set; }

    public MockHttpHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseBody = responseBody;
        _statusCode = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody)
        });
    }
}
