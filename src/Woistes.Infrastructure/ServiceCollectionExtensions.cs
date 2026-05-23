using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Woistes.Domain;

namespace Woistes.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWoistesInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<WoistesDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<ICatalogueRepository, CatalogueRepository>();

        return services;
    }
}
