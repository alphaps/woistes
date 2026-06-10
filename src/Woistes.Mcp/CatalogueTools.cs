using System.ComponentModel;
using System.Net.Http.Json;
using ModelContextProtocol.Server;

namespace Woistes.Mcp;

[McpServerToolType]
public class CatalogueTools
{
    private readonly HttpClient _http;

    public CatalogueTools(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient("woistes");
    }

    [McpServerTool(Name = "list_catalogues")]
    [Description("List all imported catalogues with summary stats (name, disk count, file count, folder count, import date)")]
    public async Task<string> ListCatalogues()
    {
        var response = await _http.GetAsync("/api/catalogues/");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [McpServerTool(Name = "get_catalogue")]
    [Description("Get detailed information about a specific catalogue including its disks (volume labels, filesystem types, sizes)")]
    public async Task<string> GetCatalogue(
        [Description("The catalogue ID")] int id)
    {
        var response = await _http.GetAsync($"/api/catalogues/{id}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [McpServerTool(Name = "browse_folder")]
    [Description("List files and subfolders in a directory. Returns entries sorted by type (folders first) then name.")]
    public async Task<string> BrowseFolder(
        [Description("The disk ID to browse")] int diskId,
        [Description("Parent entry ID (omit or null for disk root)")] long? parentId = null)
    {
        var url = $"/api/browse/{diskId}/children";
        if (parentId.HasValue)
            url += $"?parentId={parentId.Value}";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [McpServerTool(Name = "search_files")]
    [Description("Search for files by name pattern (glob-style: * matches any chars, ? matches one char). Returns matching entries with full paths.")]
    public async Task<string> SearchFiles(
        [Description("Glob pattern to match file names (e.g. *.mp3, report*.pdf)")] string pattern,
        [Description("Optional catalogue ID to limit search scope")] int? catalogueId = null,
        [Description("Number of results to skip (for pagination)")] int? skip = null,
        [Description("Maximum results to return (default 50, max 200)")] int? take = null)
    {
        var queryParams = new List<string> { $"pattern={Uri.EscapeDataString(pattern)}" };
        if (catalogueId.HasValue)
            queryParams.Add($"catalogueId={catalogueId.Value}");
        if (skip.HasValue)
            queryParams.Add($"skip={skip.Value}");
        if (take.HasValue)
            queryParams.Add($"take={take.Value}");

        var url = $"/api/search?{string.Join("&", queryParams)}";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
