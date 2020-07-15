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
using Microsoft.DotNet.Internal.Testing.Utility;

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

        protected void SetUp(KustoOptions customOptions = null)
        {
            var collection = new ServiceCollection();
            collection.AddOptions();
            collection.AddLogging(l =>
            {
                l.AddProvider(new XUnitLogger(_output));
            });

            collection.Configure<KustoOptions>(options =>
            {
                options.IngestConnectionString = customOptions != null ? customOptions.IngestConnectionString : "fakekustoconnectionstring";
                options.Database = customOptions != null ? customOptions.Database : "fakekustodatbase";
            });

            collection.AddScoped<TelemetryController>();

            var kustoIngestClientMock = new Mock<IKustoIngestClient>();
            kustoIngestClientMock.Setup(x => x.IngestFromStreamAsync(It.IsAny<System.IO.Stream>(), It.IsAny<KustoIngestionProperties>(), null))
                .Returns(Task.FromResult(Mock.Of<IKustoIngestionResult>()));

            collection.AddSingleton(kustoIngestClientMock.Object);

            _services = collection.BuildServiceProvider();
            _controller = _services.GetRequiredService<TelemetryController>();
        }

        public void Dispose()
        {
            _services.Dispose();
        }

        [Fact]
        public async void TestArcadeValidationTelemetryCollection()
        {
            SetUp();
            IActionResult result = await _controller.CollectArcadeValidation(new ArcadeValidationData
            {
                BuildDateTime = new DateTimeOffset(2001, 2, 3, 16, 5, 6, 7, TimeSpan.Zero),
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
            SetUp(new KustoOptions());
            await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.CollectArcadeValidation(new ArcadeValidationData()));
        }
    }
}
