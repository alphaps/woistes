using Microsoft.EntityFrameworkCore;
using Woistes.Api.Components;
using Woistes.Api.Endpoints;
using Woistes.CtfParser;
using Woistes.Domain;
using Woistes.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Woistes");

if (string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<WoistesDbContext>(options =>
        options.UseInMemoryDatabase("Woistes"));
    builder.Services.AddScoped<ICatalogueRepository, CatalogueRepository>();
}
else
{
    builder.Services.AddWoistesInfrastructure(connectionString);
}

builder.Services.AddSingleton<ICtfParser, CtfFileParser>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.MapStaticAssets();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok("healthy"));

app.MapCatalogueEndpoints();
app.MapBrowseEndpoints();
app.MapSearchEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }
