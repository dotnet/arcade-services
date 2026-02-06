// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// ReSharper disable once CheckNamespace

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Internal.Helix.BuildFailureAnalysis;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Providers;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Services;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMarkdownGenerator(this IServiceCollection services)
        {
            services.TryAddSingleton<IMarkdownGenerator, MarkdownGenerator>();
            services.TryAddSingleton<HandlebarHelpers>();
            return services;
        }
    }
}
