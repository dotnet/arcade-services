using System;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.Internal.DependencyInjection;

public static class ClientFactoryExtensions
{
    public static IServiceCollection AddClientFactory<TOptions, TClient>(this IServiceCollection services, Func<TOptions, IServiceProvider, TClient> factory)
        where TClient : class
    {
        return services.AddSingleton<IClientFactory<TClient>>(provider =>
        {
            var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<TOptions>>();
            return new ClientFactory<TOptions, TClient>(optionsMonitor, o => factory(o, provider));
        });
    }
        
    public static IServiceCollection AddClientFactory<TOptions, TClient>(this IServiceCollection services, Func<TOptions, TClient> factory)
        where TClient : class
    {
        return services.AddSingleton<IClientFactory<TClient>>(provider =>
        {
            var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<TOptions>>();
            return new ClientFactory<TOptions, TClient>(optionsMonitor, o => factory(o));
        });
    }

    public static IServiceCollection AddClientFactory<TOptions, TClientInterface, TClientImplementation>(this IServiceCollection services)
        where TClientImplementation : TClientInterface
        where TClientInterface : class
    {
        return services.AddClientFactory<TOptions, TClientInterface>((o, provider) =>
            ActivatorUtilities.CreateInstance<TClientImplementation>(provider, o));
    }

    public static Reference<TClient> GetClient<TClient>(this IClientFactory<TClient> factory) => factory.GetClient(Options.DefaultName);
}
