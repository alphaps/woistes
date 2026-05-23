using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Woistes.Infrastructure;

namespace Woistes.Api.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "WoistesTest_" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<WoistesDbContext>)
                    || d.ServiceType == typeof(WoistesDbContext)
                    || d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true)
                .ToList();
            foreach (var d in descriptorsToRemove)
                services.Remove(d);

            services.AddDbContext<WoistesDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));
        });
    }
}
