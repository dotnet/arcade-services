using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Internal.Logging
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddOperationTracking(
            this IServiceCollection collection,
            Action<OperationManagerOptions> configure)
        {
            collection.AddSingleton<OperationManager>();
            collection.AddOptions();
            collection.Configure(configure);
            return collection;
        }
    }
}
