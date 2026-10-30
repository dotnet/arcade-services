// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Microsoft.DotNet.Darc.Options;

/// <summary>
/// Factories for the option and argument shapes darc uses that System.CommandLine does not provide
/// out of the box.
/// </summary>
internal static class CliOptions
{
    /// <summary>
    /// An option that may be repeated or given several space-separated values, e.g.
    /// <c>--additional-remote a --additional-remote b</c> or <c>--additional-remote a b</c>.
    /// </summary>
    /// <remarks>
    /// Collection options are never left unset: when the option is omitted the property is assigned an
    /// empty collection, which is what darc has always done.
    /// </remarks>
    public static Option<string[]> List(string name, string description, params string[] aliases)
    {
        Option<string[]> option = new(name, aliases)
        {
            Description = description,
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => [],
        };

        RequireValues(option);
        return option;
    }

    /// <summary>
    /// An option whose values may additionally be packed into a single token, e.g. <c>--ids 1,2,3</c>.
    /// </summary>
    /// <inheritdoc cref="List(string, string, string[])" path="/remarks"/>
    public static Option<string[]> SeparatedList(char separator, string name, string description, params string[] aliases)
    {
        Option<string[]> option = new(name, aliases)
        {
            Description = description,
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => [],
            CustomParser = result => Split(result, separator),
        };

        RequireValues(option);
        return option;
    }

    /// <inheritdoc cref="SeparatedList(char, string, string, string[])"/>
    public static Option<int[]> SeparatedIntList(char separator, string name, string description, params string[] aliases)
    {
        Option<int[]> option = new(name, aliases)
        {
            Description = description,
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => [],
            CustomParser = result => ParseInts(result, separator),
        };

        RequireValues(option);
        return option;
    }

    /// <summary>
    /// Enum option that only accepts the exact casing of the enum members, matching the behaviour
    /// darc has always had for <c>--output-format</c>.
    /// </summary>
    public static Option<TEnum> CaseSensitiveEnum<TEnum>(string name, string description, TEnum defaultValue, params string[] aliases)
        where TEnum : struct, Enum
    {
        Option<TEnum> option = new(name, aliases)
        {
            Description = description,
            DefaultValueFactory = _ => defaultValue,
            CustomParser = result =>
            {
                if (result.Tokens.Count == 0)
                {
                    // RequireValue reports the missing value; the parser must not throw before it runs.
                    return defaultValue;
                }

                var token = result.Tokens.Single().Value;
                if (Enum.TryParse<TEnum>(token, ignoreCase: false, out var value) && Enum.IsDefined(value))
                {
                    return value;
                }

                result.AddError($"'{token}' is not a valid value for {name}. Valid values are: "
                    + string.Join(", ", Enum.GetNames<TEnum>()) + " (case sensitive).");
                return default;
            },
        };

        RequireValue(option);
        return option;
    }

    /// <summary>
    /// A positional argument taking a single value.
    /// </summary>
    public static Argument<string> Value(string name, string description, bool required)
        => new(name)
        {
            Description = description,
            Arity = required ? ArgumentArity.ExactlyOne : ArgumentArity.ZeroOrOne,
        };

    /// <summary>
    /// A positional argument taking any number of values.
    /// </summary>
    /// <inheritdoc cref="List(string, string, string[])" path="/remarks"/>
    public static Argument<string[]> ValueList(string name, string description, bool required)
    {
        Argument<string[]> argument = new(name)
        {
            Description = description,
            Arity = required ? ArgumentArity.OneOrMore : ArgumentArity.ZeroOrMore,
        };

        if (!required)
        {
            argument.DefaultValueFactory = _ => [];
        }

        return argument;
    }

    /// <summary>
    /// Stops a scalar option from consuming a token the command recognises as one of its own options,
    /// so that e.g. <c>--target-repo --help</c> still prints help instead of binding <c>--help</c> as
    /// the repository name.
    /// </summary>
    /// <remarks>
    /// System.CommandLine only declines to consume a recognised token when the option's arity allows
    /// zero values, so the missing value has to be reported by a validator rather than by the arity.
    /// An option that was not supplied on the command line still produces a result when it declares a
    /// default, and that result must be left alone. Values that merely look like options but are not
    /// recognised, such as <c>--bogus</c> or the negative number <c>-5</c>, are still bound.
    /// </remarks>
    public static void RequireValue<TValue>(Option<TValue> option)
    {
        option.Arity = ArgumentArity.ZeroOrOne;
        RejectMissingValue(option);
    }

    /// <summary>
    /// The <see cref="RequireValue{TValue}(Option{TValue})"/> equivalent for options taking several values.
    /// </summary>
    /// <inheritdoc cref="RequireValue{TValue}(Option{TValue})" path="/remarks"/>
    private static void RequireValues<TValue>(Option<TValue> option)
    {
        option.Arity = ArgumentArity.ZeroOrMore;
        RejectMissingValue(option);
    }

    private static void RejectMissingValue<TValue>(Option<TValue> option)
        => option.Validators.Add(result =>
        {
            if (!result.Implicit && result.Tokens.Count == 0)
            {
                // Matches the wording and the name System.CommandLine uses when arity reports the
                // missing value itself, which is the alias the user typed rather than the option name.
                var name = result.IdentifierToken?.Value ?? option.Name;
                result.AddError($"Required argument missing for option: '{name}'.");
            }
        });

