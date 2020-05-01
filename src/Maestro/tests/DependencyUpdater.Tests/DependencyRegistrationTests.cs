using System;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Data;
using Moq;
using Xunit;

namespace DependencyUpdater.Tests
{
    public class DependencyRegistrationTests
    {
        [Fact]
        public void AreDependenciesRegistered()
        {
            Assert.True(DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                    {
                        Environment.SetEnvironmentVariable("ENVIRONMENT", "XUNIT");
                        ServiceHost.ConfigureDefaultServices(s);
                        Program.Configure(s);

                        // The "IReliableStateManager" is provided by stateful services
                        s.AddSingleton(Mock.Of<IReliableStateManager>());

                        s.AddScoped<DependencyUpdater>();
                    },
                    out string message),
                message);
        }
    }
}
