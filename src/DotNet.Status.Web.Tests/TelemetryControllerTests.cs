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
    [TestFixture]
    public class TelemetryControllerTests
    {
        private class TestData : IDisposable
        {
            public readonly TelemetryController Controller;
            private readonly ServiceProvider _services;

            public TestData(TelemetryController controller, ServiceProvider services)
            {
                Controller = controller;
                _services = services;
            }

            public void Dispose()
            {
                _services?.Dispose();
            }
        }

        private static TestData SetUp(KustoOptions customOptions = null)
        {
            var collection = new ServiceCollection();
            collection.AddOptions();
            collection.AddLogging(l =>
            {
                l.AddProvider(new NUnitLogger());
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

            var services = collection.BuildServiceProvider();
            return new TestData(services.GetRequiredService<TelemetryController>(), services);
        }

        [Test]
        public async Task TestArcadeValidationTelemetryCollection()
        {
            using TestData testData = SetUp();
            IActionResult result = await testData.Controller.CollectArcadeValidation(new ArcadeValidationData
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
        public async Task TestArcadeValidationTelemetryCollectionWithMissingKustoConnectionString()
        {
            using TestData testData = SetUp(new KustoOptions());
            await (((Func<Task>)(() => testData.Controller.CollectArcadeValidation(new ArcadeValidationData())))).Should().ThrowExactlyAsync<InvalidOperationException>();
        }
    }
}
