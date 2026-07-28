using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valentinos.Application.Assets;
using Valentinos.Infrastructure.Assets;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));
        services.AddScoped<IAssetService, AssetService>();
        return services;
    }
}
