using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Woistes.Mcp;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);

var baseUrl = Environment.GetEnvironmentVariable("WOISTES_API_URL") ?? "http://localhost:5000";

var cookie = Environment.GetEnvironmentVariable("WOISTES_COOKIE") ?? "";

builder.Services.AddHttpClient("woistes", client =>
{
    client.BaseAddress = new Uri(baseUrl);
    if (!string.IsNullOrEmpty(cookie))
        client.DefaultRequestHeaders.Add("Cookie", cookie);
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
