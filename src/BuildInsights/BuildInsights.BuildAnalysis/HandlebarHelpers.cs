// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using HandlebarsDotNet;
using HandlebarsDotNet.Helpers;
using HandlebarsDotNet.IO;
using HandlebarsDotNet.PathStructure;
using Microsoft.Extensions.DependencyInjection;
using BuildInsights.BuildAnalysis.Models;
using Microsoft.Internal.Helix.Utility.UserSentiment;

namespace BuildInsights.BuildAnalysis;

public enum TrackingLinkId
{
    TestArtifactsLink = 1,
    TestConfigurationArtifactsLink = 2,
    TestTestLink = 3,
    TestConfigurationTestLink = 4,
    TestHistoryLink = 5,
    TestConfigurationHistoryLink = 6,
}

public class HandlebarHelpers
{
    private readonly IServiceProvider _provider;

    public HandlebarHelpers(IServiceProvider provider)
    {
        _provider = provider;
    }

    public abstract class BlockHelper : IHelperDescriptor<BlockHelperOptions>
    {
        public abstract PathInfo Name { get; }
        public object Invoke(in BlockHelperOptions options, in Context context, in Arguments arguments)
        {
            return this.ReturnInvoke(options, context, arguments);
        }

        public abstract void Invoke(
            in EncodedTextWriter output,
            in BlockHelperOptions options,
            in Context context,
            in Arguments arguments);
    }

    public abstract class InlineHelper : IHelperDescriptor<HelperOptions>
    {
        public abstract PathInfo Name { get; }
        public object Invoke(in HelperOptions options, in Context context, in Arguments arguments)
        {
            return this.ReturnInvoke(options, context, arguments);
        }

        public abstract void Invoke(
            in EncodedTextWriter output,
            in HelperOptions options,
            in Context context,
            in Arguments arguments);
    }

    public const int ResultsLimit = 3;

    public void AddHelpers(IHandlebars hb)
    {
        foreach (var helper in typeof(HandlebarHelpers).GetNestedTypes())
        {
            if (helper.IsAbstract)
                continue;

            if (typeof(BlockHelper).IsAssignableFrom(helper))
            {
                hb.RegisterHelper((BlockHelper)ActivatorUtilities.CreateInstance(_provider, helper));
            }
            if (typeof(InlineHelper).IsAssignableFrom(helper))
            {
                hb.RegisterHelper((InlineHelper)ActivatorUtilities.CreateInstance(_provider, helper));
            }
        }

        TargetBranchName(hb);
        DateTimeFormatter(hb);
        LimitedEach(hb);
        OrHelper(hb);
        EqHelper(hb);
        GreaterThanLimit(hb);
        GetNumberOfRecordsNotDisplayed(hb);
        ForTake(hb);
    }

    public static void TargetBranchName(IHandlebars hb)
    {
        hb.RegisterHelper("TargetBranchName", (writer, context, parameters) =>
        {
            if (parameters.Length == 1 && parameters[0] is string name)
            {
                writer.Write(name);
            }
            else
            {
                writer.WriteSafeString("target branch");
            }
        });
    }


    public static void GreaterThanLimit(IHandlebars hb)
    {
        hb.RegisterHelper("gt", (context, parameters) =>
        {
            if (parameters.Length != 1 || !(parameters[0] is int))
            {
                throw new HandlebarsException("{{#AreRecordsNotDisplayed}} helper must have an int");
            }
            int totalRecords = (int)parameters[0];
            int numberOfResultsNotDisplayed = totalRecords - ResultsLimit;
            return numberOfResultsNotDisplayed > 0;
        });
    }

    public static void GetNumberOfRecordsNotDisplayed(IHandlebars hb)
    {
        hb.RegisterHelper("GetNumberOfRecordsNotDisplayed", (writer, context, parameters) =>
        {
            if (parameters.Length != 1 || !(parameters[0] is int))
            {
                throw new HandlebarsException("{{#GetNumberOfRecordsNotDisplayed}} helper must have an int");
            }

            int totalRecords = (int)parameters[0];
            int numberOfResultsNotDisplayed = totalRecords - ResultsLimit;
            writer.Write($"{numberOfResultsNotDisplayed}");
        });
    }

    public static void OrHelper(IHandlebars hb)
    {
        hb.RegisterHelper("or", (context, parameters) =>
        {
            if (parameters.Length < 2)
                throw new HandlebarsException("{{# (or ) }} helper must have at least two arguments");

            bool orResult = false;
            foreach (object obj in parameters)
            {
                if (!(obj is bool objBool))
                    throw new HandlebarsException("{{# (or ) }} helper only supports arguments of type: bool");

                orResult = objBool || orResult;
            }

            return orResult;
        });
    }

