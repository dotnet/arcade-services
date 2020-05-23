using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace DotNet.Status.Web.Tests
{
    public class DependencyRegistrationTests
    {
        [Fact]
        public void AreDependenciesRegistered()
        {
            var config = new ConfigurationBuilder();
            var collection = new ServiceCollection();
            Mock<IWebHostEnvironment> env = new Mock<IWebHostEnvironment>();
            env.SetupGet(e => e.EnvironmentName).Returns(Environments.Development);

            collection.AddSingleton<IConfiguration>(config.Build());
            collection.AddSingleton<Startup>();
            collection.AddSingleton(env.Object);
            collection.AddSingleton(env.As<IHostEnvironment>().Object);

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

                        s.AddLogging();
                        s.AddOptions();
                        startup.ConfigureServices(s);
                    },
                    out string message,
                    additionalScopedTypes: controllerTypes),
                message);
        }
    }
}
