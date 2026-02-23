// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using BuildInsights.KnownIssues.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace BuildInsights.KnownIssues.Tests
{
    [TestFixture]
    public class KnownIssueMatchTests
    {


        [TestCase("REPOSITORY-ABC", "REPOSITORY-ABC", true)]
        [TestCase("REPOSITORY-ABC", "REPOSITORY-123", false)]
        public void KnownIssueMatchComparerEqualsTestRepository(string repositoryA, string repositoryB, bool expectedResult)
        {
            KnownIssueMatch A = MocKnownIssueMatch(repository: repositoryA);
            KnownIssueMatch B = MocKnownIssueMatch(repository: repositoryB);

            A.Equals(B).Should().Be(expectedResult);
        }

        [TestCase(123, 123, true)]
        [TestCase(123, 456, false)]
        public void KnownIssueMatchComparerEqualsTestIssueId(int idA, int idB, bool expectedResult)
        {
            KnownIssueMatch A = MocKnownIssueMatch(issueId: idA);
            KnownIssueMatch B = MocKnownIssueMatch(issueId: idB);

            A.Equals(B).Should().Be(expectedResult);
        }

        [TestCase("JOB-ABC", "JOB-ABC", true)]
        [TestCase("JOB-ABC", "JOB-123", false)]
        public void KnownIssueMatchComparerEqualsTestJob(string jobA, string jobB, bool expectedResult)
        {
            KnownIssueMatch A = MocKnownIssueMatch(jobId: jobA);
            KnownIssueMatch B = MocKnownIssueMatch(jobId: jobB);

            A.Equals(B).Should().Be(expectedResult);
        }

        [TestCase(1234, 1234, true)]
        [TestCase(1234, 5678, false)]
        public void KnownIssueMatchComparerEqualsTestBuild(int buildA, int buildB, bool expectedResult)
        {
            KnownIssueMatch A = MocKnownIssueMatch(buildId: buildA);
            KnownIssueMatch B = MocKnownIssueMatch(buildId: buildB);

            A.Equals(B).Should().Be(expectedResult);
        }


        [Test]
        public void KnownIssuesMatchEqualsNullValuesTest()
        {
            KnownIssueMatch A = MocKnownIssueMatch();

            A.Equals(null).Should().BeFalse();
        }

        private KnownIssueMatch MocKnownIssueMatch(string repository = "TEST-REPOSITORY-ABC", int issueId = 1234, string jobId = "JOB-ID-1234", int buildId = 6789)
        {
            return new KnownIssueMatch
            {
                BuildId = buildId,
                BuildRepository = "REPOSITORY-TEST",
                IssueId = issueId,
                IssueRepository = repository,
                IssueType = "ISSUE-TYPE-TEST",
                JobId = jobId,
                StepName = "STEP-NAME-TEST",
                LogURL = "example.com",
                StepStartTime = new DateTimeOffset(2001, 2, 12, 3, 4, 5, TimeSpan.Zero)
            };
        }
    }
}
