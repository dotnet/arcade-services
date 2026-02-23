// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using AwesomeAssertions;
using BuildInsights.Api.Controllers.Models;
using NUnit.Framework;

namespace BuildInsights.KnownIssues.Tests;

[TestFixture]
public class KnownIssueReprocessBuildMessageTests
{
    [Test]
    public void KnownIssueReprocessBuildMessageDeserializesFromHandmadeMessage()
    {
        string testOrgId = "test-org";

        KnownIssueReprocessingMessage buildMessage = new KnownIssueReprocessingMessage

        {
            ProjectId = "FAKE-PROJECT-NAME",
            BuildId = 10,
            OrganizationId = testOrgId
        };

        string jsonMessage = JsonSerializer.Serialize(buildMessage);

        KnownIssueReprocessingMessage deserializedBuildMessage = JsonSerializer.Deserialize<KnownIssueReprocessingMessage>(jsonMessage);

        deserializedBuildMessage.Should().NotBeNull();
        deserializedBuildMessage.BuildId.Should().Be(10);
        deserializedBuildMessage.OrganizationId.Should().Be(testOrgId);
    }
}
