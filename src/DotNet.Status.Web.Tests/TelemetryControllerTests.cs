using System;
using System.Threading.Tasks;
using DotNet.Status.Web.Controllers;
using FluentAssertions;
using Kusto.Ingest;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace DotNet.Status.Web.Tests
{
    [TestFixture, NonParallelizable]
    public class TelemetryControllerTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private TelemetryController _controller;
        private ServiceProvider _services;

        [SetUp]
        public void TelemetryControllerTests_SetUp()
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

        [TearDown]
        public void Dispose()
        {
            _services.Dispose();
        }

        [Test]
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
            result.Should().NotBeNull();
            result.Should().BeOfType<OkResult>();
        }

        [Test]
        public async void TestArcadeValidationTelemetryCollectionWithMissingKustoConnectionString()
        {
            SetUp(new KustoOptions());
            await (((Func<Task>)(() => _controller.CollectArcadeValidation(new ArcadeValidationData())))).Should().ThrowExactlyAsync<InvalidOperationException>();
        }
    }
}
