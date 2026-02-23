// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests.Models;

[TestFixture]
public class BuildAnalysisUpdateOverridenResultTests
{

    public string bodyWitOutPreviousverride = "<details open>\r\n<summary><h2>Known Repository Errors</h2></summary>\r\n\r\n<ul>\r\n:x:<a href=\"https://dev.azure.com/dnceng-public/cbb18261-c48f-4abb-8651-8cdcb5474649/_build/results?buildId=537934\">20240123.1 / FailingTests</a> <b>and 1 more hits</b>  - <a href=\"https://github.com/maestro-auth-test/build-result-analysis-test/issues/796\">[Testing] Validate known issue.</a>\r\n</ul>\r\n\r\n</details>";

    [Test]
    public void GetCheckResultCleanBody_WhenCheckResultBodyHasNoOverride()
    {

        var actual = new BuildAnalysisUpdateOverridenResult("ANY_REASON", "FAILED_TEST", "PASS_TEST", bodyWitOutPreviousverride);
        var expected = new StringBuilder();
        expected.AppendLine(BuildAnalysisUpdateOverridenResult.OverrideResultIdentifier);
        expected.AppendLine(bodyWitOutPreviousverride);

        actual.CheckResultBody.Should().Be(expected.ToString());
    }

    [Test]
    public void GetCheckResultCleanBody_WhenCheckResultBodyHasPreviousOverride()
    {

        var bodyWithPreviousOverride = $"<h3>:bangbang: Build Analysis Check Result has been manually overridden </h3>\r\n<ul>\r\n<li>The build analysis check result has been updated by the user for the following reason: <i> Test :smile:</i> </br></li>\r\n</ul>\r\n\r\n<!--  Build Analysis Check Run Override -->{bodyWitOutPreviousverride}";

        var actual = new BuildAnalysisUpdateOverridenResult("ANY_REASON", "FAILED_TEST", "PASS_TEST", bodyWithPreviousOverride);

        var expected = new StringBuilder();
        expected.AppendLine(BuildAnalysisUpdateOverridenResult.OverrideResultIdentifier);
        expected.AppendLine(bodyWitOutPreviousverride);
        actual.CheckResultBody.Should().Be(expected.ToString());
    }

    [Test]
    public void BuildAnalysisUpdateOverridenResultViewIsIn()
    {
        var actual = new BuildAnalysisUpdateOverridenResult("ANY_REASON", "FAILED_TEST", "PASS_TEST", bodyWitOutPreviousverride);
        actual.CheckResultBody.Should().Contain(BuildAnalysisUpdateOverridenResult.OverrideResultIdentifier);
    }
}
