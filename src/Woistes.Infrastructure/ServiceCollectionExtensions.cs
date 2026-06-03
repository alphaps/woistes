using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Woistes.Domain;

namespace Woistes.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWoistesInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<WoistesDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                // Large catalogues import 100k+ rows; bigger batches mean far
                // fewer round-trips than the default (which would also flood logs).
                sql.MaxBatchSize(1000)));

        services.AddScoped<ICatalogueRepository, CatalogueRepository>();

        return services;
    }
}
