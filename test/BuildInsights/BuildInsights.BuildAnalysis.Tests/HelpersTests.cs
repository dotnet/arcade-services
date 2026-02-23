// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using AwesomeAssertions;
using BuildInsights.BuildAnalysis.HandleBar;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Models.Views;
using BuildInsights.KnownIssues.Models;
using HandlebarsDotNet;
using HandlebarsDotNet.Helpers;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests
{
    [TestFixture]
    public class HelpersTests
    {
        private const string SentimentHost = "https://sentiment.example";

        private class TestData<THelper> : IDisposable, IAsyncDisposable
        {
            private readonly ServiceProvider _provider;
            public THelper Helper { get; }
            public IHandlebars Handlebars { get; }

            private TestData(ServiceProvider provider, THelper helper, IHandlebars handlebars)
            {
                _provider = provider;
                Helper = helper;
                Handlebars = handlebars;
            }

            public void Dispose()
            {
                _provider.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                return _provider.DisposeAsync();
            }

            public class Builder
            {
                public TestData<THelper> Build()
                {
                    ServiceCollection collection = new ServiceCollection();

                    ServiceProvider provider = collection.BuildServiceProvider();
                    IHandlebars hb = HandlebarsDotNet.Handlebars.Create();
                    var helper = ActivatorUtilities.CreateInstance<THelper>(provider);
                    if (helper is IHelperDescriptor<HelperOptions> inlineHelper)
                    {
                        hb.RegisterHelper(inlineHelper);
                    }
                    else if (helper is IHelperDescriptor<BlockHelperOptions> blockHelper)
                    {
                        hb.RegisterHelper(blockHelper);
                    }

                    return new TestData<THelper>(provider, helper, hb);
                }
            }

            public static TestData<THelper> BuildDefault() => new Builder().Build();
        }

        [TestCase("Logs", "LinkLogs", "[Logs](LinkLogs)")]
        [TestCase("Logs", null, "")]
        public void LinkToTest(string text, string url, string expectedOutput)
        {
            using var testData = TestData<MarkdownLinkHelper>.BuildDefault();
            string source = "{{LinkTo Text Url}}";
            HandlebarsTemplate<object, object> template = testData.Handlebars.Compile(source);

            string output = template(new { Text = text, Url = url });

            output.Should().Be(expectedOutput);
        }

        [TestCase("Logs", "LinkLogs", "<a href=\"LinkLogs\">Logs</a>")]
        [TestCase("Logs", null, "")]
        public void LinkToHtmlTest(string text, string url, string expectedOutput)
        {
            using var testData = TestData<HtmlLinkHelper>.BuildDefault();
            string source = "{{LinkToHtml Text Url}}";
            HandlebarsTemplate<object, object> template = testData.Handlebars.Compile(source);

            string output = template(new { Text = text, Url = url });
            output.Should().Be(expectedOutput);
        }

        [TestCase("master", "in master")]
        [TestCase(null, "in target branch")]
        public void TargetBranchNameTest(string target, string expectedOutput)
        {
            IHandlebars hb = Handlebars.Create();
            HandlebarHelpers.TargetBranchName(hb);
            string source = "in {{#TargetBranchName target}}";

            HandlebarsTemplate<object, object> template = hb.Compile(source);

            string output = template(new { target = target });
            output.Should().Be(expectedOutput);
        }

        private static StepResultView BuildStep()
        {
            return new StepResultView(
                stepName: "StepNameTest",
                pipelineBuildName: null,
                linkToBuild: null,
                linkToStep: null,
                errors: new ImmutableArray<Error>(),
                failureRate: null,
                stepHierarchy: ImmutableList<string>.Empty,
                knownIssues: ImmutableList<KnownIssue>.Empty,
                parameters: new MarkdownParameters(new MergedBuildResultAnalysis(), "TEST-SNAPSHOT", "TEST-PULL-REQUEST", new Repository("TEST=REPOSITORY", true))
            );
        }

        [Test]
        [TestCase(false, true, "TrueOr")]
        [TestCase(true, false, "TrueOr")]
        [TestCase(true, true, "TrueOr")]
        [TestCase(false, false, "")]
        public void OrHelperTest(bool value1, bool value2, string expectedResult)
        {
            IHandlebars hb = Handlebars.Create();
            HandlebarHelpers.OrHelper(hb);
            string source = "{{#if (or value1 value2)}}TrueOr{{/if}}";
            HandlebarsTemplate<object, object> template = hb.Compile(source);

            string output = template(new { value1, value2 });
            output.Should().Be(expectedResult);
        }

        [Test]
        [TestCase("Repo", "Infrastructure", "")]
        [TestCase("Infrastructure", "Infrastructure", "TrueEq")]
        [TestCase("Infrastructure", "infrastructure", "")]
        public void EqHelperTest(string value1, string value2, string expectedResult)
        {
            IHandlebars hb = Handlebars.Create();
            HandlebarHelpers.EqHelper(hb);
            string source = "{{#if (eq value1 value2)}}TrueEq{{/if}}";
            HandlebarsTemplate<object, object> template = hb.Compile(source);

            string output = template(new { value1, value2 });
            output.Should().Be(expectedResult);
        }

        [Test]
        public void DateTimeFormatter()
        {
            IHandlebars hb = Handlebars.Create();
            HandlebarHelpers.DateTimeFormatter(hb);
            string source = "{{Date}}";
            HandlebarsTemplate<object, object> template = hb.Compile(source);

            string output = template(new { Date = new DateTimeOffset(2021, 2, 26, 0, 0, 0, 0, TimeSpan.Zero) });

            output.Should().Be("2021-02-26");
        }

        [Test]
        [TestCase("1234", 2, "12")]
        [TestCase("1234", 6, "1234")]
        public void TruncateTest(string str, int len, string expectedResult)
        {
            using var testData = TestData<TruncateHelper>.BuildDefault();
            string source = "{{truncate str len}}";
            HandlebarsTemplate<object, object> template = testData.Handlebars.Compile(source);

            string output = template(new { str, len });

            output.Should().Be(expectedResult);
        }

        [Test]
        [TestCase("1234", "abcd")]
        [TestCase(1234, 2)]
        [TestCase("1234", -1)]
        public void TruncateExceptionsTest(object str, object len)
        {
            using var testData = TestData<TruncateHelper>.BuildDefault();
            string source = "{{truncate str len}}";
            HandlebarsTemplate<object, object> template = testData.Handlebars.Compile(source);

            Action action = () => template(new { str, len });

            action.Should().Throw<HandlebarsException>();
        }

        [TestCase("Stuff. Failure log: https://helix.test", "Stuff. <a href=\"https://helix.test\">[Failure log]</a>")]
        [TestCase("Check the Test tab or this console log: https://helix.test", "<a href=\"https://helix.test\">Check the Test tab or [this console log]</a>")]
        [TestCase("No link here", "No link here")]
        public void RenderKnownLinkFormats(string errorText, string expectedMarkdown)
        {
            using var testData = TestData<RenderKnownLinks>.BuildDefault();
            string source = "{{RenderKnownlinks errorText}}";

            HandlebarsTemplate<object, object> template = testData.Handlebars.Compile(source);

            string output = template(new { errorText });
            output.Should().Be(expectedMarkdown);
        }

        [TestCase("12345678910", 5, "<i>expand to see the full error</i><ul><details><summary>12345</summary>678910</details></ul>")]
        [TestCase("12345678910", 20, "12345678910")]
        public void SplitMessageIntoCollapsibleSectionsByLength(string errorText, int length, string expectedMarkdown)
        {
            using var testData = TestData<SplitMessageIntoCollapsibleSectionsByLength>.BuildDefault();
            string source = "{{SplitMessageIntoCollapsibleSectionsByLength errorText length}}";

            HandlebarsTemplate<object, object> template = testData.Handlebars.Compile(source);

            string output = template(new { errorText, length });
            output.Should().Be(expectedMarkdown);
        }

        [TestCase(500)]
        [TestCase(1)]
        [TestCase(HandlebarHelpers.ResultsLimit)]
        public void GreaterThanLimitTest(int totalRecords)
        {
            bool expectedResult = totalRecords > HandlebarHelpers.ResultsLimit;

            IHandlebars hb = Handlebars.Create();
            HandlebarHelpers.GreaterThanLimit(hb);
            string source = "{{#if (gt totalRecords)}}true{{else}}false{{/if}}";
            HandlebarsTemplate<object, object> template = hb.Compile(source);
            string output = template(new { totalRecords });
            output.Should().Be(expectedResult ? "true" : "false");
        }

        [TestCase(500)]
        [TestCase(1)]
        [TestCase(HandlebarHelpers.ResultsLimit)]
        public void GetNumberOfRecordsNotDisplayed(int totalRecords)
        {
            int expectedResult = totalRecords - HandlebarHelpers.ResultsLimit;

            IHandlebars hb = Handlebars.Create();
            HandlebarHelpers.GetNumberOfRecordsNotDisplayed(hb);
            string source = "{{#GetNumberOfRecordsNotDisplayed totalRecords}}";
            HandlebarsTemplate<object, object> template = hb.Compile(source);
            string output = template(new { totalRecords });
            output.Should().Be(expectedResult.ToString());
        }

        [Test]
        public void FailingConfigurationBlockWithNoFailingConfigurationsTest()
        {
            Dictionary<string, object> context = new Dictionary<string, object>()
            {
                { "FailingConfigurations", new List<FailingConfiguration>() { } }
            };
            string expectedResult = "";

            using var testData = TestData<FailingConfigurationBlock>.BuildDefault();
            string source = "{{FailingConfigurationBlock}}";
            HandlebarsTemplate<object, object> template = testData.Handlebars.Compile(source);

            string output = template(context);

            output.Should().Be(expectedResult);
        }

        [Test]
        public void FailingConfigurationBlockWithThreeFailingConfigurationsAndWithLinksTest()
        {
            Dictionary<string, object> context = new Dictionary<string, object>()
            {
                { "FailingConfigurations", new List<FailingConfiguration>()
                    {
                        new() {
                            Configuration = MockConfiguration("FAKE.CONFIGURATION.WINDOWS"),
                            TestLogs = "https://www.test.com/TestLogs1",
                            HistoryLink = "https://www.test.com/HistoryLink1",
                            ArtifactLink = "https://www.test.com/ArtifactLink1"
                        },
                        new() {
                            Configuration = MockConfiguration("FAKE.CONFIGURATION.LINUX"),
                            TestLogs = "https://www.test.com/TestLogs2",
                            HistoryLink = "https://www.test.com/HistoryLink2",
                            ArtifactLink = "https://www.test.com/ArtifactLink2"
                        },
                        new() {
                            Configuration = MockConfiguration("FAKE.CONFIGURATION.ARM"),
                            TestLogs = "https://www.test.com/TestLogs3",
                            HistoryLink = "https://www.test.com/HistoryLink3",
                            ArtifactLink = "https://www.test.com/ArtifactLink3"
                        }
                    }
                }
            };

            using var testData = TestData<FailingConfigurationBlock>.BuildDefault();
            string source = "{{FailingConfigurationBlock}}";
            HandlebarsTemplate<object, object> template = testData.Handlebars.Compile(source);

            string output = template(context);

            output.Should().Contain("<details open>");
            output.Should().Contain("Failing Configurations (3)");
            foreach (var config in (context["FailingConfigurations"] as List<FailingConfiguration>))
            {
                output.Should().Contain(config.Configuration.Name);
                output.Should().Contain(config.TestLogs);
                output.Should().Contain(config.HistoryLink);
                output.Should().Contain(config.ArtifactLink);
            }
        }

        [Test]
        public void FailingConfigurationBlockWithFourFailingConfigurationsAndNoLinksTest()
        {
            Dictionary<string, object> context = new Dictionary<string, object>()
            {
                { "FailingConfigurations", new List<FailingConfiguration>()
                    {
                        new() {
                            Configuration = MockConfiguration("FAKE.CONFIGURATION.WINDOWS")
                        },
                        new() {
                            Configuration = MockConfiguration("FAKE.CONFIGURATION.LINUX")
                        },
                        new() {
                            Configuration = MockConfiguration("FAKE.CONFIGURATION.ARM")
                        },
                        new() {
                            Configuration = MockConfiguration("FAKE.CONFIGURATION.DOCKER")
                        }
                    }
                }
            };

            using var testData = TestData<FailingConfigurationBlock>.BuildDefault();
            string source = "{{FailingConfigurationBlock}}";
            HandlebarsTemplate<object, object> template = testData.Handlebars.Compile(source);

            string output = template(context);

            output.Should().Contain("<details>");
            output.Should().Contain("Failing Configurations (4)");
            foreach (var config in (context["FailingConfigurations"] as List<FailingConfiguration>))
            {
                output.Should().Contain(config.Configuration.Name);
                output.Should().NotContain("[Details]");
                output.Should().NotContain("[History]");
                output.Should().NotContain("[Artifacts]");
            }
        }

        private Configuration MockConfiguration(string name)
        {
            var testCaseResult = new TestCaseResult("", MockDateTimeOffset(), TestOutcomeValue.Failed, 0, 1, 2,
                new PreviousBuildRef(), "", "", "", null, 55000, 1);

            return new Configuration(name, "ANY_ORGANIZATION", "ANY_PROJECT", testCaseResult);
        }

        private static DateTimeOffset MockDateTimeOffset()
        {
            return new DateTimeOffset(2021, 5, 12, 0, 0, 0, TimeSpan.Zero);
        }
    }
}
