using Microsoft.Extensions.DependencyInjection;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Configuration;

namespace DotNet.Status.Web;

public static class KustoIngestExtension
{
    public static IServiceCollection AddKustoIngest(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<KustoOptions>(config);
        services.AddSingleton<IKustoIngestClientFactory, KustoIngestClientFactory>();
        return services;
    }
}
