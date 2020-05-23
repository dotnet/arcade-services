using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    public class DelegatedStatelessWebServiceStartupTests
    {
        [Fact]
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
            Assert.NotNull(outer);
            Assert.Equal("Test string", outer.Value);
            var inner = innerProvider.GetService<InnerTest>();
            Assert.NotNull(inner);
            Assert.Equal("Test string:Inner", inner.Value);

            var appBuilder = new ApplicationBuilder(innerProvider);
            delegated.Configure(appBuilder);
            RequestDelegate built = appBuilder.Build();
            var ctx = new DefaultHttpContext();
            built(ctx);

            Assert.True(ctx.Response.Headers.TryGetValue("TestHeader", out var headerValues));
            Assert.Equal("TestHeaderValue", headerValues.ToString());
        }

        [Fact]
        public void RequiresIStartup()
        {
            var collection = new ServiceCollection();
            ServiceProvider provider = collection.BuildServiceProvider();
            Assert.Throws<InvalidOperationException>(() =>
                new DelegatedStatelessWebServiceStartup<DelegatedStatelessWebServiceStartupTests>(
                    provider,
                    null,
                    service => { }
                )
            );
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
