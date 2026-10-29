// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.Linq;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests;

/// <summary>
/// Covers the parsing behaviours that the CLI surface baseline cannot express, all of which had to
/// be preserved when darc moved from CommandLineParser to System.CommandLine.
/// </summary>
[TestFixture]
public class CommandLineParsingTests
{
    [Test]
    public void OmittedOptionsKeepTheirPropertyInitializers()
    {
        var options = Parse(new GetSubscriptionHistoryCommandLineOptions(), "get-subscription-history");

        options.Limit.Should().Be(10);
    }

    [Test]
    public void OmittedCollectionOptionsAreEmptyRatherThanNull()
    {
        var options = Parse(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions");

        // Neither property declares an initializer, yet darc has always handed the operations an
        // empty collection rather than null.
        options.Frequencies.Should().NotBeNull().And.BeEmpty();
        options.SubscriptionIds.Should().NotBeNull().And.BeEmpty();
    }

    [Test]
    public void OmittedVmrPathFallsBackToTheCurrentDirectory()
    {
        var options = Parse(new AddRepoCommandLineOptions(), "add-repo", "https://github.com/dotnet/runtime:abc123");

        options.VmrPath.Should().Be(Environment.CurrentDirectory);
    }

    [Test]
    public void SeparatedListOptionsPreserveEmptyEntries()
    {
        Parse(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions", "--ids", "a,,b")
            .SubscriptionIds.Should().Equal("a", string.Empty, "b");
    }

    [Test]
    public void CollectionOptionsRequireAtLeastOneValue()
    {
        ParseResultOf(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions", "--ids")
            .Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("--ids");
    }

    [Test]
    public void CollectionOptionsDoNotSwallowMistypedOptions()
    {
        ParseResultOf(new ForwardFlowCommandLineOptions(), "forwardflow", "--additional-remote", "--bogus")
            .Errors.Should().Contain(error => error.Message.Contains("--bogus"));

        ParseResultOf(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions", "--ids", "a", "--bogus")
            .Errors.Should().Contain(error => error.Message.Contains("--bogus"));
    }

    [Test]
    public void IntegerListOptionsRejectNonNumericValues()
    {
        ParseResultOf(new GatherDropCommandLineOptions(), "gather-drop", "--id", "1,x")
            .Errors.Should().Contain(error => error.Message.Contains("'x' is not a valid integer"));
    }

    [Test]
    public void SeparatedListOptionsAcceptPackedAndRepeatedValues()
    {
        var packed = Parse(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions", "--ids", "a,b,c");
        packed.SubscriptionIds.Should().Equal("a", "b", "c");

        var repeated = Parse(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions", "--ids", "a", "--ids", "b");
        repeated.SubscriptionIds.Should().Equal("a", "b");
    }

    [Test]
    public void ListOptionsAcceptRepeatedAndSpaceSeparatedValues()
    {
        var repeated = Parse(
            new ForwardFlowCommandLineOptions(),
            "forwardflow",
            "--additional-remote",
            "runtime:/one",
            "--additional-remote",
            "sdk:/two");

        repeated.AdditionalRemotes.Should().Equal("runtime:/one", "sdk:/two");

        var packed = Parse(
            new ForwardFlowCommandLineOptions(),
            "forwardflow",
            "--additional-remote",
            "runtime:/one",
            "sdk:/two");

        packed.AdditionalRemotes.Should().Equal("runtime:/one", "sdk:/two");
    }

    [Test]
    public void OutputFormatIsCaseSensitive()
    {
        Parse(new GetChannelsCommandLineOptions(), "get-channels", "--output-format", "json")
            .OutputFormat.Should().Be(DarcOutputType.json);

        ParseResultOf(new GetChannelsCommandLineOptions(), "get-channels", "--output-format", "JSON")
            .Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("case sensitive");
    }

    [Test]
    public void ExactAndRegexCannotBeCombined()
    {
        ParseResultOf(new GetSubscriptionsCommandLineOptions(), "get-subscriptions", "--exact", "--regex")
            .Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("cannot be used together");
    }

    [Test]
    public void RedeclaredOptionsBindToTheDerivedProperty()
    {
        var options = Parse(new LoginCommandLineOptions(), "login", "--bar-uri", "https://example.test");

        options.BarUri.Should().Be("https://example.test");
        options.BuildAssetRegistryBaseUri.Should().BeNull();
    }

    [Test]
    public void PositionalArgumentsBindAlongsideOptions()
    {
        var options = Parse(new AddRepoCommandLineOptions(), "add-repo", "https://github.com/dotnet/runtime:abc123");

        options.Repositories.Should().Equal("https://github.com/dotnet/runtime:abc123");
    }

    private static T Parse<T>(T options, params string[] args) where T : CommandLineOptions
    {
        var result = ParseResultOf(options, args);
        result.Errors.Should().BeEmpty();
        options.Bind(result);
        return options;
    }

    private static ParseResult ParseResultOf(CommandLineOptions options, params string[] args)
    {
        RootCommand root = new();
        root.Add(Program.CreateCommand(options));
        return root.Parse(args);
    }
}
