using Kusto.Ingest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;

namespace DotNet.Status.Web
{
    public static class KustoIngestExtension
    {
        public static IServiceCollection AddKustoIngest(this IServiceCollection services, Action<IOptions<KustoOptions>> configure)
        {
            services.Configure(configure);
            services.AddSingleton<IKustoIngestClientFactory, KustoIngestClientFactory>();
            return services;
        }
    }

    public class KustoIngestClientFactory : IKustoIngestClientFactory
    {
        private readonly IOptions<KustoOptions> _kustoOptions;
        private readonly ConcurrentDictionary<string, IKustoIngestClient> _clients = new ConcurrentDictionary<string, IKustoIngestClient>();

        public KustoIngestClientFactory(IOptions<KustoOptions> options)
        {
            _kustoOptions = options;
        }

        public IKustoIngestClient GetClient()
        {
            string ingestConnectionString = _kustoOptions.Value.IngestConnectionString;

            if (string.IsNullOrWhiteSpace(ingestConnectionString))
                throw new InvalidCastException($"Kusto {nameof(_kustoOptions.Value.IngestConnectionString)} is not configured in settings or related KeyVault");

            return _clients.GetOrAdd(ingestConnectionString, _ => KustoIngestFactory.CreateQueuedIngestClient(ingestConnectionString));
        }
    }

    public interface IKustoIngestClientFactory
    {
        IKustoIngestClient GetClient();
    }
}
