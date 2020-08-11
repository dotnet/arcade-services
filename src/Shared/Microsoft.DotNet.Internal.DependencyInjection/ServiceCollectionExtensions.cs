using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.Internal.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection EnableLazy(this IServiceCollection services)
        {
            return services.AddTransient(typeof(Lazy<>), typeof(ResolvingLazy<>));
        }
        
        public static IServiceCollection Configure<TOptions>(
            this IServiceCollection services,
            Action<TOptions, IServiceProvider> configure) where TOptions : class
        {
            return services.AddSingleton<IConfigureOptions<TOptions>>(
                provider => new ConfigureOptions<TOptions>(options => configure(options, provider)));
        }

        public static IServiceCollection Configure<TOptions>(
            this IServiceCollection services,
            string sectionName,
            Action<TOptions, IConfiguration> configure) where TOptions : class
        {
            return services.AddSingleton<IConfigureOptions<TOptions>>(
                provider => new ConfigureOptions<TOptions>(
                    options => configure(options,
                        provider.GetRequiredService<IConfiguration>().GetSection(sectionName)
                    )
                )
            );
        }
    }
}
