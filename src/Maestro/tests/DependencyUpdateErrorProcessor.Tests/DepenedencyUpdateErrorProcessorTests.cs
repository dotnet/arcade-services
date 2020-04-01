using System;
using Maestro.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.DotNet.DarcLib;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Data;
using Moq;
using ServiceFabricMocks;

namespace DependencyUpdateErrorProcessor.Tests
{
    public class DependencyUpdateErrorProcessorTests : IDisposable
    {
        private readonly Lazy<BuildAssetRegistryContext> _context;
        protected readonly Mock<IHostingEnvironment> Env;
        protected readonly ServiceProvider Provider;
        protected readonly IServiceScope Scope;
        protected readonly MockReliableStateManager StateManager;
        protected readonly Mock<IRemoteFactory> RemoteFactory;

        public DependencyUpdateErrorProcessorTests()
        {
            var services = new ServiceCollection();
            StateManager = new MockReliableStateManager();
            RemoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
            Env = new Mock<IHostingEnvironment>(MockBehavior.Strict);
            services.AddSingleton(Env.Object);
            services.AddSingleton<IReliableStateManager>(StateManager);
            services.AddLogging();
            services.AddDbContext<BuildAssetRegistryContext>(
                options => { options.UseInMemoryDatabase("BuildAssetRegistry"); });
            Provider = services.BuildServiceProvider();
            services.Configure<GitHubTokenProviderOptions>(
                (options) =>
                {
                    var configuration = new Mock<IConfiguration>();
                    var configurationSection = new Mock<IConfigurationSection>();
                    configurationSection.Setup(a => a.Value).Returns("github");
                    configuration.Setup(a => a.GetSection("TestValueKey")).Returns(configurationSection.Object);
                    configurationSection.Object.Bind(options);
                }
            );
            Provider = services.BuildServiceProvider();
            services.Configure<DependencyUpdateErrorProcessorOptions>(
                (options) =>
                {
                    ConfigurationBuilder builder = new ConfigurationBuilder();
                    var configuration = new Mock<IConfiguration>();
                    var configurationSection = new Mock<IConfigurationSection>();
                    configurationSection.Setup(a => a.Value).Returns("testvalue");
                    configuration.Setup(a => a.GetSection("TestValueKey")).Returns(configurationSection.Object);
                    options.IsEnabled = true;
                    options.FyiHandle = "@epananth";
                    options.GithubUrl = "https://github.com/maestro-auth-test/maestro-test2";
                }
            );
            Provider = services.BuildServiceProvider();
            Scope = Provider.CreateScope();
            _context = new Lazy<BuildAssetRegistryContext>(GetContext);
        }

        public BuildAssetRegistryContext Context => _context.Value;

        private BuildAssetRegistryContext GetContext()
        {
            return Scope.ServiceProvider.GetRequiredService<BuildAssetRegistryContext>();
        }

        public void Dispose()
        {
            Env.VerifyAll();
            Scope.Dispose();
            Provider.Dispose();
        }
    }
}
