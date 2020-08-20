using DotNet.Status.Web.Controllers;
using FluentAssertions;
using NUnit.Framework;
using Octokit;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text.Json;

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
            var resource = string.Format($"{thisClass.Namespace}.Files.IssueEventPayload.json");
            using var stream = asm.GetManifestResourceStream(resource);
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }

        [Test]
        public void IssueEventDeserialization_SameAsExpected()
        {
            var expected = new IssuesHookData
            {
                Action = "labeled",
                Issue = new IssuesHookIssue
                {
                    Assignee = new IssuesHookUser
                    {
                        Login = "TestLabelNotifyUserX",
                    },
                    Labels = ImmutableArray.Create(new[]
                    {
                        new IssuesHookLabel {Name = "area-GC-coreclr"},
                        new IssuesHookLabel {Name = "area-Serialization"},
                        new IssuesHookLabel {Name = "area-cat"},
                    }),
                    Number = 217,
                    State = ItemState.Open,
                    Title = "Intermittent serialization error in GC during build",
                    Url = "https://api.github.com/repos/thatguy-int-tests/issue-notify-tests/issues/217",
                    HtmlUrl = "https://github.com/thatguy-int-tests/issue-notify-tests/issues/217",
                    Body = "This one is a mystery"
                },
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
