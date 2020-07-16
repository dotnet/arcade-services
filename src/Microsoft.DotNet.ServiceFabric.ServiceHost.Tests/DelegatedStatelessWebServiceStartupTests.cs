using System;
using System.IO;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    [TestFixture]
    public class DelegatedStatelessWebServiceStartupTests
    {
        [Test]
        public void CallsForwardedToResolvedStartup()
        {
            var collection = new ServiceCollection();
            collection.AddSingleton<MyStartup>();
            ServiceProvider provider = collection.BuildServiceProvider();
            var delegated = new DelegatedStatelessWebServiceStartup<MyStartup>(
                provider,
                new HostEnvironment("TESTING", "TestApp", Path.GetTempPath(), null),
                service => service.AddSingleton(new OuterTest("Test string"))
            );

            var innerCollection = new ServiceCollection();

            delegated.ConfigureServices(innerCollection);

            ServiceProvider innerProvider = innerCollection.BuildServiceProvider();

            var outer = innerProvider.GetService<OuterTest>();
            outer.Should().NotBeNull();
            outer.Value.Should().Be("Test string");
            var inner = innerProvider.GetService<InnerTest>();
            inner.Should().NotBeNull();
            inner.Value.Should().Be("Test string:Inner");

            var appBuilder = new ApplicationBuilder(innerProvider);
            delegated.Configure(appBuilder);
            RequestDelegate built = appBuilder.Build();
            var ctx = new DefaultHttpContext();
            built(ctx);

            ctx.Response.Headers.TryGetValue("TestHeader", out var headerValues).Should().BeTrue();
            headerValues.ToString().Should().Be("TestHeaderValue");
        }

        [Test]
        public void RequiresIStartup()
        {
            var collection = new ServiceCollection();
            ServiceProvider provider = collection.BuildServiceProvider();
            (((Func<object>)(() =>
                new DelegatedStatelessWebServiceStartup<DelegatedStatelessWebServiceStartupTests>(
                    provider,
                    null,
                    service => { }
                )
))).Should().ThrowExactly<InvalidOperationException>();
        }

        private class MyStartup : IStartup
        {
            public void Configure(IApplicationBuilder app)
            {
                app.Use(async (ctx, next) =>
                {
                    ctx.Response.Headers.Add("TestHeader", "TestHeaderValue");
                    await next();
                });
            }

            public IServiceProvider ConfigureServices(IServiceCollection services)
            {
                services.AddSingleton<InnerTest>();
                return services.BuildServiceProvider();
            }
        }

        private class OuterTest
        {
            public OuterTest(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        private class InnerTest
        {
            public InnerTest(OuterTest outer)
            {
                Value = outer.Value + ":Inner";
            }

            public string Value { get; }
        }
    }
}
