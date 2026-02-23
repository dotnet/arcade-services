// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace BuildInsights.KnownIssuesProcessor.Tests
{
    [TestFixture]
    public class DependencyRegistrationTests
    {
        [Test]
        public void AreDependenciesRegistered()
        {
            DependencyInjectionValidation.IsDependencyResolutionCoherent(
                    services =>
                    {
                        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
                        ServiceHost.ConfigureDefaultServices(services);
                        Program.ConfigureServices(services);
                    },
                    out string message,
                    additionalScopedTypes: new[] { typeof(KnownIssuesProcessor) }).Should().BeTrue(message);
        }
    }
}