    /// <summary>
    /// Rewrites a short option bundle carrying an attached value, e.g. <c>-npackage</c> or
    /// <c>-fvpreview</c>, into its explicit form (<c>-n=package</c>, <c>-f -v=preview</c>), and keeps
    /// such a token attached to the option in front of it when that option is still waiting for its
    /// value, e.g. <c>--name -npackage</c>.
    /// </summary>
    /// <remarks>
    /// System.CommandLine only keeps an attached value together with its option while the option
    /// demands a value, which the zero minimum arity <see cref="RequireValue{TValue}"/> relies on
    /// takes away. Without this, <c>-npackage</c> would unbundle into <c>-n -p ackage</c> whenever
    /// the value happens to start with another alias the command knows. Bundles made up purely of
    /// flags, such as <c>-fl</c>, are left alone so that they keep unbundling.
    /// </remarks>
    internal static string[] AttachShortOptionValues(Command command, string[] args)
    {
        List<string> normalized = new(args.Length);
        List<Command> path = [command];
        var literal = false;

        foreach (var arg in args)
        {
            if (literal)
            {
                normalized.Add(arg);
            }
            else if (arg == "--")
            {
                literal = true;
                normalized.Add(arg);
            }
            else if (!CouldBundleAValue(arg))
            {
                normalized.Add(arg);

                var subcommand = path[^1].Subcommands.FirstOrDefault(candidate => candidate.Name == arg);
                if (subcommand is not null)
                {
                    path.Add(subcommand);
                }
            }
            else if (normalized.Count > 0 && ExpectsAValue(path, normalized[^1]))
            {
                normalized[^1] = $"{normalized[^1]}={arg}";
            }
            else
            {
                normalized.AddRange(Unbundle(path, arg));
            }
        }

        return [.. normalized];
    }

    /// <summary>
    /// Splits a short option bundle at the first alias that takes a value, so that the rest of the
    /// token stays that option's value. Mirrors how System.CommandLine unbundles a greedy option.
    /// </summary>
    private static IEnumerable<string> Unbundle(List<Command> path, string arg)
    {
        for (var i = 1; i < arg.Length; i++)
        {
            if (arg[i] is ':' or '=')
            {
                // System.CommandLine attaches the remainder itself once an assignment appears.
                break;
            }

            var option = FindOption(path, $"-{arg[i]}");
            if (option is null)
            {
                // An unrecognised character makes System.CommandLine treat the rest as a value already.
                break;
            }

            if (TakesAValue(option) && i != arg.Length - 1)
            {
                var value = i + 1;
                if (arg[value] is ':' or '=')
                {
                    value++;
                }

                if (value == arg.Length)
                {
                    // An assignment with nothing behind it, e.g. '-fv='. Rewriting it would drop the
                    // empty value, because an explicit assignment with an empty remainder produces no
                    // argument token at all, while the bundle tokenizer does emit one.
                    break;
                }

                return [.. arg[1..i].Select(flag => $"-{flag}"), $"-{arg[i]}={arg[value..]}"];
            }
        }

        return [arg];
    }

    private static bool CouldBundleAValue(string arg)
        => arg.Length > 2
            && arg[0] == '-'
            && arg[1] != '-'
            && arg[2] != '='
            && arg[2] != ':';

    /// <summary>
    /// Tells whether the given argument ends in an option that still needs a value, which is true for
    /// a long option, a short option and a flag bundle whose last alias takes a value.
    /// </summary>
    private static bool ExpectsAValue(List<Command> path, string arg)
    {
        if (arg.Length < 2 || arg[0] != '-' || arg.Contains('=') || arg.Contains(':'))
        {
            return false;
        }

        if (arg[1] == '-')
        {
            return FindOption(path, arg) is Option option && TakesAValue(option);
        }

        Option last = null;
        for (var i = 1; i < arg.Length; i++)
        {
            last = FindOption(path, $"-{arg[i]}");
            if (last is null)
            {
                return false;
            }
        }

        return last is not null && TakesAValue(last);
    }

    private static Option FindOption(List<Command> path, string alias)
    {
        for (var i = path.Count - 1; i >= 0; i--)
        {
            var option = path[i].Options.FirstOrDefault(
                candidate => (i == path.Count - 1 || candidate.Recursive)
                    && (candidate.Name == alias || candidate.Aliases.Contains(alias)));

            if (option is not null)
            {
                return option;
            }
        }

        return null;
    }

    private static bool TakesAValue(Option option)
        => option.Arity.MaximumNumberOfValues > 0
            && option.ValueType != typeof(bool)
            && option.ValueType != typeof(bool?);

    private static string[] Split(ArgumentResult result, char separator)
        => [.. result.Tokens.SelectMany(token => token.Value.Split(separator))];

    private static int[] ParseInts(ArgumentResult result, char separator)
    {
        var values = Split(result, separator);
        var parsed = new int[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            if (!int.TryParse(values[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed[i]))
            {
                result.AddError($"'{values[i]}' is not a valid integer.");
                return [];
            }
        }

        return parsed;
    }
}
