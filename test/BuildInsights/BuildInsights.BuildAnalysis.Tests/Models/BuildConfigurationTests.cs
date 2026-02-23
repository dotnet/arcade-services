// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests.Models;

[TestFixture]
public class BuildConfigurationTests
{
    private readonly string buildConfigurationFileJson =
        "{\r\n\"RetryByAnyError\": false,\r\n\"RetryCountLimit\": 5,\r\n\"RetryByErrors\": [\r\n{\r\n\"ErrorRegex\": \"RetryErrorRegex01\"\r\n},\r\n{\r\n\"ErrorRegex\": \"RetryErrorRegex02\"\r\n}\r\n],\r\n\"RetryByPipeline\": {\r\n\"RetryJobs\":[{ \"JobName\": \"RetryByPipeline+RetryJobs+JobName\" } ],\r\n\"RetryStages\": [\r\n{ \"StageName\": \"RetryStage01\" },\r\n{ \"StageName\": \"RetryStage02\" }\r\n],\r\n\"RetryPhases\": [\r\n{ \"PhaseName\": \"RetryPhase01\" },\r\n { \"PhaseName\": \"RetryPhase02\" }\r\n]\r\n}\r\n}";

    private readonly string buildConfigurationRetryByErrorsInPipeline =
        "{\r\n\"RetryByErrorsInPipeline\":\r\n{\r\n\"ErrorInPipelineByStage\":[\r\n{\r\n\"StageName\": \"A\",\r\n \"ErrorRegex\":\"StageRegex\"\r\n}\r\n],\r\n\"ErrorInPipelineByJobs\":[\r\n{\r\n\"JobsNames\":[\r\n\"JA\",\r\n\"JB\"\r\n],\r\n\"ErrorRegex\":\"JobRegex\"\r\n}\r\n],\r\n\"ErrorInPipelineByJobsInStage\":[\r\n{\r\n\"StageName\":\"SA\",\r\n\"JobsNames\":[\r\n\"SJA\",\r\n\"SJB\"\r\n],\r\n\"ErrorRegex\":\"JobStageRegex\"\r\n}\r\n]\r\n}\r\n}";

    [Test]
    public void JsonConvertBuildConfiguration()
    {
        BuildConfiguration buildSetting = JsonSerializer.Deserialize<BuildConfiguration>(buildConfigurationFileJson);
        buildSetting.RetryCountLimit.Should().Be(5);
        buildSetting.RetryByErrors.Should().HaveCount(2);
        buildSetting.RetryByAnyError.Should().BeFalse();
        buildSetting.RetryByPipeline.RetryJobs.Should().HaveCount(1);
        buildSetting.RetryByPipeline.RetryPhases.Should().HaveCount(2);
        buildSetting.RetryByPipeline.RetryStages.Should().HaveCount(2);
        buildSetting.RetryByPipeline.RetryStages.First().StageName.Should().Be("RetryStage01");
        buildSetting.RetryByErrors.First().ErrorRegex.Should().Be("RetryErrorRegex01");
    }

    [Test]
    public void JsonConverterBuildConfigurationWithRetryErrosInPipeline()
    {
        BuildConfiguration buildSetting = JsonSerializer.Deserialize<BuildConfiguration>(buildConfigurationRetryByErrorsInPipeline);
        buildSetting.RetryByAnyError.Should().BeFalse();
        buildSetting.RetryByErrorsInPipeline.ErrorInPipelineByStage.Should().HaveCount(1);
        buildSetting.RetryByErrorsInPipeline.ErrorInPipelineByStage.First().ErrorRegex.Should().Be("StageRegex");
        buildSetting.RetryByErrorsInPipeline.ErrorInPipelineByJobs.First().JobsNames.Should().HaveCount(2);
        buildSetting.RetryByErrorsInPipeline.ErrorInPipelineByJobs.First().ErrorRegex.Should().Be("JobRegex");
        buildSetting.RetryByErrorsInPipeline.ErrorInPipelineByJobsInStage.First().StageName.Should().Be("SA");
        buildSetting.RetryByErrorsInPipeline.ErrorInPipelineByJobsInStage.First().JobsNames.Should().HaveCount(2);
        buildSetting.RetryByErrorsInPipeline.ErrorInPipelineByJobsInStage.First().ErrorRegex.Should().Be("JobStageRegex");
    }
}
