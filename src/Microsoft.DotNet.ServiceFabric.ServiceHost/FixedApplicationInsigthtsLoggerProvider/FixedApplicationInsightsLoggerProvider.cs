// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights.AspNetCore.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    [ProviderAlias("FixedApplicationInsights")]
    public class FixedApplicationInsightsLoggerProvider : ILoggerProvider
    {
        private readonly ILoggerProvider _inner;
        private readonly TelemetryClient _telemetryClient;

        public FixedApplicationInsightsLoggerProvider(
            TelemetryClient telemetryClient,
            Func<string, LogLevel, bool> filter,
#pragma warning disable CS0618 // Type or member is obsolete
            IOptions<ApplicationInsightsLoggerOptions> options)
#pragma warning restore CS0618 // Type or member is obsolete
        {
            _telemetryClient = telemetryClient;
            // OFC the ApplicationInsights stuff is all internal so we can't inherit any of it

            string appInsightsLoggerProviderTypeName =
                $"Microsoft.ApplicationInsights.AspNetCore.Logging.ApplicationInsightsLoggerProvider, {typeof(ApplicationInsightsLoggerFactoryExtensions).Assembly.FullName}";
            Type appInsightsLoggerProviderType = Type.GetType(appInsightsLoggerProviderTypeName);
            if (appInsightsLoggerProviderType == null)
            {
                throw new TypeLoadException($"Could not load type {appInsightsLoggerProviderTypeName}");
            }

            _inner = (ILoggerProvider) Activator.CreateInstance(
                appInsightsLoggerProviderType,
                telemetryClient,
                filter,
                options);
        }

        public void Dispose()
        {
            _inner.Dispose();
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FixedApplicationInsightsLogger(_inner.CreateLogger(categoryName), _telemetryClient);
        }
    }
}
