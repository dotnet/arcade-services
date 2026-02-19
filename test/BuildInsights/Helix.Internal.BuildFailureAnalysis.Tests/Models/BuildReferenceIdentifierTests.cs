using AwesomeAssertions;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Tests.Models
{
    [TestFixture]
    public class BuildReferenceIdentifierTests
    {
        [TestCase("OrgA", "ProjectA", 0, 1, "RepositoryA", "abcd123", true)]
        [TestCase("OrgA", "ProjectB", 0, 1, "RepositoryA", "abcd123", false)]
        [TestCase("OrgA", "ProjectA", 1, 1, "RepositoryA", "abcd123", false)]
        [TestCase("OrgA", "ProjectA", 0, 1, "RepositoryB", "abcd123", false)]
        [TestCase("OrgA", "ProjectA", 0, 1, "RepositoryA", "abcd1234", false)]
        [TestCase("OrgA", "ProjectB", 1, 1, "RepositoryB", "abcd1234", false)]
        public void BuildReferenceIdentifierEqualsTest(string org, string project, int buildId, int definitionId, string repositoryId, string sha, bool expected)
        {
            BuildReferenceIdentifier buildReferenceIdentifier = new BuildReferenceIdentifier("OrgA", "ProjectA", 0,"any_buildUrl", 1, "definitionNameA","RepositoryA", "abcd123", "");
            BuildReferenceIdentifier buildReferenceIdentifierB = new BuildReferenceIdentifier(org, project, buildId, "any_buildUrl", definitionId, "definitionNameB", repositoryId, sha, "any_targetBranch");

            buildReferenceIdentifier.Equals(buildReferenceIdentifierB).Should().Be(expected);
        }

        [Test]
        public void BuildReferenceIdentifierNullTest()
        {
            BuildReferenceIdentifier buildReferenceIdentifier = new BuildReferenceIdentifier("TestingOrg", "TestingProject", 0, "any_buildUrl", 1, "","TestingRepositoryId", "abcdefgijklmn", "");

            buildReferenceIdentifier.Equals(null).Should().BeFalse();
        }
    }
}
