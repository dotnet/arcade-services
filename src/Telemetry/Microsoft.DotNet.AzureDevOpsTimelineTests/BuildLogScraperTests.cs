using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    public class BuildLogScraperTests
    {
        private Mock<ILogger<BuildLogScraper>> _logger = new Mock<ILogger<BuildLogScraper>>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private static readonly string _microsoftHostedAgentImageName = "windows-2019";
        private static readonly string _oneESImageName = "Build.Ubuntu.1804.Amd64";

        private BuildLogScraper _buildLogScraper;

        public BuildLogScraperTests()
        {
            _buildLogScraper = new BuildLogScraper(_logger.Object, new MockAzureClient());
        }

        [Test]
        public async Task BuildLogScraperShouldExtractMicrosoftHostedPoolImageName()
        {
            var imageName = await _buildLogScraper.ExtractMicrosoftHostedPoolImageNameAsync(@$"Virtual Environment
Environment: {_microsoftHostedAgentImageName}
Version: 20220223.1", _cancellationTokenSource.Token);

            Assert.AreEqual(_microsoftHostedAgentImageName, imageName);
        }

        [Test]
        public async Task BuildLogScraperShouldExtractOneESHostedPoolImageName()
        {
            var imageName = await _buildLogScraper.ExtractOneESHostedPoolImageNameAsync(@$"SKU: Standard_D4_v3
Image: {_oneESImageName}
Image Version: 2022.0219.013407", _cancellationTokenSource.Token);

            Assert.AreEqual(_oneESImageName, imageName);
        }

        [Test]
        public async Task BuildLogScraperShouldntExtractAnything()
        {
            Assert.AreEqual(string.Empty, await _buildLogScraper.ExtractOneESHostedPoolImageNameAsync("Incorrect string", _cancellationTokenSource.Token));
            Assert.AreEqual(string.Empty, await _buildLogScraper.ExtractMicrosoftHostedPoolImageNameAsync("Incorrect string", _cancellationTokenSource.Token));
        }
    }
}
