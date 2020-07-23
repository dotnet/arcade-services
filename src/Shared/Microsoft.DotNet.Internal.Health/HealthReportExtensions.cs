// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Internal.Health
{
    public static class HealthReportExtensions
    {
        public static IServiceCollection AddHealthReporting(this IServiceCollection services, Action<HealthReportingBuilder> configure)
        {
            services.AddSingleton(typeof(IHealthReport<>), typeof(HealthReport<>));
            configure(new HealthReportingBuilder(services));
            return services;
        }
    }
}
