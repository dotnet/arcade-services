// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;

namespace Microsoft.DotNet.Kusto
{
    public static class KustoServiceCollectionExtensions
    {
        public static IServiceCollection AddKustoClientProvider(this IServiceCollection services, Action<IServiceProvider, KustoClientProviderOptions> configure = null)
        {
            services.AddSingleton<IKustoClientProvider, KustoClientProvider>();
            if (configure != null)
            {
                services.AddSingleton<IConfigureOptions<KustoClientProviderOptions>>(
                    provider => new ConfigureOptions<KustoClientProviderOptions>(options => configure(provider, options))
                );
            }
            return services;
        }

        public static IServiceCollection AddKustoClientProvider(this IServiceCollection services, Action<KustoClientProviderOptions> configure)
        {
            services.AddSingleton<IKustoClientProvider, KustoClientProvider>();
            services.Configure<KustoClientProviderOptions>(configure);
            return services;
        }
    }
}
