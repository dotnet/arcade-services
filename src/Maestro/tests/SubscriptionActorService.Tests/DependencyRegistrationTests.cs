using System;
using FluentAssertions;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace SubscriptionActorService.Tests
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
                        s.AddScoped<SubscriptionActor>();
                        s.AddScoped<PullRequestActor>();
                    },
                    out string message).Should().BeTrue();
        }
    }
}