    /// <summary>
    /// Registers helper for string comparison in handlebar <paramref name="hb"/>
    /// The string comparison is case sensitive
    /// </summary>
    /// <param name="hb"></param>
    /// <exception cref="HandlebarsException"></exception>

    public static void EqHelper(IHandlebars hb)
    {
        hb.RegisterHelper("eq", (context, parameters) =>
        {
            if (parameters.Length != 2)
                throw new HandlebarsException("{{# (eq ) }} helper must have two arguments");

            return parameters[0].ToString().Equals(parameters[1].ToString());
        });
    }

    public static void LimitedEach(IHandlebars hb)
    {
        hb.RegisterHelper("limited-each", (writer, options, context, arguments) =>
        {
            if (arguments.Length == 0)
            {
                throw new HandlebarsException("{{#limited-each}} helper must have an ICollection argument");
            }

            if (arguments[0] is ICollection collection)
            {
                IEnumerator enumerator = collection.GetEnumerator();
                for (int i = 0; i < ResultsLimit; i++)
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }

                    options.Template(writer, enumerator.Current);
                }

                int countNotShowedResults = collection.Count - ResultsLimit;
                if (arguments.Length > 2 && countNotShowedResults > 0)
                {
                    string markdownFormat = arguments[1] as string;
                    string message = arguments[2] as string;

                    writer.WriteSafeString($"{markdownFormat} {countNotShowedResults} {message} \n");
                }
            }
        });
    }

    public static void ForTake(IHandlebars hb)
    {
        hb.RegisterHelper("for-take", (writer, options, context, arguments) =>
        {
            if (arguments.Length < 2)
            {
                throw new HandlebarsException("{{#for-take}} helper must have an ICollection argument and an int argument");
            }

            if (arguments[0] is not ICollection collection)
            {
                throw new HandlebarsException("{{#for-take}} helper expects argument to be ICollection");
            }

            if (arguments[1] is not int takeRecords || takeRecords < 0)
            {
                throw new HandlebarsException("{{#for-take}} helper expects argument to be positive int");
            }

            int skippedResultsCount = collection.Count - takeRecords;
            if (arguments.Length == 4 && skippedResultsCount > 0)
            {
                if (arguments[2] is not string markdownFormat)
                {
                    throw new HandlebarsException("{{for-take}} helper expects third argument to be string");
                }

                if (arguments[3] is not string message)
                {
                    throw new HandlebarsException("{{for-take}} helper expects fourth argument to be string");
                }

                writer.WriteSafeString($"{markdownFormat} {skippedResultsCount} {message} \n");
            }

            IEnumerator enumerator = collection.GetEnumerator();
            for (int i = 0; i < takeRecords; i++)
            {
                if (!enumerator.MoveNext())
                {
                    break;
                }

                options.Template(writer, enumerator.Current);
            }
        });
    }

    public class FailingConfigurationBlock : InlineHelper
    {
        public override PathInfo Name => "FailingConfigurationBlock";

        public override void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context, in Arguments arguments)
        {
            Dictionary<string, object> contextDict = (Dictionary<string, object>)context.Value;
            List<FailingConfiguration> configs = contextDict["FailingConfigurations"] as List<FailingConfiguration>;
            if (configs != null)
            {
                int configCount = configs.Count;

                if (configCount > 0)
                {
                    if (configCount > 3)
                    {
                        output.WriteSafeString("<details>\n");
                    }
                    else
                    {
                        output.WriteSafeString("<details open>\n");
                    }

                    if (configCount > 1)
                    {
                        output.WriteSafeString("<summary><h4>Failing Configurations (" + configCount + ")</h4></summary>\n\n");
                    }
                    else
                    {
                        output.WriteSafeString("<summary><h4>Failing Configuration</h4></summary>\n\n");
                    }

                    output.WriteSafeString("<ul>");

                    foreach (var fc in configs)
                    {
                        output.WriteSafeString("<li>");
                        output.WriteSafeString($"<a href=\"{fc.Configuration.Url}\">{fc.Configuration.Name}</a>");
                        if (fc.TestLogs != null) output.WriteSafeString($"<a href=\"{fc.TestLogs}\">[Details]</a> ");
                        if (fc.HistoryLink != null) output.WriteSafeString($"<a href=\"{fc.HistoryLink}\">[History]</a> ");
                        if (fc.ArtifactLink != null) output.WriteSafeString($"<a href=\"{fc.ArtifactLink}\">[Artifacts]</a> ");
                        output.WriteSafeString("</li>");
                    }

                    output.WriteSafeString("</ul>");
                    output.WriteSafeString("</details>");
                }
            }
        }
    }

    public class SplitMessageIntoCollapsibleSectionsByLength : InlineHelper
    {
        public override PathInfo Name => "SplitMessageIntoCollapsibleSectionsByLength";

        public override void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context,
            in Arguments arguments)
        {
            if (arguments.Length != 2)
            {
                throw new HandlebarsException($"{{{{#{Name}}}}} helper must have two arguments");
            }

            if (!(arguments[0] is string errorMessage))
            {
                throw new HandlebarsException($"{{{{#{Name}}}}} helper expects argument to be string");
            }

            if (!(arguments[1] is int splitLength))
            {
                throw new HandlebarsException($"{{{{#{Name}}}}} helper expects argument to be int");
            }

            if (errorMessage.Length <= splitLength)
            {
                RenderKnownLinks renderKnown = new RenderKnownLinks();
                renderKnown.Invoke(output, options, context, new Arguments(arguments[0]));
            }
            else
            {
                output.WriteSafeString("<i>expand to see the full error</i>");
                output.WriteSafeString("<ul><details><summary>");
                output.Write(errorMessage[..splitLength]);
                output.WriteSafeString("</summary>");
                output.Write(errorMessage[splitLength..]);
                output.WriteSafeString("</details></ul>");
            }
        }
    }

    public class RenderKnownLinks : InlineHelper
    {
        public override PathInfo Name => "RenderKnownLinks";

        public override void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context, in Arguments arguments)
        {
            if (arguments.Length != 1)
            {
                throw new HandlebarsException($"{{{{#{Name}}}}} helper must have one argument");
            }

            if (!(arguments[0] is string errorMessage))
            {
                throw new HandlebarsException($"{{{{#{Name}}}}} helper expects argument to be string");
            }

            if (RewriteConsoleLogLink(in output, errorMessage))
            {
                return;
            }
            else if (RewriteFailureLogLink(in output, errorMessage))
            {
                return;
            }
            else
            {
                output.Write(errorMessage);
            }
        }

        private bool RewriteConsoleLogLink(in EncodedTextWriter output, string errorMessage)
        {
            string consoleLogPrefix = "Check the Test tab or this console log: ";

            int markerStartIndex = errorMessage.IndexOf(consoleLogPrefix);

            if (markerStartIndex < 0)
            {
                return false;
            }

            int urlStartIndex = markerStartIndex + consoleLogPrefix.Length;

            output.Write(errorMessage[..markerStartIndex]);
            output.WriteSafeString($"<a href=\"{errorMessage[urlStartIndex..]}\">Check the Test tab or [this console log]</a>");

            return true;
        }

        private bool RewriteFailureLogLink(in EncodedTextWriter output, string errorMessage)
        {
            string failureLogPrefix = "Failure log: ";

            int markerStartIndex = errorMessage.IndexOf(failureLogPrefix);

            if (markerStartIndex < 0)
            {
                return false;
            }

            int urlStartIndex = markerStartIndex + failureLogPrefix.Length;

            output.Write(errorMessage[..markerStartIndex]);
            output.WriteSafeString($"<a href=\"{errorMessage[urlStartIndex..]}\">[Failure log]</a>");

            return true;
        }
    }

    public class TruncateHelper : InlineHelper
    {
        public override PathInfo Name => "Truncate";

        public override void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context, in Arguments arguments)
        {
            if (arguments.Length != 2)
            {
                throw new HandlebarsException("{{#truncate}} helper must have two arguments");
            }

            if (!(arguments[0] is string input))
            {
                throw new HandlebarsException("{{#truncate}} helper expects first argument to be string");
            }

            if (!(arguments[1] is int length) || length < 0)
            {
                throw new HandlebarsException("{{#truncate}} helper expects second argument to be a positive integer ");
            }

            int maxTruncateLength = length < input.Length ? length : input.Length;
            output.Write(input.Substring(0, maxTruncateLength));
        }
    }
    public class SnapshotIdCommentHelper : InlineHelper
    {
        public override PathInfo Name => "SnapshotIdComment";

        public override void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context, in Arguments arguments)
        {
            if(arguments.Length < 1)
            {
                return;
            }

            output.WriteSafeString($"<!-- SnapshotId: {arguments[0]} -->");
        }
    }
    public class SentimentTrackingHelper : InlineHelper
    {
        private readonly FeatureSentimentInjector _injector;

        public SentimentTrackingHelper(SentimentInjectorFactory factory)
        {
            _injector = factory.CreateForFeature(SentimentFeature.DeveloperWorkflowGitHubCheckTab);
        }

        public override PathInfo Name => "SentimentTracking";

        public override void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context, in Arguments arguments)
        {
            UserSentimentParameters parameters;
            if (arguments.Hash.TryGetValue("params", out var parametersObject))
            {
                parameters = (UserSentimentParameters) parametersObject;
            }
            else
            {
                parameters = (UserSentimentParameters) arguments[0];
            }

            FeatureSentimentInjector injector = _injector;

            if (parameters != null)
            {
                Add(ref injector, "r", parameters.Repository);
                Add(ref injector, "b", parameters.BuildId);
                Add(ref injector, "ut", parameters.HasUniqueTestFailures);
                Add(ref injector, "ub", parameters.HasUniqueBuildFailures);
                Add(ref injector, "rb", parameters.IsRetryWithUniqueBuildFailures);
                Add(ref injector, "rt", parameters.IsRetryWithUniqueTestFailures);
                Add(ref injector, "s", parameters.SnapshotId);
                Add(ref injector, "ki", parameters.KnownIssues);

                string commitHash = parameters.CommitHash;
                if (!string.IsNullOrEmpty(commitHash))
                {
                    if (commitHash.Length > 12)
                        commitHash = commitHash[..12];
                    injector = injector.WithProperty("c", commitHash);
                }

                Add(ref injector, "e", parameters.IsEmpty);
            }

            output.WriteSafeString(injector.GetMarkdown());
        }
        
        private static void Add(ref FeatureSentimentInjector injector, string key, bool? value)
        {
            if (!value.HasValue)
                return;
            injector = injector.WithProperty(key, value.Value ? "1" : "0");
        }

        private static void Add(ref FeatureSentimentInjector injector, string key, int? value)
        {
            if (!value.HasValue)
                return;
            injector = injector.WithProperty(key, value.Value.ToString());
        }

        private static void Add(ref FeatureSentimentInjector injector, string key, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            injector = injector.WithProperty(key, value);
        }
    }

    public abstract class BaseLinkHelper : InlineHelper
    {
        private readonly FeatureSentimentInjector _injector;

        public BaseLinkHelper(SentimentInjectorFactory factory)
        {
            _injector = factory.CreateForFeature(SentimentFeature.DeveloperWorkflowGitHubCheckTab);
        }
        public override void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context, in Arguments arguments)
        {
            if (arguments.Length > 1 && arguments[1] != null)
            {
                string urlString = arguments[1].ToString();

                RenderString(output, urlString, arguments[0]?.ToString());
            }
        }

        protected abstract void RenderString(EncodedTextWriter output, string url, string text);
    }

    public class HtmlLinkHelper : BaseLinkHelper
    {
        public override PathInfo Name => "LinkToHtml";

        public HtmlLinkHelper(SentimentInjectorFactory factory) : base(factory)
        {
        }

        protected override void RenderString(EncodedTextWriter output, string url, string text)
        {
            // TODO: text isn't technically "safe" here, but it's sort of half safe (links don't work, but other things do)
            output.WriteSafeString($"<a href=\"{url}\">{text}</a>");
        }
    }

    public class MarkdownLinkHelper : BaseLinkHelper
    {
        public override PathInfo Name => "LinkTo";

        public MarkdownLinkHelper(SentimentInjectorFactory factory) : base(factory)
        {
        }

        protected override void RenderString(EncodedTextWriter output, string url, string text)
        {
            output.WriteSafeString("[");
            output.Write(text);
            output.WriteSafeString("](");
            output.Write(url);
            output.WriteSafeString(")");
        }
    }

    public static void DateTimeFormatter(IHandlebars hb)
    {
        var formatter = new CustomDateTimeFormatter("yyyy-MM-dd");
        hb.Configuration.FormatterProviders.Add(formatter);
    }
}

public sealed class CustomDateTimeFormatter : IFormatter, IFormatterProvider
{
    private readonly string _format;

    public CustomDateTimeFormatter(string format) => _format = format;

    public void Format<T>(T value, in EncodedTextWriter writer)
    {
        if (!(value is DateTimeOffset dateTime))
            throw new ArgumentException("supposed to be DateTimeOffset");

        writer.WriteSafeString(dateTime.ToString(_format));
    }

    public bool TryCreateFormatter(Type type, out IFormatter formatter)
    {
        if (type != typeof(DateTimeOffset))
        {
            formatter = null;
            return false;
        }

        formatter = this;
        return true;
    }
}
