using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Internal.Helix.KnownIssues.Models;
using NUnit.Framework;

namespace Helix.Internal.KnownIssues.Tests;

[TestFixture]
public class KnownIssueReprocessBuildMessageTests
{
    [Test]
    public void KnownIssueReprocessBuildMessageDeserializesFromHandmadeMessage()
    {
        string testOrgId = "test-org";

        KnownIssueReprocessBuildMessage buildMessage = new KnownIssueReprocessBuildMessage

        {
            ProjectId = "FAKE-PROJECT-NAME",
            BuildId = 10,
            OrganizationId = testOrgId
        };

        string jsonMessage = JsonSerializer.Serialize(buildMessage);

        KnownIssueReprocessBuildMessage deserializedBuildMessage = JsonSerializer.Deserialize<KnownIssueReprocessBuildMessage>(jsonMessage);

        deserializedBuildMessage.Should().NotBeNull();
        deserializedBuildMessage.BuildId.Should().Be(10);
        deserializedBuildMessage.OrganizationId.Should().Be(testOrgId);
    }
}
