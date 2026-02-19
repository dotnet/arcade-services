using System;
using AwesomeAssertions;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Tests.Models
{
    [TestFixture]
    public class StartedBuildMessageTests
    {
        [TestCase("https://dev.azure.com/dnceng/00000000-0000-0000-0000-00000000000A/_apis/pipelines/321/runs/123456", "00000000-0000-0000-0000-00000000000A")]
        [TestCase("https://dnceng.visualstudio.com/00000000-0000-0000-0000-00000000000B/_apis/pipelines/686/runs/1566341", "00000000-0000-0000-0000-00000000000B")]
        public void GetIdsTest(string url, string expectedProjectId)
        {
            var startedBuildMessage = new StartedBuildMessage
            {
                Resource = new BuildStartedResource
                {
                    Id = 1,
                    Url = url
                }
            };

            string potentialProjectGuid = startedBuildMessage.GetProjectId();
            potentialProjectGuid.Should().Be(expectedProjectId);

            startedBuildMessage.GetOrgId().Should().Be("dnceng");
        }

        [Test]
        public void ExpectError_GetOrgIdWhenNonExistant()
        {
            var startedBuildMessage = new StartedBuildMessage
            {
                Resource = new BuildStartedResource
                {
                    Id = 1,
                    Url = null
                }
            };

            Action act = () => { var x = startedBuildMessage.GetOrgId(); };
            act.Should().Throw<Exception>();
        }

        [TestCase("https://dev.azure.com/dnceng/00000000-0000-0000-0000-00000000000A/_apis/not-pipelines/321/runs/123456")]
        [TestCase("https://dnceng.visualstudio.com/00000000-0000-0000-0000-00000000000B/_apis/not-pipelines/686/runs/1566341")]
        public void ExpectError_GetOrgIdWhenUrlIncorrect(string url)
        {
            var startedBuildMessage = new StartedBuildMessage
            {
                Resource = new BuildStartedResource
                {
                    Id = 1,
                    Url = url
                }
            };

            Action act = () => { var x = startedBuildMessage.GetOrgId(); };
            act.Should().Throw<Exception>();
        }
    }
}
