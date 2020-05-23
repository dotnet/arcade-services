using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Maestro.Web.Tests
{
    public class DependencyRegistrationTests
    {
        [Fact]
        public void AreDependenciesRegistered()
        {
            Environment.SetEnvironmentVariable("ENVIRONMENT", Environments.Development);

            var config = new ConfigurationBuilder();
            var collection = new ServiceCollection();

            // The only scenario we are worried about is when running in the ServiceHost
            ServiceHost.ConfigureDefaultServices(collection);

            collection.AddSingleton<IConfiguration>(config.Build());
            collection.AddSingleton<Startup>();
            using ServiceProvider provider = collection.BuildServiceProvider();
            var startup = provider.GetRequiredService<Startup>();

            IEnumerable<Type> controllerTypes = typeof(Startup).Assembly.ExportedTypes
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t));

            Assert.True(DependencyInjectionValidation.IsDependencyResolutionCoherent(
                    s =>
                    {
                        foreach (ServiceDescriptor descriptor in collection)
                        {
                            s.Add(descriptor);
                        }

                        startup.ConfigureServices(s);
                    },
                    out string message,
                    additionalScopedTypes: controllerTypes),
                message);
        }
    }
}
