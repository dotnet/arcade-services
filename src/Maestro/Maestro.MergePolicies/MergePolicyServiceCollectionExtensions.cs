// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;

namespace Maestro.MergePolicies
{
    public static class MergePolicyServiceCollectionExtensions
    {
        public static IServiceCollection AddMergePolicies(this IServiceCollection services)
        {
            services.AddTransient<IMergePolicyBuilder, NoExtraCommitsMergePolicyBuilder>();
            services.AddTransient<IMergePolicyBuilder, AllChecksSuccessfulMergePolicyBuilder>();
            services.AddTransient<IMergePolicyBuilder, NoRequestedChangesMergePolicyBuilder>();
            services.AddTransient<IMergePolicyBuilder, StandardMergePolicyBuilder>();
            return services;
        }
    }
}
