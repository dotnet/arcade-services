using Xunit;
using DotNet.Status.Web.Controllers;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Kusto.Ingest;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using System;

namespace DotNet.Status.Web.Tests
{
    public class TelemetryControllerTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private TelemetryController _controller;
        private ServiceProvider _services;

        public TelemetryControllerTests(ITestOutputHelper output)
        {
            _output = output;            
        }

        protected async Task SetUp(KustoOptions customOptions = null)
        {
            var collection = new ServiceCollection();
            collection.AddOptions();
            collection.AddLogging(l => {
                l.AddProvider(new XUnitLogger(_output));
            });

            collection.Configure<KustoOptions>(options =>
            {
                options.KustoIngestConnectionString = customOptions != null ? customOptions.KustoIngestConnectionString : "fakekustoconnectionstring";
                options.KustoDatabase = customOptions != null ? customOptions.KustoDatabase : "fakekustodatbase";
            });

            collection.AddScoped<TelemetryController>();

            var kustoIngestClientMock = new Mock<IKustoIngestClient>();
            kustoIngestClientMock.Setup(x => x.IngestFromStreamAsync(It.IsAny<System.IO.Stream>(), It.IsAny<KustoIngestionProperties>(), null))
                .Returns(Task.FromResult(Mock.Of<IKustoIngestionResult>()));

            collection.AddSingleton(kustoIngestClientMock.Object);
            
            _services = collection.BuildServiceProvider();
            _controller = await _services.GetRequiredService<TelemetryController>();
        }

        public void Dispose()
        {
            _services.Dispose();
        }

        [Fact]
        public async void TestArcadeValidationTelemetryCollection()
        {
            await SetUp();
            var result = await _controller.CollectArcadeValidation(new ArcadeValidationData
            {
                BuildDateTime = new DateTime(),
                ArcadeVersion = "fakearcadeversion",
                BARBuildID = -1,
                ArcadeBuildLink = "fakearcadebuildlink", 
                ArcadeValidationBuildLink = "fakearcadevalidationbuildlink",
                ProductRepoName = "fakeproductreponame",
                ProductRepoBuildLink = "fakeproductrepobuildlink",
                ProductRepoBuildResult = "fakeproductrepobuildresult",
                ArcadeDiffLink = "fakearcadedifflink"
            });
            Assert.NotNull(result);
            Assert.IsType<OkResult>(result);
        }

        [Fact]
        public async void TestArcadeValidationTelemetryCollectionWithMissingKustoConnectionString()
        {
            await SetUp(new KustoOptions());
            await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.CollectArcadeValidation(new ArcadeValidationData()));
        }
    }
}
