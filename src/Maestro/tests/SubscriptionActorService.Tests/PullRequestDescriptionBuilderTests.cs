using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using static SubscriptionActorService.PullRequestActorImplementation;
using FluentAssertions;
using Maestro.Data.Models;

namespace SubscriptionActorService.Tests
{
    [TestFixture]
    public class PullRequestDescriptionBuilderTests : PullRequestActorTests
    {
        public PullRequestDescriptionBuilder PullRequestDescriptionBuilder; 

        [SetUp]
        public void PullRequestDescriptionBuilderTests_SetUp()
        {
            PullRequestDescriptionBuilder = new PullRequestDescriptionBuilder(new Mock<ILoggerFactory>().Object);
        }

        private List<DependencyUpdate> CreateDependencyUpdates(char version)
        {
            return new List<DependencyUpdate>
            {
                new DependencyUpdate
                {
                    From = new DependencyDetail
                    {
                        Name = $"from dependency name 1{version}",
                        Version = $"1.0.0{version}",
                        CoherentParentDependencyName = $"from parent name 1{version}",
                        Commit = $"{version} commit from 1"
                    },
                    To = new DependencyDetail
                    {
                        Name = $"to dependency name 1{version}",
                        Version = $"1.0.0{version}",
                        CoherentParentDependencyName = $"from parent name 1{version}",
                        RepoUri = "https://amazing_uri.com",
                        Commit = $"{version} commit to 1"
                    }
                },
                new DependencyUpdate
                {
                    From = new DependencyDetail
                    {
                        Name = $"from dependency name 2{version}",
                        Version = $"1.0.0{version}",
                        CoherentParentDependencyName = $"from parent name 2{version}",
                        Commit = $"{version} commit from 2"
                    },
                    To = new DependencyDetail
                    {
                        Name = $"to dependency name 2{version}",
                        Version = $"1.0.0{version}",
                        CoherentParentDependencyName = $"from parent name 2{version}",
                        RepoUri = "https://amazing_uri.com",
                        Commit = $"{version} commit to 2"
                    }
                }
            };
        }

        public UpdateAssetsParameters CreateUpdateAssetsParameters(bool isCoherencyUpdate, string guid)
        {
            return new UpdateAssetsParameters
            {
                IsCoherencyUpdate = isCoherencyUpdate,
                SourceRepo = "The best repo",
                SubscriptionId = new Guid(guid)
            };
        }

        private string BuildCorrectPRDescriptionWhenNonCoherencyUpdate(List<DependencyUpdate> deps)
        {
            StringBuilder stringBuilder = new StringBuilder();
            foreach(DependencyUpdate dep in deps)
            {
                stringBuilder.AppendLine($"  - **{dep.To.Name}**: from {dep.From.Version} to {dep.To.Version} (parent: {dep.To.CoherentParentDependencyName})");
            }
            return stringBuilder.ToString();
        }

        private string BuildCorrectPRDescriptionWhenCoherencyUpdate(List<DependencyUpdate> deps, int startingId)
        {
            StringBuilder builder = new StringBuilder();
            List<string> urls = new List<string>();
            for(int i = 0; i < deps.Count; i++)
            {
                urls.Add(PullRequestDescriptionBuilder.GetChangesURI(deps[i].To.RepoUri, deps[i].From.Commit, deps[i].To.Commit));
                builder.AppendLine($"  - **{deps[i].To.Name}**: [from {deps[i].From.Version} to {deps[i].To.Version}][{startingId + i}]");
            }
            builder.AppendLine();
            for(int i = 0; i < urls.Count; i++)
            {
                builder.AppendLine($"[{i + startingId}]: {urls[i]}");
            }
            return builder.ToString();
        }

        [Test]
        public void ShouldReturnCalculateCorrectPRDescriptionWhenNonCoherencyUpdate()
        {
            UpdateAssetsParameters update = CreateUpdateAssetsParameters(true, "11111111-1111-1111-1111-111111111111");
            List<DependencyUpdate> deps = CreateDependencyUpdates('a');
            StringBuilder description = new StringBuilder();

            PullRequestDescriptionBuilder.CalculatePRDescription(update, deps, null, description, null, 0);
            description.ToString().Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps));
        }

        [Test]
        public void ShouldReturnCalculateCorrectPRDescriptionWhenCoherencyUpdate()
        {
            UpdateAssetsParameters update1 = CreateUpdateAssetsParameters(false, "11111111-1111-1111-1111-111111111111");
            UpdateAssetsParameters update2 = CreateUpdateAssetsParameters(false, "22222222-2222-2222-2222-222222222222");
            List<DependencyUpdate> deps1 = CreateDependencyUpdates('a');
            List<DependencyUpdate> deps2 = CreateDependencyUpdates('b');
            Build build = GivenANewBuild(true);
            StringBuilder description = new StringBuilder();

            int startingReferenceId = 0;
            startingReferenceId += PullRequestDescriptionBuilder.CalculatePRDescription(update1, deps1, null, description, build, startingReferenceId);
            startingReferenceId += PullRequestDescriptionBuilder.CalculatePRDescription(update2, deps2, null, description, build, startingReferenceId);
            description.ToString().Should().Contain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps1, 0));
            description.ToString().Should().Contain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps2, 2));
        }
    }
}
