using DotNet.Status.Web.Controllers;
using FluentAssertions;
using NUnit.Framework;
using Octokit;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DotNet.Status.Web.Models;

namespace DotNet.Status.Web.Tests
{
    [TestFixture]
    public class GitHubDeserializationTests
    {
        [Test]
        public void IssueEventDeserialization_SameAsNewtonsoft()
        {
            string payload = GetTestPayload();

            // lets try to deser it by our code and Newtonsoft which by default has compatible setting
            // if this test is passing, we know that we can consider usage of Newtonsoft.Json for deserialization
            var issueEvent = JsonSerializer.Deserialize<IssuesHookData>(payload, GitHubHookController.SerializerOptions());
            var issueEventNewtonsoft = Newtonsoft.Json.JsonConvert.DeserializeObject<IssuesHookData>(payload);

            issueEvent.Should().BeEquivalentTo(issueEventNewtonsoft);
        }

        private static string GetTestPayload()
        {
            Type thisClass = typeof(GitHubDeserializationTests);
            Assembly asm = thisClass.Assembly;
            var resource = string.Format($"{thisClass.Namespace}.EventPayloads.IssueEventPayload.json");
            using var stream = asm.GetManifestResourceStream(resource);
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }

        public User CreateUser(string login)
        {
            return new User(
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                login,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default);
        }

        [Test]
        public void IssueEventDeserialization_SameAsExpected()
        {
            var expected = new IssuesHookData
            {
                Action = "labeled",
                Issue = new Octokit.Issue(
                    url: "https://api.github.com/repos/thatguy-int-tests/issue-notify-tests/issues/217",
                    htmlUrl: "https://github.com/thatguy-int-tests/issue-notify-tests/issues/217",
                    commentsUrl: default,
                    eventsUrl: default,
                    number: 217,
                    state: ItemState.Open,
                    title: "Intermittent serialization error in GC during build",
                    body: "This one is a mystery",
                    closedBy: default,
                    user: default,
                    labels: new List<Label>
                    {
                        new Label(default, default, "area-GC-coreclr", default, default, default, default),
                        new Label(default, default, "area-GC-coreclr", default, default, default, default),
                        new Label(default, default, "area-GC-coreclr", default, default, default, default),
                    },
                    assignee: CreateUser("TestLabelNotifyUserX"),
                    assignees: default,
                    milestone: default,
                    comments: default,
                    pullRequest: default,
                    closedAt: default,
                    createdAt: default,
                    updatedAt: default,
                    id: default,
                    nodeId: default,
                    locked: default,
                    repository: default,
                    reactions: default),
                Label = new IssuesHookLabel
                {
                    Name = "area-cat"
                },
                Repository = new IssuesHookRepository
                {
                    Name = "issue-notify-tests",
                    Owner = new IssuesHookUser { Login = "thatguy-int-tests" },
                    Id = 987654321,
                },
                Sender = new IssuesHookUser
                {
                    Login = "thatguy",
                }
            };

            var issueEvent = JsonSerializer.Deserialize<IssuesHookData>(GetTestPayload(), GitHubHookController.SerializerOptions());

            issueEvent.Should().BeEquivalentTo(expected);
        }
    }
}
