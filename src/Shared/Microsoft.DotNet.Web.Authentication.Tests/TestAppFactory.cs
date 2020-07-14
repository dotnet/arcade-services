using System;
using System.IO;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Web.Authentication.Tests
{
    public class TestAppFactory : WebApplicationFactory<EmptyTestStartup>
    {
        private readonly ITestOutputHelper _output;
        private readonly string _rootPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        private Action<IServiceCollection> _configureServices;
        private Action<IApplicationBuilder> _configureBuilder;

        public TestAppFactory(ITestOutputHelper output)
        {
            _output = output;
        }

        public void ConfigureServices(Action<IServiceCollection> configureServices)
        {
            _configureServices += configureServices;
        }

        public void ConfigureBuilder(Action<IApplicationBuilder> configureBuilder)
        {
            _configureBuilder += configureBuilder;
        }

        protected override IWebHostBuilder CreateWebHostBuilder()
        {
            return WebHost.CreateDefaultBuilder<EmptyTestStartup>(Array.Empty<string>());
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(_rootPath);
            builder.UseContentRoot(_rootPath).UseWebRoot(_rootPath);
            builder.ConfigureLogging(l =>
            {
                l.SetMinimumLevel(LogLevel.Trace);
                l.AddProvider(new XUnitLogger(_output));
            });
            if (_configureServices != null)
                builder.ConfigureServices(_configureServices);
            if (_configureBuilder != null)
                builder.Configure(_configureBuilder);
            base.ConfigureWebHost(builder);
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                Directory.Delete(_rootPath, true);
            }
            catch
            {
                // Really don't care
            }

            base.Dispose(disposing);
        }
    }
}
