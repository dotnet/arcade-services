using System;
using System.Threading.Tasks;
using DotNet.Status.Web.Controllers;
using FluentAssertions;
using Kusto.Ingest;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace DotNet.Status.Web.Tests;

[TestFixture]
public partial class TelemetryControllerTests
{
    [TestDependencyInjectionSetup]
    public static class TestDataConfiguration
    {
        public static void Default(IServiceCollection collection)
        {
            collection.AddOptions();
            collection.AddLogging(l =>
            {
                l.AddProvider(new NUnitLogger());
            });
        }

        public static Func<IServiceProvider, TelemetryController> Controller(IServiceCollection collection)
        {
            collection.AddScoped<TelemetryController>();
            return s => s.GetRequiredService<TelemetryController>();
        }

        public static void KustoClient(IServiceCollection collection, string connectionString, string database)
        {
            collection.Configure<KustoOptions>(options =>
            {
                options.IngestConnectionString = connectionString ?? "fakekustoconnectionstring";
                options.Database = database ?? "fakekustodatbase";
            });
                
            var kustoIngestClientMock = new Mock<IKustoIngestClient>();
            kustoIngestClientMock.Setup(x => x.IngestFromStreamAsync(It.IsAny<System.IO.Stream>(), It.IsAny<KustoIngestionProperties>(), null))
                .Returns(Task.FromResult(Mock.Of<IKustoIngestionResult>()));

            var kustoIngestClientFactoryMock = new Mock<IKustoIngestClientFactory>();
            kustoIngestClientFactoryMock.Setup(x => x.GetClient())
                .Returns(kustoIngestClientMock.Object);

            collection.AddSingleton(kustoIngestClientFactoryMock.Object);
        }
    }

    [Test]
    public async Task TestArcadeValidationTelemetryCollection()
    {
        using TestData testData = TestData.Default.Build();
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
        using TestData testData = TestData.Default
            .WithConnectionString("")
            .Build();
        await (((Func<Task>) (() => testData.Controller.CollectArcadeValidation(new ArcadeValidationData()))))
            .Should().ThrowExactlyAsync<InvalidOperationException>();
    }
}
