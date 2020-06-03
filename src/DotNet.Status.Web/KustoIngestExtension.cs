using Kusto.Ingest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;

namespace DotNet.Status.Web
{
    public static class KustoIngestExtension
    {
        public static IServiceCollection AddKustoIngest(this IServiceCollection services, Action<IOptions<KustoOptions>> configure)
        {
            services.Configure(configure);
            services.AddSingleton<IKustoIngestClient>(options =>
                KustoIngestFactory.CreateQueuedIngestClient(
                    options.GetRequiredService<IOptions<KustoOptions>>().Value.KustoIngestConnectionString));
            return services;
        }
    }
}
