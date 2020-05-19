using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Internal.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection EnableLazy(this IServiceCollection services)
        {
            return services.AddTransient(typeof(Lazy<>), typeof(ResolvingLazy<>));
        }
    }
}
