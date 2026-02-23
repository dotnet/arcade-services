using System;
using AwesomeAssertions;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace BuildInsights.KnownIssuesMonitor.Tests
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
                    additionalScopedTypes: new[] {typeof(KnownIssuesMonitor) }).Should().BeTrue(message);
        }
    }
}
