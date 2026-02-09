// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.UserSentiment;
using HandlebarsDotNet;
using HandlebarsDotNet.Helpers;
using HandlebarsDotNet.IO;
using HandlebarsDotNet.PathStructure;
using QueueInsights.Models;

namespace QueueInsights;

internal class InsightMarkdownHelpers
{
    private static string BasePath { get; } =
        Path.GetDirectoryName(typeof(InsightMarkdownHelpers).Assembly.Location);

    private static void LoadPartials(IHandlebars hb)
    {
        string partials = Path.GetFullPath(Path.Combine(BasePath, "Templates", "Partials"));

        foreach (string filePath in Directory.EnumerateFiles(partials, "*.hbs", SearchOption.AllDirectories))
        {
            string template = File.ReadAllText(filePath);
            hb.RegisterTemplate(Path.GetFileNameWithoutExtension(filePath), template);
        }
    }

    public static Dictionary<string, HandlebarsTemplate<object, object>> Compile(IHandlebars hb)
    {
        LoadPartials(hb);
        return Directory.GetFiles(Path.Combine(BasePath, "Templates"), "*.hbs", SearchOption.TopDirectoryOnly)
            .ToDictionary(Path.GetFileNameWithoutExtension, file => hb.Compile(File.ReadAllText(file)));
    }

    public static void RegisterHelpers(IHandlebars hb, SentimentInjectorFactory factory)
    {
        hb.RegisterHelper("QueueLink", QueueLinkHelper);
        hb.RegisterHelper(new SentimentTrackingHelper(factory));
    }

    public static void RegisterFormatters(IHandlebars hb)
    {
        hb.Configuration.FormatterProviders.Add(new TimeSpanFormatter());
    }

    private static void QueueLinkHelper(EncodedTextWriter output, Context context, Arguments arguments)
    {
        string queueName = arguments[0].ToString();
        output.WriteSafeString("[");
        output.Write(arguments[0]);
        output.WriteSafeString("](");
        output.Write(GrafanaUrlGenerator.GetGrafanaUrlForQueue(queueName));
        output.WriteSafeString(")");
    }

    public sealed class TimeSpanFormatter : IFormatter, IFormatterProvider
    {
        public void Format<T>(T value, in EncodedTextWriter writer)
        {
            if (value is not TimeSpan span)
                throw new ArgumentException("supposed to be TimeSpan");

            if (span == TimeSpan.Zero)
            {
                writer.Write("0 seconds");
                return;
            }

            static string FormatPart(int quantity, string name)
            {
                return quantity > 0 ? $"{quantity} {name}{(quantity > 1 ? "s" : "")}" : null;
            }

            writer.Write(string.Join(", ",
                new[]
                {
                    FormatPart(span.Days, "day"),
                    FormatPart(span.Hours, "hour"),
                    FormatPart(span.Minutes, "minute"),
                    FormatPart(span.Seconds, "second")
                }.Where(x => x != null)));
        }

        public bool TryCreateFormatter(Type type, out IFormatter formatter)
        {
            if (type != typeof(TimeSpan))
            {
                formatter = null;
                return false;
            }

            formatter = this;
            return true;
        }
    }

    public class SentimentTrackingHelper : IHelperDescriptor<HelperOptions>
    {
        private readonly FeatureSentimentInjector _injector;

        public SentimentTrackingHelper(SentimentInjectorFactory factory)
        {
            _injector = factory.CreateForFeature(SentimentFeature.HelixQueueInsights);
        }


        public object Invoke(in HelperOptions options, in Context context, in Arguments arguments)
        {
            return this.ReturnInvoke(options, context, arguments);
        }

        public void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context,
            in Arguments arguments)
        {
            UserSentimentParameters parameters;
            if (arguments.Hash.TryGetValue("params", out object parametersObject))
                parameters = (UserSentimentParameters)parametersObject;
            else
                parameters = (UserSentimentParameters)arguments[0];

            FeatureSentimentInjector injector = _injector;

            if (parameters != null)
            {
                Add(ref injector, "r", parameters.Repository);
                Add(ref injector, "pr", parameters.PullRequest);

                string commitHash = parameters.CommitHash;
                if (!string.IsNullOrEmpty(commitHash))
                {
                    if (commitHash.Length > 12)
                        commitHash = commitHash[..12];
                    injector = injector.WithProperty("c", commitHash);
                }

                Add(ref injector, "p", parameters.WasPending);
            }

            output.WriteSafeString(injector.GetMarkdown());
        }

        private static void Add(ref FeatureSentimentInjector injector, string key, bool value)
        {
            Add(ref injector, key, value ? "1" : "0");
        }

        public PathInfo Name => "SentimentTracking";

        private static void Add(ref FeatureSentimentInjector injector, string key, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            injector = injector.WithProperty(key, value);
        }
    }
}
