using Woistes.Api.Components;
using Woistes.Api.Endpoints;
using Woistes.CtfParser;
using Woistes.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Woistes")
    ?? "Server=localhost;Database=Woistes;Trusted_Connection=True;TrustServerCertificate=True";

builder.Services.AddWoistesInfrastructure(connectionString);
builder.Services.AddSingleton<ICtfParser, CtfFileParser>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok("healthy"));

app.MapCatalogueEndpoints();
app.MapBrowseEndpoints();
app.MapSearchEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }
