using System;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    public static class DotNetArcadeServicesServiceCollectionExtensions
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
            services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>().GetSection(sectionName);
                return new ConfigurationChangeTokenSource<TOptions>(Options.Options.DefaultName, config);
            });
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
