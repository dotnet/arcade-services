using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.Internal.Helix.GitHub.Models;
using NUnit.Framework;
using Octokit;
using CheckRun = Microsoft.Internal.Helix.GitHub.Models.CheckRun;
using CheckStatus = Octokit.CheckStatus;

namespace Microsoft.Internal.Helix.GitHub.Tests.Models
{
    [TestFixture]
    public class CheckRunTest
    {
        private const int AzurePipelinesAppID = 9426;

        [Test]
        public void CheckRunEqualityComparerTest()
        {
            CheckRun checkRunA = new CheckRun(CreateOctokitCheckRun(AzurePipelinesAppID, "abcdefghijklmnopqrz", "123|456|" + CreateGuid("AB"), "A"));
            CheckRun checkRunB = new CheckRun(CreateOctokitCheckRun(AzurePipelinesAppID, "abcdefghijklmnopqrz", "123|456|" + CreateGuid("AB"), "B"));
            CheckRun checkRunC = new CheckRun(CreateOctokitCheckRun(AzurePipelinesAppID + 1, "abcdefghijklmnopqrz", "123|456|" + CreateGuid("AB"), "B"));

            var checkRunEqualityComparer= new CheckRunEqualityComparer();
            checkRunEqualityComparer.Equals(checkRunA, checkRunB).Should().BeTrue();
            checkRunEqualityComparer.Equals(checkRunB, checkRunC).Should().BeFalse();
        }

        [Test]
        public void CheckRunEqualityComparerNullTest()
        {
            CheckRun checkRunA = new CheckRun(CreateOctokitCheckRun(AzurePipelinesAppID, "abcdefghijklmnopqrz", "123|456|" + CreateGuid("AB"), "A"));

            var checkRunEqualityComparer = new CheckRunEqualityComparer();
            checkRunEqualityComparer.Equals(null, null).Should().BeTrue();
            checkRunEqualityComparer.Equals(null, checkRunA).Should().BeFalse();
        }

        [Test]
        public void CheckRunParseExternalIdTest()
        {
            var checkRun = new CheckRun(CreateOctokitCheckRun(AzurePipelinesAppID, "abcdefghijklmnopqrz", "123|456|" + CreateGuid("AB"), "name123A"));

            checkRun.AzureDevOpsPipelineId.Should().Be(123);
            checkRun.AzureDevOpsBuildId.Should().Be(456);
            checkRun.AzureDevOpsProjectId.Should().Be("00000000-0000-0000-0000-0000000000ab");
        }

        [Test]
        public void CheckRunNoErrorWhenNotAzDOCheck()
        {
            Action act = () => new CheckRun(CreateOctokitCheckRun(155, "abcdefghijklmnopqrz", null, "name123A"));
            act.Should().NotThrow<ExternalIdParseException>();
        }

        [Test]
        public void CheckRunExceptionWhenMissingProjectId()
        {
            Action act = () =>  new CheckRun(CreateOctokitCheckRun(AzurePipelinesAppID, "abcdefghijklmnopqrz", "123|456", "name"));
            act.Should().Throw<ExternalIdParseException>().WithMessage("External Id '123|456' was not in an expected format.");
        }

        [Test]
        public void CheckRunExceptionWhenExpectedGiud()
        {
            Action act = () => new CheckRun(CreateOctokitCheckRun(AzurePipelinesAppID, "abcdefghijklmnopqrz", "123|456|789", "name"));
            act.Should().Throw<ExternalIdParseException>().WithMessage("External id has '789' for project id, expected a GUID");
        }

        [TestCase(CheckStatus.Completed, Microsoft.Internal.Helix.GitHub.Models.CheckStatus.Completed)]
        [TestCase(CheckStatus.Queued, Microsoft.Internal.Helix.GitHub.Models.CheckStatus.Queued)]
        [TestCase(CheckStatus.InProgress, Microsoft.Internal.Helix.GitHub.Models.CheckStatus.InProgress)]
        public void CheckRunStatus(CheckStatus status, Microsoft.Internal.Helix.GitHub.Models.CheckStatus expectedStatus)
        {
            CheckRun checkRun = new CheckRun(CreateOctokitCheckRun(status));
            checkRun.Status.Should().Be(expectedStatus);
        }

        private static Guid CreateGuid(string name)
        {
            return Guid.Parse($"00000000-0000-0000-0000-{name.PadLeft(12, '0')}");
        }

        private static Octokit.CheckRun CreateOctokitCheckRun(CheckStatus status)
        {
            DateTimeOffset dateTimeOffset = new DateTimeOffset(2021, 5, 27, 12, 0, 0, 0, TimeSpan.Zero);
            var gitHubApp = new GitHubApp(0, default, default, default, default, default, default, default,
                dateTimeOffset, dateTimeOffset, default, default);

            return new Octokit.CheckRun(0, default, "1|2|" + CreateGuid("A"), default, default, default, status,
                Octokit.CheckConclusion.Failure, dateTimeOffset, dateTimeOffset, default, default, default, gitHubApp,
                default);
        }

        private static Octokit.CheckRun CreateOctokitCheckRun(int gitHubAppId, string headShad, string externalId, string name)
        {
            DateTimeOffset dateTimeOffset = new DateTimeOffset(2021,5,27,12,0,0,0,TimeSpan.Zero);
            var gitHubApp = new GitHubApp(gitHubAppId, default, default, default, default, default, default, default,
                dateTimeOffset, dateTimeOffset, default, default);

            return new Octokit.CheckRun(0, headShad, externalId, default, default, default, CheckStatus.Completed,
                Octokit.CheckConclusion.Failure, dateTimeOffset, dateTimeOffset, default, name, default, gitHubApp,
                default);
        }
    }
}
