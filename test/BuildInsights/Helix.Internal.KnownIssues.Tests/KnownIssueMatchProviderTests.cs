using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Internal.Helix.KnownIssues.Models;
using Microsoft.Internal.Helix.KnownIssues.Providers;
using NUnit.Framework;

namespace Helix.Internal.KnownIssues.Tests;

[TestFixture]
public partial class KnownIssueMatchProviderTests
{
    [TestDependencyInjectionSetup]
    public static class TestSetup
    {
        public static void Defaults(IServiceCollection collection)
        {
            collection.Configure<KnownIssuesAnalysisLimits>(o =>
            {
                o.FailingTestCountLimit = 1234;
                o.HelixLogsFilesLimit = 1234;
                o.LogLinesCountLimit = 1234;
                o.RecordCountLimit = 1234;
            });
        }

        public static Func<IServiceProvider, KnownIssuesMatchProvider> Provider(IServiceCollection collection)
        {
            collection.AddSingleton<KnownIssuesMatchProvider>();
            return s => s.GetRequiredService<KnownIssuesMatchProvider>();
        }
    }

    [TestCase("Assert.True() Failure", "Expected: True", 1)] // consecutive errors
    [TestCase("Assert.True() Failure", "Actual:   False", 1)] // non consecutive errors]
    [TestCase("Assert.True() Failure", "Expected: Something unexpected", 0)] // no all errors are present
    [TestCase("Actual:   False", "Assert.True() Failure", 0)] // all errors present but different order
    public async Task KnownIssueMatchProviderMultiLineStringTest(string firstErrorMessage, string secondErrorMessage, int expectedResult)
    {
        string errorLine = "Assert.True() Failure\r\nExpected: True\r\nActual:   False";
        var errorMessages = new List<string> {firstErrorMessage, secondErrorMessage};

        await using TestData testData = await TestData.Default.BuildAsync();
        List<KnownIssue> result = testData.Provider.GetKnownIssuesInString(errorLine, MockKnownIssue(errorMessages, false));

        result.Should().HaveCount(expectedResult);
    }

    [TestCase("Assert.True() Failure\r\nExpected: True\r\nActual:   False", 1)] // single line with actual error
    [TestCase("Assert.True() Failure", 1)] // single line with section of the error
    public async Task KnownIssueMatchProviderMultiLineStringSingleErrorTest(string errorMessage, int expectedResult)
    {
        string errorLine = "Assert.True() Failure\r\nExpected: True\r\nActual:   False";
        var errorMessages = new List<string> {errorMessage};

        await using TestData testData = await TestData.Default.BuildAsync();
        List<KnownIssue> result = testData.Provider.GetKnownIssuesInString(errorLine, MockKnownIssue(errorMessages, false));

        result.Should().HaveCount(expectedResult);
    }

    [Test]
    public async Task KnownIssueMatchProviderSimpleStringMultiErrorsTest()
    {
        string errorLine = "Assert.True() Failure";
        var errorMessages = new List<string> {"Assert.True() Failure", "Expected: True"};

        await using TestData testData = await TestData.Default.BuildAsync();
        List<KnownIssue> result = testData.Provider.GetKnownIssuesInString(errorLine, MockKnownIssue(errorMessages, false));

        result.Should().HaveCount(0);
    }

    [Test]
    public async Task KnownIssueMatchProviderSimpleStringSingleErrorsTest()
    {
        string errorLine = "Assert.True() Failure";
        var errorMessage = new List<string> {"Assert.True() Failure"};

        await using TestData testData = await TestData.Default.BuildAsync();
        List<KnownIssue> result = testData.Provider.GetKnownIssuesInString(errorLine, MockKnownIssue(errorMessage, false));

        result.Should().HaveCount(1);
    }

    [TestCase("Assert.True() Failure", "Expected: True", 1)] // consecutive errors
    [TestCase("Assert.True() Failure", "Actual:   False", 1)] // non consecutive errors]
    [TestCase("Assert.True() Failure", "Expected: Something unexpected", 0)] // no all errors are present
    [TestCase("Actual:   False", "Assert.True() Failure", 0)] // all errors present but different order
    public async Task KnownIssueMatchProviderStreamTest(string firstErrorMessage, string secondErrorMessage, int expectedResult)
    {
        var errorMessages = new List<string> {firstErrorMessage, secondErrorMessage};
        Stream stream = new MemoryStream(Encoding.UTF8.GetBytes("Assert.True() Failure\r\nExpected: True\r\nActual:   False"));

        await using TestData testData = await TestData.Default.BuildAsync();
        List<KnownIssue> result = await testData.Provider.GetKnownIssuesInStream(stream, MockKnownIssue(errorMessages, false));

        result.Should().HaveCount(expectedResult);
    }

    [TestCase("Assert.True() Failure\r\nExpected: True\r\nActual:   False", 0)] // single line with actual error
    [TestCase("Assert.True() Failure", 1)] // single line with section of the error
    public async Task KnownIssueMatchProviderStreamSingleErrorTest(string errorMessage, int expectedResult)
    {
        Stream stream = new MemoryStream(Encoding.UTF8.GetBytes("Assert.True() Failure\r\nExpected: True\r\nActual:   False"));
        var errorMessages = new List<string> {errorMessage};

        await using TestData testData = await TestData.Default.BuildAsync();
        List<KnownIssue> result = await testData.Provider.GetKnownIssuesInStream(stream, MockKnownIssue(errorMessages, false));

        result.Should().HaveCount(expectedResult);
    }

    private List<KnownIssue> MockKnownIssue(List<string> errorsToMatch, bool isRegex)
    {
        return new List<KnownIssue> {new(null, errorsToMatch, KnownIssueType.Infrastructure, new KnownIssueOptions(regexMatching: isRegex))};
    }
}
