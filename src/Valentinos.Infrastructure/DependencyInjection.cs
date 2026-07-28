using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Valentinos.Application.Assets;
using Valentinos.Application.Qr;
using Valentinos.Infrastructure.Assets;
using Valentinos.Infrastructure.Persistence;
using Valentinos.Infrastructure.Qr;

namespace Valentinos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString, IConfiguration? configuration = null)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IAssetService, AssetService>();

        var qrOptions = new QrOptions();
        configuration?.GetSection("Qr").Bind(qrOptions);
        services.AddSingleton(qrOptions);
        services.AddSingleton<IQrRenderer, SkiaQrRenderer>();

        return services;
    }
}
