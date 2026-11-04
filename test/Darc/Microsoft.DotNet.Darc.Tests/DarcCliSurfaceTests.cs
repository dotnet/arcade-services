// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Options;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests;

/// <summary>
/// Pins down the whole darc command line surface: every verb, every option and every positional
/// argument, together with the property each one binds to. The baseline was generated from the
/// CommandLineParser based implementation, so any unintended change made while moving to
/// System.CommandLine (or later on) shows up as a diff here.
/// </summary>
[TestFixture]
public class DarcCliSurfaceTests
{
    private const string BaselineFileName = "DarcCliSurface.baseline.txt";

    [Test]
    public void CliSurfaceMatchesBaseline()
    {
        var actual = DescribeSurface();
        var baselinePath = Path.Combine(TestContext.CurrentContext.TestDirectory, BaselineFileName);
        var expected = File.ReadAllText(baselinePath).Replace("\r\n", "\n");

        if (actual != expected)
        {
            var actualPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "DarcCliSurface.actual.txt");
            File.WriteAllText(actualPath, actual);
            TestContext.Out.WriteLine($"Actual surface written to {actualPath}");
        }

        actual.Split('\n').Should().Equal(
            expected.Split('\n'),
            "the darc command line surface must not change (baseline: {0})",
            BaselineFileName);
    }

    private static string DescribeSurface()
    {
        var sb = new StringBuilder();
        AppendVerbs(sb, "darc", Program.GetOptions());
        AppendVerbs(sb, "darc vmr", Program.GetVmrOptions());
        return sb.ToString().Replace("\r\n", "\n");
    }

    private static void AppendVerbs(StringBuilder sb, string prefix, CommandLineOptions[] options)
    {
        foreach (var verb in options)
        {
            sb.AppendLine($"{prefix} {verb.Verb}");
            sb.AppendLine($"  help: {Normalize(verb.VerbDescription)}");

            var symbols = verb.Symbols;
            var index = 0;

            foreach (var symbol in symbols.Where(s => s.Symbol is Argument))
            {
                var argument = (Argument)symbol.Symbol;
                sb.AppendLine($"  value[{index++}] meta={Normalize(argument.Name)} required={symbol.Required} "
                    + $"type={TypeName(argument.ValueType)} prop={PropertyName(symbol)} "
                    + $"help={Normalize(argument.Description)}");
            }

            foreach (var symbol in symbols
                .Where(s => s.Symbol is Option)
                .OrderBy(s => s.Symbol.Name, StringComparer.Ordinal))
            {
                var option = (Option)symbol.Symbol;
                sb.AppendLine($"  option {option.Name} short={ShortName(option)} required={symbol.Required} "
                    + $"separator={symbol.Separator?.ToString() ?? "-"} default={FormatDefault(option)} "
                    + $"type={TypeName(option.ValueType)} prop={PropertyName(symbol)} "
                    + $"help={Normalize(option.Description)}");
            }

            sb.AppendLine();
        }
    }

    private static string ShortName(Option option)
        => option.Aliases.FirstOrDefault(a => a.StartsWith('-') && !a.StartsWith("--", StringComparison.Ordinal)) ?? "-";

    private static string PropertyName(CliSymbol symbol)
    {
        // The binding expressions are simple setter lambdas such as "value => GitLocation = value".
        var match = Regex.Match(symbol.BindingExpression ?? string.Empty, @"=>\s*(?<name>\w+)\s*=");
        return match.Success ? match.Groups["name"].Value : "-";
    }

    private static string TypeName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is Type underlying)
        {
            return TypeName(underlying) + "?";
        }

        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
        {
            var element = type.IsArray ? type.GetElementType() : type.GetGenericArguments().FirstOrDefault();
            return $"list<{(element == null ? "?" : TypeName(element))}>";
        }

        return type.Name;
    }

    private static string FormatDefault(Option option)
        => option.HasDefaultValue ? FormatValue(option.GetDefaultValue()) : "-";

    private static string FormatValue(object value)
        => value switch
        {
            null => "-",
            string s => Normalize(s),
            IEnumerable e => "[" + string.Join(",", e.Cast<object>().Select(x => x?.ToString())) + "]",
            _ => value.ToString()
        };

    private static string Normalize(string text)
        => string.IsNullOrEmpty(text) ? "-" : text.Replace("\r", string.Empty).Replace("\n", " ").Trim();
}
