using Xunit;
using DotNet.Status.Web.Controllers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Kusto.Ingest;
using System.Threading.Tasks;

namespace DotNet.Status.Web.Tests
{
    public class TelemetryControllerTests
    {
<<<<<<< HEAD
        // TODO: Test for required values in data

=======
>>>>>>> Adding API for collecting telemetry from Arcade Validation runs; test project for DotNet.Status.Web
        [Fact]
        public void TestArcadeValidationTelemetryCollection()
        {
            var mockLoggerMock = new Mock<ILogger<TelemetryController>>();
            var telemetryOptionsMock = new Mock<IOptionsSnapshot<TelemetryOptions>>();
            var telemetryOptions = new TelemetryOptions
            {
                KustoIngestConnectionString = "fakekustoconnectionstring",
                KustoDatabase = "fakekustodatbase"
            };
            telemetryOptionsMock.SetupGet(x => x.Value).Returns(telemetryOptions);
            var kustoIngestResultMock = new Mock<IKustoIngestionResult>();
            var kustoIngestClientMock = new Mock<IKustoIngestClient>();
            kustoIngestClientMock.Setup(x => x.IngestFromStreamAsync(It.IsAny<System.IO.Stream>(), It.IsAny<KustoIngestionProperties>(), null))
                .Returns(Task.FromResult(kustoIngestResultMock.Object));
            var controller = new TelemetryController(mockLoggerMock.Object, telemetryOptionsMock.Object, kustoIngestClientMock.Object);
            var result = controller.CollectArcadeValidation(new ArcadeValidationData
            {
                BuildDateTime = new System.DateTime(),
                ArcadeVersion = "fakearcadeversion",
                BARBuildID = -1,
                ArcadeBuildLink = "fakearcadebuildlink", 
                ArcadeValidationBuildLink = "fakearcadevalidationbuildlink",
                ProductRepoName = "fakeproductreponame",
                ProductRepoBuildLink = "fakeproductrepobuildlink",
                ProductRepoBuildResult = "fakeproductrepobuildresult",
                ArcadeDiffLink = "fakearcadedifflink"
            });
            result.Wait();
            Assert.NotNull(result);
            Assert.IsType<OkResult>(result.Result);
        }

        [Fact]
        public void TestArcadeValidationTelemetryCollectionWithNullOptions()
        {
            var mockLoggerMock = new Mock<ILogger<TelemetryController>>();
            var telemetryOptionsMock = new Mock<IOptionsSnapshot<TelemetryOptions>>();
            var kustoIngestClientMock = new Mock<IKustoIngestClient>();
            var controller = new TelemetryController(mockLoggerMock.Object, telemetryOptionsMock.Object, kustoIngestClientMock.Object);
            var result = controller.CollectArcadeValidation(null);
            result.Wait();
            Assert.NotNull(result);
            var resultObject = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(500, resultObject.StatusCode);
        }

        [Fact]
        public void TestArcadeValidationTelemetryCollectionWithMissingKustoConnectionString()
        {
            var mockLoggerMock = new Mock<ILogger<TelemetryController>>();
            var telemetryOptionsMock = new Mock<IOptionsSnapshot<TelemetryOptions>>();
            var kustoIngestClientMock = new Mock<IKustoIngestClient>();
            var controller = new TelemetryController(mockLoggerMock.Object, telemetryOptionsMock.Object, kustoIngestClientMock.Object);
            var result = controller.CollectArcadeValidation(new ArcadeValidationData());
            result.Wait();
            Assert.NotNull(result);            
            var resultObject = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(500, resultObject.StatusCode);
        }
    }
}
