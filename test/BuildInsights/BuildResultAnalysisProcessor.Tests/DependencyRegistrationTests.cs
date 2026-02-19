using System;
using AwesomeAssertions;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildResultAnalysisProcessor.Tests
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
                additionalScopedTypes: new[] { typeof(BuildResultAnalysisProcessor) }).Should().BeTrue(message);
        }
    }
}
