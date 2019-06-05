// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    // Fix for app insights issue https://github.com/Microsoft/ApplicationInsights-aspnetcore/issues/491
    public static class FixedApplicationInsightsLoggerFactoryExtensions
    {
        public static ILoggingBuilder AddFixedApplicationInsights(this ILoggingBuilder builder, LogLevel minLevel)
        {
            return builder.AddFixedApplicationInsights((category, logLevel) => logLevel >= minLevel);
        }

        public static ILoggingBuilder AddFixedApplicationInsights(
            this ILoggingBuilder builder,
            Func<string, LogLevel, bool> filter)
        {
            builder.Services.AddSingleton<ILoggerProvider>(
                provider => new FixedApplicationInsightsLoggerProvider(
                    provider.GetRequiredService<TelemetryClient>(),
                    filter,
                    provider.GetRequiredService<IOptions<
#pragma warning disable CS0618 // Type or member is obsolete
                        Microsoft.ApplicationInsights.AspNetCore.Logging.ApplicationInsightsLoggerOptions>>()));
#pragma warning restore CS0618 // Type or member is obsolete
            return builder;
        }
    }
}
