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
                // Matches the wording System.CommandLine uses when arity alone reports the value.
                result.AddError($"Required argument missing for option: '{option.Name}'.");
            }
        });

    /// <summary>
    /// Rewrites a short option carrying an attached value, e.g. <c>-npackage</c>, into its explicit
    /// form <c>-n=package</c>, and keeps such a token attached to the option in front of it when that
    /// option is still waiting for its value, e.g. <c>--name -npackage</c>.
    /// </summary>
    /// <remarks>
    /// System.CommandLine only keeps an attached value together with its option while the option
    /// demands a value, which the zero minimum arity <see cref="RequireValue{TValue}"/> relies on
    /// takes away. Without this, <c>-npackage</c> would unbundle into <c>-n -p ackage</c> whenever
    /// the value happens to start with another alias the command knows. Values attached to flags are
    /// left alone so that bundles such as <c>-fq</c> keep working.
    /// </remarks>
    internal static string[] AttachShortOptionValues(Command command, string[] args)
    {
        List<string> normalized = new(args.Length);
        var current = command;
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
            else if (!LooksLikeAShortOptionWithAnAttachedValue(arg))
            {
                normalized.Add(arg);
                current = current.Subcommands.FirstOrDefault(subcommand => subcommand.Name == arg) ?? current;
            }
            else if (normalized.Count > 0 && NamesAnOptionTakingAValue(current, normalized[^1]))
            {
                normalized[^1] = $"{normalized[^1]}={arg}";
            }
            else if (NamesAnOptionTakingAValue(current, arg[..2]))
            {
                normalized.Add($"{arg[..2]}={arg[2..]}");
            }
            else
            {
                normalized.Add(arg);
            }
        }

        return [.. normalized];
    }

    private static bool LooksLikeAShortOptionWithAnAttachedValue(string arg)
        => arg.Length > 2
            && arg[0] == '-'
            && arg[1] != '-'
            && arg[2] != '='
            && arg[2] != ':';

    private static bool NamesAnOptionTakingAValue(Command command, string alias)
        => command.Options.Any(option => TakesAValue(option) && (option.Name == alias || option.Aliases.Contains(alias)));

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
