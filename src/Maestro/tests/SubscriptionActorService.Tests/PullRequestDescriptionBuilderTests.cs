using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using static SubscriptionActorService.PullRequestActorImplementation;
using FluentAssertions;

namespace SubscriptionActorService.Tests
{
    [TestFixture]
    public class PullRequestDescriptionBuilderTests
    {
        public PullRequestDescriptionBuilder PullRequestDescriptionBuilder; 

        [SetUp]
        public void PullRequestDescriptionBuilderTests_SetUp()
        {
            PullRequestDescriptionBuilder = new PullRequestDescriptionBuilder(new Mock<ILoggerFactory>().Object);
        }

        private List<DependencyUpdate> CreateDependencyUpdates()
        {
            return new List<DependencyUpdate>
            {
                new DependencyUpdate
                {
                    From = new DependencyDetail
                    {
                        Name = "from dependency name 1",
                        Version = "1.0.0",
                        CoherentParentDependencyName = "from parent name 1"
                    },
                    To = new DependencyDetail
                    {
                        Name = "to dependency name 1",
                        Version = "1.0.0",
                        CoherentParentDependencyName = "from parent name 2"
                    }
                },
                new DependencyUpdate
                {
                    From = new DependencyDetail
                    {
                        Name = "from dependency name 2",
                        Version = "1.0.0",
                        CoherentParentDependencyName = "from parent name 2"
                    },
                    To = new DependencyDetail
                    {
                        Name = "to dependency name 2",
                        Version = "1.0.0",
                        CoherentParentDependencyName = "from parent name 2"
                    }
                }
            };
        }

        private string BuildCorrectPRDescriptionWhenNonCoherencyUpdate()
        {
            StringBuilder stringBuilder = new StringBuilder();
            return stringBuilder.ToString();
        }

        [Test]
        public void ShouldReturnCalculateCorrectPRDescriptionWhenNonCoherencyUpdate()
        {
            UpdateAssetsParameters update = new UpdateAssetsParameters
            {
                IsCoherencyUpdate = true
            };
            List<DependencyUpdate> deps = CreateDependencyUpdates();
            StringBuilder description = new StringBuilder();

            PullRequestDescriptionBuilder.CalculatePRDescription(update, deps, null, description, null, 0);
            description.Should().Equals(BuildCorrectPRDescriptionWhenNonCoherencyUpdate());
        }
    }
}
