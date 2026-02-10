// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;
using HandlebarsDotNet.IO;

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

    public static void RegisterHelpers(IHandlebars hb)
    {
        hb.RegisterHelper("QueueLink", QueueLinkHelper);
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
}
