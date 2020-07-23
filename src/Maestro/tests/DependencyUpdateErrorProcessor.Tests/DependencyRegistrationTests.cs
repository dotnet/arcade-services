using System;
using FluentAssertions;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using NUnit.Framework;

namespace DependencyUpdateErrorProcessor.Tests
{
    [TestFixture]
    public class DependencyRegistrationTests
    {
        [Test]
        public void AreDependenciesRegistered()
        {
            DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                    {
                        Environment.SetEnvironmentVariable("ENVIRONMENT", "XUNIT");
                        ServiceHost.ConfigureDefaultServices(s);
                        Program.Configure(s);
                    },
                    out string message).Should().BeTrue();
        }
    }
}
